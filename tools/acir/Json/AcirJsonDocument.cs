using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Cascode.ACIR.Json;

/// <summary>
/// Root JSON document representing an ACIR-EL circuit.
/// </summary>
public sealed record AcirJsonDocument
{
    [JsonPropertyName("acirVersion")]
    public string AcirVersion { get; init; } = ACIRVersion.Current;

    [JsonPropertyName("traits")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AcirJsonTrait>? Traits { get; init; }

    [JsonPropertyName("circuit")]
    public required AcirJsonCircuitInfo Circuit { get; init; }

    [JsonPropertyName("supplies")]
    public IReadOnlyList<string> Supplies { get; init; } = [];

    [JsonPropertyName("grounds")]
    public IReadOnlyList<string> Grounds { get; init; } = [];

    [JsonPropertyName("ports")]
    public IReadOnlyList<AcirJsonPort> Ports { get; init; } = [];

    [JsonPropertyName("nets")]
    public IReadOnlyList<AcirJsonNet> Nets { get; init; } = [];

    [JsonPropertyName("components")]
    public IReadOnlyList<AcirJsonComponent> Components { get; init; } = [];

    [JsonPropertyName("instances")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AcirJsonInstance>? Instances { get; init; }

    [JsonPropertyName("attaches")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AcirJsonAttach>? Attaches { get; init; }

    [JsonPropertyName("constraints")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AcirJsonConstraints? Constraints { get; init; }

    [JsonPropertyName("harness")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AcirJsonHarness? Harness { get; init; }

    [JsonPropertyName("benchDefinitions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AcirJsonBenchDefinition>? BenchDefinitions { get; init; }
}

/// <summary>
/// Circuit metadata including name, traits, and elaboration level.
/// </summary>
public sealed record AcirJsonCircuitInfo
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("traits")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Traits { get; init; }

    [JsonPropertyName("level")]
    public string Level { get; init; } = "EL";

    [JsonPropertyName("inline")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Inline { get; init; }

    [JsonPropertyName("parameters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AcirJsonCircuitParameter>? Parameters { get; init; }

    [JsonPropertyName("sizes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AcirJsonSizeDeclaration>? Sizes { get; init; }
}

/// <summary>
/// Port declaration with name and kind (domain).
/// </summary>
public sealed record AcirJsonPort
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }
}

/// <summary>
/// Net declaration with name and kind (domain).
/// </summary>
public sealed record AcirJsonNet
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }
}

/// <summary>
/// Component (device) declaration at EL level.
/// </summary>
public sealed record AcirJsonComponent
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("connections")]
    public required IReadOnlyDictionary<string, string> Connections { get; init; }

    [JsonPropertyName("params")]
    public required IReadOnlyDictionary<string, string> Params { get; init; }

    [JsonPropertyName("process")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Process { get; init; }
}

/// <summary>
/// Constraints block containing numeric and tech constraints.
/// </summary>
public sealed record AcirJsonConstraints
{
    [JsonPropertyName("numeric")]
    public IReadOnlyList<AcirJsonNumericConstraint> Numeric { get; init; } = [];

    [JsonPropertyName("tech")]
    public IReadOnlyList<AcirJsonTechConstraint> Tech { get; init; } = [];
}

/// <summary>
/// Numeric constraint with SI-base value for machine processing.
/// </summary>
public sealed record AcirJsonNumericConstraint
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
/// Technology constraint with SI-base value for machine processing.
/// </summary>
public sealed record AcirJsonTechConstraint
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
public sealed record AcirJsonBenchDefinition
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("trait")]
    public required string Trait { get; init; }

    [JsonPropertyName("builtin")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Builtin { get; init; }

    [JsonPropertyName("template")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Template { get; init; }

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
public sealed record AcirJsonHarness
{
    [JsonPropertyName("supply")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AcirJsonHarnessSupply? Supply { get; init; }

    [JsonPropertyName("biases")]
    public IReadOnlyList<AcirJsonHarnessBias> Biases { get; init; } = [];

    [JsonPropertyName("loads")]
    public IReadOnlyList<AcirJsonHarnessLoad> Loads { get; init; } = [];

    [JsonPropertyName("sweeps")]
    public IReadOnlyList<AcirJsonHarnessSweep> Sweeps { get; init; } = [];
}

/// <summary>
/// Supply voltage definition with SI-base value (volts).
/// </summary>
public sealed record AcirJsonHarnessSupply
{
    [JsonPropertyName("net")]
    public required string Net { get; init; }

    [JsonPropertyName("voltage")]
    public required double Voltage { get; init; }
}

/// <summary>
/// Bias voltage definition with SI-base value (volts).
/// </summary>
public sealed record AcirJsonHarnessBias
{
    [JsonPropertyName("net")]
    public required string Net { get; init; }

    [JsonPropertyName("voltage")]
    public required double Voltage { get; init; }
}

/// <summary>
/// Load definition with capacitance and/or resistance in SI-base units.
/// </summary>
public sealed record AcirJsonHarnessLoad
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
public sealed record AcirJsonHarnessSweep
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
/// Trait definition in JSON format.
/// </summary>
public sealed record AcirJsonTrait
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("ports")]
    public IReadOnlyList<AcirJsonPort> Ports { get; init; } = [];

    [JsonPropertyName("connectors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AcirJsonConnector>? Connectors { get; init; }
}

/// <summary>
/// Trait connector definition in JSON format.
/// </summary>
public sealed record AcirJsonConnector
{
    [JsonPropertyName("targetTrait")]
    public required string TargetTrait { get; init; }

    [JsonPropertyName("mappings")]
    public IReadOnlyList<AcirJsonMapping> Mappings { get; init; } = [];
}

/// <summary>
/// Port mapping in a connector.
/// </summary>
public sealed record AcirJsonMapping
{
    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("target")]
    public required string Target { get; init; }
}

/// <summary>
/// Circuit parameter declaration in JSON format.
/// </summary>
public sealed record AcirJsonCircuitParameter
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
public sealed record AcirJsonInstance
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

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

public sealed record AcirJsonSizeDeclaration
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
public sealed record AcirJsonAttach
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
    public IReadOnlyList<AcirJsonMapping>? Overrides { get; init; }
}
