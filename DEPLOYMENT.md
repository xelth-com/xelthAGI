# XelthAGI Server Deployment Guide

Deploy the AI automation server as a microservice on port 3232.

> **⚠️ IMPORTANT:** All API paths use UPPERCASE for QR code optimization!
> Uppercase letters enable alphanumeric encoding in QR codes, reducing QR size and fitting more information.

## Prerequisites

- Ubuntu/Debian Linux server
- Node.js 18+ installed
- Git installed
- API keys for Gemini or Claude

## Quick Deployment Steps

### 1. Clone Repository

```bash
cd /var/www
git clone https://github.com/xelth-com/xelthAGI.git
cd xelthAGI/server
```

### 2. Install Dependencies

```bash
npm install
```

### 3. Configure Environment

```bash
cp .env.example .env
nano .env
```

**Edit .env:**
```bash
# LLM Configuration
LLM_PROVIDER=gemini  # or "claude"

# API Keys
CLAUDE_API_KEY=your_claude_api_key_here
GEMINI_API_KEY=your_gemini_api_key_here

# Model Selection
CLAUDE_MODEL=claude-sonnet-4-5-20250929
GEMINI_MODEL=gemini-3-flash-preview

# Server Configuration
HOST=0.0.0.0
PORT=3232
DEBUG=false

# Agent Configuration
MAX_STEPS=50
TEMPERATURE=0.7
```

### 4. Test Server

```bash
npm start
```

Server should start on `http://0.0.0.0:3232`

Test health endpoint:
```bash
curl http://localhost:3232/HEALTH
```

### 5. Setup as Systemd Service (Production)

Create service file:
```bash
sudo nano /etc/systemd/system/xelth-agi.service
```

**Service content:**
```ini
[Unit]
Description=XelthAGI Automation Server
After=network.target

[Service]
Type=simple
User=www-data
WorkingDirectory=/var/www/xelthAGI/server
ExecStart=/usr/bin/node src/index.js
Restart=on-failure
RestartSec=10
StandardOutput=journal
StandardError=journal
SyslogIdentifier=xelth-agi

# Environment
Environment="NODE_ENV=production"

[Install]
WantedBy=multi-user.target
```

**Enable and start:**
```bash
sudo systemctl daemon-reload
sudo systemctl enable xelth-agi
sudo systemctl start xelth-agi
sudo systemctl status xelth-agi
```

**View logs:**
```bash
sudo journalctl -u xelth-agi -f
```

### 6. Alternative: PM2 (Recommended for Node.js)

Install PM2:
```bash
sudo npm install -g pm2
```

Start server:
```bash
cd /var/www/xelthAGI/server
pm2 start src/index.js --name xelth-agi
pm2 save
pm2 startup
```

View status and logs:
```bash
pm2 status
pm2 logs xelth-agi
pm2 monit
```

### 7. Configure Nginx Reverse Proxy (Optional)

If you want to expose via domain (e.g., xelth.com/AGI):

```bash
sudo nano /etc/nginx/sites-available/xelth-agi
```

**Nginx config:**
```nginx
server {
    listen 80;
    server_name xelth.com;

    # NOTE: UPPERCASE paths for QR code optimization
    location /AGI/ {
        proxy_pass http://localhost:3232/;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection 'upgrade';
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_cache_bypass $http_upgrade;

        # Timeout settings for long requests
        proxy_connect_timeout 120s;
        proxy_send_timeout 120s;
        proxy_read_timeout 120s;
    }
}
```

Enable site:
```bash
sudo ln -s /etc/nginx/sites-available/xelth-agi /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl reload nginx
```

### 8. Firewall Configuration

Open port 3232 (if direct access needed):
```bash
sudo ufw allow 3232/tcp
sudo ufw status
```

For nginx only:
```bash
sudo ufw allow 'Nginx Full'
```

## Updating the Server

```bash
cd /var/www/xelthAGI
git pull

cd server
npm install

# Restart service
sudo systemctl restart xelth-agi
# OR with PM2
pm2 restart xelth-agi
```

## Monitoring

### Check service status:
```bash
# Systemd
sudo systemctl status xelth-agi

# PM2
pm2 status
```

### View logs:
```bash
# Systemd
sudo journalctl -u xelth-agi -f

# PM2
pm2 logs xelth-agi --lines 100
```

### Monitor resource usage:
```bash
# PM2
pm2 monit

# System
htop
```

## Troubleshooting

### Service won't start:
```bash
# Check logs
sudo journalctl -u xelth-agi -n 50

# Test manually
cd /var/www/xelthAGI/server
node src/index.js
```

### Port already in use:
```bash
# Find process on port 3232
sudo lsof -i :3232
sudo netstat -tulpn | grep 3232

# Kill if needed
sudo kill -9 <PID>
```

### Environment variables not loading:
```bash
# Check .env exists
ls -la /var/www/xelthAGI/server/.env

# Verify permissions
sudo chown -R www-data:www-data /var/www/xelthAGI
```

### API errors:
```bash
# Test API keys
curl -X POST http://localhost:3232/HEALTH

# Check .env configuration
cat /var/www/xelthAGI/server/.env
```

## Security Recommendations

1. **Don't expose port 3232 directly** - use nginx reverse proxy
2. **Use HTTPS** - setup SSL certificate with Let's Encrypt
3. **Restrict access** - use firewall rules or nginx IP whitelist
4. **Keep API keys secure** - never commit .env to git
5. **Regular updates** - keep Node.js and dependencies updated

## URLs After Deployment

> **⚠️ ALL PATHS MUST BE UPPERCASE** for QR code optimization!

- **Direct:** `http://your-server-ip:3232`
- **With nginx:** `http://xelth.com/AGI`
- **Health check:** `http://your-server-ip:3232/HEALTH`
- **Decision endpoint:** `http://your-server-ip:3232/DECIDE`

## Client Configuration

Update client server URL to:
```
http://your-server-ip:3232
```

Or if using nginx:
```
https://xelth.com/AGI
```

**Remember:** All endpoint paths must be UPPERCASE (e.g., `/HEALTH`, `/DECIDE`)

---

**Deployment complete!** 🚀

For issues, check logs and GitHub issues: https://github.com/xelth-com/xelthAGI/issues
