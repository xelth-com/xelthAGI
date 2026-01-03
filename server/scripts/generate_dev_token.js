const authService = require('../src/authService');

try {
    // Form payload for dev environment
    // cid = 88888888 (Dev ID)
    const payload = {
        cid: 88888888,
        org: "Local Dev Team",
        role: "admin",
        host: "localhost"
    };

    // Generate XLT token
    const token = authService.createToken(payload);

    console.log(token);
} catch (e) {
    console.error("Error generating token:", e);
    process.exit(1);
}
