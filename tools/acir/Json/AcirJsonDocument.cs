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

    [JsonPropertyName("circuit")]
    public required AcirJsonCircuitInfo Circuit { get; init; }

    [JsonPropertyName("supply")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Supply { get; init; }

    [JsonPropertyName("ground")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Ground { get; init; }

    [JsonPropertyName("ports")]
    public IReadOnlyList<AcirJsonPort> Ports { get; init; } = [];

    [JsonPropertyName("nets")]
    public IReadOnlyList<AcirJsonNet> Nets { get; init; } = [];

    [JsonPropertyName("components")]
    public IReadOnlyList<AcirJsonComponent> Components { get; init; } = [];

    [JsonPropertyName("constraints")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AcirJsonConstraints? Constraints { get; init; }

    [JsonPropertyName("harness")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AcirJsonHarness? Harness { get; init; }

    [JsonPropertyName("benches")]
    public IReadOnlyList<string> Benches { get; init; } = [];
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
/// Constraints block containing numeric, tech, and measure constraints.
/// </summary>
public sealed record AcirJsonConstraints
{
    [JsonPropertyName("numeric")]
    public IReadOnlyList<AcirJsonNumericConstraint> Numeric { get; init; } = [];

    [JsonPropertyName("tech")]
    public IReadOnlyList<AcirJsonTechConstraint> Tech { get; init; } = [];

    [JsonPropertyName("measure")]
    public IReadOnlyList<AcirJsonMeasure> Measure { get; init; } = [];
}

/// <summary>
/// Numeric constraint with SI-base value for machine processing.
/// </summary>
public sealed record AcirJsonNumericConstraint
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

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
/// Measurement intent specifying a metric to extract from simulation.
/// </summary>
public sealed record AcirJsonMeasure
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

    [JsonPropertyName("capacitance")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double Capacitance { get; init; }

    [JsonPropertyName("resistance")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double Resistance { get; init; }
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
