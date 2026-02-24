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

/**
 * Load the native Cascode addon from a prebuilt package or a local build.
 *
 * Attempts to load a prebuilt platform package first, then falls back to local build artifacts.
 * @returns {object} The loaded native addon module.
 * @throws {Error} If no addon could be loaded; the error message lists attempted sources and failures.
 */
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

/**
 * Invoke the native addon's `call` method with the given request and parse its JSON response.
 * @param {{call: function}} native - Native addon exposing a `call(session, method, json)` function.
 * @param {*} session - Session handle previously returned by `createSession`.
 * @param {string} method - Method name to invoke on the native addon.
 * @param {*} request - Request payload to be serialized to JSON and sent to the native addon.
 * @returns {*} The parsed response object returned by the native addon.
 */
function parseCall(native, session, method, request) {
  return JSON.parse(native.call(session, method, JSON.stringify(request)));
}

/**
 * Invoke the native "document.open" method and parse its JSON response.
 * @param {*} native - The loaded native addon module.
 * @param {*} session - The native session handle.
 * @param {*} req - The request payload (object or JSON string) to send to the native call.
 * @returns {object} The parsed JSON response from the native call.
 */
function open(native, session, req) {
  return parseCall(native, session, "document.open", req);
}

/**
 * Invoke the native addon's "document.updateText" method for a session and return the parsed response.
 * @param {Object} native - The loaded native addon module.
 * @param {*} session - The session handle returned by the native addon.
 * @param {Object|string} req - The request payload for the updateText call (object or JSON string).
 * @returns {Object} The parsed JSON response from the native method.
 */
function updateText(native, session, req) {
  return parseCall(native, session, "document.updateText", req);
}

/**
 * Close a document in the given session using the native addon.
 * @param {Object} native - The loaded native addon module.
 * @param {*} session - The session handle returned by createSession.
 * @param {Object} req - Request payload for the "document.close" call.
 * @returns {Object} The parsed JSON response returned by the native call.
 */
function close(native, session, req) {
  return parseCall(native, session, "document.close", req);
}

/**
 * Request a schematic render from the native addon and return its parsed response.
 * @param {object} native - The native addon module to invoke.
 * @param {*} session - The session handle previously returned by createSession.
 * @param {object|string} req - The render request (object or JSON string) to send to the native addon.
 * @returns {object} The parsed response object returned by the native render operation.
 */
function render(native, session, req) {
  return parseCall(native, session, "render.schematic", req);
}

/**
 * Invoke the native "schematic.applyOperations" method and return its parsed result.
 * @param {object} native - The native addon object exposing call bindings.
 * @param {*} session - The native session handle.
 * @param {*} req - The request payload to send; typically an object or JSON string.
 * @returns {object} The parsed response object from the native "schematic.applyOperations" call.
 */
function applyOps(native, session, req) {
  return parseCall(native, session, "schematic.applyOperations", req);
}

/**
 * Run electrical rule check for the given session and request.
 * @param {object} native - Loaded native addon exposing RPC methods.
 * @param {any} session - Session identifier returned by `createSession`.
 * @param {object|string} req - Request payload for the ERC operation.
 * @returns {object} The parsed response from the native `erc.run` call.
 */
function erc(native, session, req) {
  return parseCall(native, session, "erc.run", req);
}

/**
 * Invoke the native "emit.run" operation for the given session.
 * @param {object} native - The loaded native addon module.
 * @param {*} session - The session handle returned by createSession.
 * @param {object|string} req - The request payload (object or JSON string) for the emit operation.
 * @returns {object} The parsed response object from the "emit.run" operation.
 */
function emit(native, session, req) {
  return parseCall(native, session, "emit.run", req);
}

/**
 * Invoke the native "verify.run" method for the given session and request.
 * @param {*} native - The native addon module used to perform the call.
 * @param {*} session - The native session handle.
 * @param {string|Object} req - The request payload (JSON string or object) to send.
 * @returns {Object} The parsed response object returned by the native verify.run call.
 */
function verify(native, session, req) {
  return parseCall(native, session, "verify.run", req);
}

/**
 * Invoke the native "job.start" method for a session and return its parsed response.
 * @param {object} native - The loaded native addon exposing call APIs.
 * @param {*} session - The session handle returned by createSession.
 * @param {object} req - The request payload to send to the native method.
 * @returns {object} The parsed response object returned by the native addon.
 */
function jobStart(native, session, req) {
  return parseCall(native, session, "job.start", req);
}

/**
 * Polls the status of a background job.
 * @param {*} native - The native addon module to invoke.
 * @param {*} session - The session handle returned by createSession.
 * @param {Object} req - The request payload for the job poll.
 * @returns {*} The parsed response from the native `job.poll` call.
 */
function jobPoll(native, session, req) {
  return parseCall(native, session, "job.poll", req);
}

/**
 * Invoke the native "job.cancel" method and return its parsed JSON response.
 * @param {any} native - The loaded native addon module exposing the `call` entrypoint.
 * @param {number|string|object} session - Session identifier returned by `createSession`.
 * @param {object} req - Request payload for the cancel operation.
 * @returns {object} Parsed JSON response from the native `job.cancel` call.
 */
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
  stdlibPath,
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
