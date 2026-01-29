using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language;

/// <summary>
/// Root ACIR document representing one or more circuits at a specific elaboration level.
/// </summary>
public sealed class ACIRDocument
{
    /// <summary>
    /// ACIR format major version.
    /// </summary>
    public int VersionMajor { get; init; } = 1;

    /// <summary>
    /// ACIR format minor version.
    /// </summary>
    public int VersionMinor { get; init; } = 0;

    /// <summary>
    /// Bundle type definitions declared at the file level.
    /// </summary>
    public List<BundleType> BundleTypes { get; init; } = new();

    /// <summary>
    /// Trait definitions declared at the file level (after bundles, before circuits).
    /// </summary>
    public List<TraitDefinition> Traits { get; init; } = new();

    /// <summary>
    /// Bench definitions declared at the file level (after traits, before circuits).
    /// </summary>
    public List<BenchDefinition> BenchDefinitions { get; init; } = new();

    /// <summary>
    /// Primitive device templates declared at the file level.
    /// </summary>
    public List<PrimitiveDefinition> Primitives { get; init; } = new();

    /// <summary>
    /// Circuit definitions in this document.
    /// </summary>
    public List<Circuit> Circuits { get; init; } = new();
}

/// <summary>
/// Defines a bundle type (e.g., Diff) with its fields and domains.
/// </summary>
public sealed class BundleType
{
    /// <summary>Bundle type name (e.g., "Diff").</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Field definitions mapping field names to domains.</summary>
    public Dictionary<string, string> Fields { get; init; } = new();
}

/// <summary>
/// Utility methods for bundle type expansion.
/// </summary>
public static class BundleExpander
{
    /// <summary>
    /// Expands a port to its terminal paths, recursively expanding bundle types.
    /// For example, port "IN" of type "Diff" expands to ["IN.P", "IN.N"].
    /// Non-bundle types return the original path.
    /// </summary>
    /// <param name="basePath">The base path (e.g., port name).</param>
    /// <param name="typeName">The type of the port (e.g., "Diff", "analog").</param>
    /// <param name="bundlesByName">Dictionary of bundle types by name.</param>
    /// <returns>Enumerable of expanded terminal paths.</returns>
    public static IEnumerable<string> ExpandToTerminalPaths(
        string basePath,
        string typeName,
        IReadOnlyDictionary<string, BundleType> bundlesByName
    )
    {
        if (!bundlesByName.TryGetValue(typeName, out var bundle))
        {
            // Not a bundle type - return the original path
            yield return basePath;
            yield break;
        }

        // Expand each field of the bundle in declaration order
        foreach (var field in bundle.Fields)
        {
            var fieldPath = $"{basePath}.{field.Key}";
            foreach (var path in ExpandToTerminalPaths(fieldPath, field.Value, bundlesByName))
            {
                yield return path;
            }
        }
    }

    /// <summary>
    /// Builds a dictionary of bundle types by name from an ACIR document.
    /// Returns an empty dictionary if document is null.
    /// </summary>
    public static Dictionary<string, BundleType> GetBundlesByName(ACIRDocument? document)
    {
        return document?.BundleTypes.ToDictionary(b => b.Name, StringComparer.Ordinal)
            ?? new Dictionary<string, BundleType>(StringComparer.Ordinal);
    }
}

/// <summary>
/// Represents a circuit definition in ACIR.
/// </summary>
public sealed class Circuit
{
    /// <summary>Circuit name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Optional traits this circuit implements.</summary>
    public List<string>? Traits { get; init; }

    /// <summary>Elaboration level (HL, ML, or EL).</summary>
    public ACIRLevel Level { get; init; } = ACIRLevel.ML;

    /// <summary>
    /// Whether this circuit should be inlined during SPICE emission.
    /// When true, devices and nets merge into parent with hierarchical naming.
    /// </summary>
    public bool Inline { get; init; }

    /// <summary>Optional package path.</summary>
    public string? Package { get; init; }

    /// <summary>
    /// Circuit parameter declarations (typed parameters with optional defaults).
    /// </summary>
    public List<CircuitParameter> Parameters { get; init; } = new();

    /// <summary>
    /// Size pack declarations (named key/value maps with optional defaults).
    /// </summary>
    public List<SizeDeclaration> Sizes { get; init; } = new();

    /// <summary>Supply declarations.</summary>
    public List<string> Supplies { get; init; } = new();

    /// <summary>Ground declarations.</summary>
    public List<string> Grounds { get; init; } = new();

    /// <summary>Port declarations.</summary>
    public List<PortDeclaration> Ports { get; init; } = new();

    /// <summary>Slot declarations (HL level only).</summary>
    public List<SlotDeclaration> Slots { get; init; } = new();

    /// <summary>Fill block content (ML and EL levels).</summary>
    public FillBlock? Fill { get; init; }

    /// <summary>Constraints block.</summary>
    public ConstraintsBlock? Constraints { get; init; }

    /// <summary>Harness block.</summary>
    public HarnessBlock? Harness { get; init; }

    /// <summary>Provenance block.</summary>
    public ProvenanceBlock? Provenance { get; init; }
}

/// <summary>
/// Declares a port on a circuit.
/// </summary>
public sealed class PortDeclaration
{
    /// <summary>Port direction (input, output, or bidirectional).</summary>
    public required PortDirection Direction { get; init; }

    /// <summary>Port name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Port type (domain or bundle type name).</summary>
    public string Type { get; init; } = string.Empty;
}

/// <summary>
/// Directional intent for a port at the circuit boundary.
/// </summary>
public enum PortDirection
{
    Input,
    Output,
    Io,
}

/// <summary>
/// Extension methods for <see cref="PortDirection"/> serialization.
/// </summary>
public static class PortDirectionExtensions
{
    /// <summary>
    /// Converts a <see cref="PortDirection"/> value to its canonical ACIR string representation.
    /// </summary>
    public static string ToAcirString(this PortDirection direction) =>
        direction switch
        {
            PortDirection.Input => "input",
            PortDirection.Output => "output",
            PortDirection.Io => "io",
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
        };

    /// <summary>
    /// Attempts to parse a string into a <see cref="PortDirection"/> value.
    /// </summary>
    public static bool TryParse(string? raw, out PortDirection direction)
    {
        direction = default;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        switch (raw.Trim().ToLowerInvariant())
        {
            case "input":
                direction = PortDirection.Input;
                return true;
            case "output":
                direction = PortDirection.Output;
                return true;
            case "io":
                direction = PortDirection.Io;
                return true;
            default:
                return false;
        }
    }
}

/// <summary>
/// Declares a slot at HL level.
/// </summary>
public sealed class SlotDeclaration
{
    /// <summary>Slot identifier.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Terminal bindings for this slot.</summary>
    public Dictionary<string, string> Bindings { get; init; } = new();

    /// <summary>Required traits (single trait or list).</summary>
    public List<string> Traits { get; init; } = new();

    /// <summary>Parameter values.</summary>
    public Dictionary<string, ParamValue> Params { get; init; } = new();
}

/// <summary>
/// Fill block containing nets, instances, and devices.
/// </summary>
public sealed class FillBlock
{
    /// <summary>Net declarations.</summary>
    public List<NetDeclaration> Nets { get; init; } = new();

    /// <summary>Local size declarations.</summary>
    public List<SizeDeclaration> Sizes { get; init; } = new();

    /// <summary>Instance declarations (ML level).</summary>
    public List<InstanceDeclaration> Instances { get; init; } = new();

    /// <summary>Device declarations (EL level).</summary>
    public List<DeviceDeclaration> Devices { get; init; } = new();

    /// <summary>Attach statements (EL level, for trait-based composition).</summary>
    public List<AttachStatement> Attaches { get; init; } = new();

    /// <summary>Connection statements.</summary>
    public List<ConnectionStatement> Connections { get; init; } = new();
}

/// <summary>
/// Declares a net within a fill block.
/// </summary>
public sealed class NetDeclaration
{
    /// <summary>Net identifier.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Net domain.</summary>
    public string Domain { get; init; } = string.Empty;
}

/// <summary>
/// Declares an instance at ML level.
/// </summary>
public sealed class InstanceDeclaration
{
    /// <summary>Instance identifier.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Motif type name.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Terminal bindings.</summary>
    public Dictionary<string, string> Bindings { get; init; } = new();

    /// <summary>Parameter values.</summary>
    public Dictionary<string, ParamValue> Params { get; init; } = new();

    /// <summary>Size pack assignments for this instance.</summary>
    public Dictionary<string, SizePack> Sizes { get; init; } = new();

    /// <summary>Instance-level connect statements.</summary>
    public List<ConnectionStatement> Connects { get; init; } = new();
}

/// <summary>
/// Declares a primitive device at EL level.
/// </summary>
public sealed class DeviceDeclaration
{
    /// <summary>Device type (nmos, pmos, resistor, capacitor, inductor, diode).</summary>
    public string DeviceType { get; init; } = string.Empty;

    /// <summary>Device identifier.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Primitive template name referenced by this device.</summary>
    public string Primitive { get; init; } = string.Empty;

    /// <summary>Optional named size pack reference.</summary>
    public string? SizeName { get; init; }

    /// <summary>Optional inline size expression for this device.</summary>
    public SizePack? Size { get; init; }

    /// <summary>Terminal bindings.</summary>
    public Dictionary<string, string> Bindings { get; init; } = new();
}

/// <summary>
/// Primitive device template definition.
/// </summary>
public sealed class PrimitiveDefinition
{
    /// <summary>Device kind (nmos, pmos, resistor, capacitor, inductor, diode).</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>Primitive template name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Concrete device key (model/subckt/P-cell name).</summary>
    public string Device { get; init; } = string.Empty;

    /// <summary>Name of the size parameter in the primitive signature.</summary>
    public string SizeParameter { get; init; } = string.Empty;

    /// <summary>Parameter mapping expressions for the device key.</summary>
    public Dictionary<string, string> Params { get; init; } = new();
}

/// <summary>
/// Connection statement.
/// </summary>
public sealed class ConnectionStatement
{
    /// <summary>Source terminal path.</summary>
    public string From { get; init; } = string.Empty;

    /// <summary>Destination net.</summary>
    public string To { get; init; } = string.Empty;
}

/// <summary>
/// Represents a parameter value that may be symbolic or numeric.
/// </summary>
public sealed class ParamValue
{
    /// <summary>Symbolic expression (e.g., "Auto", "ratio").</summary>
    public string? Symbolic { get; init; }

    /// <summary>Numeric value with optional unit (e.g., "1u", "100n", "1.8V").</summary>
    public string? Numeric { get; init; }

    /// <summary>String literal value.</summary>
    public string? Literal { get; init; }
}

/// <summary>
/// Bench definition declared at document scope.
/// </summary>
public sealed class BenchDefinition
{
    /// <summary>Bench alias name used in constraints (e.g., "ACBench").</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Trait this bench applies to.</summary>
    public string Trait { get; init; } = string.Empty;

    /// <summary>Builtin bench template name.</summary>
    public string? Builtin { get; init; }

    /// <summary>Optional bench configuration values.</summary>
    public Dictionary<string, string> Config { get; init; } = new();

    /// <summary>Metric outputs produced by this bench.</summary>
    public List<string> Outputs { get; init; } = new();
}

/// <summary>
/// Constraints block.
/// </summary>
public sealed class ConstraintsBlock
{
    /// <summary>Numeric constraints.</summary>
    public List<NumericConstraint> Numeric { get; init; } = new();

    /// <summary>Technology constraints.</summary>
    public List<TechConstraint> Tech { get; init; } = new();

    /// <summary>Graph constraints.</summary>
    public List<GraphConstraint> Graph { get; init; } = new();
}

/// <summary>
/// Numeric constraint expressing an inequality over a circuit metric with explicit units.
/// </summary>
public sealed class NumericConstraint
{
    /// <summary>Unique identifier for this constraint (e.g., "c_gbw").</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Bench alias that provides the metric.</summary>
    public string Bench { get; init; } = string.Empty;

    /// <summary>The metric being constrained (e.g., "GainBandwidth", "PhaseMargin").</summary>
    public string Metric { get; init; } = string.Empty;

    /// <summary>Optional node reference where the metric is measured.</summary>
    public NodeRef? Node { get; init; }

    /// <summary>Comparison operator: &gt;=, &lt;=, ==, &gt;, or &lt;.</summary>
    public string Op { get; init; } = string.Empty;

    /// <summary>Numeric bound for the constraint (e.g., "100M", "55").</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>Physical unit for the value (e.g., "Hz", "dB", "deg").</summary>
    public string Unit { get; init; } = string.Empty;
}

/// <summary>
/// Node reference with a scope prefix (e.g., net::OUT, term::dp.M_P.D).
/// </summary>
public sealed class NodeRef
{
    /// <summary>Scope prefix (e.g., "net", "term", "port", "diff").</summary>
    public string Scope { get; init; } = string.Empty;

    /// <summary>Path within the scope (e.g., "OUT", "dp.M_P.D").</summary>
    public string Path { get; init; } = string.Empty;

    public override string ToString() => $"{Scope}::{Path}";
}

/// <summary>
/// Technology constraint expressing limits on device parameters.
/// </summary>
public sealed class TechConstraint
{
    /// <summary>Unique identifier for this constraint (e.g., "t_lmin").</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Device parameter being constrained (e.g., "L", "W").</summary>
    public string Param { get; init; } = string.Empty;

    /// <summary>Comparison operator: &gt;=, &lt;=, ==, &gt;, or &lt;.</summary>
    public string Op { get; init; } = string.Empty;

    /// <summary>Numeric bound for the constraint (e.g., "180n").</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>Physical unit for the value (e.g., "m" for meters).</summary>
    public string Unit { get; init; } = string.Empty;

    /// <summary>Scope of the constraint: "*" for all devices, a type selector, or an instance id.</summary>
    public string Scope { get; init; } = string.Empty;
}

/// <summary>
/// Graph constraint expressing structural properties of the circuit graph.
/// </summary>
public sealed class GraphConstraint
{
    /// <summary>Unique identifier for this constraint (e.g., "g_card_tail").</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Constraint rule expression (e.g., "cardinality", "path_exists", "fanout").</summary>
    public string Rule { get; init; } = string.Empty;

    /// <summary>Additional key-value properties for the rule (e.g., selector, bounds, endpoints).</summary>
    public Dictionary<string, string> Properties { get; init; } = new();
}

/// <summary>
/// Harness block.
/// </summary>
public sealed class HarnessBlock
{
    public List<SupplyValue> Supplies { get; init; } = new();
    public List<BiasValue> Biases { get; init; } = new();
    public List<SourceValue> Sources { get; init; } = new();
    public List<LoadValue> Loads { get; init; } = new();
    public List<SweepCondition> Sweeps { get; init; } = new();
    public IcmrRange? Icmr { get; init; }
    public List<string> Pvt { get; init; } = new();
}

/// <summary>
/// Supply value in harness.
/// </summary>
public sealed class SupplyValue
{
    public string Net { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

/// <summary>
/// Bias value in harness (DC voltage for bias ports).
/// </summary>
public sealed class BiasValue
{
    public string Net { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

/// <summary>
/// Source value in harness.
/// </summary>
public sealed class SourceValue
{
    public string Net { get; init; } = string.Empty;
    public string? Z { get; init; }
}

/// <summary>
/// A single load element (capacitor or resistor).
/// </summary>
public sealed record LoadElement(string Type, string Value);

/// <summary>
/// Load value in harness.
/// </summary>
public sealed class LoadValue
{
    public string Net { get; init; } = string.Empty;
    public List<LoadElement> Elements { get; init; } = new();
}

/// <summary>
/// ICMR range.
/// </summary>
public sealed class IcmrRange
{
    public string Min { get; init; } = string.Empty;
    public string Max { get; init; } = string.Empty;
}

/// <summary>
/// Sweep condition in harness (DC bias sweep range).
/// </summary>
public sealed class SweepCondition
{
    /// <summary>Sweep condition name (e.g., "InputDCBias", "InputDCCommonMode").</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Sweep start value (e.g., "0.3V").</summary>
    public string Start { get; init; } = string.Empty;

    /// <summary>Sweep stop value (e.g., "1.5V").</summary>
    public string Stop { get; init; } = string.Empty;

    /// <summary>Sweep step value (e.g., "100mV"). Null if auto-step or [Auto] was specified.</summary>
    public string? Step { get; init; }

    /// <summary>True if [Auto] was specified (must be resolved at EL level).</summary>
    public bool IsAuto { get; init; }
}

/// <summary>
/// Provenance block.
/// </summary>
public sealed class ProvenanceBlock
{
    public List<SourceReference> Sources { get; init; } = new();
    public List<string> Transforms { get; init; } = new();
    public Dictionary<string, string> Aliases { get; init; } = new();
}

/// <summary>
/// Source reference in provenance.
/// </summary>
public sealed class SourceReference
{
    public string File { get; init; } = string.Empty;
    public int? FromLine { get; init; }
    public int? ToLine { get; init; }
}

/// <summary>
/// Circuit parameter declaration with type and optional default.
/// </summary>
public sealed class CircuitParameter
{
    /// <summary>Parameter name.</summary>
    public required string Name { get; init; }

    /// <summary>Parameter type ("real", "int", or "bool").</summary>
    public required string Type { get; init; }

    /// <summary>Optional default value. If null, parameter is required at instantiation.</summary>
    public ParamValue? Default { get; init; }
}

/// <summary>
/// Trait definition declaring interface contract and connectors.
/// </summary>
public sealed class TraitDefinition
{
    /// <summary>Trait name.</summary>
    public required string Name { get; init; }

    /// <summary>Port declarations for this trait.</summary>
    public List<PortDeclaration> Ports { get; init; } = new();

    /// <summary>Connectors to other traits.</summary>
    public List<TraitConnector> Connectors { get; init; } = new();
}

/// <summary>
/// Connector from one trait to another, defining port mappings.
/// </summary>
public sealed class TraitConnector
{
    /// <summary>Target trait name (the trait being connected to).</summary>
    public required string TargetTrait { get; init; }

    /// <summary>Port mappings from source trait to target trait.</summary>
    public List<ConnectorMapping> Mappings { get; init; } = new();
}

/// <summary>
/// A single port-to-port mapping in a connector.
/// </summary>
public sealed record ConnectorMapping
{
    /// <summary>Source port (on the trait defining the connector).</summary>
    public required string SourcePort { get; init; }

    /// <summary>Target port (on the target trait).</summary>
    public required string TargetPort { get; init; }
}

/// <summary>
/// Attach statement for trait-based composition at EL level.
/// </summary>
public sealed record AttachStatement
{
    /// <summary>Source instance identifier.</summary>
    public required string SourceInstance { get; init; }

    /// <summary>Target instance identifiers (in chain order).</summary>
    public required IReadOnlyList<string> TargetInstances { get; init; }

    /// <summary>Connector reference in "TraitName::TargetTrait" format.</summary>
    public required string Via { get; init; }

    /// <summary>Optional anchor name for created nets (from "as" clause).</summary>
    public string? Anchor { get; init; }

    /// <summary>Optional inline override mappings.</summary>
    public IReadOnlyList<ConnectorMapping>? Overrides { get; init; }

    /// <inheritdoc />
    public bool Equals(AttachStatement? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return string.Equals(SourceInstance, other.SourceInstance, StringComparison.Ordinal)
            && string.Equals(Via, other.Via, StringComparison.Ordinal)
            && string.Equals(Anchor, other.Anchor, StringComparison.Ordinal)
            && TargetInstances.SequenceEqual(other.TargetInstances, StringComparer.Ordinal)
            && OverridesEqual(Overrides, other.Overrides);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SourceInstance, StringComparer.Ordinal);
        hash.Add(Via, StringComparer.Ordinal);
        hash.Add(Anchor, StringComparer.Ordinal);
        foreach (var target in TargetInstances)
        {
            hash.Add(target, StringComparer.Ordinal);
        }

        if (Overrides is not null)
        {
            foreach (var mapping in Overrides)
            {
                hash.Add(mapping);
            }
        }

        return hash.ToHashCode();
    }

    private static bool OverridesEqual(
        IReadOnlyList<ConnectorMapping>? left,
        IReadOnlyList<ConnectorMapping>? right
    )
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.SequenceEqual(right);
    }
}
