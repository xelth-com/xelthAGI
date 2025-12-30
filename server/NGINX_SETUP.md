# Nginx Setup for File Downloads

## Overview
This guide explains how to configure Nginx to serve static files from the `server/downloads/` directory. This allows the LLM to instruct clients to download files directly from the server without using Node.js.

## Nginx Configuration

Add the following location block to your Nginx configuration file (e.g., `/etc/nginx/sites-available/xelth.com.conf`):

```nginx
# STATIC FILE SERVING for AGI downloads
# NOTE: Using a specific path /AGI/DOWNLOADS/ to avoid conflicts and optimize QR
location /AGI/DOWNLOADS/ {
    alias /var/www/xelthAGI/server/downloads/;
    # Ensure the Nginx user (usually www-data) has read permissions for this folder
    autoindex off; # Disable directory listing
    sendfile on;   # Optimize file transfer

    # Cache settings for static files (optional, but recommended)
    expires 30d;
    add_header Cache-Control "public, no-transform";

    # If file not found, return 404
    try_files $uri $uri/ =404;
}

# xelthAGI - AI Automation API (YOUR EXISTING SETUP)
# NOTE: UPPERCASE paths for QR code optimization (alphanumeric encoding = smaller QR)
location /AGI/ {
    proxy_pass http://localhost:3232/;
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection 'upgrade';
    proxy_set_header Host $host;
    proxy_set_header X-Real-IP $remote_addr;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_cache_bypass $http_upgrade;

    # Increased timeouts for long-running AI requests
    proxy_connect_timeout 120s;
    proxy_send_timeout 120s;
    proxy_read_timeout 120s;
}
```

**IMPORTANT**: The `/AGI/DOWNLOADS/` location block must come BEFORE the `/AGI/` proxy block, so Nginx checks for static files first.

## Steps to Apply

1. Edit your Nginx configuration:
   ```bash
   sudo nano /etc/nginx/sites-available/xelth.com.conf
   ```

2. Add the location block above.

3. Verify the path in `alias` directive matches your actual server path:
   ```bash
   # Check if path exists
   ls /var/www/xelthAGI/server/downloads/
   ```

4. Test Nginx configuration:
   ```bash
   nginx -t
   ```

5. Reload Nginx:
   ```bash
   systemctl reload nginx
   ```

6. Set correct permissions (if needed):
   ```bash
   # Ensure Nginx user can read files
   sudo chown -R www-data:www-data /var/www/xelthAGI/server/downloads/
   sudo chmod -R 755 /var/www/xelthAGI/server/downloads/
   ```

## Testing

1. Place a test file in the downloads folder:
   ```bash
   echo "Test file" > /var/www/xelthAGI/server/downloads/test.txt
   ```

2. Access it via browser or curl:
   ```bash
   curl https://xelth.com/AGI/DOWNLOADS/test.txt
   ```

   You should see "Test file" in the response.

## Security Considerations

- **Public Access**: Any file in `server/downloads/` will be publicly accessible to anyone who knows the URL.
- **For Private Files**: Consider:
  - IP whitelisting in Nginx
  - Token-based authentication (requires proxying through Node.js)
  - Storing sensitive files elsewhere

## Usage in Playbooks

In your playbook markdown files, reference downloads like this:

```markdown
## Variables
- DownloadUrl: "https://xelth.com/AGI/DOWNLOADS/InBodySuite_setup.exe"
- InstallerName: "InBodySuite_setup.exe"

## Steps
1. **Download Installer**
   - Action: Download file from `{DownloadUrl}`
   - Save as: `{InstallerName}`
```

The LLM will generate a `download` command with the URL, and the client will download the file to the user's Downloads folder.
