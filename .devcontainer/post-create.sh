#!/bin/bash
set -e

echo "📦 Installing development tools..."

# Install SQLite CLI for database inspection
echo "  → Installing sqlite3..."
apt-get update > /dev/null
apt-get install -y sqlite3 > /dev/null

# Install Claude Code globally
echo "  → Installing @anthropic-ai/claude-code..."
npm install -g @anthropic-ai/claude-code > /dev/null

# Install or update dotnet-ef (Entity Framework CLI)
echo "  → Installing dotnet-ef..."
dotnet tool install --global dotnet-ef 2>/dev/null || \
  dotnet tool update --global dotnet-ef 2>/dev/null

# Restore any local dotnet tools defined in .config/dotnet-tools.json
echo "  → Restoring local dotnet tools..."
dotnet tool restore 2>/dev/null || true

# Restore NuGet packages for the solution
echo "  → Restoring NuGet packages..."
dotnet restore 2>/dev/null || true

echo "✅ DevContainer ready! Run 'claude' to start Claude Code."
