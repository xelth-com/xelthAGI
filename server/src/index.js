const express = require('express');
const cors = require('cors');
const path = require('path');
const { z } = require('zod');
const config = require('./config');
const llmService = require('./llmService');

const app = express();

app.use(express.json({ limit: '50mb' }));
app.use(cors());

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

// Global state storage for Mission Control dashboard
let globalState = {
    lastSeen: null,
    clientId: 'unknown',
    sessionName: '', // Added for frontend URL construction
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
    screenshot: null, // Agent's cropped vision
    isOnline: false,
    totalActions: 0
};

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

        // Update global state for Mission Control
        globalState.lastSeen = new Date().toISOString();
        globalState.clientId = request.ClientId;
        globalState.task = request.Task;
        globalState.history = request.History || [];
        // Keep previous screenshot if new one is empty
        if (request.State.Screenshot) {
            globalState.screenshot = request.State.Screenshot;
        }
        globalState.isOnline = true;
        globalState.totalActions = request.History ? request.History.length : 0;

        // Log Client ID
        console.log(`\n👤 Client: ${request.ClientId} | Task: ${request.Task}`);

        // 2. LLM Processing
        // Convert Zod object to plain dict logic for LLM service
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

        // Update global state with UI state and decision
        globalState.uiState = uiStateDict;
        globalState.lastDecision = {
            action: decision.action || '',
            message: decision.message || '',
            reasoning: decision.reasoning || ''
        };

        // Generate consistent Session Name for grouping logs and screenshots
        const shortId = request.ClientId.substring(0, 6);
        const safeTaskName = request.Task
            .replace(/[^a-zA-Z0-9]/g, '_')
            .replace(/_+/g, '_')
            .substring(0, 50);
        const dateStr = new Date().toISOString().slice(0, 10);
        const sessionName = `${safeTaskName}_${shortId}_${dateStr}`;

        // Update global state
        globalState.sessionName = sessionName;

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
            X: 0, Y: 0,
            DelayMs: decision.delay_ms || 100,
            Message: decision.message || ""
        };

        const reasoning = decision.reasoning || "No reasoning provided";
        console.log(`🤖 Decision: ${reasoning}`);
        console.log(`📤 Command: ${command.Action} on ${command.ElementId}`);

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
            Reasoning: reasoning
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
app.get('/API/STATE', (req, res) => {
    // Mark as offline if last seen more than 30 seconds ago
    if (globalState.lastSeen) {
        const timeSinceLastSeen = Date.now() - new Date(globalState.lastSeen).getTime();
        globalState.isOnline = timeSinceLastSeen < 30000; // 30 seconds
    }

    // Inject current server debug status
    const responseWithConfig = {
        ...globalState,
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

// API to list available logs (UPPERCASE)
app.get('/API/LOGS', (req, res) => {
    try {
        const files = fs.readdirSync(logsDir)
            .filter(f => f.endsWith('.json'))
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
    console.log("=".repeat(50) + "\n");
});
