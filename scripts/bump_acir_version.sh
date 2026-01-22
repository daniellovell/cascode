#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(dirname "$script_dir")"

VERSION_FILE="$repo_root/tools/acir/ACIRVersion.cs"
GOLDEN_DIR="$repo_root/tests/golden/acir"

# Extract Major and Minor from ACIRVersion.cs
MAJOR=$(grep -oP 'Major\s*=\s*\K\d+' "$VERSION_FILE")
MINOR=$(grep -oP 'Minor\s*=\s*\K\d+' "$VERSION_FILE")

if [[ -z "$MAJOR" || -z "$MINOR" ]]; then
    echo "Error: Could not extract version from $VERSION_FILE" >&2
    exit 1
fi

VERSION="$MAJOR.$MINOR"
echo "Updating golden files to ACIR $VERSION"

find "$GOLDEN_DIR" -name "*.cir" -exec sed -i "1s/^ACIR [0-9]\+\.[0-9]\+/ACIR $VERSION/" {} +

count=$(find "$GOLDEN_DIR" -name "*.cir" | wc -l)
echo "Updated $count files."
