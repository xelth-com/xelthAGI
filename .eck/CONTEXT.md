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
- **Vision & OCR**: Windows Media OCR + Coarse-to-Fine vision system (~75% token reduction)
- **Self-Learning**: Automated playbook generation from successful sessions

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
│   │   ├── VisionHelper.cs        # Coarse-to-fine vision
│   │   ├── OcrService.cs          # Windows Media OCR
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

### AI & Human (6)
- `ask_user`, `read_clipboard`, `write_clipboard`
- `net_search`, `create_playbook`, `zoom_in`

**Total: 25 commands**

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

### 6. Coarse-to-Fine Vision
**Problem:** 4K screenshots consume ~5MB Base64, wasting tokens.

**Solution:**
- Send low-res overview first (1280px, ~200KB)
- LLM requests `zoom_in` when text is too small
- Client sends HD crop from original

**Benefits:**
- ~75% token reduction for vision
- Better OCR on small text
- Faster transmission
- Smart: LLM decides when to zoom

### 7. Visual Override (Trust Your Eyes)
**Problem:** UI Automation tree sometimes incomplete - elements visible but not listed.

**Solution:**
- LLM instructed to trust vision over incomplete tree data
- Estimates X,Y coordinates directly from screenshot
- Clicks using visual coordinates when element_id unavailable
- Prevents infinite `inspect_screen` loops

**Benefits:**
- Self-healing when UI tree fails
- Prioritizes what's actually visible
- Pragmatic: "Better to misclick once than freeze forever"

**Location:** `server/src/llmService.js:211-218` (System Prompt Instruction #4)

### 8. Unified Notification System ⚠️ CRITICAL - DO NOT MODIFY
**STRICT RULE:** All user-facing dialogs MUST use `ShowAgentNotification()` function.

**Purpose:** Consistent, branded user experience across ALL interactions.

**Single Window Design:**
```
┌─────────────────────────────────────────────────────┐
│ 🤖 Agent ID: xxxxxxxx - [Context]      ⏱️ 10s     │
├─────────────────────────────────────────────────────┤
│ [Message Box - scrollable, read-only]              │
│                                                     │
│                                                     │
├─────────────────────────────────────────────────────┤
│ [✅ Yes/Allow] [❌ No/Deny] [❓ Don't Know] [🛑 Shutdown] │
├─────────────────────────────────────────────────────┤
│ Or type specific data/response:                    │
│ ┌───────────────────────┐ ┌──────────┐           │
│ │ [3-line text input]   │ │Send Text │           │
│ │                       │ │          │           │
│ └───────────────────────┘ └──────────┘           │
└─────────────────────────────────────────────────────┘
```

**Usage Examples:**
- **Greeting:** Start-up notification (10s timeout)
- **Completion:** Task finished notification (10s timeout)
- **Interactive Mode:** Task input request (120s timeout)
- **ask_user:** AI needs human help (300s timeout)
- **Safety Confirmation:** High-risk action approval (60s timeout)

**Ultra-Aggressive TopMost:**
- Timer: Every 500ms (!), not 2000ms
- Calls: `SetWindowPos(HWND_TOPMOST)`, `BringToFront()`, `Activate()`, `Focus()`
- Purpose: ALWAYS on top, even over Calculator, Notepad, or other TOPMOST windows
- Why: Ensures user NEVER misses critical notifications

**⛔ FORBIDDEN:**
- Creating new dialog types with different layouts
- Using `ShowUnifiedDialog()` for new features (deprecated)
- Modifying window layout without approval
- Changing button count, colors, or positions

**Location:** `Program.cs:ShowAgentNotification()`

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
- Coarse-to-fine vision system (zoom_in command)
- Windows Media OCR integration

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
- **Build:** `cd client/SupportAgent && build-release.bat`
- **Path:** client/SupportAgent/publish/
- **Executable:** SupportAgent.exe (~75MB with R2R optimization)

**Security:** Client uses embedded access tokens (`x1_...`) for authentication. Tokens are injected at build time via post-build script.

## Development Workflows

### 🚀 FAST DEV MODE (Use for Active Development!)
**Identity:** `00000000` (zero ID for local testing)
**Purpose:** Instant iteration without CI/CD overhead

**How to Run:**
```bash
cd client/SupportAgent
fast_debug.bat
```

**What It Does:**
1. Mints dev token (`dev_token.txt`) for ID `00000000`
2. Runs `dotnet run` (compiles on-the-fly, ~5 seconds)
3. Connects to production server with `--unsafe` flag
4. Executes test task

**When to Use:**
- ✅ Changing C# logic (UI Automation, Dialogs, Key inputs)
- ✅ Debugging infinite loops or errors
- ✅ Testing new commands
- ✅ Quick iterations (seconds vs minutes)

**Speed:** ~5-10 seconds (compile + run)

---

### 📦 PRODUCTION MODE (Use for Deployment Only)
**Identity:** Random Hex (e.g., `8ad5...`)
**Purpose:** Testing final patched `.exe` binary

**How to Run:**
```bash
cd client/SupportAgent
ci_cycle.bat
```

**What It Does:**
1. `dotnet publish` (R2R optimization, ~2 minutes)
2. Generates random client ID
3. Patches token into binary
4. Runs standalone `.exe`

**When to Use:**
- ✅ Final release testing
- ✅ Verifying binary patching works
- ✅ Before pushing to production

**Speed:** ~2-3 minutes (full CI cycle)

---

### ⚠️ CRITICAL RULE
**NEVER** mix workflows! Fast Dev = `00000000`, Production = random ID.
See `DEV_WORKFLOW.md` for details.

## Quick Commands

```bash
# FAST DEV (for development)
cd client/SupportAgent && fast_debug.bat

# PRODUCTION BUILD (for release)
cd client/SupportAgent && ci_cycle.bat

# Deploy server
ssh antigravity "cd /var/www/xelthAGI && git pull && pm2 restart xelthAGI"

# Health check
curl https://xelth.com/AGI/HEALTH

# Run client (production)
./SupportAgent.exe --app notepad --task "Type: Hello!" --server https://xelth.com/AGI

# Run with safety bypass
./SupportAgent.exe --app notepad --task "..." --unsafe
```

## Documentation

- **[ARCHITECTURE.md](.eck/ARCHITECTURE.md)**: Component breakdown, design patterns
- **[OPERATIONS.md](.eck/OPERATIONS.md)**: Command reference, testing, deployment
- **[SERVER_ACCESS.md](.eck/SERVER_ACCESS.md)**: SSH credentials, deployment procedures
- **[NEXT_TASK.md](NEXT_TASK.md)**: Session handoff, recommendations

## Security & Binary Patching (v1.3)

XelthAGI uses **Embedded Access Tokens** for client authentication:

- **Token Format**: `x1_{timestamp}_{random}` (e.g., `x1_lq2w9z_a1b2c3...`)
- **Build Process**: `build-release.bat` compiles EXE and injects token slot
- **Deployment**: Server patches EXE with unique token per download
- **Verification**: `npm run verify-patch` confirms binary compatibility

## Support

For issues, questions, or contributions:
1. Check [OPERATIONS.md](.eck/OPERATIONS.md) troubleshooting section
2. Review server logs: `ssh antigravity "pm2 logs xelthAGI"`
3. Inspect client console output
4. Verify health: `curl https://xelth.com/AGI/HEALTH`
