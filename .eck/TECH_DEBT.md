# Technical Debt

## High Priority

*No high priority items currently.*

## Medium Priority

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

---

## Notes for Future Development

1. **Version Control:** Consider adding a `/VERSION` endpoint to server and version check in client startup
2. **Telemetry:** Execution time logs could be aggregated for performance analysis
3. **Error Classification:** Categorize errors (transient network, permanent failure, user denial) for better retry decisions
4. **Async Improvements:** Some `Thread.Sleep` calls could be replaced with `await Task.Delay` for better async/await patterns