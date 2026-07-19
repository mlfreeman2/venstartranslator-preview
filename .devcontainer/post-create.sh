#!/bin/bash
# Runs as the vscode user (postCreateCommand); sudo only where root is required.
# Global dotnet tools must NOT run under sudo or they land in /root/.dotnet/tools
# where the vscode user can't see them.
set -e

echo "📦 Post-create setup..."

# The shared Claude Code volume mounts root-owned on first creation, and content
# written by other containers may carry a different uid — normalize recursively
sudo chown -R vscode:vscode /home/vscode/.claude

# Ensure Claude Code is installed (baked into the image; this is self-healing)
if ! command -v claude &> /dev/null; then
    echo "  → Installing @anthropic-ai/claude-code..."
    sudo npm install -g @anthropic-ai/claude-code > /dev/null
fi

# dotnet-ef is installed for the vscode user by the Dockerfile; this is the
# self-healing fallback for images that predate that fix
if ! command -v dotnet-ef &> /dev/null; then
    echo "  → Installing dotnet-ef..."
    dotnet tool install --global dotnet-ef 2>/dev/null || \
      dotnet tool update --global dotnet-ef 2>/dev/null
fi

# Restore local dotnet tools + NuGet packages
dotnet tool restore 2>/dev/null || true
dotnet restore 2>/dev/null || true

echo "✅ DevContainer ready! Run 'claude' to start Claude Code."
