#include <node_api.h>

#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <pthread.h>
#include <unistd.h>

#if defined(__linux__) || defined(__APPLE__)
#include <dlfcn.h>
#else
#error "cascode_native_addon currently supports linux and macOS only."
#endif

typedef int32_t (*cascode_create_session_fn)(const char* options_json_utf8);
typedef void (*cascode_destroy_session_fn)(int32_t session);
typedef void (*cascode_free_string_fn)(char* ptr);
typedef char* (*cascode_last_error_json_fn)(int32_t session);
typedef char* (*cascode_session_call_fn)(int32_t session, const char* request_json);
typedef char* (*cascode_version_fn)(void);

typedef struct cascode_exports_s {
  void* handle;
  cascode_create_session_fn create_session;
  cascode_destroy_session_fn destroy_session;
  cascode_free_string_fn free_string;
  cascode_last_error_json_fn last_error_json;
  cascode_session_call_fn document_open;
  cascode_session_call_fn document_update_text;
  cascode_session_call_fn document_close;
  cascode_session_call_fn convert_to_structural;
  cascode_session_call_fn convert_to_cas;
  cascode_session_call_fn render_schematic;
  cascode_session_call_fn schematic_apply_ops;
  cascode_session_call_fn erc_run;
  cascode_session_call_fn emit_run;
  cascode_session_call_fn verify_run;
  cascode_session_call_fn command_execute;
  cascode_session_call_fn job_start;
  cascode_session_call_fn job_poll;
  cascode_session_call_fn job_cancel;
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
static pthread_mutex_t g_load_mutex = PTHREAD_MUTEX_INITIALIZER;

static void set_load_error(const char* message) {
  if (message == NULL) {
    g_load_error[0] = '\0';
    return;
  }

  snprintf(g_load_error, sizeof(g_load_error), "%s", message);
}

static void extract_directory(const char* path, char* out, size_t out_size) {
  const char* slash = strrchr(path, '/');
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

static bool preload_dependency(const char* directory, const char* file_name) {
  char full_path[1024];
  snprintf(full_path, sizeof(full_path), "%s/%s", directory, file_name);
  if (access(full_path, R_OK) != 0) {
    return true;
  }

  void* handle = dlopen(full_path, RTLD_NOW | RTLD_GLOBAL);
  if (handle == NULL) {
    const char* dl_error = dlerror();
    if (dl_error == NULL) {
      dl_error = "unknown dependency load error";
    }

    char buffer[512];
    snprintf(buffer, sizeof(buffer), "Failed to preload '%s': %s", full_path, dl_error);
    set_load_error(buffer);
    return false;
  }

  return true;
}

static bool resolve_symbol(void** target, const char* name) {
  *target = dlsym(g_exports.handle, name);
  if (*target == NULL) {
    char buffer[512];
    snprintf(buffer, sizeof(buffer), "Failed to resolve symbol '%s'.", name);
    set_load_error(buffer);
    return false;
  }

  return true;
}

static bool load_exports(void) {
  bool success = false;
  pthread_mutex_lock(&g_load_mutex);

  if (g_attempted_load) {
    success = g_exports.handle != NULL;
    pthread_mutex_unlock(&g_load_mutex);
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
  if (!preload_dependency(library_dir, "libortools.so.9")) {
    goto done;
  }

  if (!preload_dependency(library_dir, "google-ortools-native.so")) {
    goto done;
  }

  g_exports.handle = dlopen(library_path, RTLD_NOW | RTLD_LOCAL);
  if (g_exports.handle == NULL) {
    const char* dl_error = dlerror();
    if (dl_error == NULL) {
      dl_error = "Unknown dlopen error.";
    }

    char buffer[512];
    snprintf(buffer, sizeof(buffer), "Failed to load '%s': %s", library_path, dl_error);
    set_load_error(buffer);
    goto done;
  }

  if (!resolve_symbol((void**)&g_exports.create_session, "cascode_create_session")) goto done;
  if (!resolve_symbol((void**)&g_exports.destroy_session, "cascode_destroy_session")) goto done;
  if (!resolve_symbol((void**)&g_exports.free_string, "cascode_free_string")) goto done;
  if (!resolve_symbol((void**)&g_exports.last_error_json, "cascode_last_error_json")) goto done;
  if (!resolve_symbol((void**)&g_exports.api_version, "cascode_api_version")) goto done;
  if (!resolve_symbol((void**)&g_exports.schema_version, "cascode_schema_version")) goto done;
  if (!resolve_symbol((void**)&g_exports.document_open, "cascode_document_open")) goto done;
  if (!resolve_symbol((void**)&g_exports.document_update_text, "cascode_document_update_text")) goto done;
  if (!resolve_symbol((void**)&g_exports.document_close, "cascode_document_close")) goto done;
  if (!resolve_symbol((void**)&g_exports.convert_to_structural, "cascode_convert_to_structural")) goto done;
  if (!resolve_symbol((void**)&g_exports.convert_to_cas, "cascode_convert_to_cas")) goto done;
  if (!resolve_symbol((void**)&g_exports.render_schematic, "cascode_render_schematic")) goto done;
  if (!resolve_symbol((void**)&g_exports.schematic_apply_ops, "cascode_schematic_apply_ops")) goto done;
  if (!resolve_symbol((void**)&g_exports.erc_run, "cascode_erc_run")) goto done;
  if (!resolve_symbol((void**)&g_exports.emit_run, "cascode_emit_run")) goto done;
  if (!resolve_symbol((void**)&g_exports.verify_run, "cascode_verify_run")) goto done;
  if (!resolve_symbol((void**)&g_exports.command_execute, "cascode_command_execute")) goto done;
  if (!resolve_symbol((void**)&g_exports.job_start, "cascode_job_start")) goto done;
  if (!resolve_symbol((void**)&g_exports.job_poll, "cascode_job_poll")) goto done;
  if (!resolve_symbol((void**)&g_exports.job_cancel, "cascode_job_cancel")) goto done;
  success = true;

done:
  pthread_mutex_unlock(&g_load_mutex);
  return success;
}

static bool ensure_loaded(napi_env env) {
  if (load_exports()) {
    return true;
  }

  const char* message = g_load_error[0] == '\0' ? "Failed to load libcascode exports." : g_load_error;
  napi_throw_error(env, "CASCODE_NATIVE_LOAD_FAILED", message);
  return false;
}

static bool read_utf8_arg(napi_env env, napi_value value, char** out_text) {
  size_t length = 0;
  napi_status status = napi_get_value_string_utf8(env, value, NULL, 0, &length);
  if (status != napi_ok) {
    return false;
  }

  char* text = (char*)malloc(length + 1);
  if (text == NULL) {
    napi_throw_error(env, "CASCODE_NATIVE_OOM", "Failed to allocate UTF-8 buffer.");
    return false;
  }

  size_t copied = 0;
  status = napi_get_value_string_utf8(env, value, text, length + 1, &copied);
  if (status != napi_ok) {
    free(text);
    return false;
  }

  text[copied] = '\0';
  *out_text = text;
  return true;
}

static bool read_int32_arg(napi_env env, napi_value value, int32_t* out_value) {
  napi_status status = napi_get_value_int32(env, value, out_value);
  return status == napi_ok;
}

static napi_value make_string(napi_env env, const char* text) {
  napi_value result;
  napi_create_string_utf8(env, text, NAPI_AUTO_LENGTH, &result);
  return result;
}

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
    napi_throw_error(env, "CASCODE_CREATE_SESSION_FAILED", "cascode_create_session returned 0.");
    return NULL;
  }

  napi_value result;
  napi_create_int32(env, session, &result);
  return result;
}

static napi_value js_destroy_session(napi_env env, napi_callback_info info) {
  if (!ensure_loaded(env)) {
    return NULL;
  }

  size_t argc = 1;
  napi_value args[1];
  napi_get_cb_info(env, info, &argc, args, NULL, NULL);
  if (argc < 1) {
    napi_throw_type_error(env, "CASCODE_INVALID_ARGUMENT", "destroySession(session) requires 1 argument.");
    return NULL;
  }

  int32_t session = 0;
  if (!read_int32_arg(env, args[0], &session)) {
    napi_throw_type_error(env, "CASCODE_INVALID_ARGUMENT", "session must be an integer.");
    return NULL;
  }

  g_exports.destroy_session(session);
  return NULL;
}

static char* call_last_error_json(int32_t session) {
  if (g_exports.last_error_json == NULL) {
    return NULL;
  }

  return g_exports.last_error_json(session);
}

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

static cascode_session_call_fn resolve_method_fn(const char* method_name) {
  const method_entry_t table[] = {
      {"document.open", g_exports.document_open},
      {"document.updateText", g_exports.document_update_text},
      {"document.close", g_exports.document_close},
      {"convert.toStructural", g_exports.convert_to_structural},
      {"convert.toCas", g_exports.convert_to_cas},
      {"render.schematic", g_exports.render_schematic},
      {"schematic.applyOperations", g_exports.schematic_apply_ops},
      {"erc.run", g_exports.erc_run},
      {"emit.run", g_exports.emit_run},
      {"verify.run", g_exports.verify_run},
      {"command.execute", g_exports.command_execute},
      {"job.start", g_exports.job_start},
      {"job.poll", g_exports.job_poll},
      {"job.cancel", g_exports.job_cancel},
  };

  size_t count = sizeof(table) / sizeof(table[0]);
  for (size_t i = 0; i < count; i++) {
    if (strcmp(table[i].method, method_name) == 0) {
      return table[i].fn;
    }
  }

  return NULL;
}

static napi_value js_call(napi_env env, napi_callback_info info) {
  if (!ensure_loaded(env)) {
    return NULL;
  }

  size_t argc = 3;
  napi_value args[3];
  napi_get_cb_info(env, info, &argc, args, NULL, NULL);
  if (argc < 3) {
    napi_throw_type_error(env, "CASCODE_INVALID_ARGUMENT", "call(session, method, requestJson) requires 3 arguments.");
    return NULL;
  }

  int32_t session = 0;
  if (!read_int32_arg(env, args[0], &session)) {
    napi_throw_type_error(env, "CASCODE_INVALID_ARGUMENT", "session must be an integer.");
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
    napi_throw_type_error(env, "CASCODE_INVALID_METHOD", "Unknown method name.");
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

static napi_value js_last_error_json(napi_env env, napi_callback_info info) {
  if (!ensure_loaded(env)) {
    return NULL;
  }

  size_t argc = 1;
  napi_value args[1];
  napi_get_cb_info(env, info, &argc, args, NULL, NULL);
  if (argc < 1) {
    napi_throw_type_error(env, "CASCODE_INVALID_ARGUMENT", "lastErrorJson(session) requires 1 argument.");
    return NULL;
  }

  int32_t session = 0;
  if (!read_int32_arg(env, args[0], &session)) {
    napi_throw_type_error(env, "CASCODE_INVALID_ARGUMENT", "session must be an integer.");
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

static napi_value js_api_version(napi_env env, napi_callback_info info) {
  (void)info;
  if (!ensure_loaded(env)) {
    return NULL;
  }

  char* value = g_exports.api_version();
  if (value == NULL) {
    napi_throw_error(env, "CASCODE_VERSION_FAILED", "cascode_api_version returned null.");
    return NULL;
  }

  napi_value result = make_string(env, value);
  g_exports.free_string(value);
  return result;
}

static napi_value js_schema_version(napi_env env, napi_callback_info info) {
  (void)info;
  if (!ensure_loaded(env)) {
    return NULL;
  }

  char* value = g_exports.schema_version();
  if (value == NULL) {
    napi_throw_error(env, "CASCODE_VERSION_FAILED", "cascode_schema_version returned null.");
    return NULL;
  }

  napi_value result = make_string(env, value);
  g_exports.free_string(value);
  return result;
}

static napi_value init(napi_env env, napi_value exports) {
  napi_property_descriptor descriptors[] = {
      {"createSession", NULL, js_create_session, NULL, NULL, NULL, napi_default, NULL},
      {"destroySession", NULL, js_destroy_session, NULL, NULL, NULL, napi_default, NULL},
      {"call", NULL, js_call, NULL, NULL, NULL, napi_default, NULL},
      {"lastErrorJson", NULL, js_last_error_json, NULL, NULL, NULL, napi_default, NULL},
      {"apiVersion", NULL, js_api_version, NULL, NULL, NULL, napi_default, NULL},
      {"schemaVersion", NULL, js_schema_version, NULL, NULL, NULL, napi_default, NULL},
  };

  napi_define_properties(env, exports, sizeof(descriptors) / sizeof(descriptors[0]), descriptors);
  return exports;
}

NAPI_MODULE(NODE_GYP_MODULE_NAME, init)
