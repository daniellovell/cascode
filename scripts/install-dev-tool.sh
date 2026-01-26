#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(dirname "$script_dir")"
csproj="$repo_root/tools/cli/Cascode.Cli.csproj"
nupkg_dir="$repo_root/build/nupkg"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet SDK (10+) is required to build and install cascode." >&2
  exit 1
fi

# Tools are placed under $DOTNET_ROOT/tools when DOTNET_ROOT is set, otherwise
# $HOME/.dotnet/tools. For custom paths, use --tool-path explicitly or document
# the chosen location in your environment setup.
if [[ -n "${DOTNET_ROOT:-}" ]]; then
  tools_dir="$DOTNET_ROOT/tools"
else
  tools_dir="$HOME/.dotnet/tools"
fi

mkdir -p "$nupkg_dir"
rm -f "$nupkg_dir"/*.nupkg

echo "Packing Cascode CLI (dev)..."
dotnet pack "$csproj" -c Release -o "$nupkg_dir" -p:Version=0.0.0-dev -p:InformationalVersion=dev
nupkg=$(ls "$nupkg_dir"/Cascode.Cli.*.nupkg 2>/dev/null | head -n 1 || true)
if [[ -z "${nupkg:-}" ]]; then
  echo "Pack did not produce Cascode.Cli nupkg in $nupkg_dir" >&2
  exit 1
fi
pkg_version=$(basename "$nupkg" | sed -E 's/^Cascode\.Cli\.([^.]+\.[^.]+\.[^.]+.*)\.nupkg$/\1/')
if [[ -z "$pkg_version" ]]; then
  echo "Could not parse package version from $nupkg" >&2
  exit 1
fi

# Check if tool is already installed at the target path
if [[ -x "$tools_dir/cascode" ]]; then
  echo "Uninstalling existing Cascode.Cli tool from $tools_dir..."
  dotnet tool uninstall --tool-path "$tools_dir" Cascode.Cli || true
fi

echo "Installing Cascode.Cli $pkg_version to $tools_dir..."
dotnet tool install --tool-path "$tools_dir" --add-source "$nupkg_dir" --version "$pkg_version" Cascode.Cli

echo
echo "cascode is installed to: $tools_dir"
echo "Ensure $tools_dir is on your PATH, then run: cascode --version"
