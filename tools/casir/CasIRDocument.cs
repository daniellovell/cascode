using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Cascode.CasIR;

public sealed class CasirDocument
{
    [JsonPropertyName("ir_version")]
    public int IrVersion { get; init; } = 1;

    [JsonPropertyName("format")]
    public string Format { get; init; } = "casir-json-1";

    [JsonPropertyName("level")]
    public CasIRLevel Level { get; init; } = CasIRLevel.ML;

    [JsonPropertyName("nets")]
    public List<Net> Nets { get; init; } = new();

    [JsonPropertyName("bundles")]
    public List<Bundle> Bundles { get; init; } = new();

    [JsonPropertyName("motifs")]
    public List<MotifInstance> Motifs { get; init; } = new();
}

public sealed class Net
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("domain")]
    public string Domain { get; init; } = string.Empty;

    [JsonPropertyName("rail")]
    public string? Rail { get; init; }

    [JsonPropertyName("roles")]
    public List<string>? Roles { get; init; }
}

public sealed class Bundle
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = "Diff";

    [JsonPropertyName("fields")]
    public BundleFields Fields { get; init; } = new();
}

public sealed class BundleFields
{
    [JsonPropertyName("p")]
    public string P { get; init; } = string.Empty;

    [JsonPropertyName("n")]
    public string N { get; init; } = string.Empty;
}

public sealed class MotifInstance
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("traits")]
    public List<string>? Traits { get; init; }

    [JsonPropertyName("ports")]
    public Dictionary<string, string> Ports { get; init; } = new();

    [JsonPropertyName("params")]
    public Dictionary<string, ParamValue>? Params { get; init; }
}

public sealed class ParamValue
{
    [JsonPropertyName("symbolic")]
    public string? Symbolic { get; init; }

    [JsonPropertyName("value")]
    public double? Value { get; init; }

    [JsonPropertyName("unit")]
    public string? Unit { get; init; }
}

