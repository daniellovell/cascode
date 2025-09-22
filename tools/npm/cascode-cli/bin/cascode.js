#!/usr/bin/env node
"use strict";

const { spawn } = require("child_process");
const fs = require("fs");
const path = require("path");

function rid() {
  const p = process.platform;
  const a = process.arch;
  if (p === "win32") return a === "arm64" ? "win-arm64" : "win-x64";
  if (p === "darwin") return a === "arm64" ? "osx-arm64" : "osx-x64";
  if (p === "linux") return a === "arm64" ? "linux-arm64" : "linux-x64";
  return `${p}-${a}`;
}

function binPath() {
  const base = path.join(__dirname, "..", "vendor", rid());
  const names = process.platform === "win32" ? ["cascode.exe", "Cascode.Cli.exe"] : ["cascode", "Cascode.Cli"];
  for (const n of names) {
    const p = path.join(base, n);
    if (fs.existsSync(p)) return p;
  }
  return path.join(base, names[0]);
}

const exe = binPath();
if (!fs.existsSync(exe)) {
  console.error("cascode: no platform binary found at", exe);
  console.error(
    "Install step could not fetch a prebuilt binary. Options:\n" +
      "  1) Ensure your network permits downloads from GitHub Releases and reinstall, or set CASCODE_DOWNLOAD_BASE.\n" +
      "  2) Use the .NET tool:   dotnet tool install -g Cascode.Cli\n" +
      "  3) Download a release binary manually from https://github.com/daniellovell/cascode/releases"
  );
  process.exit(1);
}

const args = process.argv.slice(2);
const child = spawn(exe, args, { stdio: "inherit" });
child.on("exit", (code, signal) => {
  if (signal) {
    process.kill(process.pid, signal);
  } else {
    process.exit(code ?? 0);
  }
});
