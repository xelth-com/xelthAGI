const authService = require('../src/authService');

// Test token generation and validation
console.log("=== XLT Token System Test ===\n");

// 1. Generate token
console.log("1. Generating token...");
const payload = {
    cid: 88888888,
    org: "Local Dev Team",
    role: "admin",
    host: "localhost"
};

const token = authService.createToken(payload);
console.log(`   Token: ${token.substring(0, 50)}...`);
console.log(`   Length: ${token.length} chars\n`);

// 2. Validate token
console.log("2. Validating token...");
const client = authService.validateToken(token);

if (client) {
    console.log("   ✅ Token validation PASSED");
    console.log(`   Status: ${client.status}`);
    console.log(`   Created: ${client.created_at}`);
    console.log(`   Payload: ${JSON.stringify(client.payload, null, 2)}\n`);
} else {
    console.log("   ❌ Token validation FAILED\n");
    process.exit(1);
}

// 3. Test invalid token
console.log("3. Testing invalid token...");
const invalidClient = authService.validateToken("xlt_invalid_token");
if (!invalidClient) {
    console.log("   ✅ Invalid token correctly rejected\n");
} else {
    console.log("   ❌ Invalid token should be rejected\n");
    process.exit(1);
}

// 4. Test tampered token
console.log("4. Testing tampered token...");
const tamperedToken = token.slice(0, -10) + "0000000000";
const tamperedClient = authService.validateToken(tamperedToken);
if (!tamperedClient) {
    console.log("   ✅ Tampered token correctly rejected\n");
} else {
    console.log("   ❌ Tampered token should be rejected\n");
    process.exit(1);
}

console.log("=== All tests PASSED ===");
