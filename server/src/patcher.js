const fs = require('fs');
const path = require('path');

// CLI mode: Accept exe path and token as arguments
if (require.main === module) {
    const args = process.argv.slice(2);
    if (args.length < 2) {
        console.log("Usage: node patcher.js <exe_path> <token>");
        console.log("Example: node patcher.js ../client/SupportAgent/publish/SupportAgent.exe x1_test_123");
        process.exit(1);
    }

    const exePath = path.resolve(args[0]);
    const token = args[1];

    console.log(`Patching: ${exePath}`);
    console.log(`Token: ${token}`);

    try {
        const patched = patchExe(exePath, token);
        fs.writeFileSync(exePath, patched);
        console.log("SUCCESS: Binary patched");
    } catch (e) {
        console.error("ERROR:", e.message);
        process.exit(1);
    }
    process.exit(0);
}

// Target the compiled binary.
// In a real scenario, this should be a "clean" template file, not the active one.
// We assume 'SupportAgent.exe' in public/downloads is the template.
const SOURCE_EXE = path.join(__dirname, '../public/downloads/SupportAgent.exe');

// MUST match the C# constant exactly
const PLACEHOLDER_TEXT = "XELTH_TOKEN_SLOT_00000000000000000000000000000000000000000000000";

function patchExe(exePath, token) {
    if (!fs.existsSync(exePath)) {
        throw new Error("Source executable not found: " + exePath);
    }

    if (token.length > PLACEHOLDER_TEXT.length) {
        throw new Error(`Token too long! Max ${PLACEHOLDER_TEXT.length}, got ${token.length}`);
    }

    // 1. Read binary
    const binary = fs.readFileSync(exePath);

    // 2. Prepare buffers (UTF-16LE for .NET strings)
    const searchBuf = Buffer.from(PLACEHOLDER_TEXT, 'utf16le');

    // Pad token with spaces to match slot length exactly
    const paddedToken = token.padEnd(PLACEHOLDER_TEXT.length, ' ');
    const replaceBuf = Buffer.from(paddedToken, 'utf16le');

    // 3. Find placeholder - search at the end of the file
    const searchStart = Math.max(4096, binary.length - 512);
    let offset = -1;

    for (let i = searchStart; i <= binary.length - searchBuf.length; i++) {
        let found = true;
        for (let j = 0; j < searchBuf.length; j++) {
            if (binary[i + j] !== searchBuf[j]) {
                found = false;
                break;
            }
        }
        if (found) {
            offset = i;
            break;
        }
    }

    if (offset === -1) {
        throw new Error("Placeholder not found in binary! Run inject_token_slot.ps1 during build.");
    }

    // 4. Create new buffer and patch
    const patched = Buffer.alloc(binary.length);
    binary.copy(patched);
    replaceBuf.copy(patched, offset);

    return patched;
}

function generatePatchedBinary(token) {
    return patchExe(SOURCE_EXE, token);
}

module.exports = { generatePatchedBinary, patchExe };
