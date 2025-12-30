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

        return `You are a UI automation agent. Your task is to help complete the following objective:

**TASK**: ${task}

**CURRENT WINDOW**: ${uiState.WindowTitle || 'Unknown'}

**MODE**: ${visionMode}

**ACTION HISTORY** (last 10 actions):
${historyText}

**AVAILABLE UI ELEMENTS (Text Tree)**:
${elementsSummary}

${hasScreenshot ? '**SCREENSHOT**: Provided - analyze the image for visual context.' : '**SCREENSHOT**: Not provided (Saving bandwidth).'}

**YOUR JOB**:
Analyze the current UI state and determine the NEXT SINGLE ACTION to complete the task.

**CRITICAL WORKFLOW FOR TEXT WRITING TASKS**:
When your task involves writing text to a document/text field:
1. **FIRST**: Check current content by looking at the 'Value' field of text elements in UI tree
2. **IF CONTENT EXISTS**: Clear it COMPLETELY before writing (use Ctrl+A then Delete, or click and select all)
3. **WRITE**: Type your text
4. **VERIFY**: After writing, check the 'Value' field again to confirm EXACT match with target text
5. **FIX IF NEEDED**: If content doesn't match exactly, clear and rewrite

**GOAL**: The document should contain ONLY the text you wrote, nothing else. No old text, no duplicates.

**INSTRUCTIONS**:
1. **PREFER TEXT TREE**: Try to solve the task using ONLY the Text Tree above. It is faster and cheaper.
2. **REQUEST VISION ONLY IF NEEDED**: If you strictly cannot find the element (e.g., custom UI, icons without text, complex visual state), you may request a screenshot.
3. **To request a screenshot**, return action: "inspect_screen" and set "text" parameter to quality level:
   - "20" for checking window layout/existence (Low quality, very cheap)
   - "50" for finding buttons/icons (Medium quality)
   - "70" for reading small text/captchas (High quality, expensive)

**RESPONSE FORMAT** (JSON only):
{
    "action": "click|type|key|select|wait|download|inspect_screen",
    "element_id": "element_automation_id (or blank for key/inspect_screen)",
    "text": "text to type OR key command OR quality level",
    "url": "download URL (only for 'download' action)",
    "local_file_name": "filename to save (only for 'download' action)",
    "message": "explanation of what you're doing",
    "task_completed": true|false,
    "reasoning": "why you chose this action. If requesting screen, explain why text tree failed."
}

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

            summaryLines.push(
                `  [${status}] ${elem.Type}: '${elem.Name}' (id: ${elem.Id})${valueText}`
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
