# Self-hosted GitHub Actions runner for StarTruckerMP.
#
# The client and server builds need .NET 10 SDK, Node.js (overlay-ui) and the game
# interop DLLs, which are mounted read-only at /dlls when the container runs.
#
# Build:  docker build -t startrucker-runner:local ci/runner
# Run:    ./ci/runner/install-runner.sh
#
# Pin the base image if you want reproducible builds:
#   docker build --build-arg BASE_IMAGE=myoung34/github-runner:latest -t startrucker-runner:local ci/runner

ARG BASE_IMAGE=myoung34/github-runner:latest
FROM ${BASE_IMAGE}

USER root

ARG DOTNET_CHANNEL=10.0
ARG NODE_VERSION=20.18.1

ENV DEBIAN_FRONTEND=noninteractive \
    DOTNET_ROOT=/usr/share/dotnet \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        git \
        libicu-dev \
        unzip \
        xz-utils \
        zip \
    && rm -rf /var/lib/apt/lists/*

# .NET 10 SDK - the launcher targets net10.0, the client builds against net6.0.
RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && bash /tmp/dotnet-install.sh --channel "${DOTNET_CHANNEL}" --install-dir "${DOTNET_ROOT}" --no-path \
    && rm -f /tmp/dotnet-install.sh \
    && ln -sf "${DOTNET_ROOT}/dotnet" /usr/local/bin/dotnet

# Node.js 20 - `npm ci` in StarTruckerMP.Server/overlay-ui.
RUN curl -fsSLO "https://nodejs.org/dist/v${NODE_VERSION}/node-v${NODE_VERSION}-linux-x64.tar.xz" \
    && tar -xJf "node-v${NODE_VERSION}-linux-x64.tar.xz" -C /usr/local --strip-components=1 \
    && rm -f "node-v${NODE_VERSION}-linux-x64.tar.xz"

ENV PATH="${DOTNET_ROOT}:/usr/local/bin:${PATH}"

# Fail the build here instead of in a workflow run if a tool is missing.
RUN dotnet --list-sdks \
    && node --version \
    && npm --version \
    && zip --version >/dev/null

LABEL org.opencontainers.image.title="StarTruckerMP self-hosted runner" \
      org.opencontainers.image.description="GitHub Actions runner with .NET 10 SDK, Node.js 20 and zip for StarTruckerMP" \
      org.opencontainers.image.source="https://github.com/Kaofan1/StarTruckerMP"
