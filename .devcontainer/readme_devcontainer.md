# .NET 10 DevContainer with Claude Code (Alpine Linux)

A lightweight, fast-building development container for .NET 10 projects with integrated Claude Code AI assistant support. Built on Alpine Linux for significantly faster build times compared to Ubuntu/Debian images.

## 🚀 Features

- **.NET 10 SDK** on Alpine Linux (minimal footprint)
- **Claude Code** AI coding assistant
- **Fast builds** - Alpine image is ~3x smaller than Ubuntu
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
- `post-create.sh`
- `claude_auth_script.sh`

### 2. Open in DevContainer

1. Open your project in VS Code
2. Press `F1` and select "Dev Containers: Reopen in Container"
3. Wait for the container to build (first time takes ~1-2 minutes with Alpine!)

### 3. Authenticate Claude Code

Run the authentication helper:
```bash
bash .devcontainer/claude_auth_script.sh
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
- **Alpine builds ~3x faster than Ubuntu images**

### Windows (Docker Desktop + WSL2)

- Browser opens automatically for authentication via WSL
- Config persists in `%USERPROFILE%\.config\claude`
- Ensure WSL2 integration is enabled in Docker Desktop
- **Significantly faster builds than Ubuntu-based containers**

### Linux

- Browser opens automatically if display is configured
- Config persists in `~/.config/claude`
- **Fastest performance, especially with Alpine's minimal footprint**

## 📁 File Structure

```
.devcontainer/
├── devcontainer.json    # Main configuration
├── Dockerfile           # Alpine-based container definition
├── post-create.sh       # Post-creation setup script
├── claude_auth_script.sh # Authentication helper
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
"forwardPorts": [8080, 8443]
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

### Add Alpine Packages

Edit the Dockerfile and use `apk add`:
```dockerfile
RUN apk add --no-cache package-name
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

### Alpine-Specific Issues

1. **Package not found:**
   - Alpine uses different package names than Ubuntu
   - Search packages: `apk search package-name`
   - Use `apk add --no-cache package-name` to install

2. **glibc compatibility:**
   - Alpine uses musl libc instead of glibc
   - .NET Core/10 works perfectly with musl
   - Some third-party native libraries may need Alpine-specific versions

3. **Missing bash:**
   - Alpine uses ash by default, but bash is installed in this container
   - Check the terminal profile setting if needed

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

## 📊 Performance Comparison

**Alpine vs Ubuntu Build Times:**
- Alpine (first build): ~1-2 minutes
- Ubuntu (first build): ~3-5 minutes
- **Speed improvement: ~60-70% faster**

**Image Sizes:**
- Alpine: ~800MB (SDK + tools)
- Ubuntu: ~2.5GB (SDK + tools)
- **Size reduction: ~68% smaller**

## 📚 Resources

- [Claude Code Documentation](https://docs.claude.com/en/docs/claude-code)
- [.NET Documentation](https://docs.microsoft.com/dotnet)
- [Dev Containers Documentation](https://code.visualstudio.com/docs/devcontainers/containers)
- [Alpine Linux Packages](https://pkgs.alpinelinux.org/packages)
- [Anthropic Console](https://console.anthropic.com) (for API keys)

## 🔄 Updating

To update Claude Code:
```bash
npm update -g @anthropic-ai/claude-code
```

To update the container:
1. Pull latest base image: `docker pull mcr.microsoft.com/dotnet/sdk:10.0-alpine`
2. Rebuild container: F1 → "Dev Containers: Rebuild Container"

## 📝 License

This DevContainer configuration is provided as-is for use in your projects.

## 🎉 Why Alpine?

Alpine Linux is chosen for this DevContainer because:
- **Smaller size** - Base image is ~150MB vs ~500MB for Ubuntu
- **Faster builds** - Less to download and extract
- **Better performance** - Minimal overhead
- **Security** - Smaller attack surface with fewer packages
- **Production parity** - Many production containers use Alpine

Perfect for development where you rebuild containers frequently!
