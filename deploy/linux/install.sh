#!/usr/bin/env bash
# PRODVISOR AI demo - installer for a Linux server (Ubuntu/Debian, RHEL/Rocky/Alma, Fedora).
# Run as root from the unpacked package directory:   sudo ./install.sh            (port 5080)
#                                                     sudo PORT=8080 ./install.sh
# Safe to run again for updates: App_Data (database, uploads, logs, keys) and appsettings.Production.json are kept.
set -euo pipefail

APP_DIR=/opt/prodvisor-ai
APP_USER=prodvisor
PORT="${PORT:-5080}"
SRC_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

[[ $EUID -eq 0 ]] || { echo "Run as root: sudo ./install.sh"; exit 1; }
[[ -f "$SRC_DIR/app/ProdvisorAi.Demo" ]] || { echo "app/ProdvisorAi.Demo not found next to install.sh - unpack the whole package first."; exit 1; }

echo "==> System packages (ICU for .NET, fonts for rendering DXF texts)"
if command -v apt-get >/dev/null; then
  apt-get update -qq
  ICU_PKG="$(apt-cache search --names-only '^libicu[0-9]+$' | awk '{print $1}' | sort -V | tail -1)"
  DEBIAN_FRONTEND=noninteractive apt-get install -y -qq ca-certificates fontconfig fonts-dejavu-core ${ICU_PKG:-libicu-dev} >/dev/null
elif command -v dnf >/dev/null; then
  dnf install -y -q ca-certificates fontconfig dejavu-sans-fonts libicu >/dev/null
elif command -v yum >/dev/null; then
  yum install -y -q ca-certificates fontconfig dejavu-sans-fonts libicu >/dev/null
else
  echo "   (unknown package manager - make sure ICU (libicu) and a TTF font like DejaVu Sans are installed)"
fi

echo "==> Service user '$APP_USER'"
id "$APP_USER" >/dev/null 2>&1 || useradd --system --no-create-home --shell /usr/sbin/nologin "$APP_USER"

echo "==> Application files -> $APP_DIR"
systemctl stop prodvisor-ai 2>/dev/null || true
mkdir -p "$APP_DIR/App_Data"
# Replace the program files, keep the data and the local configuration override.
find "$APP_DIR" -mindepth 1 -maxdepth 1 ! -name App_Data ! -name appsettings.Production.json -exec rm -rf {} +
cp -a "$SRC_DIR/app/." "$APP_DIR/"
chmod +x "$APP_DIR/ProdvisorAi.Demo"
chown -R "$APP_USER:$APP_USER" "$APP_DIR"
chmod 700 "$APP_DIR/App_Data"          # contains the encrypted API key and its key ring

echo "==> systemd service (port $PORT)"
sed "s/__PORT__/$PORT/" "$SRC_DIR/prodvisor-ai.service" > /etc/systemd/system/prodvisor-ai.service
systemctl daemon-reload
systemctl enable --now prodvisor-ai >/dev/null

echo "==> Waiting for the application"
for _ in $(seq 1 30); do
  if curl -fsS "http://localhost:$PORT/api/info" >/dev/null 2>&1; then
    echo
    echo "OK - PRODVISOR AI is running:  http://$(hostname -I 2>/dev/null | awk '{print $1}'):$PORT"
    echo "     status:  systemctl status prodvisor-ai      logs:  journalctl -u prodvisor-ai -f"
    exit 0
  fi
  sleep 1
done
echo "The service did not answer on port $PORT. Check:  journalctl -u prodvisor-ai -n 50 --no-pager"
exit 1
