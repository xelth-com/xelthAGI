const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const DB_PATH = path.join(__dirname, '../db/clients.json');
const DB_DIR = path.dirname(DB_PATH);

// Ensure DB directory exists
if (!fs.existsSync(DB_DIR)) {
    fs.mkdirSync(DB_DIR, { recursive: true });
}

class AuthService {
    constructor() {
        this.clients = this._loadDb();
    }

    _loadDb() {
        try {
            if (fs.existsSync(DB_PATH)) {
                return JSON.parse(fs.readFileSync(DB_PATH, 'utf8'));
            }
        } catch (e) {
            console.error("Failed to load auth DB:", e);
        }
        return { clients: [] };
    }

    _saveDb() {
        try {
            fs.writeFileSync(DB_PATH, JSON.stringify(this.clients, null, 2));
        } catch (e) {
            console.error("Failed to save auth DB:", e);
        }
    }

    /**
     * Generates a new structured token
     * Format: x1_{timestamp_base36}_{random_hex}
     * Prefix "x1" denotes Xelth Protocol v1
     */
    createToken(metadata = {}) {
        const version = "x1";
        const timestamp = Date.now().toString(36); // Base36 timestamp
        const random = crypto.randomBytes(16).toString('hex'); // 32 chars

        // Example: x1_lq2w9z_a1b2c3d4e5f6g7h8i9j0k1l2m3n4o5p
        const token = `${version}_${timestamp}_${random}`;

        const newClient = {
            token: token,
            created_at: new Date().toISOString(),
            status: 'active',
            metadata: metadata
        };

        this.clients.clients.push(newClient);
        this._saveDb();

        return token;
    }

    validateToken(token) {
        // Find client and check if active
        const client = this.clients.clients.find(c => c.token === token);
        if (client && client.status === 'active') {
            return client;
        }
        return null;
    }
}

module.exports = new AuthService();
