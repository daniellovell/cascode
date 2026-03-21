using System.Collections.Generic;

namespace Cascode.Language;

public sealed class MetricsBlock
{
    public List<MetricDeclaration> Declarations { get; init; } = new();
    public List<MetricAssignment> Assignments { get; init; } = new();
}

public sealed class MetricDeclaration
{
    public string Name { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public List<string> RequiredQualifiers { get; init; } = new();
}

public sealed class MetricAssignment
{
    public string Name { get; init; } = string.Empty;
    public string? Qualifier { get; init; }
    public string? Corner { get; init; }
    public string Value { get; init; } = string.Empty;
}

public sealed class SelectionArgument
{
    public string? Axis { get; init; }
    public string Value { get; init; } = string.Empty;
}

public sealed class PartDefinition
{
    public string Name { get; init; } = string.Empty;
    public bool IsAbstract { get; init; }
    public string? BasePart { get; init; }
    public List<string> Implements { get; init; } = new();
    public List<CircuitParameter> Parameters { get; init; } = new();
    public Dictionary<string, string> ParamMappings { get; init; } = new();
    public List<string> Supplies { get; init; } = new();
    public List<string> Grounds { get; init; } = new();
    public List<PortDeclaration> Ports { get; init; } = new();
    public List<PartCorner> Corners { get; init; } = new();
    public PartCatalog Catalog { get; init; } = new();
}

public sealed class PartCorner
{
    public string Name { get; init; } = string.Empty;
    public Dictionary<string, string> Fields { get; init; } = new();
}

public sealed class PartCatalog
{
    public PartCatalogBody? Defaults { get; init; }
    public List<PartCatalogEntry> Entries { get; init; } = new();
    public List<PartVariantAxis> Variants { get; init; } = new();
}

public sealed class PartCatalogEntry
{
    public string Name { get; init; } = string.Empty;
    public PartCatalogBody Body { get; init; } = new();
}

public sealed class PartVariantAxis
{
    public string Name { get; init; } = string.Empty;
    public List<PartVariantOption> Options { get; init; } = new();
}

public sealed class PartVariantOption
{
    public string Name { get; init; } = string.Empty;
    public PartCatalogBody Body { get; init; } = new();
    public List<SelectionArgument> Excludes { get; init; } = new();
}

public sealed class PartCatalogBody
{
    public Dictionary<string, string> Fields { get; init; } = new();
    public List<CatalogOption> Options { get; init; } = new();
    public List<PinMapEntry> Pins { get; init; } = new();
    public List<UnitGroup> Units { get; init; } = new();
    public MetricsBlock? Metrics { get; init; }
}

public sealed class CatalogOption
{
    public Dictionary<string, string> Fields { get; init; } = new();
}

public sealed class PinMapEntry
{
    public string Pad { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
}

public sealed class UnitGroup
{
    public string Name { get; init; } = string.Empty;
    public Dictionary<string, string> Fields { get; init; } = new();
}
