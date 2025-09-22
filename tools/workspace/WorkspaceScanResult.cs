namespace Cascode.Workspace;

public sealed class WorkspaceScanResult
{
    public WorkspaceScanResult(
        string workspaceRoot,
        IReadOnlyList<WorkspaceLibrary> libraries,
        IReadOnlyList<ModelDeckRecord> modelDecks,
        IReadOnlyList<SpectreModel> models,
        IReadOnlyList<string> warnings)
    {
        WorkspaceRoot = workspaceRoot;
        Libraries = libraries;
        ModelDecks = modelDecks;
        Models = models;
        Warnings = warnings;
    }

    public string WorkspaceRoot { get; }

    public IReadOnlyList<WorkspaceLibrary> Libraries { get; }

    public IReadOnlyList<ModelDeckRecord> ModelDecks { get; }

    public IReadOnlyList<SpectreModel> Models { get; }

    public IReadOnlyList<string> Warnings { get; }
}
