#!/bin/bash
# XelthAGI Server Deployment Script

set -e  # Exit on error

echo "========================================="
echo "  XelthAGI Server Deployment"
echo "========================================="
echo ""

# Check Node.js
if ! command -v node &> /dev/null; then
    echo "❌ Node.js not found. Please install Node.js 18+"
    exit 1
fi

echo "✅ Node.js version: $(node --version)"
echo ""

# Install dependencies
echo "📦 Installing dependencies..."
npm install
echo ""

# Check .env
if [ ! -f .env ]; then
    echo "⚠️  .env not found. Creating from example..."
    cp .env.example .env
    echo ""
    echo "⚠️  IMPORTANT: Edit .env and add your API keys!"
    echo "   nano .env"
    echo ""
    read -p "Press Enter after editing .env..."
fi

# Test configuration
echo "🔍 Testing server configuration..."
if ! node -e "require('./src/config')"; then
    echo "❌ Configuration error. Check .env file."
    exit 1
fi
echo ""

# Ask deployment method
echo "Select deployment method:"
echo "1) PM2 (recommended for production)"
echo "2) Systemd service"
echo "3) Manual (npm start)"
read -p "Choice (1-3): " choice

case $choice in
    1)
        echo ""
        echo "📦 Setting up PM2..."

        # Install PM2 if not present
        if ! command -v pm2 &> /dev/null; then
            echo "Installing PM2 globally..."
            sudo npm install -g pm2
        fi

        # Stop existing instance
        pm2 delete xelth-agi 2>/dev/null || true

        # Start with PM2
        pm2 start src/index.js --name xelth-agi
        pm2 save

        echo ""
        echo "✅ Server started with PM2!"
        echo ""
        echo "Commands:"
        echo "  pm2 status          - View status"
        echo "  pm2 logs xelth-agi  - View logs"
        echo "  pm2 restart xelth-agi - Restart server"
        echo "  pm2 stop xelth-agi    - Stop server"
        echo ""

        # Setup startup script
        read -p "Setup PM2 to start on boot? (y/n): " startup
        if [ "$startup" = "y" ]; then
            pm2 startup
            echo ""
            echo "Run the command above to complete startup setup"
        fi
        ;;

    2)
        echo ""
        echo "📝 Creating systemd service..."

        SERVICE_FILE="/etc/systemd/system/xelth-agi.service"
        CURRENT_DIR=$(pwd)
        CURRENT_USER=$(whoami)

        sudo tee $SERVICE_FILE > /dev/null <<EOF
[Unit]
Description=XelthAGI Automation Server
After=network.target

[Service]
Type=simple
User=$CURRENT_USER
WorkingDirectory=$CURRENT_DIR
ExecStart=/usr/bin/node src/index.js
Restart=on-failure
RestartSec=10
StandardOutput=journal
StandardError=journal
SyslogIdentifier=xelth-agi
Environment="NODE_ENV=production"

[Install]
WantedBy=multi-user.target
EOF

        sudo systemctl daemon-reload
        sudo systemctl enable xelth-agi
        sudo systemctl start xelth-agi

        echo ""
        echo "✅ Systemd service created and started!"
        echo ""
        echo "Commands:"
        echo "  sudo systemctl status xelth-agi   - View status"
        echo "  sudo journalctl -u xelth-agi -f   - View logs"
        echo "  sudo systemctl restart xelth-agi  - Restart"
        echo "  sudo systemctl stop xelth-agi     - Stop"
        echo ""

        sudo systemctl status xelth-agi --no-pager
        ;;

    3)
        echo ""
        echo "🚀 Starting server manually..."
        echo ""
        npm start
        ;;

    *)
        echo "Invalid choice"
        exit 1
        ;;
esac

echo ""
echo "========================================="
echo "  Deployment Complete! 🎉"
echo "========================================="
echo ""
echo "Server URL: http://localhost:3232"
echo "Health check: curl http://localhost:3232/health"
echo ""
