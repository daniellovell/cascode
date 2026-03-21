@cascode/cascode-cli (npm wrapper)

This package enables a one-line install of the Cascode CLI via npm:

    npm install -g @cascode/cascode-cli

How it works
- On install, a small script downloads a prebuilt, self-contained `cascode` binary
  for your OS/architecture from the GitHub Releases page.
- The `cascode` command is provided via the package `bin` and forwards args to the binary.
- Supported environments match [tools/npm/cascode-cli/package.json](./package.json): Node `>=16` on
  `darwin`, `linux`, or `win32`.
- Simulator dependencies are managed by the CLI. Install ngspice with:

      cascode install ngspice

  This default path downloads a prebuilt ngspice package from the same GitHub release tag as the
  installed Cascode CLI version. To force source mode, run:

      cascode install ngspice --from-source

  Source builds require the native toolchain that the CLI installer expects:
  `7z` on Windows, or `bison`, `flex`, `autoconf`, `automake`, `libtool`, `make`, and `cc` on Unix.

Environment variables
- `CASCODE_DOWNLOAD_BASE` (optional): override the GitHub Releases base URL, e.g.
  `https://your.mirror.example.com/cascode/releases/download`.
- `CASCODE_VERSION` (optional): fallback version used only when `package.json` cannot be read during install.

Fallbacks
- If a prebuilt binary cannot be downloaded (e.g., due to network policy), you can
  install the .NET global tool version:

    dotnet tool install -g Cascode.Cli

Notes
- Release artifacts are expected to be named `cascode-<rid>.<zip|tar.gz>`, where rid is one of:
  `win-x64`, `win-arm64`, `osx-x64`, `osx-arm64`, `linux-x64`, `linux-arm64`.
- The repository’s release workflow should publish those assets alongside tags matching the npm package version.
