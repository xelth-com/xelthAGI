# Support Agent - C# + FlaUI Client 🤖

> Lightweight Windows automation client powered by AI (Claude or Gemini)

## 🎯 Overview

This is a **next-generation support automation system** combining:
- **C# Client** with FlaUI for Windows UI Automation (~5-10MB)
- **Node.js Server** with LLM integration (Claude Sonnet 4 or Gemini Flash)
- **Intelligent decision-making** without bloated dependencies

Perfect for automating technical support tasks like configuring printers, installing software, or troubleshooting applications.

## 🏗️ Architecture

```
┌─────────────────┐
│  C# Client      │  ← Lightweight Windows app (~5-10MB)
│  (FlaUI)        │  ← Scans UI, executes commands
└────────┬────────┘
         │ HTTP/JSON
         ▼
┌─────────────────┐
│  Node.js Server │  ← Brain of the operation
│  (Express)      │  ← Decides what to do next
└────────┬────────┘
         │ API
         ▼
┌─────────────────┐
│  LLM Service    │  ← Claude Sonnet 4 OR Gemini Flash
│  (Anthropic/    │  ← Analyzes UI and plans actions
│   Google)       │  ← Auto-fallback for Gemini models
└─────────────────┘
```

### Why This Architecture?

| Component | Why? |
|-----------|------|
| **C# Client** | Native Windows, small size, no Python bloat, fewer antivirus issues |
| **FlaUI** | Built on UIAutomation (native to Windows), mature and stable |
| **Node.js Server** | Fast, lightweight, easy LLM integration, flexible model switching |
| **Claude/Gemini** | Choose power (Claude) or cost-efficiency (Gemini) with auto-fallback |

## 🚀 Quick Start

### Prerequisites

- **Windows 10/11** (for client)
- **.NET 8 SDK** (for building client)
- **Node.js 18+** (for server)
- API key for **Claude** or **Gemini**

### 1. Setup Server

```bash
cd server

# Install dependencies
npm install

# Configure environment
cp .env.example .env
# Edit .env and add your API keys

# Start server
npm start

# Or for development with auto-reload
npm run dev
```

Server will start on `http://localhost:5000`

### 2. Build Client

```bash
cd client/SupportAgent

# Restore packages
dotnet restore

# Build
dotnet build

# Run (development)
dotnet run -- --app "Notepad" --task "Type hello world"

# Publish (production)
dotnet publish -c Release
```

The compiled executable will be in `bin/Release/net8.0-windows/win-x64/publish/`

### 3. Run Automation

```bash
# Basic usage
SupportAgent.exe --app "InBodySuite" --task "Configure printer to InBody770"

# With custom server
SupportAgent.exe --app "Calculator" --task "Calculate 15 + 27" --server http://my-server:5000

# Multiple tasks
SupportAgent.exe --app "Settings" --task "Enable dark mode"
```

## 📦 Configuration

### Server Configuration (.env)

```bash
# Choose LLM provider
LLM_PROVIDER=claude  # or "gemini"

# API Keys
CLAUDE_API_KEY=sk-ant-xxxxx
GEMINI_API_KEY=xxxxx

# Models
CLAUDE_MODEL=claude-sonnet-4-5-20250929
GEMINI_MODEL=gemini-3-flash-preview  # Auto-falls back to gemini-2.5-flash

# Server
HOST=0.0.0.0
PORT=5000
```

### Client Command Line Options

| Option | Description | Example |
|--------|-------------|---------|
| `--app` | Target application name or window title | `--app "InBodySuite"` |
| `--task` | Task description for AI | `--task "Configure printer"` |
| `--server` | Server URL (default: localhost:5000) | `--server http://10.0.0.5:5000` |

## 💡 Use Cases

### Example 1: Configure Printer
```bash
SupportAgent.exe \
  --app "InBody Suite" \
  --task "Open settings, navigate to printer configuration, select InBody770 printer"
```

### Example 2: Install Software
```bash
SupportAgent.exe \
  --app "Setup Wizard" \
  --task "Click Next through all installation steps, accept license agreement"
```

### Example 3: Troubleshooting
```bash
SupportAgent.exe \
  --app "Error Dialog" \
  --task "Click OK and close all error windows"
```

## 🔧 How It Works

### 1. Client Scans UI
```csharp
var uiState = automationService.GetWindowState(window);
// Collects all buttons, text fields, checkboxes, etc.
```

### 2. Sends to Server
```json
{
  "State": {
    "WindowTitle": "InBody Suite - Settings",
    "Elements": [
      {"Id": "btn_printer", "Name": "Printer Settings", "Type": "Button"},
      {"Id": "cmb_printer", "Name": "Select Printer", "Type": "ComboBox"}
    ]
  },
  "Task": "Configure printer to InBody770",
  "History": ["clicked settings", "opened printer menu"]
}
```

### 3. LLM Decides Next Action
```json
{
  "action": "click",
  "element_id": "cmb_printer",
  "message": "Opening printer selection dropdown",
  "reasoning": "Need to select InBody770 from the dropdown",
  "task_completed": false
}
```

### 4. Client Executes
```csharp
await automationService.ExecuteCommand(window, command);
// Clicks the dropdown, selects option, etc.
```

### 5. Repeat Until Task Complete
The loop continues until `task_completed: true` or max steps reached.

## 🎛️ Advanced Features

### Gemini Auto-Fallback (New!)

The Node.js server now includes intelligent fallback for Gemini models:
- **Primary**: `gemini-3-flash-preview` (latest, fastest)
- **Fallback**: `gemini-2.5-flash` (stable backup)

If the primary model fails (rate limit, API error, etc.), the server automatically tries the fallback model. No manual intervention needed!

```javascript
// Happens automatically in llmService.js
models = [
  { name: 'gemini-3-flash-preview', temperature: 0.7 },
  { name: 'gemini-2.5-flash', temperature: 0.7 }
]
```

### Switch Between Claude and Gemini

Edit `server/.env`:
```bash
# Use Claude for complex tasks
LLM_PROVIDER=claude
CLAUDE_MODEL=claude-sonnet-4-5-20250929

# Use Gemini for cost efficiency
LLM_PROVIDER=gemini
GEMINI_MODEL=gemini-3-flash-preview
```

### Custom Actions

The client supports these actions:
- `click` - Click on UI element
- `type` - Type text into field
- `select` - Select dropdown item
- `wait` - Wait for N milliseconds
- `mouse_move` - Move mouse to coordinates

Add more in `UIAutomationService.cs:ExecuteCommand()`

### Remote Server

Run server on a different machine:
```bash
# On server machine
npm start  # Listens on 0.0.0.0:5000

# On client machine
SupportAgent.exe --app "MyApp" --task "Do something" --server http://192.168.1.100:5000
```

## 📊 Comparison

### Server: Node.js vs Python

| Feature | Node.js (Current) | Python (Old) |
|---------|-------------------|--------------|
| **Startup Time** | ~100ms | ~500ms |
| **Memory Usage** | ~50MB | ~100MB |
| **Dependencies** | 4 packages | 6 packages |
| **Type Safety** | Zod schemas | Pydantic models |
| **Auto-reload** | `npm run dev` | Manual restart |
| **LLM Fallback** | ✅ Built-in | ❌ No |

### vs Python-Based Solutions (like windows-use)

| Feature | This Solution | Python-based |
|---------|--------------|--------------|
| **Client Size** | ~5-10 MB | ~150-200 MB |
| **Startup Time** | Instant | 3-5 seconds |
| **Antivirus Issues** | Rare | Common |
| **Windows Native** | ✅ Yes | ⚠️ Embedded Python |
| **LLM Flexibility** | ✅ Claude + Gemini | Usually one model |

### Cost Comparison

| Task Type | Recommended LLM | Cost per 1M tokens |
|-----------|-----------------|-------------------|
| Simple (clicks, forms) | Gemini Flash | $0.075 / $0.30 |
| Complex (troubleshooting) | Claude Sonnet 4 | $3.00 / $15.00 |

## 🛠️ Development

### Project Structure
```
xelthAGI/
├── client/
│   └── SupportAgent/
│       ├── Program.cs              # Main entry point
│       ├── Services/
│       │   ├── UIAutomationService.cs      # FlaUI wrapper
│       │   └── ServerCommunicationService.cs  # HTTP client
│       └── Models/
│           ├── UIState.cs          # UI state model
│           └── Command.cs          # Command model
│
├── server/
│   ├── src/
│   │   ├── index.js                # Express server
│   │   ├── llmService.js           # Claude/Gemini integration
│   │   ├── llm.provider.js         # LLM client initialization
│   │   └── config.js               # Configuration
│   ├── package.json                # Node.js dependencies
│   └── .env.example                # Environment template
│
└── README.md
```

### Building from Source

```bash
# Client
cd client/SupportAgent
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true

# Server
cd server
npm install
npm start
```

### Adding New Features

**Add new UI actions:**
Edit `client/SupportAgent/Services/UIAutomationService.cs:ExecuteCommand()`

**Modify LLM prompt:**
Edit `server/src/llmService.js:_buildPrompt()`

**Add new LLM provider:**
- Add client initialization in `server/src/llm.provider.js`
- Create new method in `server/src/llmService.js` (e.g., `_askOpenAI()`)

## 🐛 Troubleshooting

### Client can't find window
- Make sure the application is running
- Try using exact window title: `--app "Exact Window Title"`
- Check with FlaUI Inspector tool

### Server connection failed
- Verify server is running: `curl http://localhost:5000/health`
- Check firewall settings
- Try `--server http://127.0.0.1:5000` instead of localhost

### LLM errors
- Verify API keys in `.env`
- Check API quota/limits
- Review server logs for details

### Element not found
- LLM might be using wrong element ID
- Try describing task more specifically
- Increase `MAX_STEPS` in config

## 📝 License

MIT License - see LICENSE file

## 🙏 Credits

- **FlaUI** - Windows UI Automation library
- **Anthropic Claude** - Advanced AI reasoning
- **Google Gemini** - Cost-effective AI with auto-fallback
- **Express.js** - Fast, minimalist Node.js web framework
- **Zod** - TypeScript-first schema validation

## 🚀 Future Improvements

- [ ] Screenshot support for vision models
- [ ] Multi-monitor support
- [ ] Browser automation (Selenium integration)
- [ ] Action recording/playback
- [ ] Web dashboard for monitoring
- [ ] Docker deployment

---

**Made with ❤️ for better technical support automation**
