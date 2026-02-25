using System;
using System.Collections.Generic;

namespace Cascode.Language;

/// <summary>
/// A part declaration and its catalog backing.
/// </summary>
public sealed class PartDefinition
{
    /// <summary>Part name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>True when the part is abstract and cannot be directly instantiated.</summary>
    public bool IsAbstract { get; init; }

    /// <summary>Optional base part referenced by <c>extends</c>.</summary>
    public string? BasePart { get; init; }

    /// <summary>Arguments passed to the base part constructor.</summary>
    public Dictionary<string, ParamValue> BaseArguments { get; init; } =
        new(StringComparer.Ordinal);

    /// <summary>Interfaces implemented by this part.</summary>
    public List<string> Implements { get; init; } = new();

    /// <summary>Constructor parameters.</summary>
    public List<CircuitParameter> Parameters { get; init; } = new();

    /// <summary>Named size declarations available within the part body.</summary>
    public List<SizeDeclaration> Sizes { get; init; } = new();

    /// <summary>Parameter forwarding mappings.</summary>
    public Dictionary<string, string> ParameterMappings { get; init; } =
        new(StringComparer.Ordinal);

    /// <summary>Declared part terminals.</summary>
    public List<PortDeclaration> Ports { get; init; } = new();

    /// <summary>Declared supply pins.</summary>
    public List<string> Supplies { get; init; } = new();

    /// <summary>Declared ground pins.</summary>
    public List<string> Grounds { get; init; } = new();

    /// <summary>Declared metric corners.</summary>
    public List<CornerDefinition> Corners { get; init; } = new();

    /// <summary>Metric assignments declared on the part.</summary>
    public List<MetricAssignment> Metrics { get; init; } = new();

    /// <summary>Catalog defaults, explicit entries, and variants.</summary>
    public PartCatalog Catalog { get; set; } = new();
}

/// <summary>
/// Catalog container for concrete part entries and variant axes.
/// </summary>
public sealed class PartCatalog
{
    /// <summary>Shared defaults merged into entries and variant options.</summary>
    public PartEntryData Defaults { get; init; } = new();

    /// <summary>Explicit named entries.</summary>
    public List<PartCatalogEntry> Entries { get; init; } = new();

    /// <summary>Variant axes used to generate entries.</summary>
    public List<PartVariantAxis> Variants { get; init; } = new();
}

/// <summary>
/// A single explicit catalog entry.
/// </summary>
public sealed class PartCatalogEntry
{
    /// <summary>Entry name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Entry payload.</summary>
    public PartEntryData Data { get; init; } = new();
}

/// <summary>
/// Variant axis used to generate catalog entries.
/// </summary>
public sealed class PartVariantAxis
{
    /// <summary>Axis name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Options on this axis.</summary>
    public List<PartVariantOption> Options { get; init; } = new();
}

/// <summary>
/// A single variant option body.
/// </summary>
public sealed class PartVariantOption
{
    /// <summary>Option name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Option payload merged into effective entry data.</summary>
    public PartEntryData Data { get; init; } = new();

    /// <summary>Excluded cross-axis combinations.</summary>
    public List<PartVariantExclusion> Exclusions { get; init; } = new();
}

/// <summary>
/// Excluded cross-axis option pairing for a variant option.
/// </summary>
public sealed record PartVariantExclusion(string Axis, string Option);

/// <summary>
/// Shared entry-shaped data used by defaults, entries, and variant options.
/// </summary>
public sealed class PartEntryData
{
    /// <summary>Scalar catalog fields (for example, mpn, footprint, spice, custom metadata).</summary>
    public Dictionary<string, string> Fields { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Procurement options.</summary>
    public List<PartCatalogOption> Options { get; init; } = new();

    /// <summary>Pin mapping rows.</summary>
    public List<PartPinMap> Pins { get; init; } = new();

    /// <summary>Optional multi-unit grouping metadata.</summary>
    public List<PartUnitGroup> Units { get; init; } = new();

    /// <summary>Metric assignments scoped to this entry payload.</summary>
    public List<MetricAssignment> Metrics { get; init; } = new();

    /// <summary>Mechanical metadata fields.</summary>
    public Dictionary<string, string> MechanicalFields { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Procurement pointer data attached to an entry.
/// </summary>
public sealed class PartCatalogOption
{
    /// <summary>Option member fields (for example, provider, sku, priority, url).</summary>
    public Dictionary<string, string> Fields { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Terminal to pad mapping in an entry.
/// </summary>
public sealed class PartPinMap
{
    /// <summary>Terminal reference on the part.</summary>
    public string Terminal { get; init; } = string.Empty;

    /// <summary>Pad list or expanded pad range values.</summary>
    public List<string> Pads { get; init; } = new();

    /// <summary>True when the original syntax used pad range expansion.</summary>
    public bool IsPadRange { get; init; }
}

/// <summary>
/// Named unit grouping for multi-unit parts.
/// </summary>
public sealed class PartUnitGroup
{
    /// <summary>Unit group name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Unit-level metadata fields.</summary>
    public Dictionary<string, string> Fields { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Declared corner metadata.
/// </summary>
public sealed class CornerDefinition
{
    /// <summary>Corner name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Corner metadata fields.</summary>
    public Dictionary<string, string> Fields { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Metric declaration contract on an interface.
/// </summary>
public sealed class MetricContract
{
    /// <summary>Metric name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Metric unit identifier.</summary>
    public string Unit { get; init; } = string.Empty;

    /// <summary>Qualifiers that must be provided for this metric.</summary>
    public List<MetricQualifier> RequiredQualifiers { get; init; } = new();
}

/// <summary>
/// Metric assignment entry on a part/circuit/binding.
/// </summary>
public sealed class MetricAssignment
{
    /// <summary>Metric name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Optional qualifier (min/max/typ).</summary>
    public MetricQualifier? Qualifier { get; init; }

    /// <summary>Optional corner name.</summary>
    public string? Corner { get; init; }

    /// <summary>Assignment value payload.</summary>
    public MetricAssignmentValue Value { get; init; } = new();
}

/// <summary>
/// Value payload for a metric assignment.
/// </summary>
public sealed class MetricAssignmentValue
{
    /// <summary>Literal scalar value when assigned directly.</summary>
    public string? Scalar { get; init; }

    /// <summary>Optional source reference for forwarded metric values.</summary>
    public MetricSourceReference? Source { get; init; }
}

/// <summary>
/// Source reference for metric assignment values.
/// </summary>
public sealed class MetricSourceReference
{
    /// <summary>Source kind (for example, <c>bench</c>).</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>Source expression text.</summary>
    public string Value { get; init; } = string.Empty;
}

/// <summary>
/// Supported metric qualifiers.
/// </summary>
public enum MetricQualifier
{
    Min,
    Max,
    Typ,
}

/// <summary>
/// Metric qualifier formatting helpers.
/// </summary>
public static class MetricQualifierExtensions
{
    /// <summary>
    /// Converts a <see cref="MetricQualifier"/> to its canonical Cascode syntax spelling.
    /// </summary>
    public static string ToCascodeString(this MetricQualifier qualifier) =>
        qualifier switch
        {
            MetricQualifier.Min => "min",
            MetricQualifier.Max => "max",
            MetricQualifier.Typ => "typ",
            _ => throw new ArgumentOutOfRangeException(nameof(qualifier), qualifier, null),
        };
}
