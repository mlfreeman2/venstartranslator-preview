# Build stage for a cross-platform .NET development container
ARG VARIANT=10.0-bookworm-slim
FROM mcr.microsoft.com/dotnet/sdk:${VARIANT}

USER root

# Set non-root user
ARG USERNAME=vscode
ARG USER_UID=1000
ARG USER_GID=$USER_UID

# Configure environment
ENV PATH="${PATH}:/home/${USERNAME}/.dotnet:/home/${USERNAME}/.dotnet/tools" \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=true \
    DEBIAN_FRONTEND=noninteractive

# Install base dependencies
RUN apt-get update && apt-get -y install --no-install-recommends \
    # Network tools
    iproute2 \
    iputils-ping \
    dnsutils \
    net-tools \
    # Build essentials
    build-essential \
    # Utilities
    curl \
    wget \
    ca-certificates \
    gnupg \
    lsb-release \
    software-properties-common \
    apt-transport-https \
    # Git and version control
    git \
    git-lfs \
    # Process management
    procps \
    htop \
    # Text editors
    vim \
    nano \
    # Archive tools
    zip \
    unzip \
    # Locales
    locales \
    && locale-gen en_US.UTF-8 \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

# Install Node.js 20.x (required for Claude Code)
RUN mkdir -p /etc/apt/keyrings \
    && curl -fsSL https://deb.nodesource.com/gpgkey/nodesource-repo.gpg.key | gpg --dearmor -o /etc/apt/keyrings/nodesource.gpg \
    && echo "deb [signed-by=/etc/apt/keyrings/nodesource.gpg] https://deb.nodesource.com/node_20.x nodistro main" | tee /etc/apt/sources.list.d/nodesource.list \
    && apt-get update \
    && apt-get install -y nodejs \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/* \
    && npm install -g npm@latest

# Install global npm tools
RUN npm install -g \
    @anthropic-ai/claude-code \
    typescript \
    eslint \
    prettier \
    && npm cache clean --force

# Install .NET global tools
RUN dotnet tool install --global dotnet-ef \
    && dotnet tool install --global dotnet-aspnet-codegenerator \
    && dotnet tool install --global dotnet-format \
    && dotnet tool install --global dotnet-outdated-tool

# Create non-root user with sudo access
RUN groupadd --gid $USER_GID $USERNAME \
    && useradd --uid $USER_UID --gid $USER_GID -m $USERNAME \
    && apt-get update \
    && apt-get install -y sudo \
    && echo $USERNAME ALL=\(root\) NOPASSWD:ALL > /etc/sudoers.d/$USERNAME \
    && chmod 0440 /etc/sudoers.d/$USERNAME \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

# Create necessary directories with correct permissions
RUN mkdir -p /home/${USERNAME}/.config/claude \
    && mkdir -p /home/${USERNAME}/.dotnet/tools \
    && mkdir -p /home/${USERNAME}/.nuget \
    && mkdir -p /home/${USERNAME}/.ssh \
    && chown -R ${USERNAME}:${USERNAME} /home/${USERNAME}

# Set up Git safe directory (for mounted workspaces)
RUN git config --system --add safe.directory '*'

# Switch to non-root user
USER $USERNAME

# Set up shell environment
RUN echo 'export PATH="$PATH:/home/'${USERNAME}'/.dotnet/tools"' >> ~/.bashrc \
    && echo 'alias ll="ls -alF"' >> ~/.bashrc \
    && echo 'alias la="ls -A"' >> ~/.bashrc \
    && echo 'alias l="ls -CF"' >> ~/.bashrc

WORKDIR /workspace

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD claude --version || exit 1