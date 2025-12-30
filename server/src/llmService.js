const { claudeClient, geminiClient } = require('./llm.provider');
const config = require('./config');
const fs = require('fs').promises;
const path = require('path');

class LLMService {
    constructor() {
        this.provider = config.LLM_PROVIDER.toLowerCase();
        this.claude = claudeClient;
        this.gemini = geminiClient;

        // Gemini models with fallback
        this.geminiPrimaryModel = config.GEMINI_MODEL || 'gemini-3-flash-preview';
        this.geminiFallbackModel = 'gemini-2.5-flash';

        if (this.provider === 'claude' && !this.claude) {
            throw new Error('Claude provider selected but CLAUDE_API_KEY is not configured');
        }
        if (this.provider === 'gemini' && !this.gemini) {
            throw new Error('Gemini provider selected but GEMINI_API_KEY is not configured');
        }
    }

    async _loadPlaybook(playbookName) {
        try {
            const playbookPath = path.join(__dirname, '..', 'playbooks', `${playbookName}.md`);
            const content = await fs.readFile(playbookPath, 'utf8');
            return content;
        } catch (error) {
            console.error(`❌ Failed to load playbook '${playbookName}': ${error.message}`);
            return null;
        }
    }

    async decideNextAction(uiState, task, history) {
        // Check if task is a playbook reference
        let effectiveTask = task;
        if (task.startsWith('playbook:')) {
            const playbookName = task.substring(9).trim(); // Remove 'playbook:' prefix
            const playbookContent = await this._loadPlaybook(playbookName);
            if (playbookContent) {
                effectiveTask = `Execute the following playbook:\n\n${playbookContent}`;
                console.log(`📖 Loaded playbook: ${playbookName}`);
            } else {
                effectiveTask = task; // Fallback to original task if playbook not found
            }
        }

        const screenshotBase64 = uiState.Screenshot || null;
        const prompt = this._buildPrompt(uiState, effectiveTask, history, screenshotBase64);

        if (this.provider === 'claude') {
            return await this._askClaude(prompt, screenshotBase64);
        } else if (this.provider === 'gemini') {
            return await this._askGemini(prompt, screenshotBase64);
        }
    }

    _buildPrompt(uiState, task, history, screenshotBase64 = null) {
        const elementsSummary = this._summarizeElements(uiState.Elements || []);
        const historyText = history && history.length > 0
            ? history.slice(-10).map((h, i) => `  ${i + 1}. ${h}`).join('\n')
            : '  (none)';

        const hasScreenshot = !!screenshotBase64;
        const visionMode = hasScreenshot ? 'VISUAL MODE (Image provided)' : 'TEXT-ONLY MODE (Economy)';

        // LOOP DETECTION: Analyze last actions for repeated patterns
        let loopWarning = '';
        if (history && history.length >= 3) {
            const lastActions = history.slice(-5); // Check last 5 actions

            // Extract action type (first word: click, type, key, etc.)
            const actionTypes = lastActions.map(h => {
                const match = h.match(/^(\w+)\s/);
                return match ? match[1] : '';
            });

            // Count consecutive identical action types
            const lastAction = actionTypes[actionTypes.length - 1];
            let consecutiveCount = 0;
            for (let i = actionTypes.length - 1; i >= 0; i--) {
                if (actionTypes[i] === lastAction) {
                    consecutiveCount++;
                } else {
                    break;
                }
            }

            // Count "NO CHANGE" markers in last actions
            const unchangedCount = lastActions.filter(h => h.includes('NO CHANGE')).length;

            if (consecutiveCount >= 3 || unchangedCount >= 3) {
                loopWarning = `

**🚨 CRITICAL WARNING: INFINITE LOOP DETECTED! 🚨**

SYSTEM ANALYSIS:
- Same action type repeated ${consecutiveCount} times in a row
- ${unchangedCount} of last ${lastActions.length} actions show "NO CHANGE"
- YOU ARE STUCK IN A LOOP!

**IMMEDIATE ACTION REQUIRED:**
1. STOP repeating the same action type (${lastAction})
2. This approach is NOT working - the UI is not responding as expected
3. Switch to a COMPLETELY DIFFERENT strategy:
   - If clicking failed -> try keyboard commands (Ctrl+A, Delete, etc.)
   - If typing failed -> request screenshot via inspect_screen
   - If element not found -> try coordinate-based click or different element
4. DO NOT click different element IDs if they all show "NO CHANGE" - the problem is not the element!

**YOU MUST CHANGE YOUR APPROACH NOW OR YOU WILL WASTE ALL 50 STEPS!**
`;
            }
        }

        return `You are a UI automation agent. Your task is to help complete the following objective:

**TASK**: ${task}

**CURRENT WINDOW**: ${uiState.WindowTitle || 'Unknown'}

**MODE**: ${visionMode}
${loopWarning}

**ACTION HISTORY** (last 10 actions):
${historyText}

**AVAILABLE UI ELEMENTS (Text Tree)**:
${elementsSummary}

${hasScreenshot ? '**SCREENSHOT**: Provided - analyze the image for visual context.' : '**SCREENSHOT**: Not provided (Saving bandwidth).'}

**YOUR JOB**:
Analyze the current UI state and determine the NEXT SINGLE ACTION to complete the task.

**SELF-HEALING LOGIC** (CRITICAL):
Look at the LAST action in the history above. Check if the UI state changed:
- Compare the state markers: [State: Title(N) -> NewTitle(M)]
- Check for [Content Modified] markers - this means text content changed (SUCCESS!)
- If state shows "NO CHANGE" - the action FAILED! DO NOT REPEAT IT!

**CRITICAL SELF-HEALING RULES:**

1. **Look at last 3 actions in history**
2. **If you see "NO CHANGE" 3+ times in a row for SAME action type (e.g., all "click"):**
   - STOP clicking immediately
   - This means clicks are NOT working
   - Switch to alternative: use keyboard commands (Ctrl+A + Delete), request screenshot, or try different approach

**ABSOLUTELY FORBIDDEN:**
- ❌ Repeating same action type >3 times when seeing "NO CHANGE"
- ❌ Clicking different element IDs when all show "NO CHANGE" (Notepad generates new IDs each time)
- ❌ Ignoring "NO CHANGE" warnings and continuing the same strategy
- ❌ Making more than 5 attempts with any single approach

**EXAMPLE OF BAD BEHAVIOR (DO NOT DO THIS):**
History shows:
- click btn_1 [NO CHANGE]
- click btn_2 [NO CHANGE]
- click btn_3 [NO CHANGE]
→ STOP clicking! Elements are not the problem. Try: Ctrl+A + Delete to clear, or request screenshot

**CORRECT BEHAVIOR:**
After 2-3 failed attempts with same action type:
1. STOP and analyze why it's failing
2. Try keyboard alternative (Ctrl+A, Delete, Ctrl+V, etc.)
3. If still stuck, request screenshot via inspect_screen
4. NEVER repeat the same failing pattern

When an action fails (NO CHANGE or FAILED marker):
1. **ANALYZE WHY**: The element might not exist, be disabled, or coordinates wrong
2. **TRY ALTERNATIVE METHOD**:
   - If element ID click failed -> try coordinate-based click using element's Bounds (X, Y)
   - If typing failed -> try selecting text first (Ctrl+A) or focusing element
   - If element not found -> request screenshot via inspect_screen for visual guidance
3. **DO NOT REPEAT** the exact same failed action - you will waste all 50 steps!

**CRITICAL WORKFLOW FOR TEXT WRITING TASKS**:
When your task involves writing text to a document/text field:
1. **FIRST**: Check current content by looking at the 'Value' field of text elements in UI tree
2. **IF CONTENT EXISTS**: Clear it COMPLETELY before writing (use Ctrl+A then Delete, or click and select all)
3. **WRITE**: Type your text
4. **VERIFY**: After writing, check the 'Value' field again to confirm EXACT match with target text
5. **FIX IF NEEDED**: If content doesn't match exactly, clear and rewrite

**GOAL**: The document should contain ONLY the text you wrote, nothing else. No old text, no duplicates.

**HUMAN ASSISTANCE** (New capability!):
You have a HUMAN OPERATOR sitting at the client machine who can help you!
Use {"action": "ask_user", "message": "your question or request"} when:

1. **CAPTCHA or 2FA**: You encounter a CAPTCHA, security challenge, or 2-factor authentication
   - Example: {"action": "ask_user", "message": "Please solve the CAPTCHA on screen and press Enter when done"}

2. **Missing Information**: You need information not available in the UI context
   - Example: {"action": "ask_user", "message": "What is the company name to enter in the form?"}
   - Example: {"action": "ask_user", "message": "What password should I use for login?"}

3. **Ambiguous Choices**: You're stuck between multiple valid approaches and need user decision
   - Example: {"action": "ask_user", "message": "Should I save the file as PDF or DOCX?"}

4. **Physical Actions**: Something requires physical interaction (inserting USB, pressing hardware button)
   - Example: {"action": "ask_user", "message": "Please insert the backup USB drive and press Enter"}

The user's response will appear in the next action history as "USER_SAID: [their response]".
Then you can continue with the task using the information they provided.

**INSTRUCTIONS**:
1. **PREFER TEXT TREE**: Try to solve the task using ONLY the Text Tree above. It is faster and cheaper.
2. **REQUEST VISION ONLY IF NEEDED**: If you strictly cannot find the element (e.g., custom UI, icons without text, complex visual state), you may request a screenshot.
3. **To request a screenshot**, return action: "inspect_screen" and set "text" parameter to quality level:
   - "20" for checking window layout/existence (Low quality, very cheap)
   - "50" for finding buttons/icons (Medium quality)
   - "70" for reading small text/captchas (High quality, expensive)

**OS / SYSTEM COMMANDS** (Fast file operations & process control):
For file management, log reading, and system tasks, use OS commands instead of UI automation - MUCH faster!

**Available Commands:**

1. **os_list**: List files/directories
   - Example: {"action": "os_list", "text": "C:\\\\Temp", "message": "Listing temp folder contents"}
   - Result appears as: OS_RESULT: [DIR] folder1\n[FILE] file.txt (25 KB)

2. **os_read**: Read text file content (max 2000 chars by default)
   - Example: {"action": "os_read", "text": "C:\\\\logs\\\\app.log", "message": "Reading application log"}
   - Optional: Set element_id to custom max chars: {"element_id": "5000"}
   - Result appears as: OS_RESULT: file content here...

3. **os_delete**: Delete file or directory (recursive)
   - Example: {"action": "os_delete", "text": "C:\\\\Temp\\\\cache", "message": "Clearing cache folder"}
   - Result: OS_RESULT: ✅ Deleted directory: C:\\\\Temp\\\\cache

4. **os_run**: Launch application or process
   - Example: {"action": "os_run", "text": "notepad.exe", "element_id": "C:\\\\file.txt", "message": "Opening file in Notepad"}
   - text = executable path, element_id = arguments (optional)
   - Result: OS_RESULT: ✅ Started process: notepad.exe (PID: 1234)

5. **os_kill**: Kill process by name
   - Example: {"action": "os_kill", "text": "notepad", "message": "Closing all Notepad instances"}
   - Result: OS_RESULT: ✅ Killed 2 process(es) named 'notepad'

6. **os_mkdir**: Create directory
   - Example: {"action": "os_mkdir", "text": "C:\\\\Projects\\\\NewFolder", "message": "Creating project folder"}

7. **os_write**: Write text to file (overwrites)
   - Example: {"action": "os_write", "text": "C:\\\\output.txt", "element_id": "Hello World", "message": "Writing output"}
   - text = file path, element_id = content to write

8. **os_exists**: Check if file/directory exists
   - Example: {"action": "os_exists", "text": "C:\\\\file.txt", "message": "Checking if file exists"}
   - Result: OS_RESULT: EXISTS: File - file.txt (25 KB) OR NOT FOUND

**When to use OS commands:**
- ✅ Clearing folders (os_delete faster than clicking in Explorer)
- ✅ Reading log files (os_read instead of opening in Notepad)
- ✅ Launching applications (os_run instead of Start Menu navigation)
- ✅ Checking if files exist before operations (os_exists)
- ✅ Creating folder structures (os_mkdir)
- ❌ Don't use for interactive tasks requiring UI (use regular automation)

**Error Handling:**
All OS commands return results prefixed with "ERROR:" if they fail:
- OS_RESULT: ERROR: Access denied: C:\\\\System
- OS_RESULT: ERROR: File not found: C:\\\\missing.txt

**IT SUPPORT TOOLKIT** (Registry, Network & Environment):
Advanced diagnostics and configuration tools for IT support tasks.

**Environment Variables:**
1. **os_getenv**: Read environment variable
   - Example: {"action": "os_getenv", "text": "PATH", "message": "Checking PATH variable"}
   - Result: OS_RESULT: PATH = C:\\\\Windows\\\\system32;...

**Windows Registry Operations:**
2. **reg_read**: Read registry value
   - Example: {"action": "reg_read", "text": "HKLM\\\\Software\\\\Microsoft\\\\Windows\\\\CurrentVersion", "element_id": "ProgramFilesDir", "message": "Checking Program Files location"}
   - Format: text = "ROOT\\\\KeyPath", element_id = "ValueName"
   - Roots: HKLM, HKCU, HKCR, HKU, HKCC
   - Result: OS_RESULT: ✅ HKLM\\\\Software\\\\...\\\\ProgramFilesDir = C:\\\\Program Files

3. **reg_write**: Write registry value (requires Admin for HKLM)
   - Example: {"action": "reg_write", "text": "HKCU\\\\Software\\\\MyApp", "element_id": "Setting", "x": 1, "message": "Updating app setting"}
   - Format: text = "ROOT\\\\KeyPath", element_id = "ValueName", x = value
   - WARNING: HKLM writes require Administrator privileges
   - Result: OS_RESULT: ✅ Set HKCU\\\\Software\\\\MyApp\\\\Setting = 1

**Network Diagnostics:**
4. **net_ping**: Ping a host to check connectivity
   - Example: {"action": "net_ping", "text": "google.com", "message": "Checking internet connectivity"}
   - Optional: x = timeout in ms (default 2000)
   - Result: OS_RESULT: ✅ Ping successful: google.com (142.250.185.78) - 15ms

5. **net_port**: Check if TCP port is open
   - Example: {"action": "net_port", "text": "localhost", "x": 3000, "message": "Checking if web server is running"}
   - Format: text = host, x = port number
   - Optional: y = timeout in ms (default 2000)
   - Result: OS_RESULT: ✅ Port 3000 is OPEN on localhost

**IT Support Use Cases:**
- ✅ Check software versions via registry (e.g., HKLM\\\\Software\\\\...)
- ✅ Read configuration from environment variables (PATH, JAVA_HOME, etc.)
- ✅ Diagnose network connectivity issues (ping servers, check ports)
- ✅ Verify services are running (check ports: 80, 443, 3306, etc.)
- ✅ Troubleshoot application settings in HKCU registry
- ⚠️  Registry writes require caution - can affect system stability

**CLIPBOARD OPERATIONS** (Extract hard-to-read text):
You can READ and WRITE clipboard content directly!

**When to use:**
- Text is not accessible via UI Automation tree (custom rendered text, images with text)
- Need to extract selected text: Select element -> Ctrl+C -> read_clipboard
- Need to paste pre-formatted text: write_clipboard -> Ctrl+V

**Commands:**
1. **read_clipboard**: Reads current clipboard content
   - Example: {"action": "read_clipboard", "message": "Reading clipboard to extract copied text"}
   - The clipboard content will appear in next history as: CLIPBOARD_CONTENT: "text here"

2. **write_clipboard**: Writes text to clipboard (without pasting)
   - Example: {"action": "write_clipboard", "text": "your text", "message": "Setting clipboard for paste"}
   - Then use: {"action": "key", "text": "Ctrl+V"} to paste

**Common pattern for stubborn UI elements:**
1. Click element or use Ctrl+A to select
2. {"action": "key", "text": "Ctrl+C"} - Copy to clipboard
3. {"action": "read_clipboard"} - Extract the text
4. Use extracted text for verification or processing

**RESPONSE FORMAT** (JSON only):
{
    "action": "click|type|key|select|wait|download|inspect_screen|ask_user|read_clipboard|write_clipboard|os_list|os_read|os_delete|os_run|os_kill|os_mkdir|os_write|os_exists|os_getenv|reg_read|reg_write|net_ping|net_port",
    "element_id": "element_automation_id (OPTIONAL for coordinate clicks)",
    "x": "X coordinate (OPTIONAL for coordinate-based click)",
    "y": "Y coordinate (OPTIONAL for coordinate-based click)",
    "text": "text to type OR key command OR quality level OR clipboard text",
    "url": "download URL (only for 'download' action)",
    "local_file_name": "filename to save (only for 'download' action)",
    "message": "explanation of what you're doing",
    "task_completed": true|false,
    "reasoning": "why you chose this action. If requesting screen, explain why text tree failed."
}

**COORDINATE-BASED CLICKS** (Fallback method):
If you cannot find element by ID or element click keeps failing:
- Look at element's Bounds in the UI tree: "Bounds: {X: 100, Y: 200, Width: 50, Height: 30}"
- Calculate center point: center_x = X + Width/2, center_y = Y + Height/2
- Use: {"action": "click", "x": center_x, "y": center_y, "element_id": ""}
- Example: {"action": "click", "x": 125, "y": 215, "message": "Clicking button by coordinates"}

**KEY COMMANDS** (for action: "key"):
- "Ctrl+A" - Select all text
- "Delete" - Delete selected/next character
- "Backspace" - Delete previous character
- "Enter" - Press Enter
- "Ctrl+C" / "Ctrl+V" / "Ctrl+X" - Copy/Paste/Cut

**EXAMPLE WORKFLOW** to clear and write text:
Step 1: {"action": "key", "text": "Ctrl+A", "message": "Selecting all existing text"}
Step 2: {"action": "key", "text": "Delete", "message": "Clearing document"}
Step 3: {"action": "type", "element_id": "doc_id", "text": "Your text here", "message": "Writing new text"}
Step 4: Check Value field to verify exact match

**RULES**:
1. Return ONLY ONE action at a time
2. If task is complete, set task_completed=true and action=""
3. Be precise - use exact element IDs from the list above
4. If element not found in text tree AND no screenshot available, request screenshot via inspect_screen
5. Think step by step

Respond with JSON only, no additional text.`;
    }

    _summarizeElements(elements) {
        if (!elements || elements.length === 0) return '  No UI elements found';

        let summaryLines = [];
        const limit = 30;

        for (let i = 0; i < Math.min(elements.length, limit); i++) {
            const elem = elements[i];
            const status = elem.IsEnabled ? '✓' : '✗';
            const valueText = elem.Value ? ` = '${elem.Value}'` : '';

            // Добавляем координаты для coordinate-based clicks
            const bounds = elem.Bounds || {};
            const centerX = bounds.X && bounds.Width ? Math.round(bounds.X + bounds.Width / 2) : 0;
            const centerY = bounds.Y && bounds.Height ? Math.round(bounds.Y + bounds.Height / 2) : 0;
            const coordsText = centerX > 0 && centerY > 0 ? ` @(${centerX},${centerY})` : '';

            summaryLines.push(
                `  [${status}] ${elem.Type}: '${elem.Name}' (id: ${elem.Id})${valueText}${coordsText}`
            );
        }

        if (elements.length > limit) {
            summaryLines.push(`  ... and ${elements.length - limit} more elements`);
        }

        return summaryLines.join('\n');
    }

    async _askClaude(prompt, screenshotBase64 = null) {
        try {
            const content = screenshotBase64
                ? [
                    { type: 'text', text: prompt },
                    {
                        type: 'image',
                        source: {
                            type: 'base64',
                            media_type: 'image/jpeg',
                            data: screenshotBase64
                        }
                    }
                ]
                : prompt;

            const response = await this.claude.messages.create({
                model: config.CLAUDE_MODEL,
                max_tokens: 1024,
                temperature: config.TEMPERATURE,
                messages: [{ role: 'user', content }]
            });

            const text = response.content[0].text;
            return this._parseJsonResponse(text);
        } catch (e) {
            console.error(`❌ Claude API error: ${e.message}`);
            return { error: e.message, task_completed: false };
        }
    }

    async _askGemini(prompt, screenshotBase64 = null) {
        const models = [
            { name: this.geminiPrimaryModel, temperature: config.TEMPERATURE },
            { name: this.geminiFallbackModel, temperature: config.TEMPERATURE }
        ];

        for (const [index, modelConfig] of models.entries()) {
            try {
                // Build parts array - text first, then image if provided
                const parts = [{ text: prompt }];
                if (screenshotBase64) {
                    parts.push({
                        inlineData: {
                            mimeType: 'image/jpeg',
                            data: screenshotBase64
                        }
                    });
                }

                const result = await this.gemini.models.generateContent({
                    model: modelConfig.name,
                    contents: [{ role: 'user', parts }],
                    config: {
                        systemInstruction: [
                            "You are a UI automation agent.",
                            "Always respond with valid JSON only, no additional text.",
                            "Do not include markdown code blocks or any formatting - return raw JSON."
                        ],
                        temperature: modelConfig.temperature,
                        maxOutputTokens: 2048
                    }
                });

                if (!result.candidates || result.candidates.length === 0) {
                    throw new Error('No candidates in response');
                }

                const content = result.candidates[0].content;
                const text = content.parts
                    .filter(part => part.text)
                    .map(part => part.text)
                    .join('');

                return this._parseJsonResponse(text);

            } catch (e) {
                console.error(`❌ Gemini model ${modelConfig.name} failed: ${e.message}`);

                // If last model, return error
                if (index === models.length - 1) {
                    return { error: e.message, task_completed: false };
                }
                // Otherwise, try next model
                console.log(`🔄 Trying fallback model: ${models[index + 1].name}`);
            }
        }

        return { error: 'All Gemini models failed', task_completed: false };
    }

    _parseJsonResponse(text) {
        try {
            text = text.trim();
            // Remove markdown code blocks if present
            if (text.startsWith('```')) {
                 const firstLineBreak = text.indexOf('\n');
                 if (firstLineBreak !== -1) {
                     // Remove first line (```json)
                     text = text.substring(firstLineBreak + 1);
                     // Remove last line (```)
                     const lastBackticks = text.lastIndexOf('```');
                     if (lastBackticks !== -1) {
                         text = text.substring(0, lastBackticks);
                     }
                 }
            }
            return JSON.parse(text);
        } catch (e) {
            // Try to find JSON object within text
            const start = text.indexOf('{');
            const end = text.lastIndexOf('}');
            if (start >= 0 && end > start) {
                try {
                    return JSON.parse(text.substring(start, end + 1));
                } catch (inner) {}
            }
            console.error('Failed to parse JSON:', text.substring(0, 200));
            throw new Error('Could not parse JSON response from LLM');
        }
    }
}

module.exports = new LLMService();
