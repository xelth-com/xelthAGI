const fs = require('fs');
const path = require('path');

// 500 characters placeholder string (MUST be before CLI mode!)
const PLACEHOLDER_TEXT = "XELTH_TOKEN_SLOT_000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000";

// CLI mode: Accept exe path and token
if (require.main === module) {
    const args = process.argv.slice(2);
    if (args.length < 2) {
        console.log("Usage: node patcher.js <exe_path> <token>");
        process.exit(1);
    }
    const exePath = path.resolve(args[0]);
    const token = args[1];

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

const SOURCE_EXE = path.join(__dirname, '../public/downloads/SupportAgent.exe');

function patchExe(exePath, token) {
    if (!fs.existsSync(exePath)) {
        throw new Error("Source executable not found: " + exePath);
    }

    if (token.length > PLACEHOLDER_TEXT.length) {
        throw new Error(`Token too long! Max ${PLACEHOLDER_TEXT.length}, got ${token.length}`);
    }

    const binary = fs.readFileSync(exePath);
    const searchBuf = Buffer.from(PLACEHOLDER_TEXT, 'utf16le');

    // Pad with spaces to match slot length
    const paddedToken = token.padEnd(PLACEHOLDER_TEXT.length, ' ');
    const replaceBuf = Buffer.from(paddedToken, 'utf16le');

    // Search from end of file (it's appended)
    const searchStart = Math.max(4096, binary.length - 2000);
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
        throw new Error("Placeholder not found in binary! Need to rebuild client.");
    }

    const patched = Buffer.alloc(binary.length);
    binary.copy(patched);
    replaceBuf.copy(patched, offset);

    return patched;
}

function generatePatchedBinary(token) {
    return patchExe(SOURCE_EXE, token);
}

module.exports = { generatePatchedBinary, patchExe };
