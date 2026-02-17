#include <dlfcn.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef int32_t (*cascode_create_session_fn)(const char* options_json_utf8);
typedef void (*cascode_destroy_session_fn)(int32_t session);
typedef void (*cascode_free_string_fn)(char* ptr);
typedef char* (*cascode_last_error_json_fn)(int32_t session);
typedef char* (*cascode_session_call_fn)(int32_t session, const char* request_json);
typedef char* (*cascode_version_fn)(void);

typedef struct exports_s {
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
  cascode_session_call_fn job_start;
  cascode_session_call_fn job_poll;
  cascode_session_call_fn job_cancel;
  cascode_session_call_fn erc_run;
  cascode_session_call_fn emit_run;
  cascode_session_call_fn verify_run;
  cascode_session_call_fn command_execute;
  cascode_version_fn api_version;
  cascode_version_fn schema_version;
} exports_t;

static void failf(const char* message) {
  fprintf(stderr, "harness failure: %s\n", message);
  exit(1);
}

static void require(bool condition, const char* message) {
  if (!condition) {
    failf(message);
  }
}

static bool resolve_symbol(void* handle, void** out, const char* name) {
  *out = dlsym(handle, name);
  if (*out == NULL) {
    fprintf(stderr, "missing symbol: %s\n", name);
    return false;
  }

  return true;
}

static exports_t load_exports(const char* library_path) {
  exports_t e;
  memset(&e, 0, sizeof(e));

  e.handle = dlopen(library_path, RTLD_NOW | RTLD_LOCAL);
  if (e.handle == NULL) {
    fprintf(stderr, "dlopen failed: %s\n", dlerror());
    exit(1);
  }

  require(resolve_symbol(e.handle, (void**)&e.create_session, "cascode_create_session"), "create_session");
  require(resolve_symbol(e.handle, (void**)&e.destroy_session, "cascode_destroy_session"), "destroy_session");
  require(resolve_symbol(e.handle, (void**)&e.free_string, "cascode_free_string"), "free_string");
  require(resolve_symbol(e.handle, (void**)&e.last_error_json, "cascode_last_error_json"), "last_error_json");
  require(resolve_symbol(e.handle, (void**)&e.document_open, "cascode_document_open"), "document_open");
  require(resolve_symbol(e.handle, (void**)&e.document_update_text, "cascode_document_update_text"), "document_update_text");
  require(resolve_symbol(e.handle, (void**)&e.document_close, "cascode_document_close"), "document_close");
  require(resolve_symbol(e.handle, (void**)&e.convert_to_structural, "cascode_convert_to_structural"), "convert_to_structural");
  require(resolve_symbol(e.handle, (void**)&e.convert_to_cas, "cascode_convert_to_cas"), "convert_to_cas");
  require(resolve_symbol(e.handle, (void**)&e.render_schematic, "cascode_render_schematic"), "render_schematic");
  require(resolve_symbol(e.handle, (void**)&e.schematic_apply_ops, "cascode_schematic_apply_ops"), "schematic_apply_ops");
  require(resolve_symbol(e.handle, (void**)&e.job_start, "cascode_job_start"), "job_start");
  require(resolve_symbol(e.handle, (void**)&e.job_poll, "cascode_job_poll"), "job_poll");
  require(resolve_symbol(e.handle, (void**)&e.job_cancel, "cascode_job_cancel"), "job_cancel");
  require(resolve_symbol(e.handle, (void**)&e.erc_run, "cascode_erc_run"), "erc_run");
  require(resolve_symbol(e.handle, (void**)&e.emit_run, "cascode_emit_run"), "emit_run");
  require(resolve_symbol(e.handle, (void**)&e.verify_run, "cascode_verify_run"), "verify_run");
  require(resolve_symbol(e.handle, (void**)&e.command_execute, "cascode_command_execute"), "command_execute");
  require(resolve_symbol(e.handle, (void**)&e.api_version, "cascode_api_version"), "api_version");
  require(resolve_symbol(e.handle, (void**)&e.schema_version, "cascode_schema_version"), "schema_version");

  return e;
}

static int parse_int_field(const char* json, const char* field) {
  char pattern[96];
  snprintf(pattern, sizeof(pattern), "\"%s\":", field);
  const char* start = strstr(json, pattern);
  if (start == NULL) {
    return -1;
  }

  start += strlen(pattern);
  while (*start == ' ' || *start == '\n' || *start == '\t') {
    start++;
  }

  return atoi(start);
}

static bool extract_string_field(const char* json, const char* field, char* out, size_t out_size) {
  char pattern[96];
  snprintf(pattern, sizeof(pattern), "\"%s\":\"", field);
  const char* start = strstr(json, pattern);
  if (start == NULL) {
    return false;
  }

  start += strlen(pattern);
  const char* end = strchr(start, '"');
  if (end == NULL) {
    return false;
  }

  size_t length = (size_t)(end - start);
  if (length + 1 > out_size) {
    return false;
  }

  memcpy(out, start, length);
  out[length] = '\0';
  return true;
}

static char* json_escape(const char* input) {
  // Test-harness-only escaping: handles backslash, quotes, and \n/\r/\t.
  // It intentionally does not emit \uXXXX escapes for other control chars
  // or non-ASCII code points, so non-ASCII input may produce invalid JSON.
  size_t input_len = strlen(input);
  size_t max_len = (input_len * 2) + 1;
  char* escaped = (char*)malloc(max_len);
  if (escaped == NULL) {
    return NULL;
  }

  size_t j = 0;
  for (size_t i = 0; i < input_len; i++) {
    char c = input[i];
    switch (c) {
      case '\\':
        escaped[j++] = '\\';
        escaped[j++] = '\\';
        break;
      case '"':
        escaped[j++] = '\\';
        escaped[j++] = '"';
        break;
      case '\n':
        escaped[j++] = '\\';
        escaped[j++] = 'n';
        break;
      case '\r':
        escaped[j++] = '\\';
        escaped[j++] = 'r';
        break;
      case '\t':
        escaped[j++] = '\\';
        escaped[j++] = 't';
        break;
      default:
        escaped[j++] = c;
        break;
    }
  }

  escaped[j] = '\0';
  return escaped;
}

static void expect_contains(const char* text, const char* needle, const char* label) {
  if (strstr(text, needle) == NULL) {
    fprintf(stderr, "expected '%s' to contain '%s'\n", label, needle);
    exit(1);
  }
}

static char* must_call(
    exports_t* e,
    cascode_session_call_fn fn,
    int32_t session,
    const char* request_json,
    const char* label) {
  char* response = fn(session, request_json);
  if (response != NULL) {
    return response;
  }

  char* error_json = e->last_error_json(session);
  fprintf(stderr, "%s failed.\n", label);
  if (error_json != NULL) {
    fprintf(stderr, "last_error_json: %s\n", error_json);
    e->free_string(error_json);
  }

  exit(1);
}

int main(int argc, char** argv) {
  if (argc != 2) {
    fprintf(stderr, "usage: %s <path-to-libcascode-shared-library>\n", argv[0]);
    return 2;
  }

  const char* library_path = argv[1];
  exports_t exports = load_exports(library_path);

  char* api_version = exports.api_version();
  require(api_version != NULL, "api_version returned null");
  expect_contains(api_version, "cascode.api/", "api_version");
  exports.free_string(api_version);

  char* schema_version = exports.schema_version();
  require(schema_version != NULL, "schema_version returned null");
  expect_contains(schema_version, "cascode.schematic/", "schema_version");
  exports.free_string(schema_version);

  int32_t session = exports.create_session("{}");
  require(session > 0, "create_session returned invalid handle");

  const char* source_text =
      "VERSION 3.2\n"
      "\n"
      "primitive NMOS Level1_NMOS(size primSize) {\n"
      "  device \"level1_nmos\"\n"
      "  params {\n"
      "    W = primSize.W\n"
      "    L = primSize.L\n"
      "    m = primSize.M\n"
      "  }\n"
      "}\n"
      "\n"
      "circuit Amp {\n"
      "  level EL\n"
      "  input IN : analog\n"
      "  output OUT : analog\n"
      "  ground GND\n"
      "  fill {\n"
      "    net n1 : analog\n"
      "    size Unit = size(W=1u, L=180n, M=1)\n"
      "    NMOS M1 = new Level1_NMOS(Unit) {\n"
      "      .D--OUT\n"
      "      .G--IN\n"
      "      .S--n1\n"
      "      .B--GND\n"
      "    }\n"
      "    NMOS M2 = new Level1_NMOS(Unit) {\n"
      "      .D--OUT\n"
      "      .G--n1\n"
      "      .S--GND\n"
      "      .B--GND\n"
      "    }\n"
      "  }\n"
      "}\n";

  char open_request[8192];
  char* escaped_source = json_escape(source_text);
  require(escaped_source != NULL, "failed to escape source JSON");
  snprintf(
      open_request,
      sizeof(open_request),
      "{\"documentId\":\"doc1\",\"text\":\"%s\"}",
      escaped_source);
  free(escaped_source);
  char* open_response = must_call(&exports, exports.document_open, session, open_request, "document_open");
  require(parse_int_field(open_response, "revision") == 1, "open revision must be 1");
  exports.free_string(open_response);

  const char* apply_request =
      "{\"documentId\":\"doc1\",\"baseRevision\":1,\"operations\":[{\"opId\":\"op-1\",\"type\":\"movePort\",\"port\":\"IN\",\"x\":1000,\"y\":1000}]}";
  char* apply_response = must_call(&exports, exports.schematic_apply_ops, session, apply_request, "schematic_apply_ops");
  require(parse_int_field(apply_response, "revision") == 2, "apply revision must be 2");
  expect_contains(apply_response, "\"sourceText\":", "apply_response");
  exports.free_string(apply_response);

  const char* cas_request = "{\"documentId\":\"doc1\"}";
  char* cas_response = must_call(&exports, exports.convert_to_cas, session, cas_request, "convert_to_cas");
  expect_contains(cas_response, "render {", "convert_to_cas response");
  exports.free_string(cas_response);

  const char* reflow_request = "{\"documentId\":\"doc1\",\"mode\":\"reflowUnlocked\"}";
  char* reflow_response = must_call(&exports, exports.render_schematic, session, reflow_request, "render_schematic reflowUnlocked");
  expect_contains(reflow_response, "\"schema\":\"cascode.render/1.0\"", "reflow response");
  exports.free_string(reflow_response);

  const char* scratch_request = "{\"documentId\":\"doc1\",\"mode\":\"rerenderFromScratch\"}";
  char* scratch_response = must_call(&exports, exports.render_schematic, session, scratch_request, "render_schematic rerenderFromScratch");
  expect_contains(scratch_response, "\"schema\":\"cascode.render/1.0\"", "scratch response");
  exports.free_string(scratch_response);

  const char* erc_request = "{\"documentId\":\"doc1\"}";
  char* erc_response = must_call(&exports, exports.erc_run, session, erc_request, "erc_run");
  exports.free_string(erc_response);

  char* emit_response = must_call(&exports, exports.emit_run, session, erc_request, "emit_run");
  exports.free_string(emit_response);

  char* verify_response = must_call(&exports, exports.verify_run, session, erc_request, "verify_run");
  exports.free_string(verify_response);

  const char* command_request = "{\"documentId\":\"doc1\",\"command\":\"noop\"}";
  char* command_response = must_call(&exports, exports.command_execute, session, command_request, "command_execute");
  exports.free_string(command_response);

  const char* job_start_request = "{\"documentId\":\"doc1\"}";
  char* job_start_response = must_call(&exports, exports.job_start, session, job_start_request, "job_start");
  char job_id[128];
  require(extract_string_field(job_start_response, "jobId", job_id, sizeof(job_id)), "job_start must return jobId");
  exports.free_string(job_start_response);

  char job_poll_request[256];
  snprintf(job_poll_request, sizeof(job_poll_request), "{\"jobId\":\"%s\"}", job_id);
  char* job_poll_response = must_call(&exports, exports.job_poll, session, job_poll_request, "job_poll");
  expect_contains(job_poll_response, "\"progress\":", "job_poll");
  exports.free_string(job_poll_response);

  char* job_cancel_response = must_call(&exports, exports.job_cancel, session, job_poll_request, "job_cancel");
  exports.free_string(job_cancel_response);

  char* invalid = exports.document_close(session, "{}");
  require(invalid == NULL, "invalid document_close should return null");
  char* last_error = exports.last_error_json(session);
  require(last_error != NULL, "last_error_json must return payload for invalid call");
  expect_contains(last_error, "CASAPI-INVALID-REQUEST", "last_error_json");
  exports.free_string(last_error);

  const char* close_request = "{\"documentId\":\"doc1\"}";
  char* close_response = must_call(&exports, exports.document_close, session, close_request, "document_close");
  exports.free_string(close_response);

  exports.destroy_session(session);
  dlclose(exports.handle);
  puts("cascode_cabi_harness: ok");
  return 0;
}
