using System.Collections.Generic;

namespace Cascode.Compiler;

internal sealed class StructuralDesign
{
    public Dictionary<string, NetInfo> Nets { get; } = new();
    public Dictionary<string, BundleInfo> Bundles { get; } = new();
    public Dictionary<string, InstanceInfo> Instances { get; } = new();
}

internal sealed class NetInfo
{
    public string Id { get; init; } = string.Empty;
    public string Domain { get; init; } = "electrical";
    public string? Rail { get; init; }
}

internal sealed class BundleInfo
{
    public string Id { get; init; } = string.Empty;
    public string PNet { get; init; } = string.Empty;
    public string NNet { get; init; } = string.Empty;
}

internal sealed class InstanceInfo
{
    public string Id { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public Dictionary<string, string> Ports { get; } = new();
}

