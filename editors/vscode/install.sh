#!/usr/bin/env bash
set -euo pipefail

echo "🎨 Installing Cascode syntax highlighting..."

SRC_DIR="$(cd "$(dirname "$0")" && pwd)"

targets=(
  "$HOME/.vscode/extensions/cascode-lang"
  "$HOME/.cursor-server/extensions/cascode-lang"
)

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
  target="$HOME/.vscode-oss/extensions/cascode-lang"
  parent_dir="$(dirname "$target")"
  mkdir -p "$parent_dir"
  
  if [ -e "$target" ]; then
    backup="${target}.backup.$(date +%Y%m%d_%H%M%S)"
    echo "  Backing up existing VSCodium installation to $(basename "$backup")"
    mv "$target" "$backup"
  fi
  
  cp -r "$SRC_DIR" "$target"
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


