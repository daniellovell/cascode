"use strict";

const fs = require("fs");
const path = require("path");

const PLATFORM_PACKAGES = {
  "darwin-arm64": "@cascode/cascode-js-darwin-arm64",
  "darwin-x64": "@cascode/cascode-js-darwin-x64",
  "linux-x64": "@cascode/cascode-js-linux-x64",
  "win32-x64": "@cascode/cascode-js-win32-x64",
};

/**
 * Constructs a platform-architecture key for the current process.
 * @returns {string} A string in the form `platform-arch` (for example, `linux-x64`, `win32-arm64`).
 */
function getPlatformKey() {
  return `${process.platform}-${process.arch}`;
}

/**
 * Get the runtime identifier (RID) for the current platform and architecture (e.g., "win-x64", "linux-arm64").
 *
 * @returns {string|null} The RID string for supported platform/architecture combinations, or `null` if unsupported.
 */
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

/**
 * Return the list of native library filenames that may be present for the current platform.
 *
 * @returns {string[]} An array of candidate native library filenames for the current process platform; returns an empty array if the platform is not recognized.
 */
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

/**
 * Prepend a directory to the native library search path for the current platform.
 *
 * On Windows this prepends to PATH, on macOS to DYLD_LIBRARY_PATH, and on other platforms to LD_LIBRARY_PATH.
 * If the provided `directory` is falsy, the function is a no-op. Existing environment values are preserved.
 * @param {string} directory - Directory to add to the front of the platform-specific library search path.
 */
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

/**
 * Load a Node native addon module from a filesystem path if the file exists.
 * @param {string} addonPath - Filesystem path to the addon (.node) or module file.
 * @returns {any|null} The required addon module when the path exists, `null` otherwise.
 */
function tryLoadAddonFromPath(addonPath) {
  if (!addonPath || !fs.existsSync(addonPath)) return null;
  return require(addonPath);
}

function getLocalAddonCandidates() {
  return [
    path.join(__dirname, "..", "build", "Release", "cascode_native_addon.node"),
    path.join(__dirname, "..", "prebuilds", "cascode_native_addon.node"),
  ];
}

/**
 * Attempts to load a platform-specific prebuilt native addon package and prepare its native library search path.
 *
 * If a mapped prebuilt package is found, this function requires the package, validates that it exports
 * `addonPath`, `libraryPath`, and `libraryDir`, sets `process.env.CASCODE_NATIVE_LIB` (if not already set),
 * and prepends `libraryDir` to the platform's library search path before loading the addon.
 *
 * @param {string[]} errors - Array to append human-readable error messages describing why loading failed.
 * @returns {object|null} The loaded native addon module on success, or `null` if no suitable package exists or loading failed.
 */
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

/**
 * Searches common installation and build locations for the platform-native library and, if found,
 * sets process.env.CASCODE_NATIVE_LIB to the discovered path and prepends that directory to the
 * process dynamic library search path.
 *
 * The function does nothing if CASCODE_NATIVE_LIB is already set or if the current platform/architecture
 * is unsupported. If no matching library is found, the environment is left unchanged.
 */
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

/**
 * Attempt to load a locally built native addon for the current platform.
 * @param {string[]} errors - Array that will receive error messages for each failed load attempt; messages are appended in-place.
 * @returns {Object|null} The loaded native addon module if found, or `null` if no local addon could be loaded.
 */
function tryLoadFromLocalBuild(errors) {
  maybeResolveNativeLibraryPath();
  const localCandidates = getLocalAddonCandidates();

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

/**
 * Load the native Cascode addon from a prebuilt package or a local build.
 *
 * Attempts to load a prebuilt platform package first, then falls back to local build artifacts.
 * @returns {object} The loaded native addon module.
 * @throws {Error} If no addon could be loaded; the error message lists attempted sources and failures.
 */
function loadAddonOrThrow() {
  const errors = [];
  maybeResolveNativeLibraryPath();
  const preferLocalBuild =
    Boolean(process.env.CASCODE_NATIVE_LIB) ||
    getLocalAddonCandidates().some((candidate) => fs.existsSync(candidate));
  if (preferLocalBuild) {
    const localAddon = tryLoadFromLocalBuild(errors);
    if (localAddon) return localAddon;
  }

  const prebuiltAddon = tryLoadFromPrebuiltPackage(errors);
  if (prebuiltAddon) return prebuiltAddon;

  if (!preferLocalBuild) {
    const localAddon = tryLoadFromLocalBuild(errors);
    if (localAddon) return localAddon;
  }

  throw new Error(
    "[cascode-native] Failed to load native addon.\n" +
      "Checked prebuilt platform package and local build paths.\n" +
      `Errors:\n- ${errors.join("\n- ")}`
  );
}

const addon = loadAddonOrThrow();

// Bundled stdlib path — try package-local first, then repo-relative.
const _bundledStdlib = path.join(__dirname, "..", "lib", "std");
const _repoStdlib = path.join(__dirname, "..", "..", "..", "lib", "std");
let stdlibPath = null;
if (fs.existsSync(_bundledStdlib)) {
  stdlibPath = _bundledStdlib;
} else if (fs.existsSync(_repoStdlib)) {
  stdlibPath = _repoStdlib;
} else {
  throw new Error(
    "[cascode-native] Standard library not found.\n" +
      `Expected either:\n- ${_bundledStdlib}\n- ${_repoStdlib}\n` +
      "If you are developing locally, ensure you are inside a cascode-lynx checkout.\n" +
      "If you installed from npm, the package may be missing bundled lib/std."
  );
}

/**
 * Create a new Cascode session using the provided JSON options.
 * @param {string} optionsJson - JSON string containing session options (e.g., "{}", configuration fields).
 * @returns {any} The created session handle to pass to other native functions.
 */
function createSession(optionsJson = "{}") {
  return addon.createSession(optionsJson);
}

/**
 * Terminates and frees a native session created by createSession.
 * @param {*} session - The native session handle returned by createSession.
 */
function destroySession(session) {
  addon.destroySession(session);
}

/**
 * Invoke a native addon method for an existing session.
 * @param {any} session - Opaque session handle returned by createSession.
 * @param {string} method - Method name to invoke (e.g., "document.open").
 * @param {string} requestJson - JSON-serialized request payload.
 * @returns {string} The JSON-serialized response from the native addon.
 */
function call(session, method, requestJson) {
  return addon.call(session, method, requestJson);
}

/**
 * Retrieve the last error for a session as a JSON string.
 * @param {*} session - Session handle returned by createSession.
 * @returns {string} The JSON-encoded error details for the session.
 */
function lastErrorJson(session) {
  return addon.lastErrorJson(session);
}

/**
 * Get the native addon's API version.
 * @returns {number} The API version number.
 */
function apiVersion() {
  return addon.apiVersion();
}

/**
 * Retrieve the schema version exposed by the native addon.
 * @returns {number} The schema version number.
 */
function schemaVersion() {
  return addon.schemaVersion();
}

function invoke(native, session, method, request = {}) {
  return JSON.parse(native.call(session, method, JSON.stringify(request)));
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
  stdlibPath,
  createSession,
  destroySession,
  call,
  invoke,
  lastErrorJson,
  apiVersion,
  schemaVersion
};
