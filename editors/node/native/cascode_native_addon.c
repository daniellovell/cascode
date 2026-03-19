#include <node_api.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#include <io.h>
#include <windows.h>
#define ACCESS _access
#define READ_OK 4
typedef HMODULE dylib_handle_t;
static SRWLOCK g_load_mutex = SRWLOCK_INIT;
/**
 * Acquire the exclusive mutex used to serialize loading and unloading of the
 * native library.
 *
 * Blocks until the lock is obtained. Protects the global load state (g_exports,
 * g_attempted_load, g_load_error).
 */
static void lock_load_mutex(void) { AcquireSRWLockExclusive(&g_load_mutex); }
/**
 * Release exclusive ownership of the global load mutex used for library
 * loading.
 *
 * Allows other threads to acquire the mutex and proceed with loading or
 * accessing the cached exports.
 */
static void unlock_load_mutex(void) { ReleaseSRWLockExclusive(&g_load_mutex); }
#elif defined(__linux__) || defined(__APPLE__)
#include <dlfcn.h>
#include <pthread.h>
#include <unistd.h>
#define ACCESS access
#define READ_OK R_OK
typedef void* dylib_handle_t;
static pthread_mutex_t g_load_mutex = PTHREAD_MUTEX_INITIALIZER;
/**
 * Acquire the global load mutex to serialize dynamic library load/unload
 * operations.
 *
 * Blocks the calling thread until the mutex protecting export loading is
 * obtained.
 */
static void lock_load_mutex(void) { pthread_mutex_lock(&g_load_mutex); }
/**
 * Release the global mutex that protects one-time library loading.
 *
 * This function unlocks the mutex guarding the load/unload sequence for the
 * native cascode library so other threads may proceed with load-related work.
 */
static void unlock_load_mutex(void) { pthread_mutex_unlock(&g_load_mutex); }
#else
#error "cascode_native_addon currently supports linux, macOS, and Windows."
#endif

typedef int32_t (*cascode_create_session_fn)(const char* options_json_utf8);
typedef void (*cascode_destroy_session_fn)(int32_t session);
typedef void (*cascode_free_string_fn)(char* ptr);
typedef char* (*cascode_last_error_json_fn)(int32_t session);
typedef char* (*cascode_session_call_fn)(int32_t session,
                                         const char* request_json);
typedef char* (*cascode_version_fn)(void);

typedef struct cascode_exports_s {
  dylib_handle_t handle;
  cascode_create_session_fn create_session;
  cascode_destroy_session_fn destroy_session;
  cascode_free_string_fn free_string;
  cascode_last_error_json_fn last_error_json;
  cascode_session_call_fn document_open;
  cascode_session_call_fn document_update_text;
  cascode_session_call_fn document_close;
  cascode_session_call_fn source_rewrite_schematic;
  cascode_session_call_fn convert_to_structural;
  cascode_session_call_fn convert_to_cas;
  cascode_session_call_fn render_schematic;
  cascode_session_call_fn schematic_apply_ops;
  cascode_session_call_fn schematic_apply_placement_edits;
  cascode_session_call_fn schematic_capture_manual_snapshot;
  cascode_session_call_fn schematic_preview_route;
  cascode_session_call_fn schematic_apply_route_edit;
  cascode_session_call_fn erc_run;
  cascode_session_call_fn emit_run;
  cascode_session_call_fn verify_run;
  cascode_session_call_fn command_execute;
  cascode_session_call_fn job_start;
  cascode_session_call_fn job_poll;
  cascode_session_call_fn job_cancel;
  cascode_session_call_fn pdk_set_dir;
  cascode_session_call_fn pdk_scan;
  cascode_session_call_fn pdk_emit_primitives;
  cascode_version_fn api_version;
  cascode_version_fn schema_version;
} cascode_exports_t;

typedef struct method_entry_s {
  const char* method;
  cascode_session_call_fn fn;
} method_entry_t;

static cascode_exports_t g_exports;
static bool g_attempted_load = false;
static char g_load_error[512];

/**
 * Set or clear the module load error message stored in the internal buffer.
 *
 * If `message` is NULL the stored load error is cleared; otherwise the provided
 * message is copied into the internal `g_load_error` buffer (truncated to fit).
 *
 * @param message Error message to store, or NULL to clear the stored error.
 */
static void set_load_error(const char* message) {
  if (message == NULL) {
    g_load_error[0] = '\0';
    return;
  }

  snprintf(g_load_error, sizeof(g_load_error), "%s", message);
}

/**
 * Extract the directory component from a filesystem path.
 *
 * Copies the directory part of `path` into `out`. If `path` contains no
 * directory separator ('/' or '\'), writes "." into `out`. The result is
 * always NUL-terminated; if the directory component is longer than
 * `out_size - 1`, it is truncated to fit.
 *
 * @param path Input filesystem path (may use '/' or '\' separators).
 * @param out Destination buffer to receive the directory component.
 * @param out_size Size of `out` in bytes; must be greater than 0.
 */
static void extract_directory(const char* path, char* out, size_t out_size) {
  const char* slash_forward = strrchr(path, '/');
  const char* slash_backward = strrchr(path, '\\');
  const char* slash = slash_forward;
  if (slash == NULL || (slash_backward != NULL && slash_backward > slash)) {
    slash = slash_backward;
  }
  if (slash == NULL) {
    snprintf(out, out_size, ".");
    return;
  }

  size_t length = (size_t)(slash - path);
  if (length >= out_size) {
    length = out_size - 1;
  }

  memcpy(out, path, length);
  out[length] = '\0';
}

/**
 * Attempt to preload a native dependency file from the given directory.
 *
 * Constructs a full path from `directory` and `file_name` and, if the file is
 * accessible, tries to load it into the process (platform-specific). On load
 * failure records a human-readable message via set_load_error().
 *
 * @param directory Directory containing the dependency; may be a relative or
 * absolute path.
 * @param file_name File name of the dependency to preload.
 * @returns `true` if the dependency is either not present/readable or was
 * successfully loaded; `false` if the file was present but failed to load (and
 * a load error was recorded).
 */
static bool preload_dependency(const char* directory, const char* file_name) {
  char full_path[1024];
  snprintf(full_path, sizeof(full_path), "%s/%s", directory, file_name);
  if (ACCESS(full_path, READ_OK) != 0) {
    return true;
  }

#if defined(_WIN32)
  HMODULE handle = LoadLibraryA(full_path);
  if (handle == NULL) {
    DWORD code = GetLastError();
    char windows_error[256];
    snprintf(windows_error, sizeof(windows_error), "Windows error code %lu",
             (unsigned long)code);

    char buffer[512];
    snprintf(buffer, sizeof(buffer), "Failed to preload '%s': %s", full_path,
             windows_error);
    set_load_error(buffer);
    return false;
  }
#else
  void* handle = dlopen(full_path, RTLD_NOW | RTLD_GLOBAL);
  if (handle == NULL) {
    const char* dl_error = dlerror();
    if (dl_error == NULL) {
      dl_error = "unknown dependency load error";
    }

    char buffer[512];
    snprintf(buffer, sizeof(buffer), "Failed to preload '%s': %s", full_path,
             dl_error);
    set_load_error(buffer);
    return false;
  }
#endif

  return true;
}

/**
 * Resolve a symbol by name from the currently loaded native library and store
 * its address.
 *
 * On success writes the resolved pointer into `*target`. On failure records a
 * load error message via set_load_error and leaves `*target` set to NULL.
 *
 * @param target Pointer to receive the resolved symbol address (stores NULL on
 * failure).
 * @param name   NUL-terminated symbol name to resolve.
 * @returns `true` if the symbol was resolved and stored in `*target`, `false`
 * otherwise.
 */
static bool resolve_symbol(void** target, const char* name) {
#if defined(_WIN32)
  *target = (void*)GetProcAddress(g_exports.handle, name);
#else
  *target = dlsym(g_exports.handle, name);
#endif
  if (*target == NULL) {
    char buffer[512];
    snprintf(buffer, sizeof(buffer), "Failed to resolve symbol '%s'.", name);
    set_load_error(buffer);
    return false;
  }

  return true;
}

/**
 * Unload the currently loaded cascode native library and clear the cached
 * exports.
 *
 * If no library is loaded, this function does nothing. After it returns the
 * global g_exports structure is zeroed, removing the stored library handle and
 * function pointers.
 */
static void unload_exports(void) {
  if (g_exports.handle != NULL) {
#if defined(_WIN32)
    FreeLibrary(g_exports.handle);
#else
    dlclose(g_exports.handle);
#endif
  }

  memset(&g_exports, 0, sizeof(g_exports));
}

/**
 * Attempt to load the Cascode native library and resolve all required symbols.
 *
 * Loads the library specified by the CASCODE_NATIVE_LIB environment variable,
 * preloads platform-specific dependencies, resolves required exported symbols
 * into g_exports, and records any load or symbol resolution error in
 * g_load_error. This function is mutex-protected and performs the load
 * operation at most once; subsequent calls return the cached result.
 *
 * @returns `true` if the library was loaded and all required symbols were
 * resolved, `false` otherwise.
 */
static bool load_exports(void) {
  bool success = false;
  lock_load_mutex();

  if (g_attempted_load) {
    success = g_exports.handle != NULL;
    unlock_load_mutex();
    return success;
  }

  g_attempted_load = true;
  set_load_error(NULL);

  const char* library_path = getenv("CASCODE_NATIVE_LIB");
  if (library_path == NULL || library_path[0] == '\0') {
    set_load_error("CASCODE_NATIVE_LIB is not set.");
    goto done;
  }

  char library_dir[1024];
  extract_directory(library_path, library_dir, sizeof(library_dir));
#if defined(_WIN32)
  SetDllDirectoryA(library_dir);
  if (!preload_dependency(library_dir, "e_sqlite3.dll")) {
    goto done;
  }
  if (!preload_dependency(library_dir, "libe_sqlite3.dll")) {
    goto done;
  }
  if (!preload_dependency(library_dir, "google-ortools-native.dll")) {
    goto done;
  }
#elif defined(__APPLE__)
  if (!preload_dependency(library_dir, "libe_sqlite3.dylib")) {
    goto done;
  }

  if (!preload_dependency(library_dir, "libortools.9.dylib")) {
    goto done;
  }

  if (!preload_dependency(library_dir, "google-ortools-native.dylib")) {
    goto done;
  }
#else
  if (!preload_dependency(library_dir, "libe_sqlite3.so")) {
    goto done;
  }
  if (!preload_dependency(library_dir, "e_sqlite3.so")) {
    goto done;
  }
  if (!preload_dependency(library_dir, "libortools.so.9")) {
    goto done;
  }

  if (!preload_dependency(library_dir, "google-ortools-native.so")) {
    goto done;
  }
#endif

#if defined(_WIN32)
  g_exports.handle = LoadLibraryA(library_path);
#else
  g_exports.handle = dlopen(library_path, RTLD_NOW | RTLD_LOCAL);
#endif
  if (g_exports.handle == NULL) {
#if defined(_WIN32)
    DWORD code = GetLastError();
    char windows_error[256];
    snprintf(windows_error, sizeof(windows_error), "Windows error code %lu",
             (unsigned long)code);
#else
    const char* dl_error = dlerror();
    if (dl_error == NULL) {
      dl_error = "Unknown dlopen error.";
    }
#endif

    char buffer[512];
#if defined(_WIN32)
    snprintf(buffer, sizeof(buffer), "Failed to load '%s': %s", library_path,
             windows_error);
#else
    snprintf(buffer, sizeof(buffer), "Failed to load '%s': %s", library_path,
             dl_error);
#endif
    set_load_error(buffer);
    goto done;
  }

  if (!resolve_symbol((void**)&g_exports.create_session,
                      "cascode_create_session"))
    goto done;
  if (!resolve_symbol((void**)&g_exports.destroy_session,
                      "cascode_destroy_session"))
    goto done;
  if (!resolve_symbol((void**)&g_exports.free_string, "cascode_free_string"))
    goto done;
  if (!resolve_symbol((void**)&g_exports.last_error_json,
                      "cascode_last_error_json"))
    goto done;
  if (!resolve_symbol((void**)&g_exports.api_version, "cascode_api_version"))
    goto done;
  if (!resolve_symbol((void**)&g_exports.schema_version,
                      "cascode_schema_version"))
    goto done;
  if (!resolve_symbol((void**)&g_exports.document_open,
                      "cascode_document_open"))
    goto done;
  if (!resolve_symbol((void**)&g_exports.document_update_text,
                      "cascode_document_update_text"))
    goto done;
  if (!resolve_symbol((void**)&g_exports.document_close,
                      "cascode_document_close"))
    goto done;
  if (!resolve_symbol((void**)&g_exports.source_rewrite_schematic,
                      "cascode_source_rewrite_schematic"))
    goto done;
  if (!resolve_symbol((void**)&g_exports.convert_to_structural,
                      "cascode_convert_to_structural"))
    goto done;
  if (!resolve_symbol((void**)&g_exports.convert_to_cas,
                      "cascode_convert_to_cas"))
    goto done;
  if (!resolve_symbol((void**)&g_exports.render_schematic,
                      "cascode_render_schematic"))
    goto done;
  if (!resolve_symbol((void**)&g_exports.schematic_apply_ops,
                      "cascode_schematic_apply_ops"))
    goto done;
  if (!resolve_symbol((void**)&g_exports.schematic_apply_placement_edits,
                      "cascode_schematic_apply_placement_edits"))
    goto done;
  if (!resolve_symbol((void**)&g_exports.schematic_capture_manual_snapshot,
                      "cascode_schematic_capture_manual_snapshot"))
    goto done;
  if (!resolve_symbol((void**)&g_exports.schematic_preview_route,
                      "cascode_schematic_preview_route"))
    goto done;
  if (!resolve_symbol((void**)&g_exports.schematic_apply_route_edit,
                      "cascode_schematic_apply_route_edit"))
    goto done;
  if (!resolve_symbol((void**)&g_exports.erc_run, "cascode_erc_run")) goto done;
  if (!resolve_symbol((void**)&g_exports.emit_run, "cascode_emit_run"))
    goto done;
  if (!resolve_symbol((void**)&g_exports.verify_run, "cascode_verify_run"))
    goto done;
  if (!resolve_symbol((void**)&g_exports.command_execute,
                      "cascode_command_execute"))
    goto done;
  if (!resolve_symbol((void**)&g_exports.job_start, "cascode_job_start"))
    goto done;
  if (!resolve_symbol((void**)&g_exports.job_poll, "cascode_job_poll"))
    goto done;
  if (!resolve_symbol((void**)&g_exports.job_cancel, "cascode_job_cancel"))
    goto done;
  if (!resolve_symbol((void**)&g_exports.pdk_set_dir, "cascode_pdk_set_dir"))
    goto done;
  if (!resolve_symbol((void**)&g_exports.pdk_scan, "cascode_pdk_scan"))
    goto done;
  if (!resolve_symbol((void**)&g_exports.pdk_emit_primitives,
                      "cascode_pdk_emit_primitives"))
    goto done;
  success = true;

done:
  if (!success) {
    unload_exports();
  }
  unlock_load_mutex();
  return success;
}

/**
 * Ensure the Cascode native library exports are loaded and available to call.
 *
 * If loading fails, throws a N-API error `CASCODE_NATIVE_LOAD_FAILED` with a
 * recorded diagnostic message.
 *
 * @returns `true` if the exports are loaded, `false` otherwise.
 */
static bool ensure_loaded(napi_env env) {
  if (load_exports()) {
    return true;
  }

  const char* message = g_load_error[0] == '\0'
                            ? "Failed to load libcascode exports."
                            : g_load_error;
  napi_throw_error(env, "CASCODE_NATIVE_LOAD_FAILED", message);
  return false;
}

/**
 * Read a JavaScript string and return a newly allocated UTF-8 NUL-terminated C
 * string.
 *
 * The function converts the provided napi_value string into a heap-allocated
 * UTF-8 C string and stores the pointer in *out_text. On success the caller
 * owns the returned buffer and must free() it. On failure the function throws
 * a N-API error and returns false.
 *
 * @param value JavaScript string value to read.
 * @param out_text Receives a pointer to the newly allocated NUL-terminated
 * UTF-8 string. Caller must free() this buffer when no longer needed.
 * @returns `true` if the string was read and allocated successfully, `false`
 * otherwise.
 */
static bool read_utf8_arg(napi_env env, napi_value value, char** out_text) {
  size_t length = 0;
  napi_status status = napi_get_value_string_utf8(env, value, NULL, 0, &length);
  if (status != napi_ok) {
    napi_throw_type_error(env, "CASCODE_INVALID_ARGUMENT",
                          "Expected a string.");
    return false;
  }

  char* text = (char*)malloc(length + 1);
  if (text == NULL) {
    napi_throw_error(env, "CASCODE_NATIVE_OOM",
                     "Failed to allocate UTF-8 buffer.");
    return false;
  }

  size_t copied = 0;
  status = napi_get_value_string_utf8(env, value, text, length + 1, &copied);
  if (status != napi_ok) {
    napi_throw_type_error(env, "CASCODE_INVALID_ARGUMENT",
                          "Expected a string.");
    free(text);
    return false;
  }

  text[copied] = '\0';
  *out_text = text;
  return true;
}

/**
 * Convert a JavaScript value to a 32-bit integer and store it in the provided
 * output pointer.
 * @param env The N-API environment.
 * @param value The JavaScript value to convert.
 * @param out_value Pointer to an int32_t that receives the converted value on
 * success.
 * @returns `true` if the value was successfully converted to a 32-bit integer,
 * `false` otherwise.
 */
static bool read_int32_arg(napi_env env, napi_value value, int32_t* out_value) {
  napi_status status = napi_get_value_int32(env, value, out_value);
  return status == napi_ok;
}

/**
 * Create a JavaScript string from a null-terminated UTF-8 C string.
 * @param text Null-terminated UTF-8 text to convert to a JS string.
 * @returns A `napi_value` representing the newly created JavaScript string.
 */
static napi_value make_string(napi_env env, const char* text) {
  napi_value result;
  napi_create_string_utf8(env, text, NAPI_AUTO_LENGTH, &result);
  return result;
}

/**
 * Create a new Cascode session and return its session identifier to JavaScript.
 *
 * Reads an optional JSON options string from the first argument (defaults to
 * "{}"), calls the native cascode create_session function, and returns the
 * resulting session id.
 *
 * @param env N-API environment.
 * @param info Callback info containing arguments and this-value.
 * @returns A JavaScript Number containing the non-zero session id.
 * @throws Throws a N-API error `CASCODE_NATIVE_LOAD_FAILED` if the native
 * library cannot be loaded.
 * @throws Throws a N-API error `CASCODE_CREATE_SESSION_FAILED` if the native
 * create_session returns 0.
 */
static napi_value js_create_session(napi_env env, napi_callback_info info) {
  if (!ensure_loaded(env)) {
    return NULL;
  }

  size_t argc = 1;
  napi_value args[1];
  napi_get_cb_info(env, info, &argc, args, NULL, NULL);

  const char* default_options = "{}";
  char* options_text = NULL;
  const char* options = default_options;
  if (argc >= 1) {
    napi_valuetype value_type;
    napi_typeof(env, args[0], &value_type);
    if (value_type == napi_string) {
      if (!read_utf8_arg(env, args[0], &options_text)) {
        return NULL;
      }

      options = options_text;
    }
  }

  int32_t session = g_exports.create_session(options);
  if (options_text != NULL) {
    free(options_text);
  }

  if (session == 0) {
    napi_throw_error(env, "CASCODE_CREATE_SESSION_FAILED",
                     "cascode_create_session returned 0.");
    return NULL;
  }

  napi_value result;
  napi_create_int32(env, session, &result);
  return result;
}

/**
 * Destroy a cascode session identified by its integer session handle.
 *
 * Validates a single numeric argument (session). On success calls the native
 * destroy_session implementation and returns JavaScript `undefined`.
 *
 * @param env N-API environment.
 * @param info N-API callback info containing the arguments.
 * @returns A napi_value representing JavaScript `undefined`.
 * @throws TypeError if the argument count is not 1 or if the session is not an
 * integer.
 */
static napi_value js_destroy_session(napi_env env, napi_callback_info info) {
  if (!ensure_loaded(env)) {
    return NULL;
  }

  size_t argc = 1;
  napi_value args[1];
  napi_get_cb_info(env, info, &argc, args, NULL, NULL);
  if (argc < 1) {
    napi_throw_type_error(env, "CASCODE_INVALID_ARGUMENT",
                          "destroySession(session) requires 1 argument.");
    return NULL;
  }

  int32_t session = 0;
  if (!read_int32_arg(env, args[0], &session)) {
    napi_throw_type_error(env, "CASCODE_INVALID_ARGUMENT",
                          "session must be an integer.");
    return NULL;
  }

  g_exports.destroy_session(session);
  napi_value result;
  napi_get_undefined(env, &result);
  return result;
}

/**
 * Retrieve the last error JSON for a session.
 *
 * @param session Session identifier returned by create_session.
 * @returns Pointer to a NUL-terminated JSON string describing the last error
 * for the session, or NULL if no error information is available.
 */
static char* call_last_error_json(int32_t session) {
  if (g_exports.last_error_json == NULL) {
    return NULL;
  }

  return g_exports.last_error_json(session);
}

/**
 * Throw a JavaScript CASCODE_CALL_FAILED exception using the session's last
 * error JSON if available; otherwise throw a generic CASCODE_CALL_FAILED error.
 * @param env N-API environment.
 * @param session Cascode session identifier used to query the last error JSON.
 * @returns NULL; the function always throws a JavaScript exception and does not
 * return a normal value.
 */
static napi_value throw_call_error(napi_env env, int32_t session) {
  char* error_json = call_last_error_json(session);
  if (error_json != NULL) {
    napi_throw_error(env, "CASCODE_CALL_FAILED", error_json);
    g_exports.free_string(error_json);
    return NULL;
  }

  napi_throw_error(env, "CASCODE_CALL_FAILED", "Cascode call returned null.");
  return NULL;
}

/**
 * Map a cascode method name to its corresponding session-call function.
 *
 * Looks up the exact, case-sensitive method name and returns the associated
 * function pointer that implements that session call.
 *
 * @param method_name Null-terminated UTF-8 method name to resolve.
 * @returns The `cascode_session_call_fn` for the given method name, or `NULL`
 *          if the method name is not recognized.
 */
static cascode_session_call_fn resolve_method_fn(const char* method_name) {
  const method_entry_t table[] = {
      {"document.open", g_exports.document_open},
      {"document.updateText", g_exports.document_update_text},
      {"document.close", g_exports.document_close},
      {"source.rewriteSchematic", g_exports.source_rewrite_schematic},
      {"convert.toStructural", g_exports.convert_to_structural},
      {"convert.toCas", g_exports.convert_to_cas},
      {"render.schematic", g_exports.render_schematic},
      {"schematic.applyOperations", g_exports.schematic_apply_ops},
      {"schematic.applyPlacementEdits",
       g_exports.schematic_apply_placement_edits},
      {"schematic.captureManualSnapshot",
       g_exports.schematic_capture_manual_snapshot},
      {"schematic.previewRoute", g_exports.schematic_preview_route},
      {"schematic.applyRouteEdit", g_exports.schematic_apply_route_edit},
      {"erc.run", g_exports.erc_run},
      {"emit.run", g_exports.emit_run},
      {"verify.run", g_exports.verify_run},
      {"command.execute", g_exports.command_execute},
      {"job.start", g_exports.job_start},
      {"job.poll", g_exports.job_poll},
      {"job.cancel", g_exports.job_cancel},
      {"pdk.setDir", g_exports.pdk_set_dir},
      {"pdk.scan", g_exports.pdk_scan},
      {"pdk.emitPrimitives", g_exports.pdk_emit_primitives},
  };

  size_t count = sizeof(table) / sizeof(table[0]);
  for (size_t i = 0; i < count; i++) {
    if (strcmp(table[i].method, method_name) == 0) {
      return table[i].fn;
    }
  }

  return NULL;
}

/**
 * Invoke a named Cascode session method with a JSON request and return its JSON
 * response as a JavaScript string.
 *
 * Expects three JavaScript arguments: `session` (int32), `method` (string), and
 * `requestJson` (string). Throws a JS TypeError for missing/invalid arguments
 * or unknown method names. If the native method call returns NULL, throws an
 * error containing the session's last error JSON (when available) or a generic
 * message.
 *
 * @returns A `napi_value` containing the response JSON as a JavaScript string,
 * or `NULL` if an exception was thrown.
 */
static napi_value js_call(napi_env env, napi_callback_info info) {
  if (!ensure_loaded(env)) {
    return NULL;
  }

  size_t argc = 3;
  napi_value args[3];
  napi_get_cb_info(env, info, &argc, args, NULL, NULL);
  if (argc < 3) {
    napi_throw_type_error(
        env, "CASCODE_INVALID_ARGUMENT",
        "call(session, method, requestJson) requires 3 arguments.");
    return NULL;
  }

  int32_t session = 0;
  if (!read_int32_arg(env, args[0], &session)) {
    napi_throw_type_error(env, "CASCODE_INVALID_ARGUMENT",
                          "session must be an integer.");
    return NULL;
  }

  char* method_name = NULL;
  char* request_json = NULL;
  if (!read_utf8_arg(env, args[1], &method_name)) {
    return NULL;
  }

  if (!read_utf8_arg(env, args[2], &request_json)) {
    free(method_name);
    return NULL;
  }

  cascode_session_call_fn method_fn = resolve_method_fn(method_name);
  if (method_fn == NULL) {
    free(method_name);
    free(request_json);
    napi_throw_type_error(env, "CASCODE_INVALID_METHOD",
                          "Unknown method name.");
    return NULL;
  }

  char* response_json = method_fn(session, request_json);
  free(method_name);
  free(request_json);

  if (response_json == NULL) {
    return throw_call_error(env, session);
  }

  napi_value result = make_string(env, response_json);
  g_exports.free_string(response_json);
  return result;
}

/**
 * Return the last error JSON for a Cascode session as a JavaScript string or
 * `null` if none exists.
 *
 * Expects one integer argument: the session id. If the native library is not
 * loaded this function will propagate the load error.
 *
 * @returns napi_value string containing the last error JSON for the session, or
 * `null` if there is no error.
 * @throws JS TypeError with code "CASCODE_INVALID_ARGUMENT" if the session
 * argument is missing or not an integer.
 * @throws JS Error "CASCODE_NATIVE_LOAD_FAILED" if the native Cascode library
 * cannot be loaded.
 */
static napi_value js_last_error_json(napi_env env, napi_callback_info info) {
  if (!ensure_loaded(env)) {
    return NULL;
  }

  size_t argc = 1;
  napi_value args[1];
  napi_get_cb_info(env, info, &argc, args, NULL, NULL);
  if (argc < 1) {
    napi_throw_type_error(env, "CASCODE_INVALID_ARGUMENT",
                          "lastErrorJson(session) requires 1 argument.");
    return NULL;
  }

  int32_t session = 0;
  if (!read_int32_arg(env, args[0], &session)) {
    napi_throw_type_error(env, "CASCODE_INVALID_ARGUMENT",
                          "session must be an integer.");
    return NULL;
  }

  char* error_json = call_last_error_json(session);
  if (error_json == NULL) {
    napi_value null_value;
    napi_get_null(env, &null_value);
    return null_value;
  }

  napi_value result = make_string(env, error_json);
  g_exports.free_string(error_json);
  return result;
}

/**
 * Retrieve the cascode library API version string.
 *
 * @param env The N-API environment.
 * @returns A JavaScript string containing the API version, or NULL if an
 * exception was thrown.
 * @throws CASCODE_VERSION_FAILED if the cascode api_version call returns NULL.
 */
static napi_value js_api_version(napi_env env, napi_callback_info info) {
  (void)info;
  if (!ensure_loaded(env)) {
    return NULL;
  }

  char* value = g_exports.api_version();
  if (value == NULL) {
    napi_throw_error(env, "CASCODE_VERSION_FAILED",
                     "cascode_api_version returned null.");
    return NULL;
  }

  napi_value result = make_string(env, value);
  g_exports.free_string(value);
  return result;
}

/**
 * Return the Cascode schema version as a JavaScript string.
 *
 * Calls the loaded Cascode library's schema_version function and converts
 * the returned UTF-8 C string into a napi_value JavaScript string.
 *
 * @returns napi_value A JavaScript string containing the schema version, or
 * NULL if an error was thrown.
 * @throws CASCODE_NATIVE_LOAD_FAILED if the native library could not be loaded.
 * @throws CASCODE_VERSION_FAILED if the Cascode `schema_version` call returned
 * NULL.
 */
static napi_value js_schema_version(napi_env env, napi_callback_info info) {
  (void)info;
  if (!ensure_loaded(env)) {
    return NULL;
  }

  char* value = g_exports.schema_version();
  if (value == NULL) {
    napi_throw_error(env, "CASCODE_VERSION_FAILED",
                     "cascode_schema_version returned null.");
    return NULL;
  }

  napi_value result = make_string(env, value);
  g_exports.free_string(value);
  return result;
}

/**
 * Attach the native addon methods to the provided module exports object.
 *
 * Defines and binds the following functions on `exports`: createSession,
 * destroySession, call, lastErrorJson, apiVersion, and schemaVersion.
 *
 * @param env The N-API environment.
 * @param exports The target exports object to receive the addon properties.
 * @returns The same `exports` object with the native functions defined on it.
 */
static napi_value init(napi_env env, napi_value exports) {
  napi_property_descriptor descriptors[] = {
      {"createSession", NULL, js_create_session, NULL, NULL, NULL, napi_default,
       NULL},
      {"destroySession", NULL, js_destroy_session, NULL, NULL, NULL,
       napi_default, NULL},
      {"call", NULL, js_call, NULL, NULL, NULL, napi_default, NULL},
      {"lastErrorJson", NULL, js_last_error_json, NULL, NULL, NULL,
       napi_default, NULL},
      {"apiVersion", NULL, js_api_version, NULL, NULL, NULL, napi_default,
       NULL},
      {"schemaVersion", NULL, js_schema_version, NULL, NULL, NULL, napi_default,
       NULL},
  };

  napi_define_properties(
      env, exports, sizeof(descriptors) / sizeof(descriptors[0]), descriptors);
  return exports;
}

NAPI_MODULE(NODE_GYP_MODULE_NAME, init)
