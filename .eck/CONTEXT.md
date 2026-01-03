# Project Context

## Description
**XelthAGI** is an AI-powered desktop automation framework that enables natural language control of Windows applications and system operations.

## Key Capabilities
- **UI Automation**: Control any Windows application using FlaUI (UIA3)
- **OS Operations**: File management, process control, registry access, network diagnostics
- **Multi-Window**: Dynamic window switching and context management
- **Human Interaction**: Request user assistance for CAPTCHA, passwords, physical actions
- **Safety Rails**: Confirmation prompts for destructive actions
- **AI Intelligence**: LLM-driven decision making with loop detection

## Architecture
**Client-Server with AI Brain**

- **Server (Node.js)**: LLM integration (Gemini/Claude), decision-making, loop detection
- **Client (C# .NET 8)**: FlaUI UI automation, OS operations, safety enforcement

See [ARCHITECTURE.md](ARCHITECTURE.md) for detailed component breakdown.

## Tech Stack

### Server
- Node.js 18+ with Express
- LLM SDKs: @google/genai (v1.34+), @anthropic-ai/sdk
- PM2 process manager
- NGINX reverse proxy

### Client
- C# 11 / .NET 8 (Windows-only)
- FlaUI 4.x (UIA3 backend)
- TextCopy library (clipboard)
- System.Net.Http

## Project Structure
```
xelthAGI/
├── client/SupportAgent/          # C# client
│   ├── Program.cs                 # Main loop, safety, state tracking
│   ├── Services/
│   │   ├── UIAutomationService.cs # FlaUI automation
│   │   ├── SystemService.cs       # OS operations
│   │   └── ServerCommunicationService.cs
│   └── Models/                    # DTOs
│
├── server/                        # Node.js server
│   └── src/
│       ├── server.js              # Express app
│       └── llmService.js          # LLM integration
│
├── .eck/                          # Project documentation
│   ├── ARCHITECTURE.md
│   ├── OPERATIONS.md
│   ├── CONTEXT.md
│   └── SERVER_ACCESS.md
│
└── NEXT_TASK.md                   # Handoff doc
```

## Command Categories

### UI Automation (6)
- `click`, `type`, `key`, `select`, `switch_window`, `wait`

### OS Operations (8)
- `os_list`, `os_read`, `os_write`, `os_delete`
- `os_run`, `os_kill`, `os_mkdir`, `os_exists`

### IT Support (5)
- `os_getenv`, `reg_read`, `reg_write`
- `net_ping`, `net_port`

### AI & Human (5)
- `ask_user`, `read_clipboard`, `write_clipboard`
- `net_search`, `create_playbook`

**Total: 24 commands**

## Key Features

### 1. Process-First Window Matching
Handles localization automatically (Calculator = Rechner/Taschenrechner/Calculatrice).

**Priority:**
1. Exact process name
2. Partial process name (starts-with)
3. Reverse partial (query contains process)
4. UWP app special handling
5. Title matching (fallback)

### 2. Deep State Detection
Tracks three dimensions:
- Window title hash
- Element count
- Content hash (all .Value fields)

Prevents false "no change" detection.

### 3. Loop Prevention
**Server-Side:**
- Analyzes command history
- Detects 3+ identical consecutive actions
- Injects critical warning into prompt

**Client-Side:**
- Deep state tracking
- Content hash comparison

### 4. Proactive Focus Management
**Problem:** Windows lose focus during automation.

**Solution:**
- Set TOPMOST before each action (holds focus)
- Cleanup on window switch (release previous)
- Dispose cleanup (restore on exit)

**Benefits:**
- No admin rights required
- Prevents focus loss during operations
- Auto-cleanup prevents sticky windows

### 5. Safety Rails
**High-Risk Actions:**
- `os_delete`, `os_kill`, `os_run`
- `reg_write`, `write_clipboard`

**Behavior:**
- Red warning + Y/n confirmation
- `--unsafe` flag bypasses (for automation)
- Denials logged for agent awareness

## Development Status

### ✅ Completed
- UI automation with FlaUI
- OS operations (file, process, registry, network)
- Multi-window switching
- Loop detection & prevention
- Safety rails
- Human interaction (ask_user)
- Clipboard operations
- Proactive focus management
- Process-first window matching

### 🚧 In Progress
- Comprehensive test suite
- Error handling refinement
- Performance optimization

### 📋 Planned
- Visual UI (tray icon/status overlay)
- Additional LLM providers
- Enhanced playbook system
- Expanded IT support commands

## Known Limitations

1. **Platform:** Windows-only (FlaUI dependency)
2. **Timing:** No auto-wait after `os_run` (must be explicit)
3. **Permissions:** Registry HKLM writes require Admin
4. **Screenshot:** Quality selection not automated
5. **Element IDs:** Dynamic regeneration in some apps (e.g., Notepad)

## Deployment

### Server
- **Host:** 152.53.15.15 (antigravity)
- **Path:** /var/www/xelthAGI
- **URL:** https://xelth.com/AGI
- **Process:** PM2 → xelthAGI
- **Port:** 3232

### Client
- **Build:** `dotnet build -c Release`
- **Path:** client/SupportAgent/bin/Release/net8.0-windows/win-x64/
- **Executable:** SupportAgent.exe

## Quick Commands

```bash
# Deploy server
ssh antigravity "cd /var/www/xelthAGI && git pull && pm2 restart xelthAGI"

# Health check
curl https://xelth.com/AGI/HEALTH

# Run client
./SupportAgent.exe --app notepad --task "Type: Hello!" --server https://xelth.com/AGI

# Run with safety bypass
./SupportAgent.exe --app notepad --task "..." --unsafe
```

## Documentation

- **[ARCHITECTURE.md](.eck/ARCHITECTURE.md)**: Component breakdown, design patterns
- **[OPERATIONS.md](.eck/OPERATIONS.md)**: Command reference, testing, deployment
- **[SERVER_ACCESS.md](.eck/SERVER_ACCESS.md)**: SSH credentials, deployment procedures
- **[NEXT_TASK.md](NEXT_TASK.md)**: Session handoff, recommendations

## Support

For issues, questions, or contributions:
1. Check [OPERATIONS.md](.eck/OPERATIONS.md) troubleshooting section
2. Review server logs: `ssh antigravity "pm2 logs xelthAGI"`
3. Inspect client console output
4. Verify health: `curl https://xelth.com/AGI/HEALTH`
