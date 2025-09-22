namespace Cascode.Workspace;

public sealed class WorkspaceScanner
{
    private readonly CdsLibParser _cdsLibParser = new();
    private readonly CdsInitScanner _cdsInitScanner = new();
    private readonly SpectreDeckInspector _deckInspector = new();

    public WorkspaceScanResult Scan(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            throw new ArgumentException("Workspace root must be provided", nameof(workspaceRoot));
        }

        var root = Path.GetFullPath(workspaceRoot);
        var warnings = new List<string>();

        var libraries = _cdsLibParser.Parse(root, warnings);

        var deckPaths = _cdsInitScanner.FindModelDecks(root, warnings);
        var deckRecords = new List<ModelDeckRecord>(deckPaths.Count);

        foreach (var deckPath in deckPaths)
        {
            var record = _deckInspector.Inspect(root, deckPath, warnings);
            deckRecords.Add(record);
        }

        return new WorkspaceScanResult(root, libraries, deckRecords, warnings);
    }
}
