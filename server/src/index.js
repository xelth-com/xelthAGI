const express = require('express');
const cors = require('cors');
const path = require('path');
const { z } = require('zod');
const config = require('./config');
const llmService = require('./llmService');
const authService = require('./authService');
const patcher = require('./patcher');

const app = express();

app.use(express.json({ limit: '50mb' }));
app.use(cors());

// --- AUTH MIDDLEWARE ---
// Must be applied before API routes but after CORS/JSON
const authenticate = (req, res, next) => {
    // Whitelist specific paths
    if (req.path === '/HEALTH' || req.path.startsWith('/DOWNLOAD')) {
        return next();
    }

    const authHeader = req.headers.authorization;
    if (!authHeader || !authHeader.startsWith('Bearer ')) {
        console.warn(`Blocked unauthorized request from ${req.ip}`);
        return res.status(401).json({ Success: false, Error: "Missing Authentication Token" });
    }

    const token = authHeader.split(' ')[1];
    const client = authService.validateToken(token);

    if (!client) {
        console.warn(`Blocked invalid token: ${token.substring(0, 10)}...`);
        return res.status(403).json({ Success: false, Error: "Invalid or Revoked Token" });
    }

    req.authClient = client;
    next();
};

// Role-based authorization: block console tokens from /DECIDE
const requireAgentRole = (req, res, next) => {
    if (req.authClient && req.authClient.role === 'view') {
        console.warn(`Blocked console token from /DECIDE: ${req.authClient.id}`);
        return res.status(403).json({ Success: false, Error: "Console tokens cannot execute commands" });
    }
    next();
};

// Protect the brain
app.use('/DECIDE', authenticate, requireAgentRole); // Agent tokens only
app.use('/API', authenticate); // Both agent and console tokens

// Ensure logs directory exists (UPPERCASE for API consistency)
const fs = require('fs');
const logsDir = path.join(__dirname, '..', 'public', 'LOGS');
if (!fs.existsSync(logsDir)) {
    fs.mkdirSync(logsDir, { recursive: true });
}

// Ensure screenshots directory exists
const screenshotsDir = path.join(__dirname, '..', 'public', 'SCREENSHOTS');
if (!fs.existsSync(screenshotsDir)) {
    fs.mkdirSync(screenshotsDir, { recursive: true });
}

// --- GARBAGE COLLECTION (CLEANUP) ---
const LOG_RETENTION_MS = 48 * 60 * 60 * 1000; // 48 hours
const SCREENSHOT_RETENTION_MS = 1 * 60 * 60 * 1000; // 1 hour

function cleanupDirectory(directory, maxAgeMs) {
    fs.readdir(directory, (err, files) => {
        if (err) {
            console.error(`Cleanup error reading ${directory}:`, err);
            return;
        }

        const now = Date.now();
        files.forEach(file => {
            const filePath = path.join(directory, file);
            fs.stat(filePath, (err, stats) => {
                if (err) return;

                const isOld = (now - stats.mtimeMs > maxAgeMs);

                if (isOld) {
                    if (stats.isDirectory()) {
                        // Recursive delete for screenshot folders
                        fs.rm(filePath, { recursive: true, force: true }, (err) => {
                            if (!err) console.log(`🧹 GC: Deleted old folder ${file}`);
                        });
                    } else {
                        // Delete file
                        fs.unlink(filePath, (err) => {
                            if (!err) console.log(`🧹 GC: Deleted old file ${file}`);
                        });
                    }
                }
            });
        });
    });
}

// Run cleanup every 10 minutes
setInterval(() => {
    cleanupDirectory(logsDir, LOG_RETENTION_MS);
    cleanupDirectory(screenshotsDir, SCREENSHOT_RETENTION_MS);
}, 10 * 60 * 1000);

// Per-client state storage for Mission Control dashboard
// Each client has isolated state, no cross-client access
const clientStates = new Map();

function getOrCreateClientState(clientId) {
    if (!clientStates.has(clientId)) {
        clientStates.set(clientId, {
            lastSeen: null,
            clientId: clientId,
            sessionName: '',
            uiState: {
                WindowTitle: '',
                ProcessName: '',
                Elements: []
            },
            task: '',
            history: [],
            lastDecision: {
                action: '',
                message: '',
                reasoning: ''
            },
            screenshot: null,
            isOnline: false,
            totalActions: 0
        });
    }
    return clientStates.get(clientId);
}

// --- Zod Models (Matching previous Pydantic models) ---

const UIElementSchema = z.object({
    Id: z.string(),
    Name: z.string(),
    Type: z.string(),
    Value: z.string(),
    IsEnabled: z.boolean(),
    Bounds: z.object({
        X: z.number(),
        Y: z.number(),
        Width: z.number(),
        Height: z.number()
    })
});

const UIStateSchema = z.object({
    WindowTitle: z.string(),
    ProcessName: z.string(),
    Elements: z.array(UIElementSchema).optional().default([]),
    Screenshot: z.string().optional().default(""),
    DebugScreenshot: z.string().optional().default("")
});

const ServerRequestSchema = z.object({
    ClientId: z.string().optional().default("unknown"),
    State: UIStateSchema,
    Task: z.string(),
    History: z.array(z.string()).optional().default([])
});

// --- Endpoints ---
// NOTE: All endpoints are UPPERCASE for QR code optimization
// Uppercase paths use alphanumeric encoding in QR codes (smaller QR size)

app.get('/HEALTH', (req, res) => {
    res.json({
        status: "healthy",
        llm_provider: config.LLM_PROVIDER,
        model: config.LLM_PROVIDER === 'claude' ? config.CLAUDE_MODEL : config.GEMINI_MODEL,
        server: "Node.js Express"
    });
});

// NEW: Download Patched Client
app.get('/DOWNLOAD/CLIENT', (req, res) => {
    try {
        const userAgent = req.headers['user-agent'] || 'unknown';
        const clientIp = req.ip;

        // 1. Generate Token (x1 prefix)
        const token = authService.createToken({
            ip: clientIp,
            ua: userAgent,
            via: 'api_download'
        });

        console.log(` Generating patched client for IP ${clientIp} with token ${token}`);

        // 2. Patch Binary
        const fileBuffer = patcher.generatePatchedBinary(token);

        // 3. Send
        const timestamp = Date.now();
        res.setHeader('Content-Disposition', `attachment; filename="SupportAgent_${timestamp}.exe"`);
        res.setHeader('Content-Type', 'application/vnd.microsoft.portable-executable');
        res.send(fileBuffer);

    } catch (e) {
        console.error("Download Error:", e.message);
        res.status(500).send("Generation failed: " + e.message + "\nEnsure 'SupportAgent.exe' exists in public/downloads.");
    }
});

app.post('/DECIDE', async (req, res) => {
    try {
        // 1. Validation
        const parseResult = ServerRequestSchema.safeParse(req.body);
        if (!parseResult.success) {
            return res.status(400).json({
                Success: false,
                Error: "Validation Error",
                Details: parseResult.error.format(),
                TaskCompleted: false
            });
        }

        const request = parseResult.data;

        // Update per-client state for Mission Control (isolated)
        const clientState = getOrCreateClientState(request.ClientId);

        // IMMEDIATE SHUTDOWN CHECK
        if (clientState.shutdownRequested) {
            console.log(`🛑 Executing shutdown for ${request.ClientId}`);
            clientState.shutdownRequested = false; // Reset flag
            return res.json({
                Command: {
                    Action: "shutdown",
                    Message: "Operator requested shutdown via Mission Control."
                },
                Success: true,
                TaskCompleted: true,
                CanonicalClientId: req.authClient.id // Maintain identity sync
            });
        }

        clientState.lastSeen = new Date().toISOString();
        clientState.clientId = request.ClientId;
        clientState.task = request.Task;
        clientState.history = request.History || [];
        // Keep previous screenshot if new one is empty
        if (request.State.Screenshot) {
            clientState.screenshot = request.State.Screenshot;
        }
        clientState.isOnline = true;
        clientState.totalActions = request.History ? request.History.length : 0;

        // Log Client ID
        const authInfo = req.authClient ? `[Auth: ${req.authClient.token.substring(0,5)}...]` : '[No Auth]';
        console.log(`\n👤 Client: ${request.ClientId} ${authInfo} | Task: ${request.Task}`);

        // 2. LLM Processing
        const uiStateDict = {
            WindowTitle: request.State.WindowTitle,
            ProcessName: request.State.ProcessName,
            Elements: request.State.Elements,
            Screenshot: request.State.Screenshot || null
        };

        const decision = await llmService.decideNextAction(
            uiStateDict,
            request.Task,
            request.History
        );

        // Update per-client state with UI state and decision
        clientState.uiState = uiStateDict;
        clientState.lastDecision = {
            action: decision.action || '',
            message: decision.message || '',
            reasoning: decision.reasoning || ''
        };

        // Generate consistent Session Name for grouping logs and screenshots
        // SECURITY: Use full clientId for proper isolation
        const clientId = request.ClientId;
        const safeTaskName = request.Task
            .replace(/[^a-zA-Z0-9]/g, '_')
            .replace(/_+/g, '_')
            .substring(0, 50);

        // Reuse existing sessionName if task hasn't changed, otherwise create new one
        let sessionName = clientState.sessionName;
        if (!sessionName || clientState.task !== request.Task) {
            const timestamp = Date.now();
            sessionName = `${clientId}_${safeTaskName}_${timestamp}`;
            clientState.sessionName = sessionName;
            clientState.task = request.Task;
        }

        // Handle Shadow Debug Screenshot (Only in DEBUG mode)
        let debugScreenshotUrl = null;
        if (config.DEBUG && request.State.DebugScreenshot) {
            try {
                // Create Session Subfolder in SCREENSHOTS
                const sessionScreenshotDir = path.join(screenshotsDir, sessionName);
                if (!fs.existsSync(sessionScreenshotDir)) {
                    fs.mkdirSync(sessionScreenshotDir, { recursive: true });
                }

                // Name image by Step Number (e.g., step_005.jpg)
                const currentStep = (request.History ? request.History.length : 0) + 1;
                const stepStr = String(currentStep).padStart(3, '0');
                const imgName = `step_${stepStr}.jpg`;

                const imgPath = path.join(sessionScreenshotDir, imgName);
                const imgBuffer = Buffer.from(request.State.DebugScreenshot, 'base64');
                fs.writeFileSync(imgPath, imgBuffer);

                // URL structure: /AGI/SCREENSHOTS/{SessionName}/{imgName}
                debugScreenshotUrl = `https://xelth.com/AGI/SCREENSHOTS/${sessionName}/${imgName}`;
                console.log(`📸 Shadow Debug Screenshot saved: ${debugScreenshotUrl}`);
            } catch (err) {
                console.error("Failed to save debug screenshot:", err.message);
            }
        }

        // 3. Handle Error from LLM Service
        if (decision.error) {
            return res.json({
                Success: false,
                Error: decision.error,
                TaskCompleted: false
            });
        }

        // 4. Check Task Completion
        if (decision.task_completed) {
            // Trigger asynchronous learning (fire-and-forget)
            console.log("🎓 Task Completed. Triggering background learning...");
            llmService.learnPlaybook(request.Task, request.History)
                .then(() => console.log("✨ Learning complete - playbook saved"))
                .catch(err => console.error("❌ Learning failed:", err.message));

            return res.json({
                Command: {
                    Action: "",
                    Message: decision.message || "Task completed successfully"
                },
                Success: true,
                TaskCompleted: true
            });
        }

        // 5. Construct Response
        const command = {
            Action: decision.action || "",
            ElementId: decision.element_id || "",
            Text: decision.text || "",
            X: decision.x || 0,
            Y: decision.y || 0,
            DelayMs: decision.delay_ms || 100,
            Message: decision.message || ""
        };

        const reasoning = decision.reasoning || "No reasoning provided";
        console.log(`🤖 Decision: ${reasoning}`);
        const coordInfo = (command.X > 0 || command.Y > 0) ? ` @(${command.X},${command.Y})` : '';
        console.log(`📤 Command: ${command.Action} on ${command.ElementId}${coordInfo}`);

        // Get the TRUE identity from the token
        const canonicalId = req.authClient.id;

        // --- SESSION LOGGING (FLIGHT RECORDER) ---
        try {
            // Reuse sessionName generated above for consistency
            const logFileName = `${sessionName}.json`;
            const logPath = path.join(logsDir, logFileName);
            const logUrl = `https://xelth.com/AGI/LOGS/${logFileName}`;

            const logEntry = {
                timestamp: new Date().toISOString(),
                step: (request.History ? request.History.length : 0) + 1,
                task: request.Task,
                ui_state: {
                    window: request.State.WindowTitle,
                    process: request.State.ProcessName,
                    elements_count: request.State.Elements ? request.State.Elements.length : 0,
                    // Store full elements only if needed, can be large
                    elements_summary: request.State.Elements ? request.State.Elements.map(e => `${e.Type}: ${e.Name} [${e.Value}]`).slice(0, 20) : []
                },
                llm_decision: decision
            };

            // Append to file (read, push, write - simple but effective for low traffic)
            let sessionData = [];
            if (fs.existsSync(logPath)) {
                try {
                    sessionData = JSON.parse(fs.readFileSync(logPath, 'utf8'));
                } catch (e) { sessionData = []; }
            }
            sessionData.push(logEntry);
            fs.writeFileSync(logPath, JSON.stringify(sessionData, null, 2));

            console.log(`📝 Log updated: ${logUrl}`);
        } catch (logErr) {
            console.error("❌ Logging failed:", logErr.message);
        }
        // ------------------------------------------

        return res.json({
            Command: command,
            Success: true,
            TaskCompleted: false,
            Reasoning: reasoning,
            CanonicalClientId: canonicalId // Force client to adopt this ID
        });

    } catch (e) {
        console.error("❌ Server Error:", e);
        res.status(500).json({
            Success: false,
            Error: e.message,
            TaskCompleted: false
        });
    }
});

// Mission Control API - Get current agent state (UPPERCASE)
// Returns ONLY the authenticated client's state (no cross-client access)
app.get('/API/STATE', (req, res) => {
    // Get the client ID from the authenticated token
    const clientId = req.authClient.id;
    const clientState = getOrCreateClientState(clientId);

    // Mark as offline if last seen more than 30 seconds ago
    if (clientState.lastSeen) {
        const timeSinceLastSeen = Date.now() - new Date(clientState.lastSeen).getTime();
        clientState.isOnline = timeSinceLastSeen < 30000; // 30 seconds
    }

    // Inject current server debug status
    const responseWithConfig = {
        ...clientState,
        serverDebugMode: config.DEBUG
    };

    res.json(responseWithConfig);
});

// API to toggle Debug Mode dynamically (UPPERCASE)
app.post('/API/SETTINGS', (req, res) => {
    if (typeof req.body.debug === 'boolean') {
        config.DEBUG = req.body.debug;
        console.log(`🔧 Runtime Config Change: DEBUG = ${config.DEBUG}`);
        return res.json({ success: true, debug: config.DEBUG });
    }
    res.status(400).json({ error: "Invalid parameter" });
});

// API to send shutdown command to agent (UPPERCASE)
app.post('/API/SHUTDOWN', (req, res) => {
    const clientId = req.authClient.id;
    const clientState = getOrCreateClientState(clientId);

    // Set a shutdown flag that will be picked up on next poll
    clientState.shutdownRequested = true;
    clientState.shutdownRequestedAt = new Date().toISOString();

    console.log(`🛑 Shutdown requested for client: ${clientId}`);
    res.json({ success: true, message: 'Shutdown command queued' });
});

// API to list available logs (UPPERCASE)
// SECURITY: Only returns logs belonging to the authenticated client
app.get('/API/LOGS', (req, res) => {
    try {
        // Get the client ID from the authenticated token
        const clientId = req.authClient.id;

        // Filter logs to show ONLY this client's sessions
        const files = fs.readdirSync(logsDir)
            .filter(f => f.endsWith('.json'))
            .filter(f => {
                // NEW: Check if filename starts with this client's ID
                // Format: {clientId}_{taskName}_{timestamp}.json
                return f.startsWith(clientId + '_');
            })
            .map(f => {
                const sessionName = f.replace('.json', '');
                const screenshotPath = path.join(screenshotsDir, sessionName);
                const hasScreenshots = fs.existsSync(screenshotPath);

                return {
                    name: f,
                    url: `/AGI/LOGS/${f}`,
                    screenshotsUrl: hasScreenshots ? `/AGI/SCREENSHOTS/${sessionName}/` : null,
                    time: fs.statSync(path.join(logsDir, f)).mtime
                };
            })
            .sort((a, b) => b.time - a.time); // Newest first
        res.json(files);
    } catch (e) {
        res.status(500).json({ error: e.message });
    }
});

// SECURITY: Protect direct access to logs and screenshots
// Only allow access to files belonging to the authenticated client
app.use('/AGI/LOGS/:filename', authenticate, (req, res, next) => {
    const clientId = req.authClient.id;
    const filename = req.params.filename;

    // Check if filename starts with this client's ID
    if (!filename.startsWith(clientId + '_')) {
        console.warn(`🔒 Blocked unauthorized log access: ${clientId} tried to access ${filename}`);
        return res.status(403).json({ error: 'Access denied: Not your log file' });
    }

    // Allow access to own files
    next();
});

app.use('/AGI/SCREENSHOTS/:sessionName/*', authenticate, (req, res, next) => {
    const clientId = req.authClient.id;
    const sessionName = req.params.sessionName;

    // Check if sessionName starts with this client's ID
    if (!sessionName.startsWith(clientId + '_')) {
        console.warn(`🔒 Blocked unauthorized screenshot access: ${clientId} tried to access ${sessionName}`);
        return res.status(403).json({ error: 'Access denied: Not your screenshot' });
    }

    // Allow access to own files
    next();
});

// Serve static files from public directory (Mission Control dashboard)
app.use(express.static(path.join(__dirname, '..', 'public')));

// --- Start Server ---

app.listen(config.PORT, config.HOST, () => {
    console.log("\n" + "=".repeat(50));
    console.log("🚀 Support Agent Server Starting (Node.js)...");
    console.log("=".repeat(50));
    console.log(`LLM Provider: ${config.LLM_PROVIDER}`);
    console.log(`Model: ${config.LLM_PROVIDER === 'claude' ? config.CLAUDE_MODEL : config.GEMINI_MODEL}`);
    console.log(`Server: http://${config.HOST}:${config.PORT}`);
    console.log(`🔒 Auth Enabled: 'x1' Token System`);
    console.log("=".repeat(50) + "\n");
});
