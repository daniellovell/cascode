using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace Cascode.Native;

public static unsafe class NativeExports
{
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

    [UnmanagedCallersOnly(EntryPoint = "cascode_destroy_session")]
    public static void DestroySession(int session)
    {
        SessionManager.DestroySession(session);
    }

    [UnmanagedCallersOnly(EntryPoint = "cascode_free_string")]
    public static void FreeString(IntPtr ptr)
    {
        if (ptr != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(ptr);
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "cascode_last_error_json")]
    public static IntPtr LastErrorJson(int session)
    {
        var error = SessionManager.GetLastError(session);
        return StringToUtf8(error);
    }

    [UnmanagedCallersOnly(EntryPoint = "cascode_api_version")]
    public static IntPtr ApiVersion()
    {
        return StringToUtf8("cascode.api/1.0");
    }

    [UnmanagedCallersOnly(EntryPoint = "cascode_schema_version")]
    public static IntPtr SchemaVersion()
    {
        return StringToUtf8("cascode.schematic/1.0");
    }

    [UnmanagedCallersOnly(EntryPoint = "cascode_document_open")]
    public static IntPtr DocumentOpen(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "document.open");
    }

    [UnmanagedCallersOnly(EntryPoint = "cascode_document_update_text")]
    public static IntPtr DocumentUpdateText(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "document.updateText");
    }

    [UnmanagedCallersOnly(EntryPoint = "cascode_document_close")]
    public static IntPtr DocumentClose(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "document.close");
    }

    [UnmanagedCallersOnly(EntryPoint = "cascode_convert_to_structural")]
    public static IntPtr ConvertToStructural(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "convert.toStructural");
    }

    [UnmanagedCallersOnly(EntryPoint = "cascode_convert_to_cas")]
    public static IntPtr ConvertToCas(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "convert.toCas");
    }

    [UnmanagedCallersOnly(EntryPoint = "cascode_render_schematic")]
    public static IntPtr RenderSchematic(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "render.schematic");
    }

    [UnmanagedCallersOnly(EntryPoint = "cascode_schematic_apply_ops")]
    public static IntPtr SchematicApplyOps(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "schematic.applyOperations");
    }

    [UnmanagedCallersOnly(EntryPoint = "cascode_erc_run")]
    public static IntPtr ErcRun(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "erc.run");
    }

    [UnmanagedCallersOnly(EntryPoint = "cascode_emit_run")]
    public static IntPtr EmitRun(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "emit.run");
    }

    [UnmanagedCallersOnly(EntryPoint = "cascode_verify_run")]
    public static IntPtr VerifyRun(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "verify.run");
    }

    [UnmanagedCallersOnly(EntryPoint = "cascode_command_execute")]
    public static IntPtr CommandExecute(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "command.execute");
    }

    [UnmanagedCallersOnly(EntryPoint = "cascode_job_start")]
    public static IntPtr JobStart(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "job.start");
    }

    [UnmanagedCallersOnly(EntryPoint = "cascode_job_poll")]
    public static IntPtr JobPoll(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "job.poll");
    }

    [UnmanagedCallersOnly(EntryPoint = "cascode_job_cancel")]
    public static IntPtr JobCancel(int session, byte* requestJson)
    {
        return Invoke(session, requestJson, "job.cancel");
    }

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

    private static IntPtr StringToUtf8(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return IntPtr.Zero;
        }

        return Marshal.StringToCoTaskMemUTF8(value);
    }

    private static string? Utf8ToString(byte* pointer)
    {
        return pointer == null ? null : Marshal.PtrToStringUTF8((IntPtr)pointer);
    }
}
