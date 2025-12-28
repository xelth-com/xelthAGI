require('dotenv').config();

module.exports = {
  LLM_PROVIDER: process.env.LLM_PROVIDER || 'claude',
  CLAUDE_API_KEY: process.env.CLAUDE_API_KEY || '',
  GEMINI_API_KEY: process.env.GEMINI_API_KEY || '',
  CLAUDE_MODEL: process.env.CLAUDE_MODEL || 'claude-3-5-sonnet-20241022',
  GEMINI_MODEL: process.env.GEMINI_MODEL || 'gemini-3-flash-preview',
  HOST: process.env.HOST || '0.0.0.0',
  PORT: parseInt(process.env.PORT || '5000', 10),
  DEBUG: (process.env.DEBUG || 'false').toLowerCase() === 'true',
  MAX_STEPS: parseInt(process.env.MAX_STEPS || '50', 10),
  TEMPERATURE: parseFloat(process.env.TEMPERATURE || '0.7')
};
