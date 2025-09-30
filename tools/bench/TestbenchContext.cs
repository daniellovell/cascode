namespace Cascode.Bench;

public sealed class TestbenchContext
{
    public required TestbenchSpec Spec { get; init; }
    public required string WorkspaceRoot { get; init; }
    public required string PdkRoot { get; init; }
    public required IReadOnlyList<string> DeckPaths { get; init; }
    public string? Section { get; init; }
    public IDictionary<string, object?> Args { get; init; } = new Dictionary<string, object?>();
}

public sealed class TestbenchPlan
{
    public required string HarnessId { get; init; }
    public required BenchBackendType Backend { get; init; }
    public required string NetlistName { get; init; }
    public required IReadOnlyDictionary<string, string> Artifacts { get; init; }
    public string? Notes { get; init; }
    public IDictionary<string, object> Data { get; init; } = new Dictionary<string, object>();
}
