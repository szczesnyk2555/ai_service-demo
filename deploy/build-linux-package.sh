#!/usr/bin/env bash
# Builds publish/prodvisor-ai-linux-x64.tar.gz - a self-contained package for a Linux x64 server (no .NET needed there).
# Run from Git Bash on Windows (or any machine with the .NET 8 SDK):   ./deploy/build-linux-package.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$ROOT/publish/linux-package"
DOTNET="$(command -v dotnet || true)"
[[ -z "$DOTNET" && -n "${LOCALAPPDATA:-}" && -x "$LOCALAPPDATA/dotnet-sdk8/dotnet.exe" ]] && DOTNET="$LOCALAPPDATA/dotnet-sdk8/dotnet.exe"
[[ -n "$DOTNET" ]] || { echo ".NET 8 SDK not found"; exit 1; }

rm -rf "$OUT" && mkdir -p "$OUT"
"$DOTNET" publish "$ROOT/src/ProdvisorAi.Demo" -c Release -r linux-x64 --self-contained true \
  -p:PublishReadyToRun=false -o "$OUT/app" --nologo -v quiet
rm -f "$OUT/app/appsettings.Development.json" "$OUT/app/"*.pdb
cp "$ROOT/deploy/linux/install.sh" "$ROOT/deploy/linux/prodvisor-ai.service" "$ROOT/docs/DEPLOY-LINUX.md" "$OUT/"
# CRLF line endings (Git on Windows) would break bash and systemd
sed -i 's/\r$//' "$OUT/install.sh" "$OUT/prodvisor-ai.service"

tar -czf "$ROOT/publish/prodvisor-ai-linux-x64.tar.gz" -C "$OUT" .
echo "Package: publish/prodvisor-ai-linux-x64.tar.gz ($(du -h "$ROOT/publish/prodvisor-ai-linux-x64.tar.gz" | cut -f1))"
