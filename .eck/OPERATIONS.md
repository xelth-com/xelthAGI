# Operational Procedures

## Quick Start

### Prerequisites
- Windows 10/11 (Client requires .NET 8 runtime)
- Node.js 18+ (Server)
- Server deployed at https://xelth.com/AGI

### Development vs Production

**🚨 CRITICAL: ALWAYS USE `dotnet run` FOR DEVELOPMENT**

#### Development Mode (Fast Iteration)
```bash
cd client/SupportAgent
dotnet run -- --server "https://xelth.com/AGI" --unsafe --task "Your test task"
```

**Why `dotnet run`:**
- ✅ Compiles with latest code (~5 seconds)
- ✅ No stale builds confusing you
- ✅ Instant feedback on code changes

**⛔ NEVER use old `publish/` builds during development!**
- They contain OUTDATED code
- They will mislead you with old behavior
- Delete them to avoid confusion: `rm -rf client/SupportAgent/publish/`

#### Production Build (Deployment Only)
```bash
cd client/SupportAgent
build-release.bat
```
Output: `publish\SupportAgent.exe` (~75MB with R2R optimization)

**Only build for production when:**
- Final release testing
- Deploying to end users
- Verifying binary patching works

### Running Production Build
```bash
cd client/SupportAgent/publish
./SupportAgent.exe --app <process_or_title> --task "<task_description>" --server https://xelth.com/AGI
```

### Flags
- `--app <name>`: Target application (process name or window title)
- `--task "<desc>"`: Natural language task description
- `--server <url>`: Server endpoint (default: https://xelth.com/AGI)
- `--unsafe`: Skip safety confirmations for high-risk actions (use for automation only)

## Command Reference

### UI Automation Commands

#### `click`
Click an element by ID or coordinates.

**Parameters:**
- `elementId` (optional): Target element UUID
- `x, y` (optional): Fallback coordinates if element not found

**Example:**
```json
{ "action": "click", "elementId": "abc-123" }
{ "action": "click", "x": 100, "y": 50 }
```

**Behavior:**
- Ensures window focus before clicking
- Falls back to coordinate click if element unavailable
- Reports success/failure

---

#### `type`
Type text into an element.

**Parameters:**
- `elementId`: Target text field UUID
- `text`: String to type (supports all Unicode characters)

**Example:**
```json
{ "action": "type", "elementId": "abc-123", "text": "Hello World!" }
```

**Behavior:**
- Character-by-character input (75ms delay per char)
- Verification: Checks if text appears in element's `.Value`
- Retries up to 2 times on verification failure
- Focuses element before typing

---

#### `key`
Press special keys or keyboard shortcuts.

**Parameters:**
- `text`: Key command (case-insensitive)

**Supported Keys:**
- `ctrl+a`, `ctrl+c`, `ctrl+v`, `ctrl+x`
- `delete`, `backspace`, `enter`, `escape`/`esc`

**Example:**
```json
{ "action": "key", "text": "ctrl+a" }
{ "action": "key", "text": "delete" }
```

---

#### `select`
Select an item from a dropdown or list.

**Parameters:**
- `elementId`: Dropdown/ListBox UUID
- `text`: Item text to select

**Example:**
```json
{ "action": "select", "elementId": "abc-123", "text": "Option 2" }
```

---

#### `switch_window`
Switch focus to a different window.

**Parameters:**
- `text`: Window title or process name

**Example:**
```json
{ "action": "switch_window", "text": "Calculator" }
{ "action": "switch_window", "text": "notepad" }
```

**Behavior:**
- Process-first matching (handles localization)
- Updates `CurrentWindow` property
- Returns error if window not found

---

#### `wait`
Pause execution.

**Parameters:**
- `delayMs`: Milliseconds to wait

**Example:**
```json
{ "action": "wait", "delayMs": 1000 }
```

---

#### `inspect_screen`
Capture a screenshot for vision analysis.

**Parameters:**
- None (quality controlled server-side)

**Example:**
```json
{ "action": "inspect_screen" }
```

**Behavior:**
- Returns base64-encoded PNG
- Quality: 50% default (configurable: 20/50/70)
- Economy mode: Only on explicit request

---

### OS Operations

#### `os_list`
List directory contents.

**Parameters:**
- `text`: Directory path

**Example:**
```json
{ "action": "os_list", "text": "C:\\Temp" }
```

**Returns:**
```
SUCCESS: [File1.txt, File2.doc, Folder1/]
```

---

#### `os_read`
Read file contents.

**Parameters:**
- `text`: File path
- `elementId` (optional): Max chars (default: 2000)

**Example:**
```json
{ "action": "os_read", "text": "C:\\Temp\\log.txt" }
{ "action": "os_read", "text": "C:\\Temp\\log.txt", "elementId": "5000" }
```

**Returns:**
```
SUCCESS: [file content truncated to max chars]
```

---

#### `os_write`
Write content to a file.

**Parameters:**
- `text`: File path
- `elementId`: Content to write

**Example:**
```json
{ "action": "os_write", "text": "C:\\Temp\\output.txt", "elementId": "Hello World!" }
```

**Returns:**
```
SUCCESS: File written
```

---

#### `os_delete`
Delete a file or directory.

**Parameters:**
- `text`: Path to delete

**Example:**
```json
{ "action": "os_delete", "text": "C:\\Temp\\old_file.txt" }
```

**Safety:** Requires confirmation (unless --unsafe flag)

---

#### `os_run`
Launch a process.

**Parameters:**
- `text`: Executable path or name

**Example:**
```json
{ "action": "os_run", "text": "notepad.exe" }
{ "action": "os_run", "text": "C:\\Program Files\\App\\app.exe" }
```

**Safety:** Requires confirmation (unless --unsafe flag)

**Note:** Does not auto-wait. Use explicit `wait` action if needed.

---

#### `os_kill`
Terminate a process.

**Parameters:**
- `text`: Process name

**Example:**
```json
{ "action": "os_kill", "text": "notepad" }
{ "action": "os_kill", "text": "CalculatorApp" }
```

**Safety:** Requires confirmation (unless --unsafe flag)

---

#### `os_mkdir`
Create a directory.

**Parameters:**
- `text`: Directory path

**Example:**
```json
{ "action": "os_mkdir", "text": "C:\\Temp\\NewFolder" }
```

---

#### `os_exists`
Check if a path exists.

**Parameters:**
- `text`: Path to check

**Example:**
```json
{ "action": "os_exists", "text": "C:\\Temp\\file.txt" }
```

**Returns:**
```
SUCCESS: EXISTS (File)
SUCCESS: EXISTS (Directory)
SUCCESS: NOT FOUND
```

---

### IT Support Commands

#### `os_getenv`
Get environment variable value.

**Parameters:**
- `text`: Variable name

**Example:**
```json
{ "action": "os_getenv", "text": "TEMP" }
{ "action": "os_getenv", "text": "PATH" }
```

**Returns:**
```
SUCCESS: C:\Users\...\AppData\Local\Temp
```

---

#### `reg_read`
Read Windows Registry value.

**Parameters:**
- `text`: Key path
- `elementId`: Value name

**Example:**
```json
{
  "action": "reg_read",
  "text": "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run",
  "elementId": "MyApp"
}
```

**Returns:**
```
SUCCESS: C:\Program Files\MyApp\app.exe
```

**Hives:**
- `HKCU`: HKEY_CURRENT_USER (no admin required)
- `HKLM`: HKEY_LOCAL_MACHINE (read-only without admin)

---

#### `reg_write`
Write Windows Registry value.

**Parameters:**
- `text`: Key path
- `elementId`: Value name
- `value`: Data to write

**Example:**
```json
{
  "action": "reg_write",
  "text": "HKCU\\Software\\MyApp\\Settings",
  "elementId": "Theme",
  "value": "Dark"
}
```

**Safety:**
- Requires confirmation (unless --unsafe flag)
- HKLM writes require Admin elevation

---

#### `net_ping`
Ping a host to check connectivity.

**Parameters:**
- `text`: Hostname or IP

**Example:**
```json
{ "action": "net_ping", "text": "google.com" }
{ "action": "net_ping", "text": "192.168.1.1" }
```

**Returns:**
```
SUCCESS: Ping to google.com: 15ms
```

---

#### `net_port`
Check if a TCP port is open.

**Parameters:**
- `text`: Hostname or IP
- `elementId`: Port number

**Example:**
```json
{ "action": "net_port", "text": "localhost", "elementId": "3232" }
```

**Returns:**
```
SUCCESS: Port 3232 on localhost: OPEN
```

---

### AI & Human Interaction

#### `ask_user`
Request human assistance.

**Parameters:**
- `text`: Question for the user

**Example:**
```json
{ "action": "ask_user", "text": "What is the CAPTCHA code displayed?" }
```

**Behavior:**
- Displays yellow console prompt
- Plays beep sound
- Waits for user input
- Logs response as `USER_SAID: ...` in history

**Use Cases:**
- CAPTCHA solving
- Password entry
- Physical actions (e.g., "Insert USB drive")
- Decision requests

---

#### `read_clipboard`
Read current clipboard contents.

**Parameters:**
- None

**Example:**
```json
{ "action": "read_clipboard" }
```

**Returns:**
- Text content (truncated at 1000 chars)
- Logged in history as `CLIPBOARD: ...`

**Pattern:**
```
1. Select text (Ctrl+A)
2. Copy (Ctrl+C)
3. read_clipboard
```

---

#### `write_clipboard`
Set clipboard contents.

**Parameters:**
- `text`: Content to write to clipboard

**Example:**
```json
{ "action": "write_clipboard", "text": "Text to copy" }
```

**Safety:** Requires confirmation (unless --unsafe flag)

**Use Case:**
- Prepare text for pasting

---

#### `net_search`
Perform web search (server-side).

**Parameters:**
- `text`: Search query

**Example:**
```json
{ "action": "net_search", "text": "Windows 11 registry path for startup apps" }
```

**Behavior:**
- Executed server-side (instant context)
- Results injected into next LLM prompt
- No client overhead

---

#### `create_playbook`
Save successful workflow as reusable playbook.

**Parameters:**
- `text`: Playbook name

**Example:**
```json
{ "action": "create_playbook", "text": "install_office_addin" }
```

**Behavior:**
- Server saves command history as JSON template
- Can be replayed on similar tasks

---

## Safety Protocols

### High-Risk Actions
The following actions require explicit user confirmation (Y/n prompt):
- `os_delete`
- `os_kill`
- `os_run`
- `reg_write`
- `write_clipboard`

### Bypassing Safety Rails
Use the `--unsafe` flag for automated testing:
```bash
./SupportAgent.exe --app notepad --task "..." --unsafe
```

**Warning:** Only use `--unsafe` in controlled environments.

### Logging
- All confirmations logged: `FAILED: User denied ... - Safety check`
- Agent is informed of denials and can adapt strategy

---

## Deployment

### Client Build & Token Injection
```bash
# Windows (requires .NET 8 SDK)
cd client/SupportAgent
build-release.bat

# This creates:
# - publish\SupportAgent.exe (~75MB)
# - Token slot placeholder injected at end of EXE
# - Ready for deployment to server/public/downloads/
```

### Local Token Patching (Development)
```bash
# After build, patch token for local testing
cd client/SupportAgent
patch_token.bat x1_dev_token

# Token is injected into the binary
# Client can now authenticate with production server
```

### Server Deployment
```bash
# SSH to server
ssh antigravity  # Alias for root@152.53.15.15

# Navigate to project
cd /var/www/xelthAGI

# Pull latest code
git pull

# Install dependencies (if needed)
cd server && npm install

# Restart server
pm2 restart xelthAGI

# Check status
pm2 status
pm2 logs xelthAGI --lines 50

# Verify binary patching
npm run verify-patch
```

### Deploy Client Binary to Server
```bash
# Build client locally first
cd client/SupportAgent
build-release.bat

# Upload to server (uses binary patching on server)
# The server will patch tokens dynamically when users download
scp publish\SupportAgent.exe antigravity:/var/www/xelthAGI/server/public/downloads/
```

### Health Check
```bash
curl https://xelth.com/AGI/HEALTH
```

Expected response:
```json
{"status": "OK", "version": "1.0"}
```

---

## Development Workflow (v1.3+)

### Local Development with Token Patching

```bash
# 1. Build the client (injects token slot placeholder)
cd client/SupportAgent
build-release.bat

# 2. Patch token locally for testing
patch_token.bat x1_dev_token

# 3. Run client against production server
cd publish
./SupportAgent.exe --task "..." --server https://xelth.com/AGI
```

### Server-Side Development

```bash
# SSH to server
ssh antigravity

# Deploy latest code
cd /var/www/xelthAGI
git pull

# Restart server
pm2 restart xelthAGI

# Check logs
pm2 logs xelthAGI --lines 50
```

### API Testing (without client)

```bash
# Health check
curl https://xelth.com/AGI/HEALTH

# Test DECIDE endpoint (requires valid token)
TOKEN=$(curl -s https://xelth.com/AGI/DOWNLOAD/CLIENT | grep -o 'x1_[^"]*')
curl -X POST https://xelth.com/AGI/DECIDE \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"ClientId":"test","Task":"test","State":{"WindowTitle":"Test","Elements":[]}}'
```

---

## Testing

### Simple Test
```bash
cd client/SupportAgent/publish
./SupportAgent.exe --app notepad --task "Type: Hello World!" --server https://xelth.com/AGI
```

### Multi-Window Test
```bash
cd client/SupportAgent/publish
./SupportAgent.exe --app notepad --task "1. Type 'Starting...'. 2. Launch calc using os_run. 3. Switch to Calculator. 4. Switch back to Notepad. 5. Type 'Done!'." --unsafe
```

### OS Operations Test
```bash
cd client/SupportAgent/publish
./SupportAgent.exe --app notepad --task "1. Check TEMP env var. 2. List C:\Temp. 3. Type result."
```

### Safety Rails Test (without --unsafe)
```bash
cd client/SupportAgent/publish
./SupportAgent.exe --app notepad --task "Delete C:\Temp using os_delete"
# Should prompt for confirmation
```

---

## Troubleshooting

### Window Not Found
**Symptom:** `switch_window` fails with "Window not found"

**Solutions:**
1. Use process name instead of title (e.g., `"notepad"` not `"Untitled - Notepad"`)
2. Add `wait` action after `os_run` to allow window to appear
3. Check OS localization (e.g., Calculator = "Rechner" in German)

### Loop Detection
**Symptom:** Agent repeats same action 3+ times

**Cause:** Element ID changes or state not detected

**Solution:**
- Loop detection auto-injects warning after 3 repeats
- Agent should try alternative approach (coordinates, different element)

### High Token Usage
**Symptom:** Tasks exceed expected cost

**Solutions:**
1. Enable economy mode (no auto-screenshots)
2. Reduce max steps limit (currently 20)
3. Use more specific task descriptions

---

## Best Practices

1. **Task Descriptions:**
   - Be specific and sequential
   - Use numbered steps for complex tasks
   - Specify exact text to type

2. **Process Names:**
   - Use process names for window matching (not titles)
   - Common processes: `notepad`, `calc`, `mspaint`, `excel`

3. **Waiting:**
   - Add explicit `wait` actions after `os_run`
   - Default wait: 1000ms (1 second)

4. **Safety:**
   - Use `--unsafe` only for automated tests
   - Review confirmation prompts carefully

5. **Debugging:**
   - Check server logs: `pm2 logs xelthAGI`
   - Review client console output
   - Inspect OS_RESULT entries in history

## New Features (v1.1)

### 1. Desktop Mode (No App)
You can now run the agent without specifying a target application. It will attach to the currently active window (e.g., Explorer, Terminal, or whatever is focused).
```bash
./SupportAgent.exe --task "Check my downloads folder"
```

### 2. Interactive Safety
By default, dangerous commands (`os_run`, `os_delete`) trigger a GUI popup.
- **Yes**: Allows action.
- **No**: Blocks action.
- **Don't Know**: Suspends action and asks Agent to explain.

To bypass (for unattended scripts):
```bash
./SupportAgent.exe --task "..." --unsafe
```

### 3. Mission Control v2.0
- **URL**: https://xelth.com/AGI/
- **Dual View**: See what the Agent sees (Left) vs Real Screen (Right).
- **Time Travel**: Click any step in "Action History" to see the screenshot from that moment.
- **Debug Toggle**: Use the switch in the header to enable/disable server-side screenshot saving.
