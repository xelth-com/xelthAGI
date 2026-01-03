require('dotenv').config();

module.exports = {
  LLM_PROVIDER: process.env.LLM_PROVIDER || 'claude',
  CLAUDE_API_KEY: process.env.CLAUDE_API_KEY || '',
  GEMINI_API_KEY: process.env.GEMINI_API_KEY || '',
  CLAUDE_MODEL: process.env.CLAUDE_MODEL || 'claude-3-5-sonnet-20241022',
  GEMINI_MODEL: process.env.GEMINI_MODEL || 'gemini-3-flash-preview',
  GOOGLE_SEARCH_API_KEY: process.env.GOOGLE_SEARCH_API_KEY || '',
  GOOGLE_SEARCH_CX: process.env.GOOGLE_SEARCH_CX || '',
  HOST: process.env.HOST || '0.0.0.0',
  PORT: parseInt(process.env.PORT || '5000', 10),
  DEBUG: (process.env.DEBUG || 'false').toLowerCase() === 'true',
  MAX_STEPS: parseInt(process.env.MAX_STEPS || '50', 10),
  TEMPERATURE: parseFloat(process.env.TEMPERATURE || '0.7'),

  // SECURITY KEYS (Must be in .env in production!)
  // Format: JSON string of key array or default array
  // created: timestamp when key became active
  KEY_STORE: process.env.KEY_STORE ? JSON.parse(process.env.KEY_STORE) : [
    {
        created: 0, // Active since epoch
        encKey: '12345678901234567890123456789012', // 32 chars (256 bit) for AES
        sigKey: 'sig_key_must_be_long_and_random_enough_to_be_secure_123'
    }
  ]
};
