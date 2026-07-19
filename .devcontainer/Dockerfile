# Ubuntu-based .NET 10 development container
ARG VARIANT=10.0
FROM mcr.microsoft.com/dotnet/sdk:${VARIANT}

USER root

# Set non-root user. UID 1000 matches the devcontainer convention and the Python
# devcontainer image, so the shared claude-code volume keeps one consistent owner.
ARG USERNAME=vscode
ARG USER_UID=1000
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
    # Python for scripting and tooling
    python3 \
    python3-pip \
    python3-venv \
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

# Playwright: Firefox (the browser actually used day-to-day) + Chromium (automation
# workhorse; the only engine with virtual-WebAuthn support for passkey testing).
# Must run as root (apt); binaries go to a shared path readable by the vscode user.
# Projects should npm-install the same pinned playwright version.
ENV PLAYWRIGHT_BROWSERS_PATH=/opt/ms-playwright
RUN npm install -g playwright@1.61.1 \
    && playwright install-deps chromium firefox \
    && playwright install chromium firefox \
    && chmod -R a+rX /opt/ms-playwright \
    && rm -rf /var/lib/apt/lists/*

# Create non-root user with sudo access. The noble-based .NET images ship a
# default `ubuntu` user/group already holding UID/GID 1000, so evict it first —
# `groupadd -f` would silently pick a different GID instead of reusing 1000.
RUN if getent passwd $USER_UID > /dev/null; then userdel -r "$(getent passwd $USER_UID | cut -d: -f1)"; fi \
    && if getent group $USER_GID > /dev/null; then groupdel "$(getent group $USER_GID | cut -d: -f1)"; fi \
    && groupadd --gid $USER_GID $USERNAME \
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

# Install .NET global tools for the vscode user. Must run AFTER the USER switch:
# installed as root they land in /root/.dotnet/tools where vscode can't use them.
RUN dotnet tool install --global dotnet-ef \
    && dotnet tool install --global dotnet-aspnet-codegenerator \
    && dotnet tool install --global dotnet-format \
    && dotnet tool install --global dotnet-outdated-tool

WORKDIR /workspace
