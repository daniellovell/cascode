using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Threading;

namespace Cascode.Native;

internal static class SessionManager
{
    private static readonly ConcurrentDictionary<int, SessionState> Sessions = new();
    private static int _nextId;

    /// <summary>
    /// Creates and registers a new session, parsing optional configuration from JSON.
    /// Supported fields: stdlibRoot, workspaceRoot, pdkRoot.
    /// </summary>
    /// <returns>The newly assigned session id.</returns>
    public static int CreateSession(string? optionsJson)
    {
        var id = Interlocked.Increment(ref _nextId);
        var state = new SessionState
        {
            Id = id,
            SyncRoot = new object(),
            Documents = new Dictionary<string, DocumentState>(StringComparer.Ordinal),
            Jobs = new Dictionary<string, BenchJob>(StringComparer.Ordinal),
        };

        if (!string.IsNullOrWhiteSpace(optionsJson))
        {
            using var doc = JsonDocument.Parse(optionsJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("stdlibRoot", out var stdlib) && stdlib.ValueKind == JsonValueKind.String)
                state.StdlibRoot = stdlib.GetString();

            if (root.TryGetProperty("workspaceRoot", out var ws) && ws.ValueKind == JsonValueKind.String)
                state.WorkspaceRoot = ws.GetString();

            if (root.TryGetProperty("pdkRoot", out var pdk) && pdk.ValueKind == JsonValueKind.String)
                state.PdkRoot = pdk.GetString();
        }

        Sessions[id] = state;
        return id;
    }

    /// <summary>
    /// Retrieve the session state for the specified session id.
    /// </summary>
    /// <param name="id">The session identifier.</param>
    /// <param name="state">When the method returns `true`, contains the session's <see cref="SessionState"/>; otherwise `null`.</param>
    /// <returns>`true` if a session with the specified id was found, `false` otherwise.</returns>
    public static bool TryGetSession(int id, [NotNullWhen(true)] out SessionState? state)
    {
        return Sessions.TryGetValue(id, out state);
    }

    /// <summary>
    /// Removes the session with the specified session id from the manager.
    /// </summary>
    /// <param name="id">The identifier of the session to remove.</param>
    /// <returns>`true` if a session with the specified id was removed; `false` if no such session existed.</returns>
    public static bool DestroySession(int id)
    {
        return Sessions.TryRemove(id, out _);
    }

    /// <summary>
    /// Records the provided JSON error payload as the last error for the session with the specified id.
    /// </summary>
    /// <param name="id">The session identifier whose last error will be set.</param>
    /// <param name="errorJson">A JSON-formatted error payload to store as the session's last error.</param>
    public static void SetLastError(int id, string errorJson)
    {
        if (Sessions.TryGetValue(id, out var session))
        {
            session.LastErrorJson = errorJson;
        }
    }

    /// <summary>
    /// Get the last recorded error payload for a session.
    /// </summary>
    /// <param name="id">Session identifier.</param>
    /// <returns>The last error JSON for the session, or null if the session does not exist or no error is recorded.</returns>
    public static string? GetLastError(int id)
    {
        return Sessions.TryGetValue(id, out var session) ? session.LastErrorJson : null;
    }
}
