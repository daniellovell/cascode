using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language;

/// <summary>
/// Root Cascode document representing one or more circuits at a specific elaboration level.
/// </summary>
public sealed partial class CascodeDocument
{
    /// <summary>
    /// Cascode format major version.
    /// </summary>
    public int VersionMajor { get; init; } = 1;

    /// <summary>
    /// Cascode format minor version.
    /// </summary>
    public int VersionMinor { get; init; } = 0;

    /// <summary>
    /// Include directives present in the parsed source document.
    /// Linked documents must not contain any includes.
    /// </summary>
    public List<IncludeDirective> Includes { get; init; } = new();

    /// <summary>
    /// File-level library namespace declared via <c>library ...</c>.
    /// <para />
    /// This is source-file metadata; linked documents typically combine multiple libraries and
    /// should leave this unset.
    /// </summary>
    public string? FileLibrary { get; init; }

    /// <summary>
    /// File-level helper functions (available to benches and other functions once linked).
    /// </summary>
    public List<FunctionDefinition> Functions { get; init; } = new();

    /// <summary>
    /// Bundle type definitions declared at the file level.
    /// </summary>
    public List<BundleType> BundleTypes { get; init; } = new();

    /// <summary>
    /// Trait definitions declared at the file level (after bundles, before circuits).
    /// </summary>
    public List<TraitDefinition> Traits { get; init; } = new();

    /// <summary>
    /// Bench definitions declared at the file level.
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
    /// Builds a dictionary of bundle types by name from an Cascode document.
    /// Returns an empty dictionary if document is null.
    /// </summary>
    public static Dictionary<string, BundleType> GetBundlesByName(CascodeDocument? document)
    {
        return document?.BundleTypes.ToDictionary(b => b.Name, StringComparer.Ordinal)
            ?? new Dictionary<string, BundleType>(StringComparer.Ordinal);
    }
}

/// <summary>
/// Represents a circuit definition in Cascode.
/// </summary>
public sealed class Circuit
{
    /// <summary>Circuit name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Optional interfaces this circuit implements.</summary>
    public List<string>? Traits { get; init; }

    /// <summary>Elaboration level (HL, ML, or EL).</summary>
    public CascodeLevel Level { get; init; } = CascodeLevel.ML;

    /// <summary>
    /// Whether this circuit should be inlined during SPICE emission.
    /// When true, devices and nets merge into parent with hierarchical naming.
    /// </summary>
    public bool Inline { get; init; }

    /// <summary>Optional library path.</summary>
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

    /// <summary>Slot block (HL level only). Null means no slot; empty block means bare <c>slot</c>.</summary>
    public SlotBlock? Slot { get; init; }

    /// <summary>Fill block content (ML and EL levels).</summary>
    public FillBlock? Fill { get; init; }

    /// <summary>Constraints block.</summary>
    public ConstraintsBlock? Constraints { get; init; }

    /// <summary>Harness block.</summary>
    public HarnessBlock? Harness { get; init; }

    /// <summary>Environment block describing operating intent (used by benches).</summary>
    public EnvBlock? Env { get; init; }

    /// <summary>Render-intent block containing sparse schematic overrides.</summary>
    public RenderBlock? Render { get; set; }

    /// <summary>Bench bindings declared on the circuit (override/extend interface benches).</summary>
    public List<BenchBinding> BenchBindings { get; init; } = new();

    /// <summary>
    /// Bench binding extensions declared on the circuit (adds statements to inherited/circuit bindings).
    /// </summary>
    public List<BenchBindingExtension> BenchBindingExtensions { get; init; } = new();

    /// <summary>Synthesis guidance (extracted to sidecar during linking).</summary>
    public SynthBlock? Synth { get; init; }

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
    /// Converts a <see cref="PortDirection"/> value to its canonical Cascode string representation.
    /// </summary>
    public static string ToCascodeString(this PortDirection direction) =>
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
/// Slot block at HL level. An empty block represents a bare <c>slot</c> marker;
/// a populated block represents a composition slot with nets, instances, and connections.
/// </summary>
public sealed class SlotBlock
{
    public List<NetDeclaration> Nets { get; init; } = new();
    public List<InstanceDeclaration> Instances { get; init; } = new();
    public List<ConnectionStatement> Connections { get; init; } = new();
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

    /// <summary>Attach statements (EL level, for interface-based composition).</summary>
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

    /// <summary>Declared instance type from source (for example <c>Some</c> in slot blocks).</summary>
    public string? DeclaredType { get; init; }

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
