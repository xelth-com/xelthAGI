// LLM Provider - Unified initialization for Claude and Gemini
const { Anthropic } = require('@anthropic-ai/sdk');
const { GoogleGenAI } = require('@google/genai');
const config = require('./config');

// Initialize Claude
let claudeClient = null;
if (config.CLAUDE_API_KEY) {
    claudeClient = new Anthropic({ apiKey: config.CLAUDE_API_KEY });
    console.log(`✅ Claude initialized: ${config.CLAUDE_MODEL}`);
} else {
    console.warn('⚠️  WARNING: CLAUDE_API_KEY is not configured.');
}

// Initialize Gemini (new @google/genai API)
let geminiClient = null;
if (config.GEMINI_API_KEY) {
    geminiClient = new GoogleGenAI({ apiKey: config.GEMINI_API_KEY });
    console.log(`✅ Gemini initialized: ${config.GEMINI_MODEL}`);
} else {
    console.warn('⚠️  WARNING: GEMINI_API_KEY is not configured.');
}

module.exports = {
    claudeClient,
    geminiClient
};
