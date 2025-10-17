#!/usr/bin/env bash
set -euo pipefail

echo "🎨 Installing Cascode syntax highlighting..."

SRC_DIR="$(cd "$(dirname "$0")" && pwd)"

declare -a targets=()
declare -A seen_targets=()

normalize_path() {
  local raw_path="$1"
  if command -v cygpath >/dev/null 2>&1; then
    cygpath -u "$raw_path"
  else
    printf '%s\n' "$raw_path"
  fi
}

add_target() {
  local candidate="$1"
  if [ -z "${candidate:-}" ]; then
    return
  fi

  local normalized
  normalized="$(normalize_path "$candidate")"

  if [ -z "$normalized" ] || [ -n "${seen_targets["$normalized"]+true}" ]; then
    return
  fi

  targets+=("$normalized")
  seen_targets["$normalized"]=1
}

add_target "$HOME/.vscode/extensions/cascode-lang"
add_target "$HOME/.cursor-server/extensions/cascode-lang"

case "${OS:-$(uname -s)}" in
  Windows_NT|MINGW*|MSYS*|CYGWIN*)
    if [ -n "${APPDATA:-}" ]; then
      add_target "$APPDATA/Cursor/User/extensions/cascode-lang"
    fi
    if [ -n "${USERPROFILE:-}" ]; then
      add_target "$USERPROFILE/.cursor/extensions/cascode-lang"
    fi
    ;;
esac

if [ "${#targets[@]}" -eq 0 ]; then
  echo "No installation targets detected. Set HOME or run inside a supported environment."
  exit 1
fi

for target in "${targets[@]}"; do
  parent_dir="$(dirname "$target")"
  mkdir -p "$parent_dir"

  if [ -e "$target" ]; then
    backup="${target}.backup.$(date +%Y%m%d_%H%M%S)"
    echo "  Backing up existing installation to $(basename "$backup")"
    mv "$target" "$backup"
  fi

  echo "  Copying to $(basename "$parent_dir") ($target)"
  cp -r "$SRC_DIR" "$target"
  echo "  ✅ Installed to $(basename "$parent_dir")"
done

if command -v codium >/dev/null 2>&1; then
  vscodium_target="$HOME/.vscode-oss/extensions/cascode-lang"
  parent_dir="$(dirname "$vscodium_target")"
  mkdir -p "$parent_dir"
  
  if [ -e "$vscodium_target" ]; then
    backup="${vscodium_target}.backup.$(date +%Y%m%d_%H%M%S)"
    echo "  Backing up existing VSCodium installation to $(basename "$backup")"
    mv "$vscodium_target" "$backup"
  fi
  
  cp -r "$SRC_DIR" "$vscodium_target"
  echo "  ✅ Installed to VSCodium"
else
  echo "  ⏭️  VSCodium not found (no 'codium' command), skipping"
fi

cat <<'EON'

✨ Installation complete!

📝 Next steps:
   1. Restart your editor
   2. Open any .cas file
   3. Syntax highlighting should work automatically

   If colors don't appear, try:
   - Press Ctrl+Shift+P (Cmd+Shift+P on macOS)
   - Type 'Developer: Reload Window'
EON


