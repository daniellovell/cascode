#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$script_dir"
csproj="$repo_root/tools/cli/Cascode.Cli.csproj"
nupkg_dir="$repo_root/build/nupkg"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet SDK (10+) is required to build and install cascode." >&2
  exit 1
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

if dotnet tool list -g 2>/dev/null | awk '{if(tolower($1)=="cascode.cli"){found=1}} END{if(found)exit 0; exit 1}'; then
  echo "Uninstalling existing Cascode.Cli global tool..."
  dotnet tool uninstall -g Cascode.Cli
fi

echo "Installing Cascode.Cli $pkg_version from $nupkg_dir..."
dotnet tool install -g --add-source "$nupkg_dir" --version "$pkg_version" Cascode.Cli

echo
echo "cascode is installed as a global tool."
echo "Ensure ~/.dotnet/tools is on your PATH, then run: cascode --version"
