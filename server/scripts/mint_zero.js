const authService = require('../src/authService');
const fs = require('fs');
const path = require('path');

console.log("🪙  Minting LOCAL DEV Token (ID: 00000000)...");

try {
    // 1. Генерируем токен для ID 0
    // Используем локальные ключи из server/src/config.js
    const payload = {
        cid: "00000000",
        role: "agent",
        tag: "local_dev_fast"
    };

    // Токен на 10 лет
    const token = authService.createToken(payload, 5256000);

    console.log(`\n✅ Generated Token: ${token.substring(0, 20)}...`);

    // 2. Сохраняем его прямо в папку клиента (используем publish для совместимости с Release логикой, и корень для Debug)
    const clientDir = path.join(__dirname, '../../client/SupportAgent');
    const tokenPath = path.join(clientDir, 'dev_token.txt');

    // Убедимся, что папка существует (на случай чистого чекаута)
    if (!fs.existsSync(clientDir)) {
        console.error(`❌ Client directory not found: ${clientDir}`);
        process.exit(1);
    }

    // Encode token as base64 for safe storage in text file
    const tokenBase64 = Buffer.from(token, 'utf8').toString('base64');
    fs.writeFileSync(tokenPath, tokenBase64, { encoding: 'utf8', flag: 'w' });

    // Также копируем в publish/ если папка существует (для совместимости)
    const publishDir = path.join(clientDir, 'publish');
    if (fs.existsSync(publishDir)) {
         fs.writeFileSync(path.join(publishDir, 'dev_token.txt'), tokenBase64, { encoding: 'utf8', flag: 'w' });
         console.log(`💾 Also saved to: ${path.join(publishDir, 'dev_token.txt')}`);
    }

    console.log(`💾 Saved to: ${tokenPath}`);
    console.log(`✅ Token stored as base64 (${tokenBase64.length} chars)`);
    console.log(`   Original token length: ${token.length} bytes`);
    console.log("   Ready for 'fast.bat'!");

} catch (e) {
    console.error("❌ Error:", e.message);
    process.exit(1);
}
