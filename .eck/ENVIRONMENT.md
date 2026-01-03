# Environment Configuration

## Project Type
**Desktop Automation Framework** (Client-Server with AI Brain)

## Server Environment Variables

**Location:** `/var/www/xelthAGI/server/.env`

### Required
```bash
# LLM Provider Selection
LLM_PROVIDER=gemini          # Options: gemini, claude

# API Keys (at least one required)
GEMINI_API_KEY=              # Google AI Studio key
CLAUDE_API_KEY=              # Anthropic API key (optional)
```

### Optional
```bash
# Model Selection
GEMINI_MODEL=gemini-3-flash-preview
CLAUDE_MODEL=claude-sonnet-4-5-20250929

# Server Configuration
HOST=0.0.0.0
PORT=3232

# Debug Mode (enables Shadow Debugging screenshots)
DEBUG=false

# Agent Limits
MAX_STEPS=50
TEMPERATURE=0.7

# Google Search (for net_search command)
GOOGLE_SEARCH_API_KEY=
GOOGLE_SEARCH_CX=
```

## Client Configuration

The C# client uses command-line arguments instead of environment variables:

```bash
./SupportAgent.exe \
  --app <process_or_title> \      # Target application
  --task "<description>" \         # Natural language task
  --server https://xelth.com/AGI \ # Server URL
  --unsafe                         # Skip safety confirmations
  --max-steps 20                   # Limit iterations
```

## Production Deployment

**Server:** xelth.com (152.53.15.15)
**SSH Alias:** `antigravity`
**PM2 Process:** `xelthAGI`
**Nginx Proxy:** `/AGI/` → `localhost:3232`

## Local Development

```bash
# Server
cd server
cp .env.example .env
# Edit .env with your API keys
npm install
npm start

# Client
cd client/SupportAgent
dotnet build -c Release
```

---

*Agent ID: local_dev*
*Last updated: 2026-01-03*
