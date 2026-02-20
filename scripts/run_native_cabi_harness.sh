#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PUBLISH_DIR="$ROOT_DIR/build/native/linux-x64"
HARNESS_SRC="$ROOT_DIR/tests/integration/native/cabi/cascode_cabi_harness.c"
HARNESS_BIN="$ROOT_DIR/build/native/cascode_cabi_harness"
LSAN_SUPPRESSIONS="$ROOT_DIR/tests/integration/native/cabi/lsan.supp"

mkdir -p "$PUBLISH_DIR"
mkdir -p "$(dirname "$HARNESS_BIN")"

dotnet publish \
  "$ROOT_DIR/tools/native/Cascode.Native/Cascode.Native.csproj" \
  --configuration Release \
  -r linux-x64 \
  -p:PublishAot=true \
  -p:EnableAotAnalyzer=false \
  -p:EnableTrimAnalyzer=false \
  -p:NoWarn=IL2026%3BIL3050%3BCS3021 \
  -o "$PUBLISH_DIR"

LIB_PATH="$(find "$PUBLISH_DIR" -maxdepth 1 -type f -name 'Cascode.Native.so' | head -n 1)"
if [[ -z "$LIB_PATH" ]]; then
  echo "No Cascode.Native.so library found in $PUBLISH_DIR" >&2
  exit 1
fi

cc \
  -std=c11 \
  -O1 \
  -g \
  -fno-omit-frame-pointer \
  -fsanitize=address,undefined \
  "$HARNESS_SRC" \
  -ldl \
  -o "$HARNESS_BIN"

LD_LIBRARY_PATH="$PUBLISH_DIR:${LD_LIBRARY_PATH:-}" \
ASAN_OPTIONS=detect_leaks=0:halt_on_error=1 \
LSAN_OPTIONS="suppressions=$LSAN_SUPPRESSIONS:print_suppressions=0" \
UBSAN_OPTIONS=halt_on_error=1 \
"$HARNESS_BIN" "$LIB_PATH"
