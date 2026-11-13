#!/bin/sh
# StarTruckerMP dedicated server installer for Linux x64.
#
# This file is a self-extracting installer: the server package is appended after the marker at the
# end of this script. Run it as root:
#
#   chmod +x StarTruckerMP-Server-linux-x64.run
#   sudo ./StarTruckerMP-Server-linux-x64.run --port 7777
#
# Options:
#   --dir PATH          install folder (default /opt/startruckermp)
#   --port N            game and HTTPS port (default 7777)
#   --max-players N     player limit (default 100)
#   --user NAME         service user (default startrucker)
#   --service NAME      systemd unit name (default startruckermp)
#   --no-start          install the unit without starting it
#   --uninstall         stop and remove the service, keep server.json and certs
#   --help              show this text
set -eu

INSTALL_DIR="/opt/startruckermp"
PORT="7777"
MAX_PLAYERS="100"
RUN_USER="startrucker"
SERVICE="startruckermp"
START_SERVICE="yes"
UNINSTALL="no"

step() { printf '\n\033[36m==> %s\033[0m\n' "$1"; }
fail() { printf '\033[31m%s\033[0m\n' "$1" >&2; exit 1; }

while [ $# -gt 0 ]; do
    case "$1" in
        --dir) INSTALL_DIR="$2"; shift 2 ;;
        --port) PORT="$2"; shift 2 ;;
        --max-players) MAX_PLAYERS="$2"; shift 2 ;;
        --user) RUN_USER="$2"; shift 2 ;;
        --service) SERVICE="$2"; shift 2 ;;
        --no-start) START_SERVICE="no"; shift ;;
        --uninstall) UNINSTALL="yes"; shift ;;
        -h|--help) sed -n '2,22p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) fail "Unknown option: $1" ;;
    esac
done

[ "$(id -u)" -eq 0 ] || fail "Run this installer as root: sudo $0"

if [ "$UNINSTALL" = "yes" ]; then
    step "Removing $SERVICE"
    systemctl disable --now "$SERVICE" 2>/dev/null || true
    rm -f "/etc/systemd/system/$SERVICE.service"
    systemctl daemon-reload
    printf '\nService removed. Settings stay in %s (server.json, certs).\n' "$INSTALL_DIR"
    printf 'Delete that folder yourself when the server is no longer needed.\n'
    exit 0
fi

[ "$(uname -s)" = "Linux" ] || fail "This installer targets Linux."
[ "$(uname -m)" = "x86_64" ] || fail "Only linux-x64 is published, this host is $(uname -m)."
case "$PORT" in (*[!0-9]*|'') fail "Port must be a number." ;; esac
[ "$PORT" -ge 1 ] && [ "$PORT" -le 65535 ] || fail "Port must be between 1 and 65535."
case "$MAX_PLAYERS" in (*[!0-9]*|'') fail "Max players must be a number." ;; esac
[ "$MAX_PLAYERS" -ge 1 ] && [ "$MAX_PLAYERS" -le 256 ] || fail "Max players must be between 1 and 256."
command -v systemctl >/dev/null || fail "systemd is required."
command -v tar >/dev/null || fail "tar is required."

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

step "Unpacking the server package"
ARCHIVE_LINE="$(awk '/^__ARCHIVE_BELOW__$/ { print NR + 1; exit }' "$0")"
[ -n "$ARCHIVE_LINE" ] || fail "The installer payload is missing."
tail -n +"$ARCHIVE_LINE" "$0" > "$TMP_DIR/payload.tar.gz"
tar -xzf "$TMP_DIR/payload.tar.gz" -C "$TMP_DIR"
[ -f "$TMP_DIR/StarTruckerMP.Server" ] || fail "The package does not contain StarTruckerMP.Server."

step "Installing to $INSTALL_DIR"
mkdir -p "$INSTALL_DIR"

# Keep the operator's settings and certificate during an upgrade.
if [ -f "$INSTALL_DIR/server.json" ]; then
    rm -f "$TMP_DIR/server.json"
fi

if [ -d "$INSTALL_DIR/certs" ]; then
    rm -rf "$TMP_DIR/certs"
fi

# An upgrade keeps the state the service had: a running server is restarted onto the new
# binary, a stopped one stays stopped.
WAS_ACTIVE="no"
if systemctl is-active --quiet "$SERVICE" 2>/dev/null; then WAS_ACTIVE="yes"; fi
WAS_INSTALLED="no"
if systemctl cat "$SERVICE" >/dev/null 2>&1; then WAS_INSTALLED="yes"; fi

cp -a "$TMP_DIR/." "$INSTALL_DIR/"
rm -f "$INSTALL_DIR/payload.tar.gz" "$INSTALL_DIR/install.sh"
chmod +x "$INSTALL_DIR/StarTruckerMP.Server"

step "Writing server.json"
if [ ! -f "$INSTALL_DIR/server.json" ]; then
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

# The unit is not enabled on purpose: an installed server stays stopped until the operator
# starts it, and `systemctl enable` makes it start with the machine when that is wanted.
if [ "$START_SERVICE" = "yes" ]; then
    if [ "$WAS_ACTIVE" = "yes" ]; then
        step "Restarting $SERVICE on the new files"
        systemctl restart "$SERVICE"
    elif [ "$WAS_INSTALLED" = "no" ]; then
        step "Starting $SERVICE"
        systemctl start "$SERVICE"
    else
        step "Leaving $SERVICE stopped, because it was stopped before the upgrade"
    fi

    sleep 2
    systemctl --no-pager --lines=0 status "$SERVICE" || true
fi

printf '\n\033[32mStarTruckerMP dedicated server installed in %s\033[0m\n' "$INSTALL_DIR"
echo "Settings     : $INSTALL_DIR/server.json"
echo "Status page  : https://localhost:$PORT/"
echo "Players join : <public ip>:$PORT (UDP and TCP)"
echo "Service      : systemctl status $SERVICE"
echo "Logs         : journalctl -u $SERVICE -f"
echo
echo "Open the port in the firewall, for example:"
echo "  ufw allow $PORT/tcp; ufw allow $PORT/udp"
echo "  firewall-cmd --add-port=$PORT/tcp --add-port=$PORT/udp --permanent; firewall-cmd --reload"

exit 0
__ARCHIVE_BELOW__
