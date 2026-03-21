#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
NODE_DIR="$ROOT_DIR/editors/node"

# Determine current platform RID
OS="$(uname -s)"
ARCH="$(uname -m)"
case "$OS-$ARCH" in
  Darwin-arm64)  RID="darwin-arm64" ;;
  Darwin-x86_64) RID="darwin-x64"   ;;
  Linux-x86_64)  RID="linux-x64"    ;;
  *)
    echo "Unsupported platform: $OS-$ARCH" >&2
    exit 1
    ;;
esac

echo "==> Publishing Cascode.Native for $RID"
"$ROOT_DIR/scripts/publish_libcascode.sh" "$RID"

echo "==> Building Node native addon"
cd "$NODE_DIR"
npm ci --omit=optional
npm run build

echo ""
echo "Done. To use in dev, set:"
echo "  export CASCODE_NATIVE_LIB=$ROOT_DIR/build/native/$RID/Cascode.Native.dylib"
