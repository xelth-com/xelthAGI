const patcher = require('./src/patcher');
const fs = require('fs');
const path = require('path');

// Colors for console
const RED = '\x1b[31m';
const GREEN = '\x1b[32m';
const RESET = '\x1b[0m';

console.log(' Testing Binary Patching Logic...');

const exePath = path.join(__dirname, 'public', 'downloads', 'SupportAgent.exe');

if (!fs.existsSync(exePath)) {
    console.log(`${RED} Error: SupportAgent.exe not found in public/downloads/${RESET}`);
    console.log('   Please build the client and copy it there first.');
    process.exit(1);
}

try {
    const testToken = "x1_TEST_TOKEN_VERIFICATION_0000000000";
    console.log(`   Target File: ${exePath}`);
    console.log(`   File Size: ${fs.statSync(exePath).size} bytes`);

    // Attempt to patch
    const patchedBuffer = patcher.generatePatchedBinary(testToken);

    // Verify the output contains our test token (in UTF-16LE)
    const tokenBuffer = Buffer.from(testToken, 'utf16le');
    if (patchedBuffer.indexOf(tokenBuffer) !== -1) {
        console.log(`${GREEN} SUCCESS: Placeholder found and patched correctly!${RESET}`);
        console.log('   The binary patching system is operational.');
    } else {
        console.log(`${RED} FAILURE: Patch function ran, but token not found in output.${RESET}`);
    }

} catch (e) {
    console.log(`${RED} ERROR: ${e.message}${RESET}`);
    console.log('   Likely cause: The placeholder string was optimized away by the C# compiler');
    console.log('   or the encoding does not match (UTF-16LE expected).');
}
