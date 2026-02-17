using System.Collections.Concurrent;
using System.Threading;

namespace Cascode.Native;

internal static class SessionManager
{
    private static readonly ConcurrentDictionary<int, SessionState> Sessions = new();
    private static int _nextId;

    public static int CreateSession(string? _)
    {
        var id = Interlocked.Increment(ref _nextId);
        var state = new SessionState
        {
            Id = id,
            SyncRoot = new object(),
            Documents = new Dictionary<string, DocumentState>(StringComparer.Ordinal),
            Jobs = new Dictionary<string, BenchJob>(StringComparer.Ordinal),
        };

        Sessions[id] = state;
        return id;
    }

    public static bool TryGetSession(int id, out SessionState state)
    {
        return Sessions.TryGetValue(id, out state!);
    }

    public static bool DestroySession(int id)
    {
        return Sessions.TryRemove(id, out _);
    }

    public static void SetLastError(int id, string errorJson)
    {
        if (Sessions.TryGetValue(id, out var session))
        {
            session.LastErrorJson = errorJson;
        }
    }

    public static string? GetLastError(int id)
    {
        return Sessions.TryGetValue(id, out var session) ? session.LastErrorJson : null;
    }
}
