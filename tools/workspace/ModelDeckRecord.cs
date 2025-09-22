namespace Cascode.Workspace;

/// <summary>
/// Represents a top-level Spectre model deck discovered from cdsinit modelFiles entries.
/// </summary>
public sealed record ModelDeckRecord(
    string DeckPath,
    string Source,
    IReadOnlyList<string> Sections,
    IReadOnlyList<string> Includes,
    IReadOnlyList<SpectreModel> Models
);
