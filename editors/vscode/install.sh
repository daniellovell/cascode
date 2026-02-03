#!/usr/bin/env bash
set -euo pipefail

echo "🎨 Installing Cascode syntax highlighting..."

SRC_DIR="$(cd "$(dirname "$0")" && pwd)"

# Read version from package.json
VERSION=$(grep -o '"version"[[:space:]]*:[[:space:]]*"[^"]*"' "$SRC_DIR/package.json" | head -1 | sed 's/.*"\([^"]*\)"$/\1/')
if [ -z "$VERSION" ]; then
  VERSION="0.1.3"
fi

EXTENSION_ID="cascode.cascode-lang"
EXTENSION_FOLDER="cascode.cascode-lang-${VERSION}-universal"

normalize_path() {
  local raw_path="$1"
  if command -v cygpath >/dev/null 2>&1; then
    cygpath -u "$raw_path"
  else
    printf '%s\n' "$raw_path"
  fi
}

# Register extension in extensions.json
register_extension() {
  local ext_dir="$1"
  local registry="$ext_dir/extensions.json"

  if [ ! -f "$registry" ]; then
    echo "[]" > "$registry"
  fi

  local abs_path
  abs_path="$(cd "$ext_dir" && pwd)/$EXTENSION_FOLDER"
  local timestamp
  timestamp=$(date +%s)000

  # Remove any existing cascode entry and add new one
  local new_entry
  new_entry=$(cat <<EOF
{"identifier":{"id":"$EXTENSION_ID"},"version":"$VERSION","location":{"\$mid":1,"path":"$abs_path","scheme":"file"},"relativeLocation":"$EXTENSION_FOLDER","metadata":{"installedTimestamp":$timestamp,"pinned":false,"source":"vsix","publisherDisplayName":"cascode","isPreReleaseVersion":false,"hasPreReleaseVersion":false}}
EOF
)

  # Use a temp file to safely modify the registry
  local tmp_file
  tmp_file=$(mktemp)

  if command -v python3 >/dev/null 2>&1; then
    python3 -c "
import json
import sys

registry_path = '$registry'
new_entry = json.loads('$new_entry')
ext_id = '$EXTENSION_ID'

try:
    with open(registry_path, 'r') as f:
        data = json.load(f)
except (json.JSONDecodeError, FileNotFoundError):
    data = []

# Remove existing cascode entries
data = [e for e in data if e.get('identifier', {}).get('id') != ext_id]
data.append(new_entry)

with open('$tmp_file', 'w') as f:
    json.dump(data, f, separators=(',', ':'))
"
    mv "$tmp_file" "$registry"
    echo "  📝 Registered in extensions.json"
  else
    rm -f "$tmp_file"
    echo "  ⚠️  python3 not found, skipping registry update"
  fi
}

declare -a ext_dirs=()

# Bash 3.x compatible array membership check
array_contains() {
  local needle="$1"
  shift
  for item in "$@"; do
    [ "$item" = "$needle" ] && return 0
  done
  return 1
}

add_ext_dir() {
  local candidate="$1"
  if [ -z "${candidate:-}" ]; then
    return
  fi

  local normalized
  normalized="$(normalize_path "$candidate")"

  if [ -z "$normalized" ]; then
    return
  fi

  # Skip if already added (deduplication)
  if array_contains "$normalized" "${ext_dirs[@]+"${ext_dirs[@]}"}"; then
    return
  fi

  ext_dirs+=("$normalized")
}

add_ext_dir "$HOME/.vscode/extensions"
add_ext_dir "$HOME/.cursor-server/extensions"

case "${OS:-$(uname -s)}" in
  Windows_NT|MINGW*|MSYS*|CYGWIN*)
    if [ -n "${APPDATA:-}" ]; then
      add_ext_dir "$APPDATA/Cursor/User/extensions"
    fi
    if [ -n "${USERPROFILE:-}" ]; then
      add_ext_dir "$USERPROFILE/.cursor/extensions"
    fi
    ;;
esac

if [ "${#ext_dirs[@]}" -eq 0 ]; then
  echo "No installation targets detected. Set HOME or run inside a supported environment."
  exit 1
fi

for ext_dir in "${ext_dirs[@]}"; do
  mkdir -p "$ext_dir"

  target="$ext_dir/$EXTENSION_FOLDER"

  # Remove old-style installation if present
  if [ -e "$ext_dir/cascode-lang" ]; then
    rm -rf "$ext_dir/cascode-lang"
  fi

  # Remove any existing versioned installation
  rm -rf "$ext_dir"/cascode.cascode-lang-*

  echo "  Copying to $ext_dir"
  cp -r "$SRC_DIR" "$target"

  register_extension "$ext_dir"
  echo "  ✅ Installed to $(basename "$ext_dir")"
done

if command -v codium >/dev/null 2>&1; then
  vscodium_ext_dir="$HOME/.vscode-oss/extensions"
  mkdir -p "$vscodium_ext_dir"

  rm -rf "$vscodium_ext_dir/cascode-lang"
  rm -rf "$vscodium_ext_dir"/cascode.cascode-lang-*

  cp -r "$SRC_DIR" "$vscodium_ext_dir/$EXTENSION_FOLDER"
  register_extension "$vscodium_ext_dir"
  echo "  ✅ Installed to VSCodium"
else
  echo "  ⏭️  VSCodium not found (no 'codium' command), skipping"
fi

cat <<'EON'

✨ Installation complete!

📝 Next steps:
   1. Restart your editor
   2. Open any .cas or .cai file
   3. Syntax highlighting should work automatically

   If colors don't appear, try:
   - Press Ctrl+Shift+P (Cmd+Shift+P on macOS)
   - Type 'Developer: Reload Window'
EON

