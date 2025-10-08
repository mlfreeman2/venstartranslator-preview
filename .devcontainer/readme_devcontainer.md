# .NET 8 DevContainer with Claude Code

A reusable, cross-platform development container for .NET 8 projects with integrated Claude Code AI assistant support.

## 🚀 Features

- **.NET 8 SDK** with common tools pre-installed
- **Claude Code** AI coding assistant
- **Cross-platform** support (macOS, Windows, Linux)
- **Docker-in-Docker** support for containerized development
- **Pre-configured VS Code** extensions for .NET development
- **Persistent configurations** for Claude, NuGet, and SSH
- **Performance optimizations** with cached mounts

## 📋 Prerequisites

- Docker Desktop (macOS/Windows) or Docker Engine (Linux)
- Visual Studio Code with Remote-Containers extension
- Claude Pro subscription OR Anthropic API key

## 🛠️ Setup Instructions

### 1. Add to Your Project

Copy these files to your .NET project's `.devcontainer/` folder:
- `devcontainer.json`
- `Dockerfile`
- `claude-auth.sh`

### 2. Open in DevContainer

1. Open your project in VS Code
2. Press `F1` and select "Dev Containers: Reopen in Container"
3. Wait for the container to build (first time takes ~3-5 minutes)

### 3. Authenticate Claude Code

Run the authentication helper:
```bash
bash .devcontainer/claude-auth.sh
```

Or manually authenticate:
```bash
claude
```

**For Claude Pro subscribers:**
- The browser will open for OAuth authentication
- Sign in with your Claude Pro account

**For API key users:**
- Set the environment variable: `export ANTHROPIC_API_KEY="your-key-here"`
- Or paste the key when prompted

## 🖥️ Platform-Specific Notes

### macOS (Docker Desktop)

- Browser authentication requires manually copying the URL from terminal
- Config persists in `~/.config/claude` on your Mac
- Docker Desktop must be running

### Windows (Docker Desktop + WSL2)

- Browser opens automatically for authentication via WSL
- Config persists in `%USERPROFILE%\.config\claude`
- Ensure WSL2 integration is enabled in Docker Desktop

### Linux

- Browser opens automatically if display is configured
- Config persists in `~/.config/claude`
- May need to install additional packages for GUI browser support

## 📁 File Structure

```
.devcontainer/
├── devcontainer.json    # Main configuration
├── Dockerfile           # Container definition
├── claude-auth.sh       # Authentication helper
└── README.md           # This file
```

## 🎯 Usage Examples

### Basic Claude Code Commands

```bash
# Start Claude Code in your project
cd /workspace
claude

# Ask Claude to explain your codebase
> What does this project do?

# Have Claude implement a feature
> Add a new endpoint for user authentication

# Get help with debugging
> Why is this test failing?
```

### .NET Development

```bash
# Create a new Web API project
dotnet new webapi -n MyApi

# Run with hot reload
dotnet watch run

# Run tests
dotnet test

# Entity Framework migrations
dotnet ef migrations add InitialCreate
dotnet ef database update
```

## ⚙️ Customization

### Modify Ports

Edit `forwardPorts` in `devcontainer.json`:
```json
"forwardPorts": [5000, 5001, 8080, 8081, 3000]
```

### Add VS Code Extensions

Edit `customizations.vscode.extensions` in `devcontainer.json`:
```json
"extensions": [
    "ms-dotnettools.csharp",
    "your-extension-id-here"
]
```

### Add Global .NET Tools

Edit the Dockerfile:
```dockerfile
RUN dotnet tool install --global your-tool-name
```

### Change .NET Version

Update `VARIANT` in `devcontainer.json`:
```json
"args": {
    "VARIANT": "8.0-bookworm-slim"  // or "7.0", "6.0", etc.
}
```

## 🔧 Troubleshooting

### Claude Authentication Issues

1. **Browser doesn't open on macOS:**
   - Copy the URL from terminal and open manually
   - This is expected behavior on macOS Docker

2. **Authentication doesn't persist:**
   - Ensure `~/.config/claude` mount is configured
   - Check file permissions on the host

3. **"Command not found: claude":**
   ```bash
   npm install -g @anthropic-ai/claude-code
   ```

### Container Issues

1. **Slow performance on Windows:**
   - Ensure WSL2 is being used (not legacy WSL1)
   - Store projects in WSL filesystem, not Windows filesystem

2. **Docker socket permission denied:**
   - Comment out Docker-in-Docker mount if not needed
   - Or ensure Docker socket has correct permissions

3. **Port already in use:**
   - Change port mappings in `forwardPorts`
   - Or stop conflicting local services

## 📚 Resources

- [Claude Code Documentation](https://docs.claude.com/en/docs/claude-code)
- [.NET Documentation](https://docs.microsoft.com/dotnet)
- [Dev Containers Documentation](https://code.visualstudio.com/docs/devcontainers/containers)
- [Anthropic Console](https://console.anthropic.com) (for API keys)

## 🔄 Updating

To update Claude Code:
```bash
npm update -g @anthropic-ai/claude-code
```

To update the container:
1. Pull latest base image: `docker pull mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim`
2. Rebuild container: F1 → "Dev Containers: Rebuild Container"

## 📝 License

This DevContainer configuration is provided as-is for use in your projects.