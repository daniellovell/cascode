"use strict";

const fs = require("fs");
const path = require("path");
const os = require("os");
const https = require("https");
const { pipeline } = require("stream/promises");
const zlib = require("zlib");
const tar = require("tar");
const extractZip = require("extract-zip");

function rid() {
  const p = process.platform;
  const a = process.arch;
  if (p === "win32") return a === "arm64" ? "win-arm64" : "win-x64";
  if (p === "darwin") return a === "arm64" ? "osx-arm64" : "osx-x64";
  if (p === "linux") return a === "arm64" ? "linux-arm64" : "linux-x64";
  return `${p}-${a}`;
}

function getPkgVersion() {
  try {
    const pkg = require(path.join(__dirname, "..", "package.json"));
    return pkg.version;
  } catch {
    return process.env.CASCODE_VERSION || "0.0.0";
  }
}

function dl(url) {
  return new Promise((resolve, reject) => {
    https
      .get(url, (res) => {
        if (res.statusCode >= 300 && res.statusCode < 400 && res.headers.location) {
          // Follow redirect
          return resolve(dl(res.headers.location));
        }
        if (res.statusCode !== 200) {
          return reject(new Error(`Download failed: ${res.statusCode} ${res.statusMessage}`));
        }
        resolve(res);
      })
      .on("error", reject);
  });
}

async function main() {
  const platformRid = rid();
  const isWin = process.platform === "win32";
  const version = getPkgVersion();
  const base = process.env.CASCODE_DOWNLOAD_BASE || "https://github.com/daniellovell/cascode/releases/download";
  const asset = `cascode-${platformRid}.${isWin ? "zip" : "tar.gz"}`;
  const url = `${base}/v${version}/${asset}`;

  const destDir = path.join(__dirname, "..", "vendor", platformRid);
  const exeName = isWin ? "cascode.exe" : "cascode";
  const altName = isWin ? "Cascode.Cli.exe" : "Cascode.Cli";
  const destExe = path.join(destDir, exeName);
  if (fs.existsSync(destExe)) {
    return; // already present
  }
  fs.mkdirSync(destDir, { recursive: true });

  console.log(`cascode: downloading ${asset} ...`);
  try {
    if (isWin) {
      const tmpDir = await fs.promises.mkdtemp(path.join(os.tmpdir(), "cascode-"));
      const zipPath = path.join(tmpDir, asset);
      const res = await dl(url);
      await pipeline(res, fs.createWriteStream(zipPath));
      await extractZip(zipPath, { dir: destDir });
    } else {
      const res = await dl(url);
      await pipeline(res, zlib.createGunzip(), tar.x({ cwd: destDir }));
    }
    // Normalize executable name to 'cascode[.exe]'
    const altExe = path.join(destDir, altName);
    if (!fs.existsSync(destExe) && fs.existsSync(altExe)) {
      fs.renameSync(altExe, destExe);
    }
    if (process.platform !== "win32" && fs.existsSync(destExe)) {
      fs.chmodSync(destExe, 0o755);
    }
    console.log("cascode: install complete.");
  } catch (err) {
    console.warn("cascode: prebuilt download failed:", err.message);
    console.warn(
      "You can either retry with network access, set CASCODE_DOWNLOAD_BASE to your mirror, or install the .NET tool: dotnet tool install -g Cascode.Cli"
    );
  }
}

main().catch((e) => {
  console.warn("cascode: unexpected error during install:", e);
});
