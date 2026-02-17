#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT_DIR/tools/native/Cascode.Native/Cascode.Native.csproj"

if [[ $# -gt 0 ]]; then
  RIDS=("$@")
else
  RIDS=("linux-x64" "darwin-x64" "darwin-arm64")
fi

for rid in "${RIDS[@]}"; do
  OUT_DIR="$ROOT_DIR/build/native/$rid"
  DOTNET_RID="$rid"
  if [[ "$rid" == "darwin-x64" ]]; then
    DOTNET_RID="osx-x64"
  elif [[ "$rid" == "darwin-arm64" ]]; then
    DOTNET_RID="osx-arm64"
  fi
  mkdir -p "$OUT_DIR"
  echo "Publishing libcascode for $rid (dotnet RID: $DOTNET_RID) -> $OUT_DIR"
  dotnet publish \
    "$PROJECT" \
    --configuration Release \
    -r "$DOTNET_RID" \
    -p:PublishAot=true \
    -p:EnableAotAnalyzer=false \
    -p:EnableTrimAnalyzer=false \
    -p:NoWarn=IL2026%3BIL3050%3BCS3021 \
    -o "$OUT_DIR"
done
