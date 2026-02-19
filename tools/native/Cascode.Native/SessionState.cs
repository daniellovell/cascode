using Cascode.Language;
using Cascode.Render.Placement;
using Cascode.Render.Routing;

namespace Cascode.Native;

internal sealed class DocumentState
{
    public required string DocumentId { get; init; }
    public required string SourceText { get; set; }
    public required CascodeDocument Document { get; set; }
    public required string CircuitName { get; set; }
    public required int Revision { get; set; }
    public required IReadOnlyList<string> ChangedEntities { get; set; }
}

internal enum JobState
{
    Running,
    Completed,
    Cancelled,
}

internal sealed class BenchJob
{
    public required string JobId { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public JobState State { get; set; } = JobState.Running;
    public int ProgressPercent { get; set; }
}

internal sealed class SessionState
{
    public required int Id { get; init; }
    public required object SyncRoot { get; init; }
    public required Dictionary<string, DocumentState> Documents { get; init; }
    public required Dictionary<string, BenchJob> Jobs { get; init; }
    public string? LastErrorJson { get; set; }
    public string? StdlibRoot { get; set; }
    public string? WorkspaceRoot { get; set; }
    public string? PdkRoot { get; set; }

    /// <summary>
    /// Builds an ordered list of search roots for include resolution.
    /// Workspace root comes first (highest priority), then stdlib.
    /// </summary>
    public IReadOnlyList<string> GetSearchRoots()
    {
        var roots = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(WorkspaceRoot))
            roots.Add(WorkspaceRoot);
        if (!string.IsNullOrWhiteSpace(StdlibRoot))
            roots.Add(StdlibRoot);
        return roots;
    }
}

internal sealed class RenderComputation
{
    public required CoarseGridResult Placement { get; init; }
    public required RoutingResult Routing { get; init; }
    public required IReadOnlyList<string> Diagnostics { get; init; }
}
