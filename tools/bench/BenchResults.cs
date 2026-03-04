using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Cascode.Bench;

/// <summary>
/// Results from a bench simulation run.
/// </summary>
public sealed class BenchResult
{
    /// <summary>Circuit name.</summary>
    [JsonPropertyName("circuit")]
    public string Circuit { get; init; } = string.Empty;

    /// <summary>Bench name.</summary>
    [JsonPropertyName("bench")]
    public string Bench { get; init; } = string.Empty;

    /// <summary>Measurements dictionary keyed by measurement ID.</summary>
    [JsonPropertyName("measurements")]
    public Dictionary<string, MeasurementResult> Measurements { get; init; } = new();
}

/// <summary>
/// Result of a single measurement.
/// </summary>
public sealed class MeasurementResult
{
    /// <summary>Metric name (e.g., "PassbandGain", "GainBandwidth").</summary>
    [JsonPropertyName("metric")]
    public string Metric { get; init; } = string.Empty;

    /// <summary>
    /// Measured scalar value. Null when the measurement result is represented by
    /// <see cref="Values"/>.
    /// </summary>
    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Value { get; init; }

    /// <summary>
    /// Measured multi-point values for spectrum/waveform measurements.
    /// </summary>
    [JsonPropertyName("values")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double[]? Values { get; init; }

    /// <summary>
    /// Optional error message when the measurement evaluation failed.
    /// When present, <see cref="Value"/> is not meaningful.
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <summary>Unit of measurement (e.g., "dB", "Hz", "deg").</summary>
    [JsonPropertyName("unit")]
    public string Unit { get; init; } = string.Empty;

    /// <summary>Optional node where measurement was taken (e.g., "OUT").</summary>
    [JsonPropertyName("node")]
    public string? Node { get; init; }

    /// <summary>Source bench that produced this measurement.</summary>
    [JsonPropertyName("bench")]
    public string? Bench { get; init; }
}
