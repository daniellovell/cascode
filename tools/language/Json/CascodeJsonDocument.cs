using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Cascode.Language.Json;

/// <summary>
/// Root JSON document representing an Cascode-EL circuit.
/// </summary>
public sealed record CascodeJsonDocument
{
    [JsonPropertyName("cascodeVersion")]
    public string Version { get; init; } = Language.CascodeVersion.Current;

    [JsonPropertyName("interfaces")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CascodeJsonTrait>? Interfaces { get; init; }

    [JsonPropertyName("primitives")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CascodeJsonPrimitive>? Primitives { get; init; }

    [JsonPropertyName("circuit")]
    public required CascodeJsonCircuitInfo Circuit { get; init; }

    [JsonPropertyName("supplies")]
    public IReadOnlyList<string> Supplies { get; init; } = [];

    [JsonPropertyName("grounds")]
    public IReadOnlyList<string> Grounds { get; init; } = [];

    [JsonPropertyName("ports")]
    public IReadOnlyList<CascodeJsonPort> Ports { get; init; } = [];

    [JsonPropertyName("fillSizes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CascodeJsonSizeDeclaration>? FillSizes { get; init; }

    [JsonPropertyName("nets")]
    public IReadOnlyList<CascodeJsonNet> Nets { get; init; } = [];

    [JsonPropertyName("components")]
    public IReadOnlyList<CascodeJsonComponent> Components { get; init; } = [];

    [JsonPropertyName("instances")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CascodeJsonInstance>? Instances { get; init; }

    [JsonPropertyName("attaches")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CascodeJsonAttach>? Attaches { get; init; }

    [JsonPropertyName("constraints")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CascodeJsonConstraints? Constraints { get; init; }

    [JsonPropertyName("harness")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CascodeJsonHarness? Harness { get; init; }

    [JsonPropertyName("benchDefinitions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CascodeJsonBenchDefinition>? BenchDefinitions { get; init; }
}

/// <summary>
/// Circuit metadata including name, interfaces, and elaboration level.
/// </summary>
public sealed record CascodeJsonCircuitInfo
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("interfaces")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Interfaces { get; init; }

    [JsonPropertyName("level")]
    public string Level { get; init; } = "EL";

    [JsonPropertyName("inline")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Inline { get; init; }

    [JsonPropertyName("parameters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CascodeJsonCircuitParameter>? Parameters { get; init; }

    [JsonPropertyName("sizes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CascodeJsonSizeDeclaration>? Sizes { get; init; }
}

/// <summary>
/// Port declaration with name, direction, and kind (domain).
/// </summary>
public sealed record CascodeJsonPort
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("direction")]
    public string? Direction { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }
}

/// <summary>
/// Net declaration with name and kind (domain).
/// </summary>
public sealed record CascodeJsonNet
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }
}

/// <summary>
/// Component (device) declaration at EL level.
/// </summary>
public sealed record CascodeJsonComponent
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("primitive")]
    public required string Primitive { get; init; }

    [JsonPropertyName("connections")]
    public required IReadOnlyDictionary<string, string> Connections { get; init; }

    [JsonPropertyName("sizeName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SizeName { get; init; }

    [JsonPropertyName("size")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? Size { get; init; }
}

/// <summary>
/// Primitive definition in JSON format.
/// </summary>
public sealed record CascodeJsonPrimitive
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("device")]
    public required string Device { get; init; }

    [JsonPropertyName("sizeParam")]
    public required string SizeParam { get; init; }

    [JsonPropertyName("params")]
    public IReadOnlyDictionary<string, string> Params { get; init; } =
        new Dictionary<string, string>();
}

/// <summary>
/// Constraints block containing bench, spec, and physical constraints.
/// </summary>
public sealed record CascodeJsonConstraints
{
    [JsonPropertyName("bench")]
    public IReadOnlyList<CascodeJsonMetricConstraint> Bench { get; init; } = [];

    [JsonPropertyName("spec")]
    public IReadOnlyList<CascodeJsonMetricConstraint> Spec { get; init; } = [];

    [JsonPropertyName("physical")]
    public IReadOnlyList<CascodeJsonPhysicalConstraint> Physical { get; init; } = [];
}

/// <summary>
/// Metric constraint with SI-base value for machine processing.
/// </summary>
public sealed record CascodeJsonMetricConstraint
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("bench")]
    public required string Bench { get; init; }

    [JsonPropertyName("metric")]
    public required string Metric { get; init; }

    [JsonPropertyName("node")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Node { get; init; }

    [JsonPropertyName("op")]
    public required string Op { get; init; }

    [JsonPropertyName("value")]
    public required double Value { get; init; }

    [JsonPropertyName("unit")]
    public required string Unit { get; init; }
}

/// <summary>
/// Physical constraint with SI-base value for machine processing.
/// </summary>
public sealed record CascodeJsonPhysicalConstraint
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("metric")]
    public required string Metric { get; init; }

    [JsonPropertyName("op")]
    public required string Op { get; init; }

    [JsonPropertyName("value")]
    public required double Value { get; init; }

    [JsonPropertyName("unit")]
    public required string Unit { get; init; }

    [JsonPropertyName("scope")]
    public required string Scope { get; init; }
}

/// <summary>
/// Bench definition at document scope.
/// </summary>
public sealed record CascodeJsonBenchDefinition
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("interface")]
    public required string Interface { get; init; }

    [JsonPropertyName("builtin")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Builtin { get; init; }

    [JsonPropertyName("config")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? Config { get; init; }

    [JsonPropertyName("outputs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Outputs { get; init; }
}

/// <summary>
/// Harness block containing supply, bias, and load definitions.
/// </summary>
public sealed record CascodeJsonHarness
{
    [JsonPropertyName("supply")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CascodeJsonHarnessSupply? Supply { get; init; }

    [JsonPropertyName("biases")]
    public IReadOnlyList<CascodeJsonHarnessBias> Biases { get; init; } = [];

    [JsonPropertyName("loads")]
    public IReadOnlyList<CascodeJsonHarnessLoad> Loads { get; init; } = [];

    [JsonPropertyName("sweeps")]
    public IReadOnlyList<CascodeJsonHarnessSweep> Sweeps { get; init; } = [];
}

/// <summary>
/// Supply voltage definition with SI-base value (volts).
/// </summary>
public sealed record CascodeJsonHarnessSupply
{
    [JsonPropertyName("net")]
    public required string Net { get; init; }

    [JsonPropertyName("voltage")]
    public required double Voltage { get; init; }
}

/// <summary>
/// Bias voltage definition with SI-base value (volts).
/// </summary>
public sealed record CascodeJsonHarnessBias
{
    [JsonPropertyName("net")]
    public required string Net { get; init; }

    [JsonPropertyName("voltage")]
    public required double Voltage { get; init; }
}

/// <summary>
/// Load definition with capacitance and/or resistance in SI-base units.
/// </summary>
public sealed record CascodeJsonHarnessLoad
{
    [JsonPropertyName("net")]
    public required string Net { get; init; }

    [JsonPropertyName("capacitances")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IReadOnlyList<double> Capacitances { get; init; } = Array.Empty<double>();

    [JsonPropertyName("resistances")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IReadOnlyList<double> Resistances { get; init; } = Array.Empty<double>();
}

/// <summary>
/// Sweep condition definition with SI-base values.
/// </summary>
public sealed record CascodeJsonHarnessSweep
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("start")]
    public required double Start { get; init; }

    [JsonPropertyName("stop")]
    public required double Stop { get; init; }

    [JsonPropertyName("step")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Step { get; init; }

    [JsonPropertyName("isAuto")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsAuto { get; init; }
}

/// <summary>
/// Interface definition in JSON format.
/// </summary>
public sealed record CascodeJsonTrait
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("ports")]
    public IReadOnlyList<CascodeJsonPort> Ports { get; init; } = [];

    [JsonPropertyName("connectors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CascodeJsonConnector>? Connectors { get; init; }
}

/// <summary>
/// Interface connector definition in JSON format.
/// </summary>
public sealed record CascodeJsonConnector
{
    [JsonPropertyName("targetInterface")]
    public required string TargetTrait { get; init; }

    [JsonPropertyName("mappings")]
    public IReadOnlyList<CascodeJsonMapping> Mappings { get; init; } = [];
}

/// <summary>
/// Port mapping in a connector.
/// </summary>
public sealed record CascodeJsonMapping
{
    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("target")]
    public required string Target { get; init; }
}

/// <summary>
/// Circuit parameter declaration in JSON format.
/// </summary>
public sealed record CascodeJsonCircuitParameter
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("default")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Default { get; init; }
}

/// <summary>
/// Instance declaration in JSON format (ML level).
/// </summary>
public sealed record CascodeJsonInstance
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("declaredType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeclaredType { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("bindings")]
    public IReadOnlyDictionary<string, string> Bindings { get; init; } =
        new Dictionary<string, string>();

    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? Params { get; init; }

    [JsonPropertyName("sizes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? Sizes { get; init; }
}

public sealed record CascodeJsonSizeDeclaration
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("default")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? Default { get; init; }
}

/// <summary>
/// Attach statement in JSON format.
/// </summary>
public sealed record CascodeJsonAttach
{
    [JsonPropertyName("sourceInstance")]
    public required string SourceInstance { get; init; }

    [JsonPropertyName("targetInstances")]
    public required IReadOnlyList<string> TargetInstances { get; init; }

    [JsonPropertyName("via")]
    public required string Via { get; init; }

    [JsonPropertyName("anchor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Anchor { get; init; }

    [JsonPropertyName("overrides")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CascodeJsonMapping>? Overrides { get; init; }
}
