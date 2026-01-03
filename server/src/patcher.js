const fs = require('fs');
const path = require('path');

// Target the compiled binary.
// In a real scenario, this should be a "clean" template file, not the active one.
// We assume 'SupportAgent.exe' in public/downloads is the template.
const SOURCE_EXE = path.join(__dirname, '../public/downloads/SupportAgent.exe');

// MUST match the C# constant exactly
const PLACEHOLDER_TEXT = "XELTH_TOKEN_SLOT_00000000000000000000000000000000000000000000000";

function generatePatchedBinary(token) {
    if (!fs.existsSync(SOURCE_EXE)) {
        throw new Error("Source executable not found at: " + SOURCE_EXE);
    }

    if (token.length > PLACEHOLDER_TEXT.length) {
        throw new Error(`Token too long! Max ${PLACEHOLDER_TEXT.length}, got ${token.length}`);
    }

    // 1. Read binary
    const binary = fs.readFileSync(SOURCE_EXE);

    // 2. Prepare buffers (UTF-16LE for .NET strings)
    const searchBuf = Buffer.from(PLACEHOLDER_TEXT, 'utf16le');

    // Pad token with spaces to match slot length exactly
    const paddedToken = token.padEnd(PLACEHOLDER_TEXT.length, ' ');
    const replaceBuf = Buffer.from(paddedToken, 'utf16le');

    // 3. Find placeholder - search at the end of the file
    // The placeholder is appended after the last PE section by inject_token_slot.ps1
    // We search the last 512 bytes where the injection is expected
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

    console.log(`   Found placeholder at offset ${offset}`);

    // 4. Create new buffer and patch
    const patched = Buffer.alloc(binary.length);
    binary.copy(patched);
    replaceBuf.copy(patched, offset);

    return patched;
}

module.exports = { generatePatchedBinary };
