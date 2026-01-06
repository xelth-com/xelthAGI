# System Architecture

## Overview
XelthAGI is a desktop automation framework that combines AI-driven decision-making with robust UI and OS automation capabilities.

## Architecture Pattern
**Client-Server with AI Brain**

```
┌─────────────────────────────────────────┐
│         Server (Node.js/Express)        │
│  ┌───────────────────────────────────┐  │
│  │   LLM Integration (Gemini/Claude) │  │
│  │   - Decision making               │  │
│  │   - Loop detection                │  │
│  │   - Context management            │  │
│  └───────────────────────────────────┘  │
│  ┌───────────────────────────────────┐  │
│  │   Server-Side Operations          │  │
│  │   - net_search (web search)       │  │
│  │   - Playbook management           │  │
│  └───────────────────────────────────┘  │
└──────────────┬──────────────────────────┘
               │ HTTPS
               │ JSON Commands
               ▼
┌─────────────────────────────────────────┐
│      Client (C# .NET 8 / FlaUI)         │
│  ┌───────────────────────────────────┐  │
│  │   UIAutomationService (FlaUI)     │  │
│  │   - Window matching & switching   │  │
│  │   - Element interaction           │  │
│  │   - Proactive focus management    │  │
│  │   - State tracking                │  │
│  └───────────────────────────────────┘  │
│  ┌───────────────────────────────────┐  │
│  │   SystemService (OS Operations)   │  │
│  │   - File management               │  │
│  │   - Process control               │  │
│  │   - Registry operations           │  │
│  │   - Network diagnostics           │  │
│  └───────────────────────────────────┘  │
│  ┌───────────────────────────────────┐  │
│  │   Safety & Human Interaction      │  │
│  │   - High-risk action confirmation │  │
│  │   - ask_user dialogs              │  │
│  │   - Clipboard operations          │  │
│  └───────────────────────────────────┘  │
└─────────────────────────────────────────┘
```

## Core Components

### 1. Server (Node.js/Express)
**Location:** `server/src/`

**Responsibilities:**
- **LLM Integration** (`llmService.js`): Communicates with Gemini/Claude APIs
- **Decision Making**: Processes UI state and determines next action
- **Loop Detection**: Server-side analysis of command history to detect infinite loops
- **Context Injection**: Injects warnings and hints into LLM prompts
- **Web Search**: Performs `net_search` operations server-side for instant context
- **Playbook Storage**: Saves successful workflows as reusable playbooks

**Key Files:**
- `index.js`: Express server, endpoints, health checks, auth middleware
- `llmService.js`: Prompt construction, LLM communication, loop detection
- `authService.js`: Token generation and validation (`x1_...` format)
- `patcher.js`: Binary patching for embedded token injection

### 2. Client (C# .NET 8)
**Location:** `client/SupportAgent/`

**Responsibilities:**
- **UI Automation**: FlaUI-based window and element manipulation
- **OS Operations**: Direct filesystem, process, registry, network operations
- **State Tracking**: Deep state detection (Title + Element Count + Content Hash)
- **Safety Rails**: User confirmation for destructive actions
- **Human Interaction**: GUI dialogs for user input (CAPTCHA, passwords)

**Key Files:**
- `Program.cs`: Main loop, command execution, state tracking, safety rails
- `Services/UIAutomationService.cs`: FlaUI automation, window management, focus control
- `Services/SystemService.cs`: OS operations (file, process, registry, network)
- `Services/ServerCommunicationService.cs`: HTTP client for server communication
- `Services/VisionHelper.cs`: Coarse-to-fine vision processing for token efficiency
- `Services/OcrService.cs`: Windows Media OCR for text recognition
- `Models/`: Command, UIState, UIElement data structures

## Key Design Patterns

### 1. Proactive Focus Management
**Problem:** Windows can lose focus during automation, causing actions to fail.

**Solution:**
```csharp
// BEFORE each action requiring focus:
SetWindowPos(HWND_TOPMOST, ...);  // Lock window on top
SetForegroundWindow(...);          // Set foreground

// AFTER task completion (Dispose):
SetWindowPos(HWND_NOTOPMOST, ...); // Release lock
```

**Benefits:**
- Prevents focus loss during typing/clicking
- No admin rights required
- Automatic cleanup prevents sticky windows

### 2. Deep State Detection
**Problem:** Simple title-based state detection misses content changes.

**Solution:**
```csharp
// Track three dimensions:
var titleHash = window.Name.GetHashCode();
var elementCount = elements.Count;
var contentHash = GetContentHash(elements); // All .Value fields

// State changed if ANY dimension changes
var stateChanged = (titleHash != prevTitle) ||
                   (elementCount != prevCount) ||
                   (contentHash != prevContent);
```

**Benefits:**
- Detects content changes even when title unchanged
- Prevents false "no change" detection
- Enables accurate loop detection

### 3. Server-Side Loop Detection
**Problem:** Infinite click loops waste tokens and time.

**Solution:**
```javascript
// Analyze command history for repeated patterns
const recentCommands = history.slice(-6);
const actionSequence = recentCommands.map(cmd =>
    `${cmd.action}:${cmd.elementId || cmd.x + ',' + cmd.y}`
);

// Detect 3+ identical consecutive actions
if (detectRepeatingPattern(actionSequence, 3)) {
    // Inject critical warning into prompt
    injectWarning("INFINITE LOOP DETECTED!");
}
```

**Benefits:**
- Prevents token waste
- Reduces test time
- Self-healing behavior

### 4. Multi-Tier Window Matching
**Problem:** Localization breaks title-based matching (Calculator vs Rechner).

**Solution:**
```csharp
// Priority tiers:
1. Exact process name match          // Highest priority
2. Partial process name match         // "calc" → "CalculatorApp"
3. Reverse partial match              // "calculator" contains "calc"
4. Special UWP app handling           // ApplicationFrameHost
5. Title exact/starts-with match
6. Title contains match (fallback)    // Lowest priority

// Special handling for common apps:
if (isCalculatorSearch && isCalculatorWindow) {
    // Handles: Rechner, Calculator, Taschenrechner, etc.
}
```

**Benefits:**
- Works across all OS localizations
- Process-first prevents false matches
- Covers edge cases (UWP apps)

### 5. Safety Rails with Bypass
**Problem:** Destructive actions need confirmation, but automation needs speed.

**Solution:**
```csharp
// High-risk actions:
var highRiskActions = new HashSet<string> {
    "os_delete", "os_kill", "reg_write",
    "os_run", "write_clipboard"
};

// Check before execution:
if (highRiskActions.Contains(action) && !unsafeMode) {
    // Red warning + Y/n prompt
    var confirmed = AskUserConfirmation(action, target);
    if (!confirmed) {
        return "FAILED: User denied ... - Safety check";
    }
}
```

**Benefits:**
- Prevents accidental data loss
- `--unsafe` flag for automated testing
- Logged denials inform the agent

### 6. Coarse-to-Fine Vision (Token Optimization)
**Problem:** Full-resolution screenshots consume excessive tokens (4K screen = ~5MB Base64).

**Solution:**
```csharp
// STEP 1: Capture high-res original to file
string originalPath = VisionHelper.GetTempPath("screen_original.png");
automationService.CaptureScreenToFile(originalPath);

// STEP 2: Create low-res overview for LLM
string lowResPath = VisionHelper.GetTempPath("screen_lowres.jpg");
double scaleFactor = VisionHelper.CreateLowResOverview(
    originalPath, lowResPath, targetLongSide: 1280);
// → Sends ~200KB instead of ~5MB (96% reduction)

// STEP 3: LLM requests zoom if needed
// Command: { action: "zoom_in", x: 100, y: 200, w: 400, h: 300 }
string cropPath = VisionHelper.GetTempPath("screen_crop.jpg");
VisionHelper.CreateHighResCrop(
    originalPath, cropPath,
    llmX, llmY, llmW, llmH, scaleFactor);
// → Sends high-quality crop of specific area
```

**Benefits:**
- **Token Efficiency**: ~75% reduction in vision tokens
- **Better OCR**: High-res crops for small text recognition
- **Faster Transmission**: Smaller payloads = faster LLM response
- **Smart**: LLM decides when zoom is needed

**Implementation:**
- VisionHelper uses `HighQualityBicubic` interpolation for text readability
- Original stored as PNG (lossless), overview as JPEG Q85, crop as JPEG Q95
- Temp files auto-cleaned after 30 minutes
- Scale factor tracked to correctly map low-res coordinates to high-res pixels

## Data Flow

### Request Flow
```
1. User → Client: CLI args (--app, --task, --server)
2. Client → Server: Initial state (POST /state)
3. Server → LLM: State + Prompt
4. LLM → Server: Next command (JSON)
5. Server → Client: Command response
6. Client: Execute command
7. Client → Server: Updated state
8. Repeat steps 3-7 until task complete or max steps
```

### State Structure
```json
{
  "title": "Window Title",
  "elements": [
    {
      "id": "uuid-v4",
      "name": "Element Name",
      "type": "Button|Edit|etc",
      "value": "current text content",
      "bounds": { "x": 10, "y": 20, "width": 100, "height": 30 }
    }
  ],
  "history": [
    { "action": "type", "elementId": "...", "text": "...", "result": "OK" }
  ],
  "lastOsOperationResult": "SUCCESS: ..." // For OS commands
}
```

## Technology Stack

### Server
- **Runtime**: Node.js 18+
- **Framework**: Express
- **LLM SDKs**: @google/genai (v1.34+), @anthropic-ai/sdk
- **Deployment**: PM2 process manager on Ubuntu
- **Web Server**: NGINX reverse proxy

### Client
- **Language**: C# 11
- **Framework**: .NET 8 (Windows-only)
- **UI Automation**: FlaUI 4.x (UIA3 backend)
- **Clipboard**: TextCopy library (STA thread safe)
- **HTTP Client**: System.Net.Http

## Security Considerations

1. **Embedded Access Tokens**: Binary-patched `x1_...` tokens for client auth
2. **Token Database**: Server maintains `db/clients.json` with active tokens
3. **Binary Patching**: Post-build injection of token slot into EXE
4. **No Credentials in Client**: API keys stored server-side only
5. **HTTPS Only**: Client-server communication encrypted
6. **Safety Rails**: Destructive actions require confirmation
7. **Admin Checks**: Registry HKLM writes require elevation
8. **User Visibility**: All actions logged to console

## Performance Optimizations

1. **Element Caching**: Reuse element references within same scan
2. **Economy Mode**: Screenshots only on-demand (not every step)
3. **Coarse-to-Fine Vision**: Low-res overview first, high-res zoom on demand (~75% token reduction)
4. **Server-Side Search**: `net_search` runs on server to avoid client overhead
5. **Loop Detection**: Prevents wasted LLM tokens
6. **Max Steps Limit**: 20 steps (reduced from 50) to prevent runaway costs
7. **Auto-Cleanup**: Vision temp files purged after 30 minutes

### 7. Visual Override (Trust Your Eyes)
**Problem:** UI Automation tree sometimes incomplete - elements visible but not in tree (software bug).

**Solution:**
```javascript
// LLM System Prompt Instruction #4:
// If you see the button in screenshot BUT it's not in element list:
// a) DO NOT loop on "inspect_screen"
// b) Estimate X,Y coordinates from image
// c) Send click with coordinates, empty element_id
// d) Reasoning: "Element not in tree, visible at [x,y]. Attempting visual click."
```

**Benefits:**
- Prevents infinite inspection loops
- Recovers from UI Automation tree bugs
- Prioritizes vision over programmatic data when mismatch occurs
- Pragmatic: Better to misclick once than freeze forever

**Implementation:**
- Added to server/src/llmService.js:211-218 in system prompt
- Instructs LLM to trust visual coordinates when tree is incomplete
- Falls back to visual clicking with estimated coordinates

## Extensibility Points

1. **New Commands**: Add to `ExecuteCommand` switch in UIAutomationService
2. **New OS Operations**: Extend SystemService class
3. **Custom Playbooks**: JSON templates for reusable workflows
4. **LLM Providers**: Add new providers in llmService.js
5. **Safety Policies**: Modify highRiskActions HashSet
