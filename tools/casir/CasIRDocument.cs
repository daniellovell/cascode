using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Cascode.CasIR;

/// <summary>
/// Root CasIR document representing a bipartite net/instance graph at a specific elaboration level.
/// </summary>
public sealed class CasirDocument
{
    public const string DefaultFormat = "casir-json-1";

    /// <summary>
    /// Schema version for CasIR JSON payloads.
    /// </summary>
    [JsonPropertyName("ir_version")]
    public int IrVersion { get; init; } = 1;

    /// <summary>
    /// Serialization format identifier; defaults to <see cref="DefaultFormat"/>.
    /// </summary>
    [JsonPropertyName("format")]
    public string Format { get; init; } = DefaultFormat;

    /// <summary>
    /// Elaboration level (HL, ML, or EL) controlling required fields and validation.
    /// </summary>
    [JsonPropertyName("level")]
    public CasIRLevel Level { get; init; } = CasIRLevel.ML;

    /// <summary>
    /// Nets that appear in port bindings; each id must be unique within the document.
    /// </summary>
    [JsonPropertyName("nets")]
    public List<Net> Nets { get; init; } = new();

    /// <summary>
    /// Optional bundles (e.g., differential pairs) mapping bundle fields to underlying nets.
    /// </summary>
    [JsonPropertyName("bundles")]
    public List<Bundle> Bundles { get; init; } = new();

    /// <summary>
    /// Motif instances; ports define the authoritative edges in the graph.
    /// </summary>
    [JsonPropertyName("motifs")]
    public List<MotifInstance> Motifs { get; init; } = new();

    /// <summary>
    /// Motif type definitions for all referenced types. Each entry describes the structure
    /// of a motif type used by instances in this document.
    /// </summary>
    [JsonPropertyName("definitions")]
    public List<MotifDefinition>? Definitions { get; init; }
}

/// <summary>
/// Describes a motif type definition, including its ports, parameters, and internal structure.
/// </summary>
public sealed class MotifDefinition
{
    /// <summary>Simple motif type name (e.g., "DiffPair").</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Fully qualified package path (e.g., "lib.std.prim").</summary>
    [JsonPropertyName("package")]
    public string? Package { get; init; }

    /// <summary>Traits this motif implements.</summary>
    [JsonPropertyName("implements")]
    public List<string>? Implements { get; init; }

    /// <summary>Parameter declarations with optional defaults.</summary>
    [JsonPropertyName("params")]
    public List<ParamDeclaration>? Params { get; init; }

    /// <summary>Port declarations defining the motif's interface.</summary>
    [JsonPropertyName("ports")]
    public List<PortDeclaration>? Ports { get; init; }

    /// <summary>Supply declarations.</summary>
    [JsonPropertyName("supplies")]
    public List<string>? Supplies { get; init; }

    /// <summary>Ground declarations.</summary>
    [JsonPropertyName("grounds")]
    public List<string>? Grounds { get; init; }

    /// <summary>Child motif instances within this definition.</summary>
    [JsonPropertyName("instances")]
    public List<MotifInstance>? Instances { get; init; }
}

/// <summary>
/// Declares a parameter on a motif definition.
/// </summary>
public sealed class ParamDeclaration
{
    /// <summary>Parameter name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Parameter type (e.g., "polarity", "bool", "int").</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>Default value if not overridden.</summary>
    [JsonPropertyName("default")]
    public string? Default { get; init; }
}

/// <summary>
/// Declares a port on a motif definition.
/// </summary>
public sealed class PortDeclaration
{
    /// <summary>Port name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Port kind (e.g., "analog", "Diff", "bias").</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;
}

/// <summary>
/// Defines a single net and its signal domain.
/// </summary>
public sealed class Net
{
    /// <summary>Net identifier referenced by motif port bindings.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Net domain: supply, ground, signal, analog, digital, mixed, bias, rf, or clock.
    /// </summary>
    [JsonPropertyName("domain")]
    public string Domain { get; init; } = string.Empty;

    /// <summary>
    /// Canonical rail name for supply or ground nets (e.g., VDD, GND); omitted for other domains.
    /// </summary>
    [JsonPropertyName("rail")]
    public string? Rail { get; init; }

    /// <summary>
    /// Optional role labels that aid matching and diagnostics (e.g., ota_out, sense).
    /// </summary>
    [JsonPropertyName("roles")]
    public List<string>? Roles { get; init; }
}

/// <summary>
/// Groups related nets, typically differential pairs, under a bundle id.
/// </summary>
public sealed class Bundle
{
    /// <summary>Bundle identifier referenced by pin paths (e.g., IN for IN.P/IN.N).</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Bundle shape; "Diff" represents a differential pair. Extensions may introduce additional shapes.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "Diff";

    /// <summary>
    /// Mapping from bundle field names to concrete nets.
    /// </summary>
    [JsonPropertyName("fields")]
    public BundleFields Fields { get; init; } = new();
}

/// <summary>
/// Resolves bundle fields to underlying nets.
/// </summary>
public sealed class BundleFields
{
    /// <summary>Positive-side net id for a differential bundle field.</summary>
    [JsonPropertyName("p")]
    public string P { get; init; } = string.Empty;

    /// <summary>Negative-side net id for a differential bundle field.</summary>
    [JsonPropertyName("n")]
    public string N { get; init; } = string.Empty;
}

/// <summary>
/// Represents a placed instance of a circuit motif or block.
/// </summary>
public sealed class MotifInstance
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Motif type name (fully qualified when available) that this instance instantiates.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Optional list of trait flags applied to the instance (e.g., "inverter", "ota"); may be omitted when no traits are asserted.
    /// </summary>
    [JsonPropertyName("traits")]
    public List<string>? Traits { get; init; }

    /// <summary>
    /// Port bindings mapping instance port paths to net or bundle identifiers.
    /// </summary>
    [JsonPropertyName("ports")]
    public Dictionary<string, string> Ports { get; init; } = new();

    /// <summary>
    /// Parameter overrides keyed by parameter name.
    /// </summary>
    /// <remarks>
    /// <c>null</c> means no parameters were provided; an empty dictionary means parameters were provided explicitly and none were set.
    /// </remarks>
    [JsonPropertyName("params")]
    public Dictionary<string, ParamValue>? Params { get; init; }
}

/// <summary>
/// Represents a parameter value that may be symbolic or numeric with an optional unit.
/// </summary>
public sealed class ParamValue
{
    /// <summary>Symbolic expression for the parameter (e.g., "Auto" or "Lmin").</summary>
    [JsonPropertyName("symbolic")]
    public string? Symbolic { get; init; }

    /// <summary>Concrete numeric value when available.</summary>
    [JsonPropertyName("value")]
    public double? Value { get; init; }

    /// <summary>Optional unit string accompanying the numeric value.</summary>
    [JsonPropertyName("unit")]
    public string? Unit { get; init; }
}
