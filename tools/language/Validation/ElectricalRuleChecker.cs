using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language.Validation;

/// <summary>
/// Performs electrical rule checking (ERC) on Cascode circuits to detect invalid circuit topologies.
/// </summary>
/// <remarks>
/// ERC rules detect electrically invalid circuits that can technically be emitted
/// but will fail simulation or produce meaningless results:
/// - ERC-001: Floating gate (MOSFET gate not driven)
/// - ERC-002: VDD-GND short (MOSFET spanning supply rails)
/// - ERC-003: Rail conflict (supply/ground mapped to multiple nets)
/// - ERC-004: Dangling net (internal net with no connections)
/// - ERC-005: Missing PDK device (EL device without PDK model - warning)
/// - ERC-007: Passive rail bridge (R/L/C directly between VDD and GND)
/// </remarks>
public static class ElectricalRuleChecker
{
    /// <summary>
    /// Performs complete-document ERC, including semantic contract validation.
    /// </summary>
    public static ValidationResult Check(CascodeDocument document, bool requirePdkDevice = false)
    {
        ArgumentNullException.ThrowIfNull(document);

        var result = CompleteDocumentSemanticValidator.Validate(document);
        if (!result.IsValid)
        {
            return result;
        }

        var circuits = document.Circuits.Where(c => c.Level is CascodeLevel.EL or CascodeLevel.ML);
        foreach (var circuit in circuits)
        {
            result.Merge(Check(circuit, document, requirePdkDevice));
        }

        return result;
    }

    /// <summary>
    /// Performs electrical rule checking on a circuit.
    /// </summary>
    /// <param name="circuit">The circuit to check (must be desugared).</param>
    /// <param name="requirePdkDevice">If true, missing PDK device is an error; otherwise a warning.</param>
    /// <returns>Validation result with any ERC violations found.</returns>
    public static ValidationResult Check(Circuit circuit, bool requirePdkDevice = false)
    {
        return Check(circuit, null, requirePdkDevice);
    }

    /// <summary>
    /// Performs electrical rule checking on a circuit with optional document context.
    /// </summary>
    /// <param name="circuit">The circuit to check (must be desugared).</param>
    /// <param name="document">Optional document for resolving primitives.</param>
    /// <param name="requirePdkDevice">If true, missing PDK device is an error; otherwise a warning.</param>
    /// <returns>Validation result with any ERC violations found.</returns>
    public static ValidationResult Check(
        Circuit circuit,
        CascodeDocument? document,
        bool requirePdkDevice = false
    )
    {
        ArgumentNullException.ThrowIfNull(circuit);

        var result = new ValidationResult();

        // For EL-level circuits, run emission validation as prerequisite.
        // For ML-level circuits, skip emission validation since they are not expected
        // to be emission-ready (topology is complete but sizing uses ?? placeholders).
        if (circuit.Level == CascodeLevel.EL)
        {
            var emitResult = EmissionValidator.Validate(circuit, document);
            if (!emitResult.IsValid)
            {
                result.Merge(emitResult);
                return result; // Cannot run ERC on structurally invalid circuit
            }
        }

        // Build circuit analysis context
        var analysis = new CircuitAnalysis(circuit);

        // Run ERC checks (these are topology-based and work on both EL and ML)
        CheckFloatingGates(circuit, analysis, result);
        CheckVddGndShorts(circuit, analysis, result);
        CheckPassiveShorts(circuit, analysis, result);
        CheckRailUniqueness(circuit, result);
        CheckDanglingNets(circuit, analysis, result);
        CheckPdkDevice(circuit, document, result, requirePdkDevice);

        return result;
    }

    /// <summary>
    /// ERC-001: Check for floating gates (MOSFET gates not connected to driven nets).
    /// </summary>
    private static void CheckFloatingGates(
        Circuit circuit,
        CircuitAnalysis analysis,
        ValidationResult result
    )
    {
        if (circuit.Fill?.Devices == null)
            return;

        foreach (var device in circuit.Fill.Devices)
        {
            var deviceType = device.DeviceType.ToLowerInvariant();
            if (deviceType is not ("nmos" or "pmos"))
                continue;

            if (!device.Bindings.TryGetValue("G", out var gateNet))
            {
                result.AddError(
                    "ERC-001",
                    $"Missing gate binding on device {device.Id}",
                    $"{device.Id}.G",
                    "MOSFET gate terminal must be connected"
                );
                continue;
            }

            if (!analysis.IsDrivenNet(gateNet))
            {
                result.AddError(
                    "ERC-001",
                    $"Floating gate on device {device.Id}",
                    $"{device.Id}.G--{gateNet}",
                    "Connect gate to a driven net (port, supply, or device output)"
                );
            }
        }
    }

    /// <summary>
    /// ERC-002: Check for VDD-GND shorts (device with drain on VDD and source on GND or vice versa).
    /// </summary>
    private static void CheckVddGndShorts(
        Circuit circuit,
        CircuitAnalysis analysis,
        ValidationResult result
    )
    {
        if (circuit.Fill?.Devices == null)
            return;

        foreach (var device in circuit.Fill.Devices)
        {
            var deviceType = device.DeviceType.ToLowerInvariant();
            if (deviceType is not ("nmos" or "pmos"))
                continue;

            var drain = device.Bindings.GetValueOrDefault("D");
            var source = device.Bindings.GetValueOrDefault("S");

            if (drain == null || source == null)
                continue;

            var drainIsSupply = analysis.IsSupply(drain);
            var drainIsGround = analysis.IsGround(drain);
            var sourceIsSupply = analysis.IsSupply(source);
            var sourceIsGround = analysis.IsGround(source);

            if ((drainIsSupply && sourceIsGround) || (drainIsGround && sourceIsSupply))
            {
                result.AddError(
                    "ERC-002",
                    $"VDD-GND short through device {device.Id}",
                    $"{device.Id} (D--{drain}, S--{source})",
                    "Check device connectivity - drain and source cannot span supply rails directly"
                );
            }
        }
    }

    /// <summary>
    /// ERC-007: Check for passive devices bridging supply rails (R/L/C between VDD and GND).
    /// </summary>
    private static void CheckPassiveShorts(
        Circuit circuit,
        CircuitAnalysis analysis,
        ValidationResult result
    )
    {
        if (circuit.Fill?.Devices == null)
            return;

        foreach (var device in circuit.Fill.Devices)
        {
            var deviceType = device.DeviceType.ToLowerInvariant();
            if (deviceType is not ("resistor" or "inductor" or "capacitor"))
                continue;

            var p = device.Bindings.GetValueOrDefault("P");
            var n = device.Bindings.GetValueOrDefault("N");

            if (p == null || n == null)
                continue;

            var pIsSupply = analysis.IsSupply(p);
            var pIsGround = analysis.IsGround(p);
            var nIsSupply = analysis.IsSupply(n);
            var nIsGround = analysis.IsGround(n);

            if ((pIsSupply && nIsGround) || (pIsGround && nIsSupply))
            {
                var deviceTypeName = deviceType switch
                {
                    "resistor" => "Resistor",
                    "inductor" => "Inductor",
                    "capacitor" => "Capacitor",
                    _ => "Passive device",
                };

                result.AddError(
                    "ERC-007",
                    $"{deviceTypeName} '{device.Id}' bridges supply rails",
                    $"{device.Id} (P--{p}, N--{n})",
                    "This creates a direct path between VDD and GND"
                );
            }
        }
    }

    /// <summary>
    /// ERC-003: Check for rail uniqueness (each supply/ground name should map to one net).
    /// </summary>
    private static void CheckRailUniqueness(Circuit circuit, ValidationResult result)
    {
        // Check for duplicate supply declarations
        var supplyDuplicates = circuit
            .Supplies.GroupBy(s => s, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        foreach (var dup in supplyDuplicates)
        {
            result.AddError(
                "ERC-003",
                $"Supply '{dup}' is declared multiple times",
                $"supply {dup}",
                "Remove duplicate supply declaration"
            );
        }

        // Check for duplicate ground declarations
        var groundDuplicates = circuit
            .Grounds.GroupBy(g => g, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        foreach (var dup in groundDuplicates)
        {
            result.AddError(
                "ERC-003",
                $"Ground '{dup}' is declared multiple times",
                $"ground {dup}",
                "Remove duplicate ground declaration"
            );
        }

        // Check for supply/ground name collision
        var collisions = circuit.Supplies.Intersect(circuit.Grounds, StringComparer.Ordinal);
        foreach (var collision in collisions)
        {
            result.AddError(
                "ERC-003",
                $"'{collision}' is declared as both supply and ground",
                collision,
                "A net cannot be both supply and ground"
            );
        }
    }

    /// <summary>
    /// ERC-004: Check for dangling nets (internal nets with no device connections).
    /// </summary>
    private static void CheckDanglingNets(
        Circuit circuit,
        CircuitAnalysis analysis,
        ValidationResult result
    )
    {
        if (circuit.Fill?.Nets == null)
            return;

        foreach (var net in circuit.Fill.Nets)
        {
            if (!analysis.HasConnections(net.Id))
            {
                // Check if net is referenced in harness (which is allowed)
                if (IsReferencedInHarness(circuit, net.Id))
                {
                    continue;
                }

                result.AddWarning(
                    "ERC-004",
                    $"Dangling net '{net.Id}' has no device connections",
                    $"net {net.Id}",
                    "Remove unused net or connect it to a device"
                );
            }
        }
    }

    private static readonly HashSet<string> GenericMosfetModels = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "nmos",
        "pmos",
        "nmos_level1",
        "pmos_level1",
    };

    /// <summary>
    /// ERC-005: Check for missing PDK device names at EL level.
    /// </summary>
    private static void CheckPdkDevice(
        Circuit circuit,
        CascodeDocument? document,
        ValidationResult result,
        bool requirePdkDevice
    )
    {
        if (circuit.Level != CascodeLevel.EL)
            return;
        if (circuit.Fill?.Devices == null)
            return;

        IReadOnlyDictionary<string, PrimitiveDefinition>? primitives = null;
        if (document is not null)
        {
            primitives = document.Primitives.ToDictionary(p => p.Name, StringComparer.Ordinal);
        }

        foreach (var device in circuit.Fill.Devices)
        {
            var deviceType = device.DeviceType.ToLowerInvariant();
            if (deviceType is not ("nmos" or "pmos"))
                continue;

            // Device is missing PDK name or using generic name (nmos/pmos)
            var modelName =
                primitives is not null
                && primitives.TryGetValue(device.Primitive, out var primitive)
                    ? primitive.Device
                    : device.DeviceType;

            var isGenericOrMissing =
                string.IsNullOrEmpty(modelName) || GenericMosfetModels.Contains(modelName);

            if (isGenericOrMissing)
            {
                if (requirePdkDevice)
                {
                    result.AddError(
                        "ERC-005",
                        $"Device '{device.Id}' using generic model '{modelName}' instead of PDK device",
                        $"device {device.Id}",
                        "Add PDK device name, e.g., sky130_fd_pr__nfet_01v8"
                    );
                }
                else
                {
                    result.AddWarning(
                        "ERC-005",
                        $"Device '{device.Id}' using generic model '{modelName}' instead of PDK device",
                        $"device {device.Id}",
                        "Consider specifying PDK device name for accurate simulation"
                    );
                }
            }
        }
    }

    /// <summary>
    /// Checks if a net is referenced in the harness block.
    /// </summary>
    private static bool IsReferencedInHarness(Circuit circuit, string netId)
    {
        if (circuit.Harness == null)
            return false;

        // Check supplies
        if (circuit.Harness.Supplies.Any(s => s.Net == netId))
            return true;

        // Check biases
        if (circuit.Harness.Biases.Any(b => b.Net == netId))
            return true;

        // Check sources
        if (circuit.Harness.Sources.Any(s => s.Net == netId))
            return true;

        // Check loads
        if (circuit.Harness.Loads.Any(l => l.Net == netId))
            return true;

        return false;
    }

    /// <summary>
    /// Internal class for analyzing circuit connectivity.
    /// </summary>
    private sealed class CircuitAnalysis
    {
        private readonly HashSet<string> _ports;
        private readonly HashSet<string> _supplies;
        private readonly HashSet<string> _grounds;
        private readonly HashSet<string> _drivenNets;
        private readonly Dictionary<string, int> _netConnectionCount;

        public CircuitAnalysis(Circuit circuit)
        {
            _ports = circuit.Ports.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            _supplies = circuit.Supplies.ToHashSet(StringComparer.Ordinal);
            _grounds = circuit.Grounds.ToHashSet(StringComparer.Ordinal);
            _drivenNets = new HashSet<string>(StringComparer.Ordinal);
            _netConnectionCount = new Dictionary<string, int>(StringComparer.Ordinal);

            // Analyze device connections
            if (circuit.Fill?.Devices != null)
            {
                foreach (var device in circuit.Fill.Devices)
                {
                    // Count connections to each net
                    foreach (var (_, netName) in device.Bindings)
                    {
                        if (!_netConnectionCount.TryGetValue(netName, out var count))
                        {
                            count = 0;
                        }
                        _netConnectionCount[netName] = count + 1;
                    }

                    // Mark output nets as driven
                    var deviceType = device.DeviceType.ToLowerInvariant();
                    if (deviceType is "nmos" or "pmos")
                    {
                        // Drain is output for MOSFETs
                        if (device.Bindings.TryGetValue("D", out var drainNet))
                        {
                            _drivenNets.Add(drainNet);
                        }
                    }
                    else if (deviceType is "resistor" or "capacitor" or "inductor")
                    {
                        // Both terminals can drive for passives
                        if (device.Bindings.TryGetValue("P", out var pNet))
                        {
                            _drivenNets.Add(pNet);
                        }
                        if (device.Bindings.TryGetValue("N", out var nNet))
                        {
                            _drivenNets.Add(nNet);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// A net is "driven" if it's a port, supply, ground, or connected to a device output.
        /// </summary>
        public bool IsDrivenNet(string netName)
        {
            return _ports.Contains(netName)
                || _supplies.Contains(netName)
                || _grounds.Contains(netName)
                || _drivenNets.Contains(netName);
        }

        /// <summary>
        /// Checks if a net is a supply rail.
        /// </summary>
        public bool IsSupply(string netName) => _supplies.Contains(netName);

        /// <summary>
        /// Checks if a net is a ground rail.
        /// </summary>
        public bool IsGround(string netName) => _grounds.Contains(netName);

        /// <summary>
        /// Checks if a net has any device connections.
        /// </summary>
        public bool HasConnections(string netName) => _netConnectionCount.ContainsKey(netName);
    }
}
