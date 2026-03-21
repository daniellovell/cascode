#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(dirname "$script_dir")"

VERSION_FILE="$repo_root/tools/language/CascodeVersion.cs"
GOLDEN_DIR="$repo_root/tests/golden/cas"

# Extract Major and Minor from CascodeVersion.cs using portable awk
MAJOR=$(awk -F'=' '/Major[[:space:]]*=/ { gsub(/[^0-9]/, "", $2); print $2 }' "$VERSION_FILE")
MINOR=$(awk -F'=' '/Minor[[:space:]]*=/ { gsub(/[^0-9]/, "", $2); print $2 }' "$VERSION_FILE")

if [[ -z "$MAJOR" || -z "$MINOR" ]]; then
    echo "Error: Could not extract version from $VERSION_FILE" >&2
    exit 1
fi

VERSION="$MAJOR.$MINOR"
LIB_DIR="$repo_root/lib"
echo "Updating files to Cascode $VERSION"

# Detect platform for portable sed -i
if [[ "$(uname)" == "Darwin" ]]; then
    SED_INPLACE=(-i '')
else
    SED_INPLACE=(-i)
fi

# Update textual Cascode golden files (first line VERSION header)
while IFS= read -r -d '' file; do
    sed "${SED_INPLACE[@]}" -E "1s/^VERSION [0-9]+\.[0-9]+/VERSION $VERSION/" "$file"
done < <(find "$GOLDEN_DIR" \( -name "*.cas" -o -name "*.cai" \) -print0)

# Update .json files (cascodeVersion field)
while IFS= read -r -d '' file; do
    sed "${SED_INPLACE[@]}" -E "s/\"cascodeVersion\": \"[0-9]+\.[0-9]+\"/\"cascodeVersion\": \"$VERSION\"/" "$file"
done < <(find "$GOLDEN_DIR" -name "*.json" -print0)

# Update lib/ Cascode source files (first line VERSION header)
while IFS= read -r -d '' file; do
    sed "${SED_INPLACE[@]}" -E "1s/^VERSION [0-9]+\.[0-9]+/VERSION $VERSION/" "$file"
done < <(find "$LIB_DIR" -name "*.cas" -print0)

# Count actually modified files using git
count=$(git -C "$repo_root" diff --name-only -- "$GOLDEN_DIR" "$LIB_DIR" 2>/dev/null | wc -l | tr -d ' ')
echo "Updated $count files."
