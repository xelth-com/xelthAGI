const authService = require('../src/authService');
const crypto = require('crypto');

try {
    // Generate unique client ID for this session
    const clientId = crypto.randomBytes(16).toString('hex');

    // AGENT TOKEN: Full access (can execute commands + view console)
    const agentPayload = {
        cid: clientId,
        role: "agent"
    };
    const agentToken = authService.createToken(agentPayload);

    // CONSOLE TOKEN: Read-only (can only view console, no commands)
    const consolePayload = {
        cid: clientId,
        role: "view"
    };
    const consoleToken = authService.createToken(consolePayload);

    // Output both tokens (agent token is used by the client executable)
    // Format: AGENT_TOKEN\nCONSOLE_TOKEN
    console.log(agentToken);
    console.error(`CONSOLE:${consoleToken}`); // Output to stderr for separate capture
} catch (e) {
    console.error("Error generating tokens:", e);
    process.exit(1);
}
