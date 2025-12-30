namespace Cascode.Workspace;

public sealed class DeviceModelMatchRecord
{
    public string DeviceCanonicalName { get; init; } = string.Empty;
    public string ModelName { get; init; } = string.Empty;
    public string Quality { get; init; } = string.Empty; // exact_name|normalized_name|class_tags|ambiguous
    public int Rank { get; init; }
    public string? Notes { get; init; }
}
