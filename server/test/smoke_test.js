const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');

// Config
const BASE_URL = 'http://localhost:5000';

async function runTests() {
    console.log("🔥 STARTING SMOKE TEST...");

    // 1. HEALTH CHECK
    console.log("\n1️⃣  Testing /HEALTH...");
    try {
        const res = await fetch(`${BASE_URL}/HEALTH`);
        if (res.status === 200) {
            const data = await res.json();
            console.log("   ✅ Server is UP:", JSON.stringify(data));
        } else {
            throw new Error(`Status ${res.status}`);
        }
    } catch (e) {
        console.error("   ❌ Server is DOWN or unreachable on port 5000. Is it running?");
        console.error("      Error:", e.message);
        process.exit(1);
    }

    // 2. AUTH SECURITY (Negative Test)
    console.log("\n2️⃣  Testing Security (Unauthorized Access)...");
    try {
        const res = await fetch(`${BASE_URL}/DECIDE`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({})
        });

        if (res.status === 401) {
            console.log("   ✅ Security Active: Request rejected (401 Unauthorized)");
        } else {
            console.error(`   ❌ SECURITY FAIL: Expected 401, got ${res.status}`);
        }
    } catch (e) { console.error("   ❌ Request failed:", e.message); }

    // 3. BINARY FACTORY (Download & Patch)
    console.log("\n3️⃣  Testing Binary Factory (/DOWNLOAD/CLIENT)...");
    let downloadedToken = "";
    try {
        const res = await fetch(`${BASE_URL}/DOWNLOAD/CLIENT`);
        if (res.status === 200) {
            const buffer = await res.arrayBuffer();
            const binary = Buffer.from(buffer);
            console.log(`   ✅ Downloaded ${binary.length} bytes`);

            // SEARCH FOR TOKEN
            // We look for "x1_" prefix in UTF-16LE
            const sig = Buffer.from("x1_", 'utf16le');
            const idx = binary.indexOf(sig);

            if (idx !== -1) {
                // Read 64 chars (128 bytes) from that position
                const tokenBuffer = binary.subarray(idx, idx + 128);
                // Remove null bytes to read as ascii for logging
                const rawString = tokenBuffer.toString().replace(/\0/g, '');
                downloadedToken = rawString.trim();
                console.log(`   ✅ Token Injected: "${downloadedToken.substring(0, 20)}..."`);
            } else {
                console.error("   ❌ Token NOT found in binary!");
            }

        } else {
            console.error(`   ❌ Download Failed: ${res.status}`);
        }
    } catch (e) { console.error("   ❌ Download Error:", e.message); }

    // 4. AUTH VALIDATION (Positive Test)
    if (downloadedToken) {
        console.log("\n4️⃣  Testing Access with New Token...");
        try {
            const res = await fetch(`${BASE_URL}/DECIDE`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'Authorization': `Bearer ${downloadedToken}`
                },
                body: JSON.stringify({
                    ClientId: "TEST_BOT",
                    Task: "Smoke Test",
                    State: { WindowTitle: "Test", ProcessName: "test", Elements: [] }
                })
            });

            if (res.status === 200) {
                const data = await res.json();
                console.log("   ✅ Auth Accepted! Server response:", data.Success ? "Success" : "Valid Response");
            } else {
                console.error(`   ❌ Auth Failed: ${res.status}`);
            }
        } catch (e) { console.error("   ❌ Auth Request Error:", e.message); }
    }

    console.log("\n🏁 TEST COMPLETE");
}

runTests();
