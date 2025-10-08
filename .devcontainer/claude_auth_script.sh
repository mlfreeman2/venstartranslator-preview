#!/bin/bash

# Claude Code Authentication Helper Script
# This script helps with authentication on different platforms

echo "🚀 Claude Code Authentication Helper"
echo "===================================="

# Function to detect platform
detect_platform() {
    if [[ "$OSTYPE" == "darwin"* ]]; then
        echo "macos"
    elif [[ -n "$WSL_DISTRO_NAME" ]]; then
        echo "wsl"
    elif [[ "$OSTYPE" == "linux-gnu"* ]]; then
        echo "linux"
    else
        echo "unknown"
    fi
}

# Function to open URL based on platform
open_url() {
    local url=$1
    local platform=$(detect_platform)
    
    echo "📱 Opening browser for authentication..."
    
    case $platform in
        macos)
            # On macOS in Docker, we need to handle this specially
            echo "On macOS Docker:"
            echo "Please copy this URL and open it in your browser:"
            echo ""
            echo "  $url"
            echo ""
            echo "After authenticating, return here and press Enter to continue..."
            read -r
            ;;
        wsl)
            # WSL can use Windows browser
            if command -v wslview &> /dev/null; then
                wslview "$url"
            elif command -v cmd.exe &> /dev/null; then
                cmd.exe /c start "$url"
            else
                echo "Please copy this URL and open it in your browser:"
                echo "  $url"
            fi
            ;;
        linux)
            # Native Linux
            if command -v xdg-open &> /dev/null; then
                xdg-open "$url"
            elif command -v gnome-open &> /dev/null; then
                gnome-open "$url"
            else
                echo "Please copy this URL and open it in your browser:"
                echo "  $url"
            fi
            ;;
        *)
            echo "Please copy this URL and open it in your browser:"
            echo "  $url"
            ;;
    esac
}

# Check if Claude is installed
if ! command -v claude &> /dev/null; then
    echo "❌ Claude Code is not installed. Installing..."
    npm install -g @anthropic-ai/claude-code
fi

echo ""
echo "✅ Claude Code is installed (version: $(claude --version 2>/dev/null || echo 'unknown'))"
echo ""

# Check authentication status
echo "🔐 Checking authentication status..."
if claude --version &> /dev/null; then
    echo "✅ Claude Code appears to be ready"
    echo ""
    echo "Try running: claude"
    echo ""
else
    echo "📝 You need to authenticate Claude Code"
    echo ""
    echo "Run 'claude' and follow the authentication prompts."
    echo ""
    echo "For Claude Pro users:"
    echo "  - You'll be redirected to sign in with your Claude Pro account"
    echo "  - The subscription provides unlimited usage within fair use limits"
    echo ""
    echo "For API key users:"
    echo "  - You can set ANTHROPIC_API_KEY environment variable"
    echo "  - Or paste your API key when prompted"
    echo ""
fi

# Platform-specific instructions
platform=$(detect_platform)
echo "🖥️  Platform detected: $platform"
echo ""

case $platform in
    macos)
        echo "📌 macOS Docker Notes:"
        echo "  - Browser authentication requires manually copying the URL"
        echo "  - Your config is persisted in ~/.config/claude"
        echo "  - Authentication will persist across container rebuilds"
        ;;
    wsl)
        echo "📌 Windows/WSL Notes:"
        echo "  - Browser should open automatically for authentication"
        echo "  - Your config is persisted in ~/.config/claude"
        echo "  - Make sure Docker Desktop is running"
        ;;
    linux)
        echo "📌 Linux Notes:"
        echo "  - Browser should open automatically if display is configured"
        echo "  - Your config is persisted in ~/.config/claude"
        ;;
esac

echo ""
echo "Ready to start! Run 'claude' to begin."