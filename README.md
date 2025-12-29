# Support Agent - C# + FlaUI Client 🤖

> Lightweight Windows automation client powered by AI (Claude or Gemini)

## 🎯 Overview

This is a **next-generation support automation system** combining:
- **C# Client** with FlaUI for Windows UI Automation (~75MB standalone)
- **Node.js Server** with LLM integration (Claude Sonnet 4 or Gemini Flash)
- **Unique Client ID** system for centralized management
- **Intelligent decision-making** without bloated dependencies

Perfect for automating technical support tasks like configuring printers, installing software, or troubleshooting applications.

## 🏗️ Architecture

```
┌─────────────────┐
│  C# Client      │  ← Standalone Windows exe (~75MB)
│  (FlaUI)        │  ← Unique Client ID, Scans UI, Executes commands
└────────┬────────┘
         │ HTTP/JSON + Client ID
         ▼
┌─────────────────┐
│  Node.js Server │  ← Brain of the operation (Port 3232)
│  (Express)      │  ← Tracks clients, Decides next action
└────────┬────────┘
         │ API
         ▼
┌─────────────────┐
│  LLM Service    │  ← Claude Sonnet 4.5 OR Gemini Flash
│  (Anthropic/    │  ← Analyzes UI and plans actions
│   Google)       │
└─────────────────┘
```

### Why This Architecture?

| Component | Why? |
|-----------|------|
| **C# Client** | Native Windows, standalone exe, unique ID tracking, auto-launches apps |
| **FlaUI** | Built on UIAutomation (native to Windows), mature and stable |
| **Node.js Server** | Fast, lightweight, easy deployment as microservice |
| **Client ID System** | Centralized management, track usage per client |
| **Claude/Gemini** | Choose power (Claude) or cost-efficiency (Gemini) |

## 🚀 Quick Start

> **For production deployment:** See [DEPLOYMENT.md](DEPLOYMENT.md)
> **For xelth.com server:** See [DEPLOYMENT-XELTH.md](DEPLOYMENT-XELTH.md) (специфичная инструкция)

> **⚠️ IMPORTANT:** All API endpoints use **UPPERCASE** paths for QR code optimization!
> Examples: `/AGI/HEALTH`, `/AGI/DECIDE`. This enables alphanumeric encoding in QR codes, significantly reducing QR size.

### Prerequisites

- **Windows 10/11** (for client)
- **.NET 8 SDK** (for building client)
- **Node.js 18+** (for server)
- API key for **Claude** or **Gemini**

### 1. Setup Server

```bash
cd server

# Create virtual environment
python -m venv venv
source venv/bin/activate  # Windows: venv\Scripts\activate

# Install dependencies
pip install -r requirements.txt

# Configure environment
cp .env.example .env
# Edit .env and add your API keys

# Start server
python main.py
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
GEMINI_MODEL=gemini-2.0-flash

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

### Switch Between Claude and Gemini

Edit `server/.env`:
```bash
# Use Claude for complex tasks
LLM_PROVIDER=claude
CLAUDE_MODEL=claude-sonnet-4-5-20250929

# Use Gemini for cost efficiency
LLM_PROVIDER=gemini
GEMINI_MODEL=gemini-2.0-flash
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
python main.py  # Listens on 0.0.0.0:5000

# On client machine
SupportAgent.exe --app "MyApp" --task "Do something" --server http://192.168.1.100:5000
```

## 📊 Comparison

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
│   ├── main.py                     # FastAPI server
│   ├── llm_service.py              # Claude/Gemini integration
│   ├── config.py                   # Configuration
│   └── requirements.txt            # Python dependencies
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
pip install -r requirements.txt
python main.py
```

### Adding New Features

**Add new UI actions:**
Edit `client/SupportAgent/Services/UIAutomationService.cs:ExecuteCommand()`

**Modify LLM prompt:**
Edit `server/llm_service.py:_build_prompt()`

**Add new LLM provider:**
Create new method in `server/llm_service.py` (e.g., `_ask_openai()`)

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
- **Google Gemini** - Cost-effective AI
- **FastAPI** - Modern Python web framework

## 🚀 Future Improvements

- [ ] Screenshot support for vision models
- [ ] Multi-monitor support
- [ ] Browser automation (Selenium integration)
- [ ] Action recording/playback
- [ ] Web dashboard for monitoring
- [ ] Docker deployment

---

**Made with ❤️ for better technical support automation**
