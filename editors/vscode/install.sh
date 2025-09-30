#!/bin/bash
# Install Cascode syntax highlighting for VS Code

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
VSCODE_EXT_DIR="$HOME/.vscode/extensions"
VSCODIUM_EXT_DIR="$HOME/.vscode-oss/extensions"

# Auto-detect Cursor extensions directory
detect_cursor_ext_dir() {
    # Check both possible Cursor extension directories
    local cursor_dir1="$HOME/.cursor/extensions"
    local cursor_dir2="$HOME/.cursor-server/extensions"
    
    # Prefer the one that exists and has extensions
    if [ -d "$cursor_dir1" ] && [ "$(ls -A "$cursor_dir1" 2>/dev/null)" ]; then
        echo "$cursor_dir1"
    elif [ -d "$cursor_dir2" ] && [ "$(ls -A "$cursor_dir2" 2>/dev/null)" ]; then
        echo "$cursor_dir2"
    # Fall back to the one that exists (even if empty)
    elif [ -d "$cursor_dir1" ]; then
        echo "$cursor_dir1"
    elif [ -d "$cursor_dir2" ]; then
        echo "$cursor_dir2"
    # Default to .cursor-server if neither exists
    else
        echo "$cursor_dir2"
    fi
}

CURSOR_EXT_DIR="$(detect_cursor_ext_dir)"

echo "🎨 Installing Cascode syntax highlighting..."

# Function to check if editor is installed
editor_exists() {
    local editor_cmd="$1"
    command -v "$editor_cmd" >/dev/null 2>&1
}

# Function to install to a specific extensions directory
install_to_dir() {
    local ext_dir="$1"
    local editor_name="$2"
    local editor_cmd="$3"
    
    # Check if editor executable exists
    if ! editor_exists "$editor_cmd"; then
        echo "  ⏭️  $editor_name not found (no '$editor_cmd' command), skipping"
        return 1
    fi
    
    # Create extensions directory if it doesn't exist
    if [ ! -d "$ext_dir" ]; then
        echo "  Creating extensions directory: $ext_dir"
        mkdir -p "$ext_dir"
    fi
    
    local target="$ext_dir/cascode-lang"
    
    # Remove existing if present
    if [ -L "$target" ] || [ -d "$target" ]; then
        echo "  Removing existing installation at $target"
        rm -rf "$target"
    fi
    
    # Create symlink
    echo "  Installing to $editor_name ($target)"
    ln -s "$SCRIPT_DIR" "$target"
    echo "  ✅ Installed to $editor_name"
    return 0
}

# Try installing to various editors
installed_count=0

if install_to_dir "$VSCODE_EXT_DIR" "VS Code" "code"; then
    installed_count=$((installed_count + 1))
fi

if install_to_dir "$CURSOR_EXT_DIR" "Cursor" "cursor"; then
    installed_count=$((installed_count + 1))
fi

if install_to_dir "$VSCODIUM_EXT_DIR" "VSCodium" "codium"; then
    installed_count=$((installed_count + 1))
fi

if [ $installed_count -eq 0 ]; then
    echo ""
    echo "❌ No compatible editors found!"
    echo "   Please install VS Code, Cursor, or VSCodium first."
    exit 1
fi

echo ""
echo "✨ Installation complete!"
echo ""
echo "📝 Next steps:"
echo "   1. Restart your editor"
echo "   2. Open any .cas file"
echo "   3. Syntax highlighting should work automatically"
echo ""
echo "   If colors don't appear, try:"
echo "   - Press Ctrl+Shift+P (Cmd+Shift+P on macOS)"
echo "   - Type 'Developer: Reload Window'"
echo ""
exit 0
