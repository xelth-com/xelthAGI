const { claudeClient, geminiClient } = require('./llm.provider');
const config = require('./config');

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

    async decideNextAction(uiState, task, history) {
        const prompt = this._buildPrompt(uiState, task, history);

        if (this.provider === 'claude') {
            return await this._askClaude(prompt);
        } else if (this.provider === 'gemini') {
            return await this._askGemini(prompt);
        }
    }

    _buildPrompt(uiState, task, history) {
        const elementsSummary = this._summarizeElements(uiState.Elements || []);
        const historyText = history && history.length > 0
            ? history.slice(-10).map((h, i) => `  ${i + 1}. ${h}`).join('\n')
            : '  (none)';

        return `You are a UI automation agent. Your task is to help complete the following objective:

**TASK**: ${task}

**CURRENT WINDOW**: ${uiState.WindowTitle || 'Unknown'}

**ACTION HISTORY** (last 10 actions):
${historyText}

**AVAILABLE UI ELEMENTS**:
${elementsSummary}

**YOUR JOB**:
Analyze the current UI state and determine the NEXT SINGLE ACTION to complete the task.

**RESPONSE FORMAT** (JSON only):
{
    "action": "click|type|select|wait",
    "element_id": "element_automation_id",
    "text": "text to type (if action is 'type')",
    "message": "explanation of what you're doing",
    "task_completed": true|false,
    "reasoning": "why you chose this action"
}

**RULES**:
1. Return ONLY ONE action at a time
2. If task is complete, set task_completed=true and action=""
3. Be precise - use exact element IDs from the list above
4. If element not found, try alternative approach or set task_completed=false with error message
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

    async _askClaude(prompt) {
        try {
            const response = await this.claude.messages.create({
                model: config.CLAUDE_MODEL,
                max_tokens: 1024,
                temperature: config.TEMPERATURE,
                messages: [{ role: 'user', content: prompt }]
            });

            const text = response.content[0].text;
            return this._parseJsonResponse(text);
        } catch (e) {
            console.error(`❌ Claude API error: ${e.message}`);
            return { error: e.message, task_completed: false };
        }
    }

    async _askGemini(prompt) {
        const models = [
            { name: this.geminiPrimaryModel, temperature: config.TEMPERATURE },
            { name: this.geminiFallbackModel, temperature: config.TEMPERATURE }
        ];

        for (const [index, modelConfig] of models.entries()) {
            try {
                const result = await this.gemini.models.generateContent({
                    model: modelConfig.name,
                    contents: [{ role: 'user', parts: [{ text: prompt }] }],
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
