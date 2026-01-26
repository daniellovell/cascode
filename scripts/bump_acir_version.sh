#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(dirname "$script_dir")"

VERSION_FILE="$repo_root/tools/acir/ACIRVersion.cs"
GOLDEN_DIR="$repo_root/tests/golden/acir"

# Extract Major and Minor from ACIRVersion.cs using portable awk
MAJOR=$(awk -F'=' '/Major[[:space:]]*=/ { gsub(/[^0-9]/, "", $2); print $2 }' "$VERSION_FILE")
MINOR=$(awk -F'=' '/Minor[[:space:]]*=/ { gsub(/[^0-9]/, "", $2); print $2 }' "$VERSION_FILE")

if [[ -z "$MAJOR" || -z "$MINOR" ]]; then
    echo "Error: Could not extract version from $VERSION_FILE" >&2
    exit 1
fi

VERSION="$MAJOR.$MINOR"
echo "Updating golden files to ACIR $VERSION"

# Detect platform for portable sed -i
if [[ "$(uname)" == "Darwin" ]]; then
    SED_INPLACE=(-i '')
else
    SED_INPLACE=(-i)
fi

# Update .cir files (first line header)
while IFS= read -r -d '' file; do
    sed "${SED_INPLACE[@]}" -E "1s/^ACIR [0-9]+\.[0-9]+/ACIR $VERSION/" "$file"
done < <(find "$GOLDEN_DIR" -name "*.cir" -print0)

# Update .json files (acirVersion field)
while IFS= read -r -d '' file; do
    sed "${SED_INPLACE[@]}" -E "s/\"acirVersion\": \"[0-9]+\.[0-9]+\"/\"acirVersion\": \"$VERSION\"/" "$file"
done < <(find "$GOLDEN_DIR" -name "*.json" -print0)

# Count actually modified files using git
count=$(git -C "$repo_root" diff --name-only -- "$GOLDEN_DIR" 2>/dev/null | wc -l | tr -d ' ')
echo "Updated $count files."
