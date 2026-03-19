using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace Cascode.Native;

public static unsafe class NativeExports
{
    /// <summary>
    /// Creates a new session using the provided JSON options and returns its identifier.
    /// </summary>
    /// <param name="optionsJsonUtf8">Pointer to a UTF-8 encoded JSON string with session options, or null to use defaults.</param>
    /// <returns>The new session identifier if creation succeeds; 0 if session creation fails.</returns>
    [UnmanagedCallersOnly(EntryPoint = "cascode_create_session")]
    public static int CreateSession(byte* optionsJsonUtf8)
    {
        try
        {
            var options = Utf8ToString(optionsJsonUtf8);
            return SessionManager.CreateSession(options);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Destroy the specified session and release its associated resources.
    /// </summary>
    /// <param name="session">The session identifier returned by CreateSession.</param>
    [UnmanagedCallersOnly(EntryPoint = "cascode_destroy_session")]
    public static void DestroySession(int session)
    {
        SessionManager.DestroySession(session);
    }

    /// <summary>
    /// Frees an unmanaged string pointer previously returned by this API; doing nothing if the pointer is zero.
    /// </summary>
    /// <param name="ptr">A pointer to unmanaged memory for a UTF-8 string previously returned by this library; may be <see cref="IntPtr.Zero"/>.</param>
    [UnmanagedCallersOnly(EntryPoint = "cascode_free_string")]
    public static void FreeString(IntPtr ptr)
    {
        if (ptr != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(ptr);
        }
    }

    /// <summary>
    /// Gets the last error JSON recorded for the specified session.
    /// </summary>
    /// <param name="session">The session identifier returned by CreateSession.</param>
    /// <returns>A pointer to a UTF-8 encoded JSON string containing the session's last error, or IntPtr.Zero if no error is recorded.</returns>
    [UnmanagedCallersOnly(EntryPoint = "cascode_last_error_json")]
    public static IntPtr LastErrorJson(int session)
    {
        var error = SessionManager.GetLastError(session);
        return StringToUtf8(error);
    }

    /// <summary>
    /// Get the native API version identifier.
    /// </summary>
    /// <returns>A pointer to a UTF-8 encoded string containing "cascode.api/1.0".</returns>
    [UnmanagedCallersOnly(EntryPoint = "cascode_api_version")]
    public static IntPtr ApiVersion()
    {
        return StringToUtf8("cascode.api/1.0");
    }

    /// <summary>
    /// Gets the schema version identifier exposed by the native API.
    /// </summary>
    /// <returns>An IntPtr pointing to a NUL-terminated UTF-8 string with the schema version (for example, "cascode.schematic/1.0").</returns>
    [UnmanagedCallersOnly(EntryPoint = "cascode_schema_version")]
    public static IntPtr SchemaVersion()
    {
        return StringToUtf8("cascode.schematic/1.0");
    }

    /// <summary>
    /// Opens a document in the given session using the provided request JSON.
    /// </summary>
    /// <param name="session">The session identifier returned by CreateSession.</param>
    /// <param name="requestJson">Pointer to a UTF-8 JSON request; if null, an empty object ("{}") is used.</param>
    /// <returns>An allocated pointer to the UTF-8 response JSON, or <see cref="IntPtr.Zero"/> on error.</returns>
    [UnmanagedCallersOnly(EntryPoint = "cascode_document_open")]
    public static IntPtr DocumentOpen(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "document.open");
    }

    /// <summary>
    /// Dispatches a "document.updateText" request for the specified session.
    /// </summary>
    /// <param name="session">The session identifier returned by CreateSession.</param>
    /// <param name="requestJson">Pointer to a UTF-8 encoded JSON request; if null the request defaults to "{}".</param>
    /// <returns>A pointer to a UTF-8 encoded JSON response, or <see cref="IntPtr.Zero"/> if an error occurred.</returns>
    [UnmanagedCallersOnly(EntryPoint = "cascode_document_update_text")]
    public static IntPtr DocumentUpdateText(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "document.updateText");
    }

    /// <summary>
    /// Handles a document close request for the specified session.
    /// </summary>
    /// <param name="session">The session identifier.</param>
    /// <param name="requestJson">Pointer to a UTF-8 JSON request describing the close operation (may be null).</param>
    /// <returns>A pointer to a UTF-8 JSON response, or <see cref="IntPtr.Zero"/> if an error occurred.</returns>
    [UnmanagedCallersOnly(EntryPoint = "cascode_document_close")]
    public static IntPtr DocumentClose(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "document.close");
    }

    /// <summary>
    /// Dispatches a "source.rewriteSchematic" request for the specified session.
    /// </summary>
    /// <param name="session">The session identifier returned by CreateSession.</param>
    /// <param name="requestJson">Pointer to a UTF-8 encoded JSON request; may be null to indicate an empty request.</param>
    /// <returns>A pointer to a UTF-8 encoded JSON response, or <see cref="IntPtr.Zero"/> if an error occurred.</returns>
    [UnmanagedCallersOnly(EntryPoint = "cascode_source_rewrite_schematic")]
    public static IntPtr SourceRewriteSchematic(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "source.rewriteSchematic");
    }

    /// <summary>
    /// Invokes the "convert.toStructural" API for the specified session using the given JSON request.
    /// </summary>
    /// <param name="session">Session identifier returned by CreateSession.</param>
    /// <param name="requestJson">Pointer to a UTF-8 encoded JSON request; may be null to indicate an empty object ({}).</param>
    /// <returns>A pointer to a UTF-8 encoded JSON response, or <see cref="IntPtr.Zero"/> if an error occurred (the session's last error will contain error details).</returns>
    [UnmanagedCallersOnly(EntryPoint = "cascode_convert_to_structural")]
    public static IntPtr ConvertToStructural(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "convert.toStructural");
    }

    /// <summary>
    /// Invokes the "convert.toCas" API for the specified session using the provided JSON request.
    /// </summary>
    /// <param name="session">The session identifier.</param>
    /// <param name="requestJson">A pointer to a UTF-8 encoded JSON request; may be null, which is treated as "{}".</param>
    /// <returns>A pointer to a UTF-8 encoded JSON response on success, or <see cref="IntPtr.Zero"/> on error (the session's last error will be recorded).</returns>
    [UnmanagedCallersOnly(EntryPoint = "cascode_convert_to_cas")]
    public static IntPtr ConvertToCas(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "convert.toCas");
    }

    /// <summary>
    /// Handles a "render.schematic" request for the specified session and returns the API response as a UTF-8 pointer.
    /// </summary>
    /// <param name="session">The session identifier to route the request to.</param>
    /// <param name="requestJson">Pointer to a UTF-8 encoded JSON request payload; if null, an empty object ("{}") is used.</param>
    /// <returns>A pointer to a UTF-8 encoded JSON response, or <see cref="IntPtr.Zero"/> if an error occurred (the session's last error is recorded).</returns>
    [UnmanagedCallersOnly(EntryPoint = "cascode_render_schematic")]
    public static IntPtr RenderSchematic(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "render.schematic");
    }

    /// <summary>
    /// Dispatches a "schematic.applyOperations" request for the specified session.
    /// </summary>
    /// <param name="session">The session identifier returned by CreateSession.</param>
    /// <param name="requestJson">Pointer to a UTF-8 encoded JSON request; may be null to indicate an empty request.</param>
    /// <returns>A pointer to a UTF-8 encoded JSON response, or <c>IntPtr.Zero</c> if an error occurred. In case of error the session's last error is updated.</returns>
    [UnmanagedCallersOnly(EntryPoint = "cascode_schematic_apply_ops")]
    public static IntPtr SchematicApplyOps(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "schematic.applyOperations");
    }

    /// <summary>
    /// Dispatches a "schematic.applyPlacementEdits" request for the specified session.
    /// </summary>
    /// <param name="session">The session identifier returned by CreateSession.</param>
    /// <param name="requestJson">Pointer to a UTF-8 encoded JSON request; may be null to indicate an empty request.</param>
    /// <returns>A pointer to a UTF-8 encoded JSON response, or <c>IntPtr.Zero</c> if an error occurred. In case of error the session's last error is updated.</returns>
    [UnmanagedCallersOnly(EntryPoint = "cascode_schematic_apply_placement_edits")]
    public static IntPtr SchematicApplyPlacementEdits(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "schematic.applyPlacementEdits");
    }

    /// <summary>
    /// Dispatches a "schematic.captureManualSnapshot" request for the specified session.
    /// </summary>
    /// <param name="session">The session identifier returned by CreateSession.</param>
    /// <param name="requestJson">Pointer to a UTF-8 encoded JSON request; may be null to indicate an empty request.</param>
    /// <returns>A pointer to a UTF-8 encoded JSON response, or <see cref="IntPtr.Zero"/> if an error occurred.</returns>
    [UnmanagedCallersOnly(EntryPoint = "cascode_schematic_capture_manual_snapshot")]
    public static IntPtr SchematicCaptureManualSnapshot(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "schematic.captureManualSnapshot");
    }

    /// <summary>
    /// Dispatches a "schematic.previewRoute" request for the specified session.
    /// </summary>
    /// <param name="session">The session identifier returned by CreateSession.</param>
    /// <param name="requestJson">Pointer to a UTF-8 encoded JSON request; may be null to indicate an empty request.</param>
    /// <returns>A pointer to a UTF-8 encoded JSON response, or <c>IntPtr.Zero</c> if an error occurred. In case of error the session's last error is updated.</returns>
    [UnmanagedCallersOnly(EntryPoint = "cascode_schematic_preview_route")]
    public static IntPtr SchematicPreviewRoute(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "schematic.previewRoute");
    }

    /// <summary>
    /// Dispatches a "schematic.applyRouteEdit" request for the specified session.
    /// </summary>
    /// <param name="session">The session identifier returned by CreateSession.</param>
    /// <param name="requestJson">Pointer to a UTF-8 encoded JSON request; may be null to indicate an empty request.</param>
    /// <returns>A pointer to a UTF-8 encoded JSON response, or <c>IntPtr.Zero</c> if an error occurred. In case of error the session's last error is updated.</returns>
    [UnmanagedCallersOnly(EntryPoint = "cascode_schematic_apply_route_edit")]
    public static IntPtr SchematicApplyRouteEdit(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "schematic.applyRouteEdit");
    }

    /// <summary>
    /// Invokes the "erc.run" API method for the specified session.
    /// </summary>
    /// <param name="session">The session identifier returned by CreateSession.</param>
    /// <param name="requestJson">A pointer to a UTF-8 JSON request; may be null to use an empty request ("{}").</param>
    /// <returns>A pointer to a UTF-8 JSON response. Returns <see cref="IntPtr.Zero"/> on error and records the error in the session's last-error store.</returns>
    [UnmanagedCallersOnly(EntryPoint = "cascode_erc_run")]
    public static IntPtr ErcRun(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "erc.run");
    }

    /// <summary>
    /// Process an "emit.run" request for the specified session using the provided JSON request.
    /// </summary>
    /// <param name="session">The session identifier to target.</param>
    /// <param name="requestJson">Pointer to a UTF-8 encoded JSON request; may be null.</param>
    /// <returns>An IntPtr to a UTF-8 encoded response string, or <see cref="IntPtr.Zero"/> if an error occurred.</returns>
    [UnmanagedCallersOnly(EntryPoint = "cascode_emit_run")]
    public static IntPtr EmitRun(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "emit.run");
    }

    /// <summary>
    /// Handles the "verify.run" API method for the given session using the provided request JSON and returns the response as a UTF-8 pointer.
    /// </summary>
    /// <param name="session">The session identifier.</param>
    /// <param name="requestJson">A pointer to a UTF-8 encoded JSON request; may be null to use an empty/default request.</param>
    /// <returns>An IntPtr pointing to a UTF-8 encoded JSON response, or IntPtr.Zero on error (the session's last error will be set).</returns>
    [UnmanagedCallersOnly(EntryPoint = "cascode_verify_run")]
    public static IntPtr VerifyRun(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "verify.run");
    }

    /// <summary>
    /// Execute the "command.execute" API method for the specified session using the provided JSON request.
    /// </summary>
    /// <param name="session">Session identifier returned by CreateSession.</param>
    /// <param name="requestJson">Pointer to a UTF-8 encoded JSON request; may be null to use an empty object ("{}").</param>
    /// <returns>Pointer to a UTF-8 encoded JSON response, or <c>IntPtr.Zero</c> on error.</returns>
    [UnmanagedCallersOnly(EntryPoint = "cascode_command_execute")]
    public static IntPtr CommandExecute(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "command.execute");
    }

    /// <summary>
    /// Handles the "job.start" API call for a session using a UTF-8 JSON request.
    /// </summary>
    /// <param name="session">The session identifier.</param>
    /// <param name="requestJson">Pointer to a UTF-8 encoded JSON request, or null to indicate an empty request ("{}").</param>
    /// <returns>A pointer to a UTF-8 encoded JSON response, or <see cref="IntPtr.Zero"/> on error. The caller is responsible for freeing the returned memory.</returns>
    [UnmanagedCallersOnly(EntryPoint = "cascode_job_start")]
    public static IntPtr JobStart(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "job.start");
    }

    /// <summary>
    /// Invokes the native "job.poll" API for the given session.
    /// </summary>
    /// <param name="session">The session identifier.</param>
    /// <param name="requestJson">Pointer to a UTF-8 JSON request payload; may be null to use an empty object.</param>
    /// <returns>A pointer to a UTF-8-encoded JSON response, or <see cref="IntPtr.Zero"/> on error (the session's last error JSON will be set).</returns>
    [UnmanagedCallersOnly(EntryPoint = "cascode_job_poll")]
    public static IntPtr JobPoll(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "job.poll");
    }

    /// <summary>
    /// Handles a "job.cancel" request for the specified session.
    /// </summary>
    /// <param name="session">Numeric session identifier returned by CreateSession.</param>
    /// <param name="requestJson">Pointer to a UTF-8 JSON request payload (nullable; treated as "{}" when null).</param>
    /// <returns>A pointer to a UTF-8 JSON response on success, or <see cref="IntPtr.Zero"/> on error (the session's last error JSON will contain details).</returns>
    [UnmanagedCallersOnly(EntryPoint = "cascode_job_cancel")]
    public static IntPtr JobCancel(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "job.cancel");
    }

    /// <summary>
    /// Handles a "pdk.setDir" request for the specified session.
    /// </summary>
    /// <param name="session">Numeric session identifier returned by CreateSession.</param>
    /// <param name="requestJson">Pointer to a UTF-8 JSON request payload (nullable; treated as "{}" when null).</param>
    /// <returns>A pointer to a UTF-8 JSON response on success, or <see cref="IntPtr.Zero"/> on error.</returns>
    [UnmanagedCallersOnly(EntryPoint = "cascode_pdk_set_dir")]
    public static IntPtr PdkSetDir(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "pdk.setDir");
    }

    /// <summary>
    /// Handles a "pdk.scan" request for the specified session.
    /// </summary>
    /// <param name="session">Numeric session identifier returned by CreateSession.</param>
    /// <param name="requestJson">Pointer to a UTF-8 JSON request payload (nullable; treated as "{}" when null).</param>
    /// <returns>A pointer to a UTF-8 JSON response on success, or <see cref="IntPtr.Zero"/> on error.</returns>
    [UnmanagedCallersOnly(EntryPoint = "cascode_pdk_scan")]
    public static IntPtr PdkScan(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "pdk.scan");
    }

    /// <summary>
    /// Handles a "pdk.emitPrimitives" request for the specified session.
    /// </summary>
    /// <param name="session">Numeric session identifier returned by CreateSession.</param>
    /// <param name="requestJson">Pointer to a UTF-8 JSON request payload (nullable; treated as "{}" when null).</param>
    /// <returns>A pointer to a UTF-8 JSON response on success, or <see cref="IntPtr.Zero"/> on error.</returns>
    [UnmanagedCallersOnly(EntryPoint = "cascode_pdk_emit_primitives")]
    public static IntPtr PdkEmitPrimitives(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "pdk.emitPrimitives");
    }

    /// <summary>
    /// Dispatches a JSON request for the specified API method within the given session and returns a pointer to the UTF-8 response.
    /// </summary>
    /// <param name="session">Identifier of the session to use for the request.</param>
    /// <param name="requestJson">Pointer to a UTF-8 encoded JSON request; may be null to indicate an empty object ("{}").</param>
    /// <param name="method">Name of the API method to invoke (for example, "document.open").</param>
    /// <returns>Pointer to a UTF-8 encoded response string on success; IntPtr.Zero on error. When an error occurs the session's last error JSON is recorded.</returns>
    private static IntPtr Invoke(int session, byte* requestJson, string method)
    {
        if (!SessionManager.TryGetSession(session, out var sessionState))
        {
            return SetErrorAndReturnNull(
                session,
                "CASAPI-INVALID-SESSION",
                $"Session '{session}' is not valid."
            );
        }

        try
        {
            lock (sessionState.SyncRoot)
            {
                var request = Utf8ToString(requestJson) ?? "{}";
                var response = SchematicApiDispatcher.Dispatch(sessionState, method, request);
                sessionState.LastErrorJson = null;
                return StringToUtf8(response);
            }
        }
        catch (ApiException ex)
        {
            return SetErrorAndReturnNull(session, ex.Code, ex.Message, ex.Details);
        }
        catch (Exception ex)
        {
            return SetErrorAndReturnNull(session, "CASAPI-INVALID-REQUEST", ex.Message);
        }
    }

    /// <summary>
    /// Store a structured API error for the given session and return a null pointer.
    /// </summary>
    /// <param name="session">The session id to associate the error with.</param>
    /// <param name="code">A machine-readable error code.</param>
    /// <param name="message">A human-readable error message.</param>
    /// <param name="details">Optional additional JSON details to include with the error.</param>
    /// <returns>IntPtr.Zero (a null pointer).</returns>
    private static IntPtr SetErrorAndReturnNull(
        int session,
        string code,
        string message,
        JsonNode? details = null
    )
    {
        var error = ApiError.ToJson(code, message, details);
        SessionManager.SetLastError(session, error);
        return IntPtr.Zero;
    }

    /// <summary>
    /// Converts a managed string to a pointer to a UTF-8 encoded, CoTaskMem-allocated buffer.
    /// </summary>
    /// <param name="value">The string to convert; if null or empty, no allocation is performed.</param>
    /// <returns>An IntPtr pointing to a CoTaskMem-allocated UTF-8 buffer containing the string, or <see cref="IntPtr.Zero"/> if <paramref name="value"/> is null or empty.</returns>
    private static IntPtr StringToUtf8(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return IntPtr.Zero;
        }

        return Marshal.StringToCoTaskMemUTF8(value);
    }

    /// <summary>
    /// Converts a UTF-8 encoded unmanaged string pointer to a managed <see cref="string"/>.
    /// </summary>
    /// <param name="pointer">Pointer to a null-terminated UTF-8 encoded string; may be null.</param>
    /// <returns>The managed string, or <c>null</c> if <paramref name="pointer"/> is null.</returns>
    private static string? Utf8ToString(byte* pointer)
    {
        return pointer == null ? null : Marshal.PtrToStringUTF8((IntPtr)pointer);
    }
}
