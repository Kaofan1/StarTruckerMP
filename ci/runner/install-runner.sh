#!/usr/bin/env bash
# One-command setup of a self-hosted GitHub Actions runner for StarTruckerMP.
#
#   ./ci/runner/install-runner.sh --dlls /srv/startrucker/dlls
#
# What it does:
#   1. checks Docker and the game interop DLLs,
#   2. builds the runner image (.NET 10 SDK, Node.js 20, zip, git),
#   3. registers the runner with the labels docker,dotnet10,
#   4. mounts the DLL folder read-only at /dlls and starts it with restart policy,
#   5. verifies dotnet/node/zip and the DLLs inside the container,
#   6. sets the ENABLE_SELF_HOSTED_BUILD repository variable (via gh, when available).
#
# The registration token is one-time. Get it automatically with an authenticated
# `gh` CLI, or pass it explicitly:  RUNNER_TOKEN=XXXX ./ci/runner/install-runner.sh

set -euo pipefail

REPO_URL="https://github.com/Kaofan1/StarTruckerMP"
REPO_SLUG="Kaofan1/StarTruckerMP"
ACTIONS_URL="${REPO_URL}/actions"
RUNNER_NAME="startrucker-docker"
LABELS="docker,dotnet10"
IMAGE="startrucker-runner:local"
CONTAINER="startrucker-runner"
DLLS_HOST="/srv/startrucker/dlls"
SET_VARIABLE=1
CHECK_DLLS=1
ACTION="install"

usage() {
    cat <<'EOF'
Usage: ci/runner/install-runner.sh [options]

Options:
  --dlls PATH          folder that contains core/ and interop/ (default: /srv/startrucker/dlls)
  --name NAME          runner and container name (default: startrucker-docker)
  --image TAG          image tag to build (default: startrucker-runner:local)
  --no-variable        do not touch the ENABLE_SELF_HOSTED_BUILD repository variable
  --skip-dlls-check    start even if the DLL folder looks incomplete
  --down               stop and remove the runner container, then exit
  -h, --help           show this help

Environment:
  RUNNER_TOKEN         registration token; when unset the script asks gh for one
EOF
}

log() { printf '\033[1;34m[runner]\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m[runner]\033[0m %s\n' "$*" >&2; }
die() { printf '\033[1;31m[runner]\033[0m %s\n' "$*" >&2; exit 1; }

while [[ $# -gt 0 ]]; do
    case "$1" in
        --dlls) DLLS_HOST="${2:?--dlls needs a path}"; shift 2 ;;
        --name) RUNNER_NAME="${2:?--name needs a value}"; CONTAINER="$RUNNER_NAME"; shift 2 ;;
        --image) IMAGE="${2:?--image needs a value}"; shift 2 ;;
        --no-variable) SET_VARIABLE=0; shift ;;
        --skip-dlls-check) CHECK_DLLS=0; shift ;;
        --down) ACTION="down"; shift ;;
        -h|--help) usage; exit 0 ;;
        *) warn "Unknown option: $1"; usage; exit 2 ;;
    esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

command -v docker >/dev/null 2>&1 || die "Docker is required: https://docs.docker.com/engine/install/"
docker info >/dev/null 2>&1 || die "The Docker daemon is not reachable (is it running, and can this user use it?)"

if [[ "$ACTION" == "down" ]]; then
    docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
    log "Container '$CONTAINER' removed. A new registration token is required to add it back."
    exit 0
fi

[[ -d "$DLLS_HOST" ]] || die "DLL folder '$DLLS_HOST' does not exist. Pass the right path with --dlls PATH."

if [[ "$CHECK_DLLS" -eq 1 ]]; then
    [[ -f "$DLLS_HOST/interop/Assembly-CSharp.dll" ]] || die "'$DLLS_HOST/interop/Assembly-CSharp.dll' is missing. Copy BepInEx/interop from the game folder."
    [[ -f "$DLLS_HOST/core/0Harmony.dll" ]] || die "'$DLLS_HOST/core/0Harmony.dll' is missing. Copy BepInEx/core from the game folder."
fi

has_gh() {
    command -v gh >/dev/null 2>&1 && gh auth status >/dev/null 2>&1
}

TOKEN="${RUNNER_TOKEN:-}"
if [[ -z "$TOKEN" ]] && has_gh; then
    log "Requesting a runner registration token through gh…"
    TOKEN="$(gh api -X POST "repos/${REPO_SLUG}/actions/runners/registration-token" --jq .token)"
fi
[[ -n "$TOKEN" ]] || die "No registration token. Run 'gh auth login' or pass RUNNER_TOKEN=<token>."

log "Building '$IMAGE' (the first build downloads the .NET 10 SDK and Node.js 20)…"
docker build -t "$IMAGE" "$SCRIPT_DIR"

log "Starting container '$CONTAINER' (runner name '$RUNNER_NAME', labels '$LABELS')…"
docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
docker run -d \
    --restart unless-stopped \
    --name "$CONTAINER" \
    -e REPO_URL="$REPO_URL" \
    -e RUNNER_TOKEN="$TOKEN" \
    -e RUNNER_NAME="$RUNNER_NAME" \
    -e RUNNER_LABELS="$LABELS" \
    -e RUNNER_SCOPE=repo \
    -v "${DLLS_HOST}:/dlls:ro" \
    "$IMAGE" >/dev/null

log "Waiting for the runner to register…"
registered=0
for _ in $(seq 1 60); do
    if docker logs "$CONTAINER" 2>&1 | grep -q "Listening for Jobs"; then
        registered=1
        break
    fi
    if docker inspect -f '{{.State.Running}}' "$CONTAINER" 2>/dev/null | grep -q false; then
        break
    fi
    sleep 2
done

if [[ "$registered" -ne 1 ]]; then
    warn "The runner did not report 'Listening for Jobs' in time."
    warn "Inspect it with: docker logs $CONTAINER"
    exit 1
fi

log "Checking the toolchain and the mounted DLLs inside the container…"
docker exec "$CONTAINER" bash -lc '
    set -e
    dotnet --list-sdks
    node --version
    npm --version
    zip --version >/dev/null
    test -f /dlls/interop/Assembly-CSharp.dll
    test -f /dlls/core/0Harmony.dll
'

if [[ "$SET_VARIABLE" -eq 1 ]]; then
    if has_gh; then
        gh variable set ENABLE_SELF_HOSTED_BUILD --repo "$REPO_SLUG" --body true
        log "Repository variable ENABLE_SELF_HOSTED_BUILD=true."
    else
        warn "gh is not available or not authenticated."
        warn "Set ENABLE_SELF_HOSTED_BUILD=true manually: Settings → Secrets and variables → Actions → Variables."
    fi
fi

log "Runner is online."
log "  Runner list:  ${REPO_URL}/settings/actions/runners"
log "  Workflows:    ${ACTIONS_URL}"
log "  Remount DLLs: rerun this script after adding files to ${DLLS_HOST}"
log "  Remove:       ./ci/runner/install-runner.sh --down"
