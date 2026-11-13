#!/usr/bin/env bash
# Installs the StarTruckerMP dedicated server as a systemd service on Linux x64.
#
# It downloads the server package from a GitHub release, unpacks it, writes server.json when it is
# missing and registers the service, so the server starts with the machine. An existing server.json
# and the certs folder are kept during an upgrade.
#
# Usage:
#   sudo ./install-server-linux.sh [--version alpha-v67] [--port 7777] [--max-players 100]
#                                  [--dir /opt/startruckermp] [--user startrucker]
#                                  [--service startruckermp] [--no-start]
set -euo pipefail

REPO="Kaofan1/StarTruckerMP"
ASSET="StarTruckerMP-server-linux-x64.zip"
VERSION="latest"
PORT="7777"
MAX_PLAYERS="100"
INSTALL_DIR="/opt/startruckermp"
RUN_USER="startrucker"
SERVICE="startruckermp"
START_SERVICE="yes"

step() { printf '\n\033[36m==> %s\033[0m\n' "$1"; }
fail() { printf '\033[31m%s\033[0m\n' "$1" >&2; exit 1; }

while [[ $# -gt 0 ]]; do
    case "$1" in
        --version) VERSION="$2"; shift 2 ;;
        --port) PORT="$2"; shift 2 ;;
        --max-players) MAX_PLAYERS="$2"; shift 2 ;;
        --dir) INSTALL_DIR="$2"; shift 2 ;;
        --user) RUN_USER="$2"; shift 2 ;;
        --service) SERVICE="$2"; shift 2 ;;
        --no-start) START_SERVICE="no"; shift ;;
        -h|--help) sed -n '2,12p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) fail "Unknown option: $1" ;;
    esac
done

[[ "$(id -u)" -eq 0 ]] || fail "Run this script as root: sudo ./install-server-linux.sh"
[[ "$(uname -s)" == "Linux" ]] || fail "This script targets Linux."
[[ "$(uname -m)" == "x86_64" ]] || fail "Only linux-x64 packages are published, this host is $(uname -m)."
[[ "$PORT" =~ ^[0-9]+$ ]] && (( PORT >= 1 && PORT <= 65535 )) || fail "Port must be between 1 and 65535."
[[ "$MAX_PLAYERS" =~ ^[0-9]+$ ]] && (( MAX_PLAYERS >= 1 && MAX_PLAYERS <= 256 )) || fail "Max players must be between 1 and 256."
command -v curl >/dev/null || fail "curl is required."
command -v unzip >/dev/null || fail "unzip is required (apt install unzip)."
command -v systemctl >/dev/null || fail "systemd is required."

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

step "Resolving release $VERSION"
if [[ "$VERSION" == "latest" ]]; then
    RELEASE_URL="https://api.github.com/repos/$REPO/releases/latest"
else
    RELEASE_URL="https://api.github.com/repos/$REPO/releases/tags/$VERSION"
fi

RELEASE_JSON="$(curl -fsSL -H 'User-Agent: StarTruckerMP-installer' "$RELEASE_URL")" \
    || fail "Could not read the release from GitHub."

DOWNLOAD_URL="$(printf '%s' "$RELEASE_JSON" \
    | grep -o "https://[^\"]*/$ASSET" | head -n 1 || true)"

[[ -n "$DOWNLOAD_URL" ]] || fail "That release has no $ASSET. Pick a release that includes the server package with --version."

TAG="$(printf '%s' "$RELEASE_JSON" | grep -o '"tag_name"[^,]*' | head -n 1 | cut -d'"' -f4)"
echo "Release $TAG"

step "Downloading $ASSET"
curl -fsSL "$DOWNLOAD_URL" -o "$TMP_DIR/$ASSET"

step "Unpacking to $INSTALL_DIR"
unzip -q "$TMP_DIR/$ASSET" -d "$TMP_DIR/server"
mkdir -p "$INSTALL_DIR"

# Keep the operator's settings and certificate during an upgrade.
if [[ -f "$INSTALL_DIR/server.json" ]]; then
    rm -f "$TMP_DIR/server/server.json"
fi

if [[ -d "$INSTALL_DIR/certs" ]]; then
    rm -rf "$TMP_DIR/server/certs"
fi

cp -a "$TMP_DIR/server/." "$INSTALL_DIR/"
chmod +x "$INSTALL_DIR/StarTruckerMP.Server"

step "Writing server.json"
if [[ ! -f "$INSTALL_DIR/server.json" ]]; then
    # od keeps this free of SIGPIPE, which pipefail would turn into a failed install.
    ADMIN_PASSWORD="$(od -An -N16 -tx1 /dev/urandom | tr -d ' \n')"
    cat > "$INSTALL_DIR/server.json" <<JSON
{
  "IpAddress": "0.0.0.0",
  "Port": $PORT,
  "MaxPlayers": $MAX_PLAYERS,
  "ApiAdminUsername": "admin",
  "ApiAdminPassword": "$ADMIN_PASSWORD",
  "ApiTokenLifetime": "08:00:00",
  "XboxRequiredTitleId": 1846147021,
  "SteamWebApiKey": "",
  "SteamAppId": 2380050,
  "DirectoryUrl": "",
  "DirectoryHost": "",
  "DirectoryName": "",
  "DirectoryDescription": ""
}
JSON
    echo "Created $INSTALL_DIR/server.json"
else
    echo "Keeping the existing $INSTALL_DIR/server.json"
fi

if ! id -u "$RUN_USER" >/dev/null 2>&1; then
    step "Creating service user $RUN_USER"
    useradd --system --home "$INSTALL_DIR" --shell /usr/sbin/nologin "$RUN_USER"
fi

chown -R "$RUN_USER:$RUN_USER" "$INSTALL_DIR"

step "Writing /etc/systemd/system/$SERVICE.service"
cat > "/etc/systemd/system/$SERVICE.service" <<UNIT
[Unit]
Description=StarTruckerMP dedicated server
After=network-online.target
Wants=network-online.target

[Service]
Type=notify
NotifyAccess=main
WorkingDirectory=$INSTALL_DIR
ExecStart=$INSTALL_DIR/StarTruckerMP.Server
User=$RUN_USER
Group=$RUN_USER
Restart=always
RestartSec=5
Environment=DOTNET_CLI_TELEMETRY_OPTOUT=1

[Install]
WantedBy=multi-user.target
UNIT

systemctl daemon-reload

if [[ "$START_SERVICE" == "yes" ]]; then
    step "Enabling and starting $SERVICE"
    systemctl enable --now "$SERVICE"
    sleep 2
    systemctl --no-pager --lines=0 status "$SERVICE" || true
else
    systemctl enable "$SERVICE"
fi

printf '\n\033[32mStarTruckerMP server %s installed in %s\033[0m\n' "$TAG" "$INSTALL_DIR"
echo "Settings     : $INSTALL_DIR/server.json"
echo "Status page  : https://localhost:$PORT/"
echo "Players join : <public ip>:$PORT (UDP and TCP)"
echo "Service      : systemctl status $SERVICE"
echo "Logs         : journalctl -u $SERVICE -f"
echo
echo "Open the port in the firewall, for example:"
echo "  ufw allow $PORT/tcp; ufw allow $PORT/udp"
echo "  firewall-cmd --add-port=$PORT/tcp --add-port=$PORT/udp --permanent; firewall-cmd --reload"
