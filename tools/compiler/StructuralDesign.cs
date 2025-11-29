using System.Collections.Generic;

namespace Cascode.Compiler;

/// <summary>
/// Intermediate structural view of a motif used before lowering to CasIR.
/// </summary>
internal sealed class StructuralDesign
{
    /// <summary>
    /// Nets keyed by identifier, including ports, supplies, and grounds.
    /// </summary>
    public Dictionary<string, NetInfo> Nets { get; } = new();

    /// <summary>
    /// Differential bundles keyed by bundle identifier.
    /// </summary>
    public Dictionary<string, BundleInfo> Bundles { get; } = new();

    /// <summary>
    /// Instances declared in the motif, keyed by instance id.
    /// </summary>
    public Dictionary<string, InstanceInfo> Instances { get; } = new();
}

/// <summary>
/// Describes a single net and its signal domain.
/// </summary>
internal sealed class NetInfo
{
    /// <summary>Identifier of the net as referenced by connects.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Domain name used for CasIR lowering. Valid values: <c>signal</c> (default), <c>analog</c>, <c>digital</c>, <c>mixed</c>, <c>supply</c>, <c>ground</c>, <c>bias</c>, <c>rf</c>, <c>clock</c>.</summary>
    public string Domain { get; init; } = "signal";

    /// <summary>Optional rail name when the domain is a supply or ground.</summary>
    public string? Rail { get; init; }
}

/// <summary>
/// Describes a differential bundle mapping to two nets.
/// </summary>
internal sealed class BundleInfo
{
    /// <summary>Bundle identifier.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Positive net id within the bundle.</summary>
    public string PNet { get; init; } = string.Empty;

    /// <summary>Negative net id within the bundle.</summary>
    public string NNet { get; init; } = string.Empty;
}

/// <summary>
/// Describes a motif instance placed within the structural design.
/// </summary>
internal sealed class InstanceInfo
{
    /// <summary>Instance identifier unique within the motif.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Fully qualified motif type name.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Pin-to-net bindings keyed by pin path (e.g., <c>dp.OUT.N</c>).</summary>
    public Dictionary<string, string> Ports { get; } = new();
}
