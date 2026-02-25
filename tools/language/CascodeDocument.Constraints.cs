using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language;

/// <summary>
/// Constraints block.
/// </summary>
public sealed class ConstraintsBlock
{
    /// <summary>Numeric constraints.</summary>
    public List<NumericConstraint> Numeric { get; init; } = new();

    /// <summary>Spec constraints evaluated against declared metrics.</summary>
    public List<SpecConstraint> Spec { get; init; } = new();

    /// <summary>Technology constraints.</summary>
    public List<TechConstraint> Tech { get; init; } = new();

    /// <summary>Graph constraints.</summary>
    public List<GraphConstraint> Graph { get; init; } = new();
}

/// <summary>
/// Spec constraint expressing a comparison over a declared metric.
/// </summary>
public sealed class SpecConstraint
{
    /// <summary>Unique identifier for this constraint.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Metric reference text (for example, "adc.Resolution" or "Resolution").</summary>
    public string MetricRef { get; init; } = string.Empty;

    /// <summary>Comparison operator: &gt;=, &lt;=, ==, &gt;, or &lt;.</summary>
    public string Op { get; init; } = string.Empty;

    /// <summary>Numeric bound for the constraint.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>Physical unit for the value.</summary>
    public string Unit { get; init; } = string.Empty;
}

/// <summary>
/// Numeric constraint expressing an inequality over a circuit metric with explicit units.
/// </summary>
public sealed class NumericConstraint
{
    /// <summary>Unique identifier for this constraint (e.g., "c_gbw").</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Base bench binding alias written by the user (e.g., "tran_bench").
    /// When no bench args are provided, this equals <see cref="Bench"/>.
    /// </summary>
    public string BenchBase { get; init; } = string.Empty;

    /// <summary>
    /// Bench invocation arguments (e.g., stim_freq=1kHz for tran_bench(stim_freq=1kHz)::...).
    /// These parameterize the bench instance, producing distinct testbench runs per arg-set.
    /// </summary>
    public List<MetricCallArg> BenchArgs { get; init; } = new();

    /// <summary>
    /// Computed bench instance name used for runtime matching and file naming.
    /// When <see cref="BenchArgs"/> is empty, equals <see cref="BenchBase"/>.
    /// Otherwise computed deterministically from BenchBase + BenchArgs.
    /// </summary>
    public string Bench { get; init; } = string.Empty;

    /// <summary>The metric being constrained (e.g., "GainBandwidth", "PhaseMargin").</summary>
    public string Metric { get; init; } = string.Empty;

    /// <summary>
    /// Optional metric invocation arguments (e.g., IntegratedInputNoise(from=10Hz, to=10MHz)).
    /// </summary>
    public List<MetricCallArg> MetricArgs { get; init; } = new();

    /// <summary>Optional node reference where the metric is measured.</summary>
    public NodeRef? Node { get; init; }

    /// <summary>Comparison operator: &gt;=, &lt;=, ==, &gt;, or &lt;.</summary>
    public string Op { get; init; } = string.Empty;

    /// <summary>Numeric bound for the constraint (e.g., "100M", "55").</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>Physical unit for the value (e.g., "Hz", "dB", "deg").</summary>
    public string Unit { get; init; } = string.Empty;
}

/// <summary>Named argument for a metric invocation within a numeric constraint.</summary>
public sealed record MetricCallArg(string Name, string Value);

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
    public List<GroundValue> Grounds { get; init; } = new();
    public List<SupplyValue> Supplies { get; init; } = new();
    public List<BiasValue> Biases { get; init; } = new();
    public List<SourceValue> Sources { get; init; } = new();
    public List<LoadValue> Loads { get; init; } = new();
    public List<SweepCondition> Sweeps { get; init; } = new();
    public IcmrRange? Icmr { get; init; }
    public List<string> Pvt { get; init; } = new();
}

/// <summary>
/// Ground reference value in harness.
/// </summary>
public sealed class GroundValue
{
    public string Net { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
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
/// Interface definition declaring interface contract and connectors.
/// </summary>
public sealed class TraitDefinition
{
    /// <summary>Interface name.</summary>
    public required string Name { get; init; }

    /// <summary>Port declarations for this interface.</summary>
    public List<PortDeclaration> Ports { get; init; } = new();

    /// <summary>Connectors to other interfaces.</summary>
    public List<TraitConnector> Connectors { get; init; } = new();

    /// <summary>Metric contracts declared by the interface.</summary>
    public List<MetricContract> Metrics { get; init; } = new();

    /// <summary>Bench bindings declared on this interface.</summary>
    public List<BenchBinding> BenchBindings { get; init; } = new();
}

/// <summary>
/// Connector from one interface to another, defining port mappings.
/// </summary>
public sealed class TraitConnector
{
    /// <summary>Target interface name (the interface being connected to).</summary>
    public required string TargetTrait { get; init; }

    /// <summary>Port mappings from source interface to target interface.</summary>
    public List<ConnectorMapping> Mappings { get; init; } = new();
}

/// <summary>
/// A single port-to-port mapping in a connector.
/// </summary>
public sealed record ConnectorMapping
{
    /// <summary>Source port (on the interface defining the connector).</summary>
    public required string SourcePort { get; init; }

    /// <summary>Target port (on the target interface).</summary>
    public required string TargetPort { get; init; }
}

/// <summary>
/// Attach statement for interface-based composition at EL level.
/// </summary>
public sealed record AttachStatement
{
    /// <summary>Source instance identifier.</summary>
    public required string SourceInstance { get; init; }

    /// <summary>Target instance identifiers (in chain order).</summary>
    public required IReadOnlyList<string> TargetInstances { get; init; }

    /// <summary>Connector reference in "InterfaceName::TargetInterface" format.</summary>
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
