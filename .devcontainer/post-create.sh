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

# # Remove docker0 interface if it exists and is DOWN (unused)
# # This prevents routing conflicts with home networks using 172.17.x.x
# if ip link show docker0 >/dev/null 2>&1; then
#   echo "  → Checking docker0 interface..."
#   if ip link show docker0 | grep -q "state DOWN"; then
#     echo "  → Removing unused docker0 interface (172.17.0.0/16 conflict avoidance)..."
#     sudo ip link delete docker0 2>/dev/null || true
#   else
#     echo "  → docker0 interface is UP, leaving it alone..."
#   fi
# fi

echo "✅ DevContainer ready! Run 'claude' to start Claude Code."
