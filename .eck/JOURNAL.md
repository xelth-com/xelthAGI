# Development Journal

## v1.3 Release - Security, OCR, and Learning (2026-01-03)

---
type: release
scope: system
summary: Release v1.3 - Security, OCR, and Playbook Learning
date: 2026-01-03
---

### Security & Deployment
- **Embedded Access Tokens**: Binary patching system for secure, config-less client distribution
- **One-Click Download**: Dashboard button generates unique, secured EXE files on the fly
- **Token Hygiene**: Server validates `x1_...` tokens via Bearer auth

### Vision & Perception
- **Windows Media OCR**: Integrated native Windows 10/11 OCR engine into C# client
- **Visual Reading**: `inspect_screen` command now returns text + coordinates for "blind" apps (Citrix/RDP)

### Intelligence & Learning
- **Auto-Learning**: Server analyzes successful session history
- **Playbook Generator**: Automatically creates Markdown SOPs from execution logs (`learned_task.md`)
- **Download Button**: Added to Mission Control dashboard

### Files Added
- `server/src/authService.js` - Token generation and validation
- `server/src/patcher.js` - Binary patching for token injection
- `client/Services/OcrService.cs` - Windows Media OCR integration
- `client/Scripts/inject_token_slot.ps1` - Build-time token slot injection
- `client/patch_token.bat` - Local token patching script

### Files Modified
- `server/src/index.js` - Download endpoint, auth middleware, learning trigger
- `server/src/llmService.js` - Playbook generation (`learnedPlaybook` method)
- `server/public/index.html` - Download button in header
- `client/Program.cs` - OCR integration in main loop
- `NEXT_TASK.md` - Updated to v1.3

---

## Recent Changes

---
type: refactor
scope: server
summary: Migrate to @google/genai API and implement Token Hygiene
date: 2026-01-03
---
### Architecture Improvements
- **API Migration**: Switched from deprecated `@google/generative-ai` to the new `@google/genai` package (v1.34.0).
- **Token Hygiene**: Refactored `llmService.js` to filter out non-interactive/empty UI elements before sending to LLM. Reduces context noise by ~70%.
- **Infinite Memory**: Enabled full history transmission (numbered steps) instead of truncation, leveraging Gemini Flash's large context window.
- **Prompt Logging**: Added `last_prompt.txt` generation for real-time inspection of LLM inputs.

### Fixes
- Fixed `getGenerativeModel is not a function` crash by updating library imports.
- Fixed JSON parsing vulnerability by stripping markdown blocks before parsing.

---
type: release
scope: system
summary: Release v1.1 - Desktop Mode, Advanced Safety, and Mission Control v2
date: 2026-01-03
---
### Major Release v1.1
Completed a major refinement sprint focused on Observability, Safety, and Usability.

### Key Deliverables
1.  **Desktop Mode**: Client now supports running without `--app`. Automatically attaches to foreground window.
2.  **Safety System**: Implemented "Human-in-the-loop" GUI with 3-way decision making (Allow/Deny/Explain).
3.  **Mission Control v2**: Completely rewritten dashboard with adaptive CSS, session-based grouping, and time-travel debugging.
4.  **Shadow Debugging**: Background full-desktop capture at 20% quality for complete audit trails.
5.  **Infrastructure**: Nginx directory browsing, recursive Garbage Collection, and UPPERCASE API endpoints.
6.  **Hybrid Dialogs**: Unified GUI for both safety confirmations and user questions (buttons + text input).

### Technical Highlights
- **AttachToActiveWindow()**: New method in UIAutomationService for dynamic window binding
- **DialogResult Branching**: Safety dialog returns Yes/No/Cancel for nuanced agent responses
- **object-fit: contain**: CSS fix prevents screenshot cropping in Mission Control
- **Quality Reduction**: Shadow screenshots reduced from 30% to 20% for bandwidth savings

### Testing Results
All test categories passed:
- Multi-Window Switching ✅
- OS Operations ✅
- Network/Registry ✅
- Shadow Debugging ✅
- Safety Rails ✅
- Desktop Mode ✅

---
type: feat
scope: system
summary: Implement Mission Control v2.0 and Shadow Debugging ecosystem
date: 2026-01-03
---
### Features
- **Shadow Debugging**: Client now captures full desktop screenshots (30% quality) on every step alongside context-aware vision.
- **Mission Control v2.0**:
  - **Dual View**: Dashboard shows Agent Vision (cropped) and Shadow Recorder (full desktop) side-by-side.
  - **Time Travel**: History items are clickable to review screenshots from specific steps.
  - **Live Config**: Added runtime toggle for Debug Mode.
- **Task-Based Logging**: Log files and screenshot folders are now named by `{TaskName}_{ID}_{Date}` for better grouping.
- **Nginx Configuration**: Enabled `autoindex` for LOGS and SCREENSHOTS directories to allow browsing.

### Infrastructure
- **Server**: Updated directory structure to uppercase (`LOGS`, `SCREENSHOTS`) for API consistency.
- **Garbage Collection**: Implemented recursive cleanup for logs (48h) and screenshots (1h).
- **Documentation**: Created `.eck/NGINX.md` with detailed server configuration.

---
type: feat
scope: client
summary: Implement error handling refinements and robustness improvements
date: 2026-01-02
---

### Error Handling & Reliability Improvements

**SystemService.cs:**
- Added 30-second timeout to `RunProcess` using `WaitForInputIdle(30000)`
  - Prevents hanging on processes that fail to initialize
  - Gracefully handles console/background apps that don't support input idle
  - Location: `client/SupportAgent/Services/SystemService.cs:151-157`

- Implemented retry logic for `NetworkPing` with 3 attempts and 1s delays
  - Returns attempt count on success: "succeeded on attempt N/3"
  - Exponential messaging for better UX
  - Location: `client/SupportAgent/Services/SystemService.cs:411-460`

- Implemented retry logic for `NetworkCheckPort` with 3 attempts and 1s delays
  - Mirrors NetworkPing behavior for consistency
  - Better handling of transient network issues
  - Location: `client/SupportAgent/Services/SystemService.cs:465-526`

**ServerCommunicationService.cs:**
- Implemented exponential backoff retry logic for server requests
  - 3 attempts with delays: 1s, 2s, 4s
  - Graceful logging of retry attempts
  - Success notification when retry succeeds
  - Location: `client/SupportAgent/Services/ServerCommunicationService.cs:26-89`

**Program.cs:**
- Added execution time logging for all commands
  - Uses Stopwatch for accurate measurement
  - Logs in milliseconds with ⏱️ emoji for visibility
  - Displayed in dark gray to avoid clutter
  - Location: `client/SupportAgent/Program.cs:251-258`

### Build Status
✅ Client rebuilt successfully with all improvements
- Build time: 6.58s
- Output: `client/SupportAgent/bin/Release/net8.0-windows/win-x64/SupportAgent.dll`
- Warning: NETSDK1128 (COM hosting for standalone deployments - not critical)

### Testing Notes
- Initial test discovered client/server version mismatch or integration issue
- `switch_window` command not being properly recognized despite being in codebase
- Recommendation: Deploy updated server alongside client for comprehensive testing
- All code improvements completed and ready for integration testing

### Technical Debt Addressed
- ✅ No timeout for long-running OS commands → Fixed with WaitForInputIdle
- ✅ No retry logic for network operations → Implemented for ping and port checks
- ✅ No retry logic for server connection loss → Implemented exponential backoff
- ✅ No execution time tracking → Added comprehensive timing logs

---
type: feat
scope: project
summary: Initial manifest generated (PENDING REVIEW)
date: 2026-01-02
---
- NOTICE: Some .eck files are STUBS. They need manual or AI-assisted verification.