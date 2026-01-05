const { claudeClient, geminiClient } = require('./llm.provider');
const config = require('./config');
const fs = require('fs');
const fsPromises = require('fs').promises;
const path = require('path');

// Constants
const DEFAULTS = {
    GEMINI_PRIMARY: 'gemini-3-flash-preview',
    GEMINI_FALLBACK: 'gemini-2.5-flash',
    MAX_ELEMENTS: 100,
    MAX_TOKENS: 1024,
    GEMINI_RETRY_ATTEMPTS: 2
};

class LLMService {
    constructor() {
        this.provider = config.LLM_PROVIDER.toLowerCase();
        this.claude = claudeClient;
        this.gemini = geminiClient;

        this.geminiPrimaryModel = config.GEMINI_MODEL || DEFAULTS.GEMINI_PRIMARY;
        this.geminiFallbackModel = DEFAULTS.GEMINI_FALLBACK;

        if (this.provider === 'claude' && !this.claude) {
            throw new Error('Claude provider selected but CLAUDE_API_KEY is not configured');
        }
        if (this.provider === 'gemini' && !this.gemini) {
            throw new Error('Gemini provider selected but GEMINI_API_KEY is not configured');
        }
    }

    // Security: Sanitize playbook names to prevent path traversal
    _sanitizePlaybookName(name) {
        return name.replace(/[^a-zA-Z0-9_-]/g, '').substring(0, 64);
    }

    async _loadPlaybook(playbookName) {
        try {
            const safeName = this._sanitizePlaybookName(playbookName);
            const playbookPath = path.join(__dirname, '..', 'playbooks', `${safeName}.md`);
            const content = await fsPromises.readFile(playbookPath, 'utf8');
            return content;
        } catch (error) {
            console.error(`❌ Failed to load playbook '${playbookName}': ${error.message}`);
            return null;
        }
    }

    async _savePlaybook(filename, content) {
        try {
            const safeName = this._sanitizePlaybookName(filename);
            if (!safeName) return 'ERROR: Invalid playbook name';
            const playbookPath = path.join(__dirname, '..', 'playbooks', `${safeName}.md`);
            await fsPromises.writeFile(playbookPath, content, 'utf8');
            return `✅ Playbook saved successfully: ${safeName}.md`;
        } catch (error) {
            return `ERROR: Failed to save playbook - ${error.message}`;
        }
    }

    async _performWebSearch(query) {
        try {
            console.log(`🔍 Performing web search: "${query}"`);
            if (!config.GOOGLE_SEARCH_API_KEY || !config.GOOGLE_SEARCH_CX) {
                return 'WEB_SEARCH_RESULT: ERROR - Google Search API Key/CX not configured.';
            }
            const url = `https://www.googleapis.com/customsearch/v1?key=${config.GOOGLE_SEARCH_API_KEY}&cx=${config.GOOGLE_SEARCH_CX}&q=${encodeURIComponent(query)}`;
            const response = await fetch(url);
            if (!response.ok) throw new Error(`Google API Error: ${response.status}`);
            const data = await response.json();
            if (!data.items || data.items.length === 0) return 'WEB_SEARCH_RESULT: No results found.';

            const topResults = data.items.slice(0, 5);
            let formattedResults = `WEB_SEARCH_RESULT for "${query}":\n\n`;
            topResults.forEach((result, index) => {
                formattedResults += `${index + 1}. ${result.title}\n   URL: ${result.link}\n   ${(result.snippet || '').replace(/\n/g, ' ')}\n\n`;
            });
            return formattedResults.trim();
        } catch (error) {
            return `WEB_SEARCH_RESULT: ERROR - ${error.message}`;
        }
    }

    async decideNextAction(uiState, task, history) {
        // Playbook Expansion
        let effectiveTask = task;
        if (task.startsWith('playbook:')) {
            const playbookName = task.substring(9).trim();
            const playbookContent = await this._loadPlaybook(playbookName);
            if (playbookContent) {
                effectiveTask = `Execute the following playbook:\n\n${playbookContent}`;
            }
        }

        const screenshotBase64 = uiState.Screenshot || null;
        const prompt = this._buildPrompt(uiState, effectiveTask, history, screenshotBase64);

        // DEBUG: Save the exact prompt we are sending to LLM (async, non-blocking)
        const debugPath = path.join(__dirname, '..', 'public', 'LOGS', 'last_prompt.txt');
        fsPromises.writeFile(debugPath, prompt).catch(() => {})

        let response;
        if (this.provider === 'claude') {
            response = await this._askClaude(prompt, screenshotBase64);
        } else {
            response = await this._askGemini(prompt, screenshotBase64);
        }

        // --- LOGIC GUARDRAIL: Prevent Premature Completion ---
        if (response && response.task_completed === true) {
            const done = response.steps_done || 0;
            const total = response.steps_total || 0;

            // Only enforce if total is explicitly stated and greater than done
            if (total > 0 && done < total) {
                console.warn(`🛡️ GUARDRAIL ACTIVE: Blocked premature completion. Steps: ${done}/${total}`);

                response.task_completed = false;

                // If the model tried to stop without an action, force a 'wait' so it gets a chance to think again
                if (!response.action) {
                    response.action = "wait";
                    response.text = "1000"; // wait 1s
                }

                // Inject override message to guide the model on the next turn
                response.message = `SYSTEM OVERRIDE: You marked task as complete, but you only finished ${done} of ${total} steps. You MUST continue with step ${done + 1}.`;

                // Update reasoning to reflect the intervention
                response.reasoning = `[SYSTEM GUARDRAIL] Prevented premature exit. ${response.reasoning}`;
            }
        }
        // -----------------------------------------------------

        // Server-Side Actions (Search / Playbook)
        if (response && response.action === 'net_search') {
            const query = response.text || '';
            const searchResults = await this._performWebSearch(query);
            const updatedHistory = [...history, `net_search: "${query}"`, searchResults];
            return await this.decideNextAction(uiState, effectiveTask, updatedHistory);
        }

        if (response && response.action === 'create_playbook') {
            const filename = response.text || 'untitled';
            const content = response.element_id || '';
            const saveResult = await this._savePlaybook(filename, content);
            const updatedHistory = [...history, `create_playbook: "${filename}"`, saveResult];
            return await this.decideNextAction(uiState, effectiveTask, updatedHistory);
        }

        return response;
    }

    _buildPrompt(uiState, task, history, screenshotBase64 = null) {
        const elementsSummary = this._summarizeElements(uiState.Elements || []);

        // INFINITE MEMORY: Send full history with numbering
        const historyText = history && history.length > 0
            ? history.map((h, i) => `Step ${i + 1}: ${h}`).join('\n')
            : '(No actions taken yet)';

        // Context Injection (OS_RESULT)
        let lastSystemResult = "None";
        if (history) {
            for (let i = history.length - 1; i >= 0; i--) {
                const entry = history[i];
                if (entry.includes("OS_RESULT:") || entry.includes("CLIPBOARD_CONTENT:") || entry.includes("WEB_SEARCH_RESULT")) {
                    lastSystemResult = entry;
                    break;
                }
            }
        }

        const visionMode = screenshotBase64 ? 'VISUAL MODE (Image provided)' : 'TEXT-ONLY MODE (Economy)';

        return `You are a UI automation agent.

**TASK**: ${task}

**BEFORE EACH ACTION**:
- Parse the task into distinct steps. IMPORTANT: "A and B" or "A, then B" = TWO steps!
  Example: "Open Calculator, calculate 5+3, then open Notepad and type result" = 4 steps:
  (1) Open Calculator, (2) Calculate 5+3, (3) Open Notepad, (4) Type result
- Check your history to see which steps are DONE
- If ANY step is NOT done, continue working - do NOT set task_completed to true
- ONLY set task_completed:true when EVERY SINGLE step is completed and verified in history

**🧠 COGNITIVE CHECK (REQUIRED)**
Look at the **SYSTEM MEMORY** below.
1. Does it contain the data you need? (ping result, file content, etc.)
2. **YES**: USE IT! Do not re-run the command.
3. **NO**: Only then run the command.

**CURRENT WINDOW**: ${uiState.WindowTitle || 'Unknown'}
**MODE**: ${visionMode}

**📢 SYSTEM MEMORY (LAST RESULT) 📢**
${lastSystemResult}

**📜 FULL ACTION HISTORY**:
${historyText}

**AVAILABLE UI ELEMENTS (Filtered for relevance)**:
${elementsSummary}

**INSTRUCTIONS**:
1. Analyze the UI Elements and History.
2. Determine the NEXT SINGLE ACTION.
3. If you see "NO CHANGE" in history multiple times, switch strategy (e.g. use keyboard instead of click).
4. If the element is not in the list, use "inspect_screen" to see it. BUT: avoid using "inspect_screen" repeatedly - if you inspected 2-3 times without finding the element, try a different approach (keyboard, switch_window, etc.).
5. **CRITICAL**: If you just used "os_run" to launch an app (e.g. calc, notepad), IMMEDIATELY use "switch_window" in the NEXT step to focus that app. DO NOT wait or inspect - switch first!
6. When typing into apps like Calculator or Notepad, use the "key" action (it supports any text, numbers, symbols).
7. **TEXT INPUT MODES** (for "type" action):
   - Default: REPLACE mode - clears field and types new text
   - "APPEND:text" - adds text to END (no clearing)
   - "PREPEND:text" - adds text to BEGINNING (no clearing)
   - "REPLACE:text" - explicitly clear and replace
   Example: If Notepad has "OLD", use "type" with "REPLACE:NEW" to get "NEW", or "APPEND: ADDED" to get "OLD ADDED"
8. **TASK COMPLETION CHECK**: ONLY set "task_completed": true when you have verified that EVERY part of the task is done. For example: "Open Calculator, calculate 5+3, then open Notepad and type result" requires FOUR steps: (1) open calc, (2) calculate, (3) open notepad, (4) type result. Do NOT mark complete until step 4 is done!

**RESPONSE FORMAT (JSON ONLY)**:
{
    "steps_total": number (how many steps needed for COMPLETE task),
    "steps_done": number (how many steps already completed in history),
    "current_step": "Step X: description of what you're doing now",
    "action": "click|type|key|select|wait|download|inspect_screen|ask_user|read_clipboard|write_clipboard|os_list|os_read|os_delete|os_run|os_kill|os_mkdir|os_write|os_exists|os_getenv|reg_read|reg_write|net_ping|net_port|net_search|switch_window",
    "element_id": "...",
    "text": "...",
    "reasoning": "Why this action for THIS SPECIFIC STEP",
    "task_completed": boolean (ONLY true if steps_done === steps_total)
}

**CRITICAL - TASK COMPLETION TIMING**:
- steps_done = number of steps ALREADY in history (completed and verified)
- Do NOT count the current action you are about to take
- Example: If you are typing "100" (final step 4), but it's not in history yet:
  → steps_done = 3 (previous steps)
  → task_completed = false (because 3 ≠ 4)
- On the NEXT turn, after seeing your action in history:
  → steps_done = 4 (now includes the typing action)
  → task_completed = true (because 4 === 4)
- NEVER mark task_completed=true in the same response where you execute the final action`;
    }

    _summarizeElements(elements) {
        if (!elements || elements.length === 0) return '  (No UI elements found)';

        let summaryLines = [];
        const limit = DEFAULTS.MAX_ELEMENTS;

        let validCount = 0;
        for (const elem of elements) {
            if (validCount >= limit) break;

            // CLEANING LOGIC: Skip useless elements
            // 1. Skip elements with no name AND no value (unless they are inputs/buttons that might be unnamed)
            const hasName = elem.Name && elem.Name.trim().length > 0;
            const hasValue = elem.Value && elem.Value.trim().length > 0;
            const isInteractive = elem.Type.includes("Button") || elem.Type.includes("Edit") || elem.Type.includes("Item");

            if (!hasName && !hasValue && !isInteractive) {
                continue; // Skip noise
            }

            // 2. Format concisely
            let line = `  [${elem.Type}]`;
            if (hasName) line += ` "${elem.Name}"`;
            if (hasValue) line += ` Val="${elem.Value}"`;
            line += ` (ID:${elem.Id})`;

            // Add coordinates for fallback clicking
            if (elem.Bounds && elem.Bounds.Width > 0) {
                const cx = Math.round(elem.Bounds.X + elem.Bounds.Width/2);
                const cy = Math.round(elem.Bounds.Y + elem.Bounds.Height/2);
                line += ` @(${cx},${cy})`;
            }

            summaryLines.push(line);
            validCount++;
        }

        if (elements.length > validCount) {
            summaryLines.push(`  ... (+${elements.length - validCount} hidden/empty elements)`);
        }

        return summaryLines.join('\n');
    }

    async _askClaude(prompt, screenshotBase64 = null) {
        try {
            const content = screenshotBase64
                ? [{ type: 'text', text: prompt }, { type: 'image', source: { type: 'base64', media_type: 'image/jpeg', data: screenshotBase64 } }]
                : prompt;

            const response = await this.claude.messages.create({
                model: config.CLAUDE_MODEL,
                max_tokens: DEFAULTS.MAX_TOKENS,
                temperature: config.TEMPERATURE,
                messages: [{ role: 'user', content }]
            });
            return this._parseJsonResponse(response.content[0].text);
        } catch (e) {
            return { error: e.message, task_completed: false };
        }
    }

    async _askGemini(prompt, screenshotBase64 = null) {
        const contents = [{ text: prompt }];
        if (screenshotBase64) {
            contents.push({ inlineData: { mimeType: 'image/jpeg', data: screenshotBase64 } });
        }

        // Retry loop for primary model
        for (let attempt = 0; attempt < DEFAULTS.GEMINI_RETRY_ATTEMPTS; attempt++) {
            try {
                const result = await this.gemini.models.generateContent({
                    model: this.geminiPrimaryModel,
                    contents: contents,
                    config: { responseMimeType: "application/json" }
                });
                return JSON.parse(result.text);
            } catch (e) {
                console.error(`Gemini Error (attempt ${attempt + 1}):`, e.message);
                if (attempt < DEFAULTS.GEMINI_RETRY_ATTEMPTS - 1) {
                    await new Promise(r => setTimeout(r, 1000 * (attempt + 1))); // Exponential backoff
                    continue;
                }
            }
        }

        // Fallback to secondary model (text-only for safety)
        try {
            console.log(`⚠️ Falling back to ${this.geminiFallbackModel}`);
            const fallback = await this.gemini.models.generateContent({
                model: this.geminiFallbackModel,
                contents: [{ text: prompt }]
            });
            return this._parseJsonResponse(fallback.text);
        } catch (err) {
            return { error: err.message, task_completed: false };
        }
    }

    _parseJsonResponse(text) {
        try {
            text = text.replace(/```json/g, '').replace(/```/g, '').trim();
            return JSON.parse(text);
        } catch (e) {
            console.error("JSON Parse Error on:", text.substring(0, 100));
            return { error: "Invalid JSON response from LLM", task_completed: false };
        }
    }

    // --- NEW: AUTOMATED LEARNING ---
    // Analyzes successful session history and creates a reusable Markdown playbook
    async learnPlaybook(task, history) {
        if (!history || history.length < 3) {
            console.log("⚠️ History too short to learn from.");
            return;
        }

        console.log(`🎓 Learning from session: "${task}"...`);

        const prompt = `You are a Process Analyst AI.
Analyze the following successful execution history and convert it into a generalized Markdown Playbook.

**ORIGINAL TASK**: ${task}

**EXECUTION HISTORY**:
${history.map((h, i) => `${i + 1}. ${h}`).join('\n')}

**INSTRUCTIONS**:
1. Identify the high-level steps taken.
2. Abstract specific values (like filenames, URLs, text) into Variables.
3. Output ONLY valid Markdown content.

**OUTPUT FORMAT (Markdown)**:
# Playbook: [Descriptive Title]

## Goal
[Brief description]

## Variables
- \`VarName\`: "DetectedValue"

## Steps
1. **[Step Name]**
   - Action: [Action Description]
   - Target: {\`VarName\`} (if applicable)
   - Note: [Any important details]

**IMPORTANT**: Return ONLY the Markdown content. Do not include "Here is the playbook" or code blocks.`;

        let playbookContent = "";
        try {
            if (this.provider === 'claude') {
                const response = await this.claude.messages.create({
                    model: config.CLAUDE_MODEL,
                    max_tokens: 2048,
                    messages: [{ role: 'user', content: prompt }]
                });
                playbookContent = response.content[0].text;
            } else {
                const result = await this.gemini.models.generateContent({
                    model: this.geminiPrimaryModel,
                    contents: [{ text: prompt }]
                });
                playbookContent = result.response.text();
            }

            // Cleanup code blocks if present
            playbookContent = playbookContent.replace(/^```markdown\n/, '').replace(/^```\n/, '').replace(/\n```$/, '');

            // Generate filename from task
            const safeName = "learned_" + task.slice(0, 30).replace(/[^a-zA-Z0-9]/g, '_').toLowerCase();
            await this._savePlaybook(safeName, playbookContent);

        } catch (e) {
            console.error("❌ Learning Failed:", e.message);
        }
    }
}

module.exports = new LLMService();
