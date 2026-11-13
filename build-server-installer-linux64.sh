#!/usr/bin/env bash
# Builds StarTruckerMP-Server-linux-x64.run: the Linux server installer with the package inside it.
#
# Usage: ./build-server-installer-linux64.sh [build-number]
#
# The result lands in artifacts/server-installer and is safe to publish on its own; it needs no
# repository checkout and no network access on the target machine.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
OUT="$ROOT/artifacts/server-linux"
DEST="$ROOT/artifacts/server-installer"
RUN_FILE="$DEST/StarTruckerMP-Server-linux-x64.run"
PAYLOAD="$DEST/server-linux-payload.tar.gz"

VERSION_ARGS=()
if [[ $# -gt 0 && -n "$1" ]]; then
    VERSION_ARGS=("-p:Version=1.0.$1")
fi

DOTNET="dotnet"
if [[ -x "$LOCALAPPDATA/dotnet-sdk-10/dotnet.exe" ]]; then
    DOTNET="$LOCALAPPDATA/dotnet-sdk-10/dotnet.exe"
    export DOTNET_ROOT="$LOCALAPPDATA/dotnet-sdk-10"
fi

echo "Publishing StarTruckerMP server (linux-x64, self-contained)..."
rm -rf "$OUT"

"$DOTNET" publish "$ROOT/StarTruckerMP.Server/StarTruckerMP.Server.csproj" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:RunSvelteBuild=false \
    "${VERSION_ARGS[@]}" \
    -o "$OUT"

if [[ ! -f "$OUT/StarTruckerMP.Server" ]]; then
    echo "Publish produced no StarTruckerMP.Server in $OUT." >&2
    exit 1
fi

# An operator's settings never travel inside the installer.
rm -f "$OUT/server.json"
rm -rf "$OUT/certs"

mkdir -p "$DEST"
rm -f "$PAYLOAD" "$RUN_FILE"

echo "Packing the payload..."
tar -czf "$PAYLOAD" -C "$OUT" .

echo "Building the self-extracting installer..."
cat "$ROOT/installer/server-linux/install.sh" "$PAYLOAD" > "$RUN_FILE"
chmod +x "$RUN_FILE"
rm -f "$PAYLOAD"

if ! grep -q '^__ARCHIVE_BELOW__$' "$RUN_FILE"; then
    echo "The installer header is missing its marker." >&2
    exit 1
fi

echo
echo "Installer created: $RUN_FILE"
if [[ $# -gt 0 && -n "$1" ]]; then
    echo "Version: 1.0.$1"
fi
