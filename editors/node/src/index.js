"use strict";

const fs = require("fs");
const path = require("path");

const PLATFORM_PACKAGES = {
  "darwin-arm64": "@cascode/native-darwin-arm64",
  "darwin-x64": "@cascode/native-darwin-x64",
  "linux-x64": "@cascode/native-linux-x64",
  "win32-x64": "@cascode/native-win32-x64",
};

function getPlatformKey() {
  return `${process.platform}-${process.arch}`;
}

function getRuntimeRid() {
  if (process.platform === "win32") {
    return process.arch === "arm64" ? "win-arm64" : "win-x64";
  }
  if (process.platform === "darwin") {
    return process.arch === "arm64" ? "darwin-arm64" : "darwin-x64";
  }
  if (process.platform === "linux") {
    return process.arch === "arm64" ? "linux-arm64" : "linux-x64";
  }
  return null;
}

function getNativeLibraryFileNames() {
  if (process.platform === "win32") {
    return ["Cascode.Native.dll", "libcascode.dll"];
  }
  if (process.platform === "darwin") {
    return ["libCascode.Native.dylib", "libcascode.dylib", "Cascode.Native.dylib"];
  }
  if (process.platform === "linux") {
    return ["libCascode.Native.so", "libcascode.so", "Cascode.Native.so"];
  }
  return [];
}

function prependLibrarySearchPath(directory) {
  if (!directory) return;
  if (process.platform === "win32") {
    const existingPath = process.env.PATH ?? "";
    process.env.PATH = `${directory}${path.delimiter}${existingPath}`;
    return;
  }

  const key = process.platform === "darwin" ? "DYLD_LIBRARY_PATH" : "LD_LIBRARY_PATH";
  const existingPath = process.env[key] ?? "";
  process.env[key] = `${directory}${path.delimiter}${existingPath}`;
}

function tryLoadAddonFromPath(addonPath) {
  if (!addonPath || !fs.existsSync(addonPath)) return null;
  return require(addonPath);
}

function tryLoadFromPrebuiltPackage(errors) {
  const packageName = PLATFORM_PACKAGES[getPlatformKey()];
  if (!packageName) {
    errors.push(`No prebuilt package mapping for platform '${getPlatformKey()}'.`);
    return null;
  }

  let bundle;
  try {
    bundle = require(packageName);
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    errors.push(`Failed to require '${packageName}': ${message}`);
    return null;
  }

  const addonPath = typeof bundle?.addonPath === "string" ? bundle.addonPath : "";
  const libraryPath = typeof bundle?.libraryPath === "string" ? bundle.libraryPath : "";
  const libraryDir = typeof bundle?.libraryDir === "string" ? bundle.libraryDir : "";

  if (!addonPath || !libraryPath || !libraryDir) {
    errors.push(`Package '${packageName}' did not export addon/library paths.`);
    return null;
  }

  if (!process.env.CASCODE_NATIVE_LIB) {
    process.env.CASCODE_NATIVE_LIB = libraryPath;
  }
  prependLibrarySearchPath(libraryDir);

  try {
    return tryLoadAddonFromPath(addonPath);
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    errors.push(`Failed loading prebuilt addon from '${addonPath}': ${message}`);
    return null;
  }
}

function maybeResolveNativeLibraryPath() {
  if (process.env.CASCODE_NATIVE_LIB) return;
  const rid = getRuntimeRid();
  if (!rid) return;

  const roots = [
    // Packaged/prepared module layout: editors/node/native/<rid>/<lib>
    path.join(__dirname, "..", "native", rid),
    // Cascode repo layout: <repo>/build/native/<rid>/<lib>
    path.join(__dirname, "..", "..", "..", "build", "native", rid),
    // Alternate nested layout used by some local installs.
    path.join(__dirname, "..", "..", "build", "native", rid),
  ];

  const names = getNativeLibraryFileNames();
  for (const root of roots) {
    for (const name of names) {
      const candidate = path.join(root, name);
      if (fs.existsSync(candidate)) {
        process.env.CASCODE_NATIVE_LIB = candidate;
        prependLibrarySearchPath(root);
        return;
      }
    }
  }
}

function tryLoadFromLocalBuild(errors) {
  maybeResolveNativeLibraryPath();
  const localCandidates = [
    path.join(__dirname, "..", "build", "Release", "cascode_native_addon.node"),
    path.join(__dirname, "..", "prebuilds", "cascode_native_addon.node"),
  ];

  for (const candidate of localCandidates) {
    try {
      const addon = tryLoadAddonFromPath(candidate);
      if (addon) return addon;
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      errors.push(`Failed loading local addon from '${candidate}': ${message}`);
    }
  }

  errors.push("No local addon found in expected build/prebuild paths.");
  return null;
}

function loadAddonOrThrow() {
  const errors = [];
  const prebuiltAddon = tryLoadFromPrebuiltPackage(errors);
  if (prebuiltAddon) return prebuiltAddon;

  const localAddon = tryLoadFromLocalBuild(errors);
  if (localAddon) return localAddon;

  throw new Error(
    "[cascode-native] Failed to load native addon.\n" +
      "Checked prebuilt platform package and local build paths.\n" +
      `Errors:\n- ${errors.join("\n- ")}`
  );
}

const addon = loadAddonOrThrow();

function createSession(optionsJson = "{}") {
  return addon.createSession(optionsJson);
}

function destroySession(session) {
  addon.destroySession(session);
}

function call(session, method, requestJson) {
  return addon.call(session, method, requestJson);
}

function lastErrorJson(session) {
  return addon.lastErrorJson(session);
}

function apiVersion() {
  return addon.apiVersion();
}

function schemaVersion() {
  return addon.schemaVersion();
}

function parseCall(native, session, method, request) {
  return JSON.parse(native.call(session, method, JSON.stringify(request)));
}

function open(native, session, req) {
  return parseCall(native, session, "document.open", req);
}

function updateText(native, session, req) {
  return parseCall(native, session, "document.updateText", req);
}

function close(native, session, req) {
  return parseCall(native, session, "document.close", req);
}

function render(native, session, req) {
  return parseCall(native, session, "render.schematic", req);
}

function applyOps(native, session, req) {
  return parseCall(native, session, "schematic.applyOperations", req);
}

function erc(native, session, req) {
  return parseCall(native, session, "erc.run", req);
}

function emit(native, session, req) {
  return parseCall(native, session, "emit.run", req);
}

function verify(native, session, req) {
  return parseCall(native, session, "verify.run", req);
}

function jobStart(native, session, req) {
  return parseCall(native, session, "job.start", req);
}

function jobPoll(native, session, req) {
  return parseCall(native, session, "job.poll", req);
}

function jobCancel(native, session, req) {
  return parseCall(native, session, "job.cancel", req);
}

const native = {
  createSession,
  destroySession,
  call,
  lastErrorJson,
  apiVersion,
  schemaVersion
};

module.exports = {
  native,
  createSession,
  destroySession,
  call,
  lastErrorJson,
  apiVersion,
  schemaVersion,
  open,
  updateText,
  close,
  render,
  applyOps,
  erc,
  emit,
  verify,
  jobStart,
  jobPoll,
  jobCancel
};
