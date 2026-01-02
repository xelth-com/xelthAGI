# Project Access & Credentials Reference

## Access & Credentials

The following confidential files are available locally but not included in snapshots:

- **SERVER_ACCESS.md**: `C:\Users\Dmytro\xelthagi\.eck\SERVER_ACCESS.md`

> **Note**: These files contain sensitive information and should only be accessed when needed.
> They are excluded from snapshots for security reasons but can be referenced on demand.

---

## 🔧 Agent Eye Self-Awareness Test

### Overview
The "Agent Eye" test verifies that the agent can perceive its own actions through the server API - a complete feedback loop where the agent can query what the server sees.

### Test Architecture
```
Agent Action → Server Perception → API State → Agent Verification → Confirmation
     ✅              ✅               ✅              ⚠️                ❌
```

### How It Works

1. **Agent performs action** (e.g., types text in Notepad)
2. **Server captures UI state** in real-time via FlaUI
3. **API endpoint** (`GET /AGI/api/state`) returns current state as JSON
4. **Agent queries API** to verify its action was captured
5. **Agent confirms** by typing verification message

### Test Results (2026-01-02)

**✅ WORKING:**
- Agent successfully types test ID
- Server captures action immediately
- API returns accurate state (window title, text value, action history)
- Client ↔ Server synchronization: 100%

**⚠️ BLOCKED:**
- Agent cannot autonomously execute `os_run` commands
- Requires interactive console approval (Y/n prompt)
- Client-side permission system blocks `curl` execution
- Agent reached MAX_STEPS=50 trying to bypass

### Permission System Behavior

The client has a **safety system** that requires manual approval for:
- `os_run` (execute system commands)
- `write_clipboard` (modify clipboard)
- Other potentially risky operations

**Important:** Even if the server approves an action (✅ User response recorded), the client-side console still requires manual "Y" input. This creates a double-approval system.

### Running Agent Eye Test

```bash
cd client/SupportAgent

dotnet run -- --app notepad \
  --task "1. Type 'AGENT_SELF_TEST_ID_777' in Notepad. 2. Execute 'cmd.exe' with arguments '/c curl -s https://xelth.com/AGI/api/state > %TEMP%\\api_state.json' using os_run to capture server perception. 3. Read the file '%TEMP%\\api_state.json' using os_read. 4. Analyze the JSON content in your history. If it contains 'AGENT_SELF_TEST_ID_777', type ' - SYNC CONFIRMED via API' in Notepad." \
  --server https://xelth.com/AGI
```

**Expected behavior:**
1. Agent types test ID successfully ✅
2. Agent requests permission for `os_run` multiple times ⚠️
3. Without manual approval, agent will retry until MAX_STEPS
4. Agent stops gracefully at step 50

### Manual Verification (Workaround)

Since autonomous verification is blocked, verify manually:

```bash
# Query the API to check agent state
curl -s https://xelth.com/AGI/api/state | head -100

# Look for:
# - "WindowTitle": "*AGENT_SELF_TEST_ID_777 – Notepad"
# - "Value": "AGENT_SELF_TEST_ID_777"
# - History contains: "type ... AGENT_SELF_TEST_ID_777"
```

### API Endpoint Details

**Endpoint:** `GET https://xelth.com/AGI/api/state`

**Response structure:**
```json
{
  "lastSeen": "2026-01-02T12:03:05.559Z",
  "clientId": "cd3ad58e977440559efeca8aa609c3c1",
  "uiState": {
    "WindowTitle": "*AGENT_SELF_TEST_ID_777 – Notepad",
    "ProcessName": "11580",
    "Elements": [
      {
        "Id": "66265e06-6697-463a-9f0b-1991b0d0db8c",
        "Name": "Text-Editor",
        "Type": "Document",
        "Value": "AGENT_SELF_TEST_ID_777",
        "IsEnabled": true,
        "Bounds": {"X":139,"Y":157,"Width":1139,"Height":479}
      }
    ]
  },
  "task": "...",
  "history": [
    "type ec16071a... AGENT_SELF_TEST_ID_777 [Content Modified: 72→96 chars]"
  ],
  "lastDecision": {
    "action": "os_exists",
    "message": "...",
    "reasoning": "..."
  },
  "screenshot": "/9j/4AAQSkZJRg..." // base64
}
```

### Recommendations for Future Tests

**Option A: Pre-approve mode**
- Create a `--test-mode` flag that auto-approves safe operations
- Only for controlled test environments

**Option B: Dedicated test endpoint**
- Create `/AGI/api/self-check` endpoint
- Agent can query without `os_run`
- Returns boolean: `{synced: true, testId: "AGENT_SELF_TEST_ID_777"}`

**Option C: Human-in-loop (current)**
- Agent requests permission via `ask_user`
- Human manually runs verification command
- Safest but requires manual intervention

### Server-Side Validation

The server logs (PM2) show perfect capture of all agent actions:

```bash
# SSH to server (see SSH section below)
ssh antigravity "pm2 logs xelthAGI --lines 100 --nostream"

# Look for:
# 👤 Client: cd3ad58e977440559efeca8aa609c3c1
# 🤖 Decision: [reasoning]
# 📤 Command: type/click/ask_user/os_run
```

---

## 🌐 SSH Access to Server

### ⚠️ IMPORTANT: Use "antigravity" Alias

**DO NOT** connect via `ssh root@xelth.com` directly - it will fail with "Permission denied".

**CORRECT METHOD:**
```bash
# Use the configured alias
ssh antigravity

# Or use IP directly with key
ssh -i ~/.ssh/netcup root@152.53.15.15
```

### SSH Configuration

The SSH config is located at `~/.ssh/config`:

```
Host antigravity
    HostName 152.53.15.15
    User root
    IdentityFile ~/.ssh/netcup
```

### Why Direct Connection Fails

1. **Host key verification**: `xelth.com` may not be in `known_hosts`
2. **No SSH key specified**: Connection attempts password auth (disabled)
3. **Domain vs IP**: Server expects connection via IP 152.53.15.15

**First-time connection will add host key:**
```bash
ssh antigravity
# Warning: Permanently added '152.53.15.15' (ED25519) to the list of known hosts.
```

### Common SSH Commands

```bash
# Test connection
ssh antigravity "echo 'Connected successfully'"

# Check server status
ssh antigravity "pm2 status"

# View logs
ssh antigravity "pm2 logs xelthAGI --lines 50 --nostream"

# Restart service
ssh antigravity "pm2 restart xelthAGI"

# Check .env configuration
ssh antigravity "cat /var/www/xelthAGI/server/.env"

# Update code from git
ssh antigravity "cd /var/www/xelthAGI && git pull && cd server && npm install && pm2 restart xelthAGI"
```

### Troubleshooting SSH

**Problem: "Permission denied (publickey,password)"**
```bash
# Check if SSH key exists
ls -la ~/.ssh/netcup

# Check SSH config
cat ~/.ssh/config

# Use verbose mode to debug
ssh -v antigravity
```

**Problem: "Host key verification failed"**
```bash
# Add host to known_hosts
ssh -o StrictHostKeyChecking=no antigravity "echo test"
```

---

## 📡 Mission Control Dashboard

The project includes a web-based Mission Control dashboard for monitoring agent activity.

**Access:** `https://xelth.com/AGI/` (deployed on server)

**Features:**
- Real-time agent monitoring
- Screenshot display
- Task history
- Client status (online/offline)
- API state visualization

**Local Development:**
```bash
cd dashboard
# Open index.html in browser
# Configure API_BASE in script to point to server
```

---

## 🚀 Quick Reference

### Run Agent
```bash
cd client/SupportAgent
dotnet run -- --app notepad --task "Type hello world" --server https://xelth.com/AGI
```

### Query API State
```bash
curl -s https://xelth.com/AGI/api/state | jq .
```

### Check Server Logs
```bash
ssh antigravity "pm2 logs xelthAGI --lines 50 --nostream"
```

### Update Server Code
```bash
# On local machine
git add .
git commit -m "description"
git push origin main

# On server
ssh antigravity "cd /var/www/xelthAGI && git pull && pm2 restart xelthAGI"
```

---

## ⚙️ Client Configuration

### MAX_STEPS Limit
The client has a `MAX_STEPS=50` safety limit to prevent infinite loops.

**Server-side:** `/var/www/xelthAGI/server/.env`
```bash
MAX_STEPS=50
```

**Effect:** Agent will stop after 50 decision cycles, even if task incomplete.

### Permission System
Client requires manual approval for:
- `os_run` - Execute system commands
- `write_clipboard` - Modify clipboard
- File operations outside workspace

**Bypass (testing only):**
- Manually press 'Y' at console when prompted
- Or modify client code to auto-approve specific commands

---

## 📝 Known Issues

### 1. Agent Cannot Self-Verify Autonomously
**Issue:** Permission system blocks `os_run` for `curl` command
**Workaround:** Query API manually or approve at console
**Status:** By design (security feature)

### 2. SSH Connection via xelth.com Fails
**Issue:** Direct SSH to domain name doesn't work
**Solution:** Use `ssh antigravity` alias instead
**Status:** Fixed with SSH config

### 3. Server Missing Dependencies
**Issue:** `duck-duck-scrape` module not installed
**Impact:** Web search functionality disabled (not critical)
**Fix:** `ssh antigravity "cd /var/www/xelthAGI/server && npm install"`

### 4. CLAUDE_API_KEY Warning
**Issue:** Claude API key not configured on server
**Impact:** None (server uses Gemini as primary LLM)
**Status:** Expected, can ignore

---

## 🎯 Testing Checklist

Before running tests:

- [ ] Server is running: `curl https://xelth.com/AGI/HEALTH`
- [ ] SSH access works: `ssh antigravity "echo test"`
- [ ] Client builds: `cd client/SupportAgent && dotnet build`
- [ ] Notepad is open (or target app)
- [ ] For `os_run` tests: Ready to approve at console
- [ ] PM2 logs accessible: `ssh antigravity "pm2 logs xelthAGI"`

After running tests:

- [ ] Check API state: `curl https://xelth.com/AGI/api/state`
- [ ] Review server logs: `ssh antigravity "pm2 logs xelthAGI --lines 100"`
- [ ] Verify client exit code: `echo $?` (should be 0)
- [ ] Check Notepad content matches expected
- [ ] Close test applications

---

## 📚 Additional Resources

- **Full server docs:** `.eck/SERVER_ACCESS.md`
- **GitHub:** https://github.com/xelth-com/xelthAGI
- **Server health:** https://xelth.com/AGI/HEALTH
- **Mission Control:** https://xelth.com/AGI/