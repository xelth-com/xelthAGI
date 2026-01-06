# Technical Debt

## High Priority

### 9. Server-Side Support for zoom_in Command
**Status:** DISCOVERED 2026-01-05
**Location:** `server/src/llmService.js`
**Issue:** Client now supports `zoom_in` command for coarse-to-fine vision, but server prompt doesn't teach LLM about this action
**Impact:** LLM cannot request zoom, making the coarse-to-fine vision system partially unusable
**Fix Required:**
- Add `zoom_in` to available actions in LLM prompt
- Document command format: `{ action: "zoom_in", x: <int>, y: <int>, element_id: "<width>", text: "<height>" }`
- Add example usage to prompt (e.g., "If text is too small to read, use zoom_in")
**Priority:** High - new feature incomplete without this

## Medium Priority

### 8. OCR Service Dependency on Windows 10.0.19041.0
**Status:** DISCOVERED 2026-01-03
**Location:** `client/SupportAgent/Services/OcrService.cs`
**Issue:** OCR requires specific Windows version (19041+), older systems cannot use OCR
**Impact:** OCR silently fails on unsupported Windows versions
**Workaround:** `ocrService.IsSupported` check before usage
**Priority:** Medium - graceful degradation implemented

### 7. PowerShell Dependency for Client Build
**Status:** DISCOVERED 2026-01-03
**Location:** `client/SupportAgent/Scripts/inject_token_slot.ps1`
**Issue:** Token slot injection requires PowerShell, which may not be available on all build environments
**Impact:** Build process fails without PowerShell
**Fix Required:**
- Create cross-platform Node.js script for token injection
- Or bundle PowerShell Core with build process
**Priority:** Medium - affects CI/CD pipelines

### 2. Build Warning: COM Hosting for Standalone Deployments
**Status:** EXISTING
**Location:** Client build process
**Warning:** `NETSDK1128: Beim COM-Hosting werden keine eigenständigen Bereitstellungen unterstützt`
**Impact:** No functional impact, but adds noise to build output
**Fix Required:** Review project settings for COM hosting configuration
**Priority:** Low - does not affect functionality

### 3. NetworkPing and NetworkCheckPort Retry Parameters Not Exposed to Server
**Status:** DISCOVERED 2026-01-02
**Location:** `client/SupportAgent/Services/UIAutomationService.cs` (command execution)
**Issue:** New retry parameters added to `NetworkPing` and `NetworkCheckPort` methods are not accessible from server commands
**Impact:** Server cannot control retry behavior (defaults to 3 attempts)
**Fix Required:**
- Add optional `retry_count` field to command JSON schema
- Update server prompt to inform LLM of retry parameter availability
**Workaround:** Currently defaults to 3 retries (reasonable for most cases)

## Low Priority

### 4. Magic Numbers in Code
**Status:** EXISTING
**Locations:**
- `CharDelayMs=75` in UIAutomationService (typing delay)
- `MaxRetries=2` in type verification
- `Thread.Sleep(1000)` in retry logic
**Fix Required:** Extract to named constants
**Example:**
```csharp
private const int TYPING_DELAY_MS = 75;
private const int TYPE_VERIFICATION_RETRIES = 2;
private const int RETRY_DELAY_MS = 1000;
```

### 5. Duplicate Code: Window Focus Logic
**Status:** EXISTING
**Location:** `UIAutomationService.cs` - focus management repeated in multiple methods
**Fix Required:** Extract to shared `EnsureWindowFocus(Window window)` helper method (if not already present)

### 6. No Unit Tests
**Status:** EXISTING
**Impact:** Only integration tests available, making regression detection harder
**Fix Required:** Add unit tests for:
- `SystemService` methods (file operations, network checks)
- `ServerCommunicationService` retry logic
- Command parsing and validation
**Recommendation:** Use xUnit or NUnit framework

## Resolved

### ✅ Modal Dialog Button Discovery (v1.6.5)
**Fixed:** 2026-01-06
**Problem:** Dialog buttons (Save, Don't Save, Cancel) not visible to agent
- Agent could see buttons in screenshots but couldn't click them
- GetWindowState only scanned main window, not modal dialogs
- Modal dialog children threw "Property not supported [#30005]" exceptions
**Solution:** Scan modal windows + safe property access
- Added window.ModalWindows scanning to GetWindowState
- Wrapped child.Name, ControlType, IsEnabled in try-catch
- Added debug logging for modal discovery
**Impact:** Dialog interactions 4x faster (5 steps instead of 20), no more vision loops
**Test:** German Notepad "Don't Save" dialog - 8 elements discovered, clicked successfully
**Location:** `client/SupportAgent/Services/UIAutomationService.cs:271-306, 462-518`

### ✅ Screenshot Race Conditions - "Speedy Gonzales" Bug (v1.6.1)
**Fixed:** 2026-01-06
**Problem:** Screenshots captured before Windows UI finished rendering (Shadow Recorder "ghost images")
- Symptoms: AI sees old state, loops, fails tasks
- Root cause: Windows UI rendering is async - actions complete but visual updates lag
**Solution:** Aggressively increased UI settlement delays
- Pre-scan delay: 200ms → 500ms
- Post-action delays: 800-2000ms → 1000-2000ms (command-specific)
- os_run: +2000ms, key: +2000ms, click: +1000ms, switch_window: +1000ms
**Impact:** Eliminated race conditions, 100% reliable "What You See Is What You Got"
**Trade-off:** ~150% slower execution, but 100% reliability
**Location:** `client/SupportAgent/Program.cs:296, 655-662`

### ✅ Blocking Debug I/O - "Observer Effect" (v1.6.2)
**Fixed:** 2026-01-06
**Problem:** Debug screenshot saving blocked main automation thread
- Symptoms: Unpredictable 100-500ms delays per screenshot
- "Observer Effect": Timing measurements included I/O overhead not present in production
**Solution:** Fire-and-forget async pattern with bitmap cloning
- `UIAutomationService.CaptureScreenToFile`: Clone bitmap, async save in Task.Run
- `VisionHelper.SaveJpeg`: Clone image, async save in Task.Run
- Error handling: All exceptions suppressed to prevent agent crashes
**Impact:** 90-495ms saved per screenshot = ~2-3 seconds per automation loop with vision
**Performance:**
- Before: 100-500ms blocking I/O per screenshot
- After: ~5-10ms cloning overhead (in-memory)
**Location:** `client/SupportAgent/Services/UIAutomationService.cs:319-357`, `VisionHelper.cs:189-224`

### ✅ Identity Split-Brain (v1.4)
**Fixed:** 2026-01-04
**Problem:** Client used local random ID, while Dashboard expected Token ID, causing "Offline" status.
**Solution:** Implemented "Identity Convergence". Server returns `CanonicalClientId` in `/DECIDE`. Client updates local persistence to match.

### ✅ Token Reading from Embedded Resources (AuthConfig.cs)
**Fixed:** 2026-01-04
**Problem:** Client was trying to read token from non-existent embedded resources, causing authentication failures
**Solution:** Changed `AuthConfig.cs` to read token from end of executable file (appended binary data)
**Details:**
- Fixed placeholder size mismatch (515 vs 500 chars)
- Updated `ReadTokenBytes()` to use `FileStream` instead of `GetManifestResourceStream`
- Token now successfully extracted from binary after patching
**Impact:** Client authentication now works correctly with XLT token system

### ✅ Mission Control Dashboard Authentication Requirement
**Fixed:** 2026-01-04
**Problem:** Dashboard couldn't access `/API/STATE` endpoint (HTTP 401 errors)
**Solution:** Moved `/API/STATE` endpoint definition before authentication middleware
**Details:**
- `/API/STATE` is now public (no auth required) for dashboard monitoring
- Other `/API/*` endpoints remain protected
- Added cache-busting parameter to prevent browser caching issues
**Impact:** Dashboard can now monitor agents in real-time without authentication

### ✅ No timeout for long-running OS commands
**Fixed:** 2026-01-02
**Solution:** Added `WaitForInputIdle(30000)` to `RunProcess` method

### ✅ No retry logic for network operations
**Fixed:** 2026-01-02
**Solution:** Implemented 3-attempt retry logic for `NetworkPing` and `NetworkCheckPort`

### ✅ No retry logic for server connection loss
**Fixed:** 2026-01-02
**Solution:** Implemented exponential backoff (1s, 2s, 4s) in `ServerCommunicationService`

### ✅ No execution time tracking
**Fixed:** 2026-01-02
**Solution:** Added Stopwatch-based logging in `Program.cs:251-258`

### ✅ Client/Server Version Synchronization Issue
**Fixed:** 2026-01-03
**Solution:** Deployed both client and server from same commit; version mismatch resolved

### ✅ @google/generative-ai deprecated API
**Fixed:** 2026-01-03
**Solution:** Migrated to `@google/genai` v1.34.0 with new API format:
- `new GoogleGenAI({ apiKey })` initialization
- `ai.models.generateContent()` method
- Direct `result.text` response access
- Multimodal support with `inlineData` format

### ✅ Playbook Learning System
**Fixed:** 2026-01-03
**Solution:** Added `learnPlaybook()` method to `llmService.js`:
- Analyzes successful session history
- Generates generalized Markdown playbooks with variables
- Saves to `server/playbooks/learned_*.md`

### ✅ OCR Integration
**Fixed:** 2026-01-03
**Solution:** Created `OcrService.cs` using Windows Media OCR:
- Converts GDI+ Bitmaps to WinRT SoftwareBitmaps
- Returns text with bounding box coordinates `@(x,y)`
- Integrated into `inspect_screen` loop

### ✅ Embedded Access Tokens (Binary Patching)
**Fixed:** 2026-01-03
**Solution:** Implemented full token injection system:
- `patcher.js` - Node.js CLI for binary patching
- `authService.js` - Token generation (`x1_{timestamp}_{random}`)
- `inject_token_slot.ps1` - Build-time placeholder injection
- `patch_token.bat` - Local development script

---

## Notes for Future Development

1. **Version Control:** Consider adding a `/VERSION` endpoint to server and version check in client startup
2. **Telemetry:** Execution time logs could be aggregated for performance analysis
3. **Error Classification:** Categorize errors (transient network, permanent failure, user denial) for better retry decisions
4. **Async Improvements:** Some `Thread.Sleep` calls in main loop could be replaced with `await Task.Delay` for better async/await patterns (v1.6.2 addressed async I/O for screenshots)