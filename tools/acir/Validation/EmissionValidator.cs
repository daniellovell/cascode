using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.ACIR.Validation;

/// <summary>
/// Validates ACIR circuits for emission-blocking issues that prevent SPICE generation.
/// </summary>
/// <remarks>
/// Emission-blocking rules:
/// - EMIT-001: Missing terminal binding (D, G, S, B for MOSFETs; P, N for passives)
/// - EMIT-002: Invalid net reference (terminal references non-existent net)
/// - EMIT-003: Missing required parameter (W, L for transistors; R for resistors)
/// - EMIT-004: Unknown device type
/// - EMIT-005: Non-EL level circuit
/// - EMIT-006: Unresolved [Auto] sweep at EL level
/// </remarks>
public static class EmissionValidator
{
    private static readonly Dictionary<string, string[]> RequiredTerminals = new(StringComparer.OrdinalIgnoreCase)
    {
        { "nmos", new[] { "D", "G", "S", "B" } },
        { "pmos", new[] { "D", "G", "S", "B" } },
        { "resistor", new[] { "P", "N" } },
        { "capacitor", new[] { "P", "N" } },
        { "inductor", new[] { "P", "N" } },
        { "diode", new[] { "A", "K" } }
    };

    private static readonly Dictionary<string, string[]> RequiredParams = new(StringComparer.OrdinalIgnoreCase)
    {
        { "nmos", new[] { "W", "L" } },
        { "pmos", new[] { "W", "L" } },
        { "resistor", new[] { "R" } },
        { "capacitor", new[] { "C" } },
        { "inductor", new[] { "L" } }
    };

    private static readonly HashSet<string> KnownDeviceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "nmos", "pmos", "resistor", "capacitor", "inductor", "diode"
    };

    /// <summary>
    /// Validates a circuit for emission-blocking issues.
    /// </summary>
    /// <param name="circuit">The circuit to validate.</param>
    /// <returns>Validation result with any errors found.</returns>
    public static ValidationResult Validate(Circuit circuit)
    {
        ArgumentNullException.ThrowIfNull(circuit);

        var result = new ValidationResult();

        // EMIT-005: Level check
        if (circuit.Level != ACIRLevel.EL)
        {
            result.AddError(
                "EMIT-005",
                $"Circuit '{circuit.Name}' is at {circuit.Level} level, but SPICE emission requires EL level",
                $"circuit {circuit.Name}",
                "Elaborate the circuit to EL level before emission");
            return result; // Cannot validate further if not EL
        }

        // EMIT-006: Unresolved Auto sweep at EL level
        if (circuit.Harness?.Sweeps != null)
        {
            foreach (var sweep in circuit.Harness.Sweeps)
            {
                if (sweep.IsAuto)
                {
                    result.AddError(
                        "EMIT-006",
                        $"Sweep condition '{sweep.Name}' contains unresolved [Auto] at EL level",
                        "harness",
                        "Resolve [Auto] to concrete numeric values during elaboration");
                }
            }
        }

        // Build set of valid nets from all sources
        var validNets = BuildValidNetSet(circuit);

        // Validate devices if present
        if (circuit.Fill?.Devices != null)
        {
            foreach (var device in circuit.Fill.Devices)
            {
                ValidateDevice(device, validNets, result);
            }
        }

        return result;
    }

    /// <summary>
    /// Builds a set of all valid net names in the circuit.
    /// </summary>
    private static HashSet<string> BuildValidNetSet(Circuit circuit)
    {
        var nets = new HashSet<string>(StringComparer.Ordinal);

        // Add ports
        foreach (var port in circuit.Ports)
        {
            nets.Add(port.Name);
        }

        // Add supplies
        foreach (var supply in circuit.Supplies)
        {
            nets.Add(supply);
        }

        // Add grounds
        foreach (var ground in circuit.Grounds)
        {
            nets.Add(ground);
        }

        // Add internal nets from fill block
        if (circuit.Fill?.Nets != null)
        {
            foreach (var net in circuit.Fill.Nets)
            {
                nets.Add(net.Id);
            }
        }

        return nets;
    }

    /// <summary>
    /// Validates a single device declaration.
    /// </summary>
    private static void ValidateDevice(DeviceDeclaration device, HashSet<string> validNets, ValidationResult result)
    {
        var deviceType = device.DeviceType.ToLowerInvariant();

        // EMIT-004: Unknown device type
        if (!KnownDeviceTypes.Contains(deviceType))
        {
            result.AddError(
                "EMIT-004",
                $"Unknown device type '{device.DeviceType}'",
                $"device {device.Id}",
                $"Use one of: {string.Join(", ", KnownDeviceTypes)}");
            return; // Cannot validate terminals/params for unknown type
        }

        // EMIT-001: Missing terminal bindings
        if (RequiredTerminals.TryGetValue(deviceType, out var terminals))
        {
            foreach (var terminal in terminals)
            {
                if (!device.Bindings.ContainsKey(terminal))
                {
                    var suggestion = deviceType switch
                    {
                        "nmos" => terminal == "B" ? "Add bulk connection, typically B->GND for NMOS" : $"Add {terminal} terminal binding",
                        "pmos" => terminal == "B" ? "Add bulk connection, typically B->VDD for PMOS" : $"Add {terminal} terminal binding",
                        _ => $"Add {terminal} terminal binding"
                    };

                    result.AddError(
                        "EMIT-001",
                        $"Device '{device.Id}' missing required terminal '{terminal}'",
                        $"device {device.Id}",
                        suggestion);
                }
            }
        }

        // EMIT-002: Invalid net references
        foreach (var (terminal, netName) in device.Bindings)
        {
            if (!validNets.Contains(netName))
            {
                var availableNets = validNets.Take(8).ToList();
                var netList = availableNets.Count < validNets.Count
                    ? string.Join(", ", availableNets) + "..."
                    : string.Join(", ", availableNets);

                result.AddError(
                    "EMIT-002",
                    $"Device '{device.Id}' terminal '{terminal}' references undefined net '{netName}'",
                    $"device {device.Id}.{terminal} -> {netName}",
                    $"Available nets: {netList}");
            }
        }

        // EMIT-003: Missing required parameters
        if (RequiredParams.TryGetValue(deviceType, out var requiredParams))
        {
            foreach (var param in requiredParams)
            {
                if (!device.Params.ContainsKey(param))
                {
                    var example = param switch
                    {
                        "W" => "W=1u",
                        "L" => "L=180n",
                        "R" => "R=10k",
                        "C" => "C=1p",
                        _ => $"{param}=<value>"
                    };

                    result.AddError(
                        "EMIT-003",
                        $"Device '{device.Id}' missing required parameter '{param}'",
                        $"device {device.Id}",
                        $"Add {param} parameter, e.g., {example}");
                }
            }
        }
    }
}
