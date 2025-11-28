# Ubuntu-based .NET 10 development container
ARG VARIANT=10.0
FROM mcr.microsoft.com/dotnet/sdk:${VARIANT}

USER root

# Set non-root user
ARG USERNAME=vscode
ARG USER_UID=1001
ARG USER_GID=$USER_UID

# Configure environment
ENV PATH="${PATH}:/home/${USERNAME}/.dotnet:/home/${USERNAME}/.dotnet/tools" \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=true \
    LANG=en_US.UTF-8 \
    LANGUAGE=en_US:en \
    LC_ALL=en_US.UTF-8 \
    DEBIAN_FRONTEND=noninteractive

# Install base dependencies
RUN apt-get update && apt-get install -y --no-install-recommends \
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
    # Shell
    bash \
    bash-completion \
    # Sudo for non-root user
    sudo \
    # SQLite for database work
    sqlite3 \
    # Locales
    locales \
    && rm -rf /var/lib/apt/lists/*

# Generate locale
RUN sed -i -e 's/# en_US.UTF-8 UTF-8/en_US.UTF-8 UTF-8/' /etc/locale.gen && \
    dpkg-reconfigure --frontend=noninteractive locales && \
    update-locale LANG=en_US.UTF-8

# Install Node.js (using NodeSource)
RUN curl -fsSL https://deb.nodesource.com/setup_lts.x | bash - \
    && apt-get install -y nodejs \
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
    && useradd --uid $USER_UID --gid $USER_GID -m -s /bin/bash $USERNAME \
    && echo "$USERNAME ALL=(root) NOPASSWD:ALL" > /etc/sudoers.d/$USERNAME \
    && chmod 0440 /etc/sudoers.d/$USERNAME

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
