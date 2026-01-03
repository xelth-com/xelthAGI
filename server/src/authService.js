const crypto = require('crypto');
const config = require('./config');

// Configuration
const ALGORITHM = 'aes-256-cbc';
const IV_LENGTH = 16; // For AES
const SEPARATOR = '_';

class AuthService {
    constructor() {
        // Load keys and sort by creation date (newest first)
        this.keys = config.KEY_STORE.sort((a, b) => b.created - a.created);
    }

    /**
     * Get active key pair for given timestamp (or now)
     */
    _getKeys(timestamp = Date.now()) {
        // Find first key that was created BEFORE this time
        const keySet = this.keys.find(k => k.created <= timestamp);
        if (!keySet) {
            throw new Error(`No valid keys found for timestamp ${timestamp}`);
        }
        return keySet;
    }

    /**
     * Create encrypted token (XLT)
     * @param {Object} payloadData - Data { cid, org, role }
     * @param {number} expiresInMinutes - Lifetime in minutes
     */
    createToken(payloadData, expiresInMinutes = 60 * 24 * 365) { // Default 1 year for dev
        const now = Date.now();
        const expiresAt = now + (expiresInMinutes * 60 * 1000);

        // 1. Select keys (active now)
        const keys = this._getKeys(now);

        // 2. Prepare metadata
        const genStr = now.toString(36);
        const expStr = expiresAt.toString(36);

        // 3. Encrypt Payload (AES-256-CBC)
        const iv = crypto.randomBytes(IV_LENGTH);
        const cipher = crypto.createCipheriv(ALGORITHM, Buffer.from(keys.encKey), iv);

        const jsonPayload = JSON.stringify(payloadData);
        let encrypted = cipher.update(jsonPayload, 'utf8', 'hex');
        encrypted += cipher.final('hex');

        const ivHex = iv.toString('hex');

        // 4. Build body for signature
        // xlt_GEN_EXP_IV_PAYLOAD
        const tokenBody = `xlt${SEPARATOR}${genStr}${SEPARATOR}${expStr}${SEPARATOR}${ivHex}${SEPARATOR}${encrypted}`;

        // 5. Sign (HMAC-SHA256)
        const signature = crypto
            .createHmac('sha256', keys.sigKey)
            .update(tokenBody)
            .digest('hex'); // 64 chars

        // Final token
        return `${tokenBody}${SEPARATOR}${signature}`;
    }

    /**
     * Validate and decrypt token
     */
    validateToken(token) {
        if (!token || !token.startsWith('xlt_')) return null;

        const parts = token.split(SEPARATOR);
        // Expecting: [xlt, gen, exp, iv, payload, sig]
        if (parts.length !== 6) return null;

        const [prefix, genStr, expStr, ivHex, encrypted, providedSig] = parts;

        // 1. Quick expiration check (public data)
        const expiresAt = parseInt(expStr, 36);
        if (Date.now() > expiresAt) {
            console.log("Token expired");
            return null;
        }

        // 2. Lookup keys by generation date
        const genTime = parseInt(genStr, 36);
        let keys;
        try {
            keys = this._getKeys(genTime);
        } catch (e) {
            console.error("Key lookup failed:", e.message);
            return null;
        }

        // 3. Verify signature
        const tokenBody = `${prefix}${SEPARATOR}${genStr}${SEPARATOR}${expStr}${SEPARATOR}${ivHex}${SEPARATOR}${encrypted}`;
        const expectedSig = crypto
            .createHmac('sha256', keys.sigKey)
            .update(tokenBody)
            .digest('hex');

        if (!crypto.timingSafeEqual(Buffer.from(providedSig), Buffer.from(expectedSig))) {
            console.error("Invalid Token Signature");
            return null;
        }

        // 4. Decrypt
        try {
            const decipher = crypto.createDecipheriv(ALGORITHM, Buffer.from(keys.encKey), Buffer.from(ivHex, 'hex'));
            let decrypted = decipher.update(encrypted, 'hex', 'utf8');
            decrypted += decipher.final('utf8');

            const payload = JSON.parse(decrypted);

            // Return client object in format expected by app
            return {
                token: token, // token itself as ID
                payload: payload, // decrypted data
                created_at: new Date(genTime).toISOString(),
                status: 'active'
            };
        } catch (e) {
            console.error("Decryption failed:", e.message);
            return null;
        }
    }
}

module.exports = new AuthService();
