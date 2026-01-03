const { claudeClient, geminiClient } = require('./llm.provider');
const config = require('./config');
const fs = require('fs');
const fsPromises = require('fs').promises;
const path = require('path');

class LLMService {
    constructor() {
        this.provider = config.LLM_PROVIDER.toLowerCase();
        this.claude = claudeClient;
        this.gemini = geminiClient;

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
            const content = await fsPromises.readFile(playbookPath, 'utf8');
            return content;
        } catch (error) {
            console.error(`❌ Failed to load playbook '${playbookName}': ${error.message}`);
            return null;
        }
    }

    async _savePlaybook(filename, content) {
        try {
            if (!filename.endsWith('.md')) filename = `${filename}.md`;
            const playbookPath = path.join(__dirname, '..', 'playbooks', filename);
            await fsPromises.writeFile(playbookPath, content, 'utf8');
            return `✅ Playbook saved successfully: ${filename}`;
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

        // DEBUG: Save the exact prompt we are sending to LLM
        // This allows the user to see "What exactly are we sending?"
        try {
            const debugPath = path.join(__dirname, '..', 'public', 'LOGS', 'last_prompt.txt');
            fs.writeFileSync(debugPath, prompt);
        } catch (e) { /* Ignore log write errors */ }

        let response;
        if (this.provider === 'claude') {
            response = await this._askClaude(prompt, screenshotBase64);
        } else {
            response = await this._askGemini(prompt, screenshotBase64);
        }

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
4. If the element is not in the list, use "inspect_screen" to see it.

**RESPONSE FORMAT (JSON ONLY)**:
{
    "action": "click|type|key|select|wait|download|inspect_screen|ask_user|read_clipboard|write_clipboard|os_list|os_read|os_delete|os_run|os_kill|os_mkdir|os_write|os_exists|os_getenv|reg_read|reg_write|net_ping|net_port|net_search|switch_window",
    "element_id": "...",
    "text": "...",
    "reasoning": "Briefly explain why you chose this action."
}`;
    }

    _summarizeElements(elements) {
        if (!elements || elements.length === 0) return '  (No UI elements found)';

        let summaryLines = [];
        const limit = 100; // Allow more elements since we are filtering garbage

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
                max_tokens: 1024,
                temperature: config.TEMPERATURE,
                messages: [{ role: 'user', content }]
            });
            return this._parseJsonResponse(response.content[0].text);
        } catch (e) {
            return { error: e.message, task_completed: false };
        }
    }

    async _askGemini(prompt, screenshotBase64 = null) {
        try {
            // New @google/genai format: contents is array of Part objects
            const contents = [{ text: prompt }];
            if (screenshotBase64) {
                contents.push({ inlineData: { mimeType: 'image/jpeg', data: screenshotBase64 } });
            }

            const result = await this.gemini.models.generateContent({
                model: this.geminiPrimaryModel,
                contents: contents,
                config: { responseMimeType: "application/json" }
            });
            return JSON.parse(result.text);
        } catch (e) {
            console.error("Gemini Error:", e.message);
            // Fallback (text-only for safety)
            try {
                const fallback = await this.gemini.models.generateContent({
                    model: this.geminiFallbackModel,
                    contents: [{ text: prompt }]
                });
                return this._parseJsonResponse(fallback.text);
            } catch (err) {
                return { error: err.message, task_completed: false };
            }
        }
    }

    _parseJsonResponse(text) {
        try {
            text = text.replace(/```json/g, '').replace(/```/g, '').trim();
            return JSON.parse(text);
        } catch (e) {
            console.error("JSON Parse Error on:", text.substring(0, 100));
            throw new Error("Invalid JSON response from LLM");
        }
    }
}

module.exports = new LLMService();
