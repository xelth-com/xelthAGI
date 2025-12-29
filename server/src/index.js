const express = require('express');
const cors = require('cors');
const { z } = require('zod');
const config = require('./config');
const llmService = require('./llmService');

const app = express();

app.use(express.json({ limit: '50mb' }));
app.use(cors());

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
    Screenshot: z.string().optional().default("")
});

const ServerRequestSchema = z.object({
    ClientId: z.string().optional().default("unknown"),
    State: UIStateSchema,
    Task: z.string(),
    History: z.array(z.string()).optional().default([])
});

// --- Endpoints ---

app.get('/health', (req, res) => {
    res.json({
        status: "healthy",
        llm_provider: config.LLM_PROVIDER,
        model: config.LLM_PROVIDER === 'claude' ? config.CLAUDE_MODEL : config.GEMINI_MODEL,
        server: "Node.js Express"
    });
});

app.post('/decide', async (req, res) => {
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

        // Log Client ID
        console.log(`\n👤 Client: ${request.ClientId} | Task: ${request.Task}`);

        // 2. LLM Processing
        // Convert Zod object to plain dict logic for LLM service
        const uiStateDict = {
            WindowTitle: request.State.WindowTitle,
            ProcessName: request.State.ProcessName,
            Elements: request.State.Elements
        };

        const decision = await llmService.decideNextAction(
            uiStateDict,
            request.Task,
            request.History
        );

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

        console.log(`🤖 Decision: ${decision.reasoning || 'No reasoning provided'}`);
        console.log(`📤 Command: ${command.Action} on ${command.ElementId}`);

        return res.json({
            Command: command,
            Success: true,
            TaskCompleted: false
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
