using System;
using System.Collections.Generic;
using System.Linq;
using Cascode.Language;

namespace Cascode.Language.Validation;

/// <summary>
/// Validates ACIR circuits for emission-blocking issues that prevent SPICE generation.
/// </summary>
/// <remarks>
/// Emission-blocking rules:
/// - EMIT-001: Missing terminal binding (D, G, S, B for MOSFETs; P, N for passives)
/// - EMIT-002: Invalid net reference (terminal references non-existent net)
/// - EMIT-003: Missing required parameter (R for resistors; C for capacitors; L for inductors)
/// - EMIT-004: Unknown device type
/// - EMIT-005: Non-EL level circuit
/// - EMIT-006: Unresolved [Auto] sweep at EL level
/// - EMIT-007: Missing size reference for MOSFETs (nmos/pmos must use size(...))
/// - HARN-001: Harness source direction mismatch or unknown port
/// - HARN-002: Harness load direction mismatch or unknown port
/// - HARN-003: Harness bias target mismatch or unknown terminal
/// </remarks>
public static class EmissionValidator
{
    private static readonly Dictionary<string, string[]> RequiredTerminals = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        { "nmos", new[] { "D", "G", "S", "B" } },
        { "pmos", new[] { "D", "G", "S", "B" } },
        { "resistor", new[] { "P", "N" } },
        { "capacitor", new[] { "P", "N" } },
        { "inductor", new[] { "P", "N" } },
        { "diode", new[] { "A", "K" } },
    };

    private static readonly Dictionary<string, string[]> RequiredParams = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        { "resistor", new[] { "R" } },
        { "capacitor", new[] { "C" } },
        { "inductor", new[] { "L" } },
    };

    private static readonly HashSet<string> KnownDeviceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "nmos",
        "pmos",
        "resistor",
        "capacitor",
        "inductor",
        "diode",
    };

    /// <summary>
    /// Validates a circuit for emission-blocking issues.
    /// </summary>
    /// <param name="circuit">The circuit to validate (must be desugared).</param>
    /// <param name="document">Optional document for resolving primitives.</param>
    /// <returns>Validation result with any errors found.</returns>
    public static ValidationResult Validate(Circuit circuit, ACIRDocument? document = null)
    {
        ArgumentNullException.ThrowIfNull(circuit);

        var result = new ValidationResult();
        var bundleAliases = BuildBundleAliasMap(circuit);

        // EMIT-005: Level check
        if (circuit.Level != ACIRLevel.EL)
        {
            result.AddError(
                "EMIT-005",
                $"Circuit '{circuit.Name}' is at {circuit.Level} level, but SPICE emission requires EL level",
                $"circuit {circuit.Name}",
                "Elaborate the circuit to EL level before emission"
            );
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
                        "Resolve [Auto] to concrete numeric values during elaboration"
                    );
                }
            }
        }

        ValidateHarnessIntent(circuit, bundleAliases, result);

        // Build set of valid nets from all sources (ports are already desugared)
        var validNets = BuildValidNetSet(circuit);
        ValidateInstanceBindings(circuit.Fill?.Instances, validNets, bundleAliases, result);

        // Build set of valid size pack names
        var validSizes = circuit.Sizes.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var sizeDefaults = circuit
            .Sizes.Where(s => s.Default is not null)
            .ToDictionary(s => s.Name, s => s.Default!, StringComparer.Ordinal);
        if (circuit.Fill?.Sizes is { Count: > 0 })
        {
            foreach (var size in circuit.Fill.Sizes)
            {
                validSizes.Add(size.Name);
                if (size.Default is not null)
                {
                    sizeDefaults[size.Name] = size.Default;
                }
            }
        }

        var primitivesByName = document?.Primitives.ToDictionary(
            p => p.Name,
            StringComparer.Ordinal
        );

        // Validate devices if present
        if (circuit.Fill?.Devices != null)
        {
            foreach (var device in circuit.Fill.Devices)
            {
                ValidateDevice(
                    device,
                    validNets,
                    validSizes,
                    sizeDefaults,
                    primitivesByName,
                    bundleAliases,
                    result
                );
            }
        }

        return result;
    }

    private static void ValidateHarnessIntent(
        Circuit circuit,
        IReadOnlyDictionary<string, string> bundleAliases,
        ValidationResult result
    )
    {
        if (circuit.Harness == null)
        {
            return;
        }

        var portsByName = new Dictionary<string, PortDeclaration>(StringComparer.Ordinal);
        foreach (var port in circuit.Ports)
        {
            // If duplicates exist, keep the first definition to avoid cascading exceptions.
            portsByName.TryAdd(port.Name, port);
        }
        var supplies = circuit.Supplies.ToHashSet(StringComparer.Ordinal);
        var grounds = circuit.Grounds.ToHashSet(StringComparer.Ordinal);

        foreach (var source in circuit.Harness.Sources)
        {
            if (!portsByName.TryGetValue(source.Net, out var port))
            {
                if (bundleAliases.TryGetValue(source.Net, out var dotted))
                {
                    result.AddError(
                        "HARN-001",
                        $"Harness source references '{source.Net}', but bundle field '{dotted}' must use dot notation",
                        $"harness source {source.Net}",
                        $"Use '{dotted}' instead of '{source.Net}' for bundle field access"
                    );
                    continue;
                }

                result.AddError(
                    "HARN-001",
                    $"Harness source references unknown port '{source.Net}'",
                    $"harness source {source.Net}",
                    "Update the harness source target to a declared port"
                );
                continue;
            }

            if (port.Direction is not (PortDirection.Input or PortDirection.Io))
            {
                result.AddError(
                    "HARN-001",
                    $"Harness source '{source.Net}' must reference an input or io port, but '{source.Net}' is declared as {port.Direction.ToAcirString()}",
                    $"harness source {source.Net}",
                    "Update the harness source target or change the port direction"
                );
            }
        }

        foreach (var load in circuit.Harness.Loads)
        {
            if (!portsByName.TryGetValue(load.Net, out var port))
            {
                if (bundleAliases.TryGetValue(load.Net, out var dotted))
                {
                    result.AddError(
                        "HARN-002",
                        $"Harness load references '{load.Net}', but bundle field '{dotted}' must use dot notation",
                        $"harness load {load.Net}",
                        $"Use '{dotted}' instead of '{load.Net}' for bundle field access"
                    );
                    continue;
                }

                result.AddError(
                    "HARN-002",
                    $"Harness load references unknown port '{load.Net}'",
                    $"harness load {load.Net}",
                    "Update the harness load target to a declared port"
                );
                continue;
            }

            if (port.Direction is not (PortDirection.Output or PortDirection.Io))
            {
                result.AddError(
                    "HARN-002",
                    $"Harness load '{load.Net}' must reference an output or io port, but '{load.Net}' is declared as {port.Direction.ToAcirString()}",
                    $"harness load {load.Net}",
                    "Update the harness load target or change the port direction"
                );
            }
        }

        foreach (var bias in circuit.Harness.Biases)
        {
            if (supplies.Contains(bias.Net) || grounds.Contains(bias.Net))
            {
                continue;
            }

            if (!portsByName.TryGetValue(bias.Net, out var port))
            {
                if (bundleAliases.TryGetValue(bias.Net, out var dotted))
                {
                    result.AddError(
                        "HARN-003",
                        $"Harness bias references '{bias.Net}', but bundle field '{dotted}' must use dot notation",
                        $"harness bias {bias.Net}",
                        $"Use '{dotted}' instead of '{bias.Net}' for bundle field access"
                    );
                    continue;
                }

                result.AddError(
                    "HARN-003",
                    $"Harness bias references unknown terminal '{bias.Net}'",
                    $"harness bias {bias.Net}",
                    "Update the harness bias target to a declared bias port, supply, or ground"
                );
                continue;
            }

            if (!string.Equals(port.Type, "bias", StringComparison.OrdinalIgnoreCase))
            {
                result.AddError(
                    "HARN-003",
                    $"Harness bias '{bias.Net}' must reference a bias-domain port, supply, or ground, but '{bias.Net}' is of type '{port.Type}'",
                    $"harness bias {bias.Net}",
                    "Change the port domain to bias or update the harness bias target"
                );
                continue;
            }

            if (port.Direction is not (PortDirection.Input or PortDirection.Io))
            {
                result.AddError(
                    "HARN-003",
                    $"Harness bias '{bias.Net}' must reference an input or io bias port, but '{bias.Net}' is declared as {port.Direction.ToAcirString()}",
                    $"harness bias {bias.Net}",
                    "Update the harness bias target or change the port direction"
                );
            }
        }
    }

    /// <summary>
    /// Builds a set of all valid net names in the circuit.
    /// </summary>
    private static HashSet<string> BuildValidNetSet(Circuit circuit)
    {
        var nets = new HashSet<string>(StringComparer.Ordinal);

        // Add ports (already desugared with dot notation preserved)
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

    private static Dictionary<string, string> BuildBundleAliasMap(Circuit circuit)
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var port in circuit.Ports)
        {
            if (!port.Name.Contains('.'))
            {
                continue;
            }

            var alias = port.Name.Replace('.', '_');
            aliases.TryAdd(alias, port.Name);
        }

        return aliases;
    }

    private static void ValidateInstanceBindings(
        IReadOnlyList<InstanceDeclaration>? instances,
        HashSet<string> validNets,
        IReadOnlyDictionary<string, string> bundleAliases,
        ValidationResult result
    )
    {
        if (instances is null)
        {
            return;
        }

        foreach (var instance in instances)
        {
            foreach (var (port, netName) in instance.Bindings)
            {
                if (bundleAliases.TryGetValue(netName, out var dotted))
                {
                    result.AddError(
                        "EMIT-002",
                        $"Instance '{instance.Id}' port '{port}' references '{netName}', but bundle field '{dotted}' must use dot notation",
                        $"instance {instance.Id}.{port}--{netName}",
                        $"Use '{dotted}' instead of '{netName}' for bundle field access"
                    );
                    continue;
                }

                if (!validNets.Contains(netName))
                {
                    result.AddError(
                        "EMIT-002",
                        $"Instance '{instance.Id}' port '{port}' references undefined net '{netName}'",
                        $"instance {instance.Id}.{port}--{netName}",
                        $"Declare '{netName}' as a port, supply, ground, or internal net"
                    );
                }
            }
        }
    }

    private static bool HasSizedEntry(SizePack pack, string key)
    {
        return pack.Entries.TryGetValue(key, out var value) && !IsUnsizedExpression(value);
    }

    private static bool IsUnsizedExpression(string? expression)
    {
        return string.IsNullOrWhiteSpace(expression)
            || expression.Contains("??", StringComparison.Ordinal);
    }

    private static IReadOnlyCollection<string> GetRequiredSizeFields(
        string deviceType,
        PrimitiveDefinition? primitive
    )
    {
        var required = new HashSet<string>(StringComparer.Ordinal);
        if (deviceType is "nmos" or "pmos")
        {
            required.Add("W");
            required.Add("L");
        }

        if (primitive is not null)
        {
            foreach (var field in PrimitiveResolver.GetSizeFields(primitive))
            {
                required.Add(field);
            }
        }

        return required;
    }

    private static IReadOnlyList<string> GetMissingSizeFields(
        SizePack pack,
        IReadOnlyCollection<string> requiredFields
    )
    {
        if (requiredFields.Count == 0)
        {
            return Array.Empty<string>();
        }

        var missing = new List<string>();
        foreach (var field in requiredFields.OrderBy(f => f, StringComparer.Ordinal))
        {
            if (!HasSizedEntry(pack, field))
            {
                missing.Add(field);
            }
        }

        return missing;
    }

    private static string BuildMissingSizeFieldMessage(
        DeviceDeclaration device,
        string? sizeName,
        IReadOnlyList<string> missingFields,
        bool isInline
    )
    {
        var hasWOrL = missingFields.Contains("W") || missingFields.Contains("L");
        if (hasWOrL)
        {
            var message = isInline
                ? $"Device '{device.Id}' inline size literal missing required W or L"
                : $"Device '{device.Id}' size pack '{sizeName}' missing required W or L";
            var extras = missingFields
                .Where(field => field is not "W" && field is not "L")
                .ToList();
            if (extras.Count > 0)
            {
                message += $" (also missing: {string.Join(", ", extras)})";
            }

            return message;
        }

        var sizeLabel = isInline ? "inline size literal" : $"size pack '{sizeName}'";
        return $"Device '{device.Id}' {sizeLabel} missing required size fields: {string.Join(", ", missingFields)}";
    }

    private static string BuildMissingSizeFieldSuggestion(IReadOnlyList<string> missingFields)
    {
        if (missingFields.Contains("W") || missingFields.Contains("L"))
        {
            var extras = missingFields
                .Where(field => field is not "W" && field is not "L")
                .ToList();
            var suggestion =
                "Size pack must contain at minimum W and L, e.g., 'size(W=2u, L=180n)'";
            if (extras.Count > 0)
            {
                suggestion += $" and include {string.Join(", ", extras)}";
            }

            return suggestion;
        }

        return $"Size pack must include {string.Join(", ", missingFields)}.";
    }

    /// <summary>
    /// Validates a single device declaration.
    /// </summary>
    private static void ValidateDevice(
        DeviceDeclaration device,
        HashSet<string> validNets,
        HashSet<string> validSizes,
        IReadOnlyDictionary<string, SizePack> sizeDefaults,
        IReadOnlyDictionary<string, PrimitiveDefinition>? primitivesByName,
        IReadOnlyDictionary<string, string> bundleAliases,
        ValidationResult result
    )
    {
        var deviceType = device.DeviceType.ToLowerInvariant();

        PrimitiveDefinition? primitive = null;
        if (primitivesByName is not null)
        {
            if (!primitivesByName.TryGetValue(device.Primitive, out primitive))
            {
                result.AddError(
                    "EMIT-008",
                    $"Device '{device.Id}' references undefined primitive '{device.Primitive}'",
                    $"device {device.Id}",
                    "Define the primitive at document scope and ensure device kind matches."
                );
            }
            else if (!primitive.Kind.Equals(deviceType, StringComparison.OrdinalIgnoreCase))
            {
                result.AddError(
                    "EMIT-008",
                    $"Device '{device.Id}' uses primitive '{device.Primitive}' with mismatched kind '{primitive.Kind}'",
                    $"device {device.Id}",
                    $"Primitive kind must be '{deviceType}' for this device."
                );
            }
        }

        // EMIT-004: Unknown device type
        if (!KnownDeviceTypes.Contains(deviceType))
        {
            result.AddError(
                "EMIT-004",
                $"Unknown device type '{device.DeviceType}'",
                $"device {device.Id}",
                $"Use one of: {string.Join(", ", KnownDeviceTypes)}"
            );
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
                        "nmos" => terminal == "B"
                            ? "Add bulk connection, typically .B--GND for NMOS"
                            : $"Add {terminal} terminal binding",
                        "pmos" => terminal == "B"
                            ? "Add bulk connection, typically .B--VDD for PMOS"
                            : $"Add {terminal} terminal binding",
                        _ => $"Add {terminal} terminal binding",
                    };

                    result.AddError(
                        "EMIT-001",
                        $"Device '{device.Id}' missing required terminal '{terminal}'",
                        $"device {device.Id}",
                        suggestion
                    );
                }
            }
        }

        // EMIT-002: Invalid net references
        foreach (var (terminal, netName) in device.Bindings)
        {
            if (bundleAliases.TryGetValue(netName, out var dotted))
            {
                result.AddError(
                    "EMIT-002",
                    $"Device '{device.Id}' terminal '{terminal}' references '{netName}', but bundle field '{dotted}' must use dot notation",
                    $"device {device.Id}.{terminal}--{netName}",
                    $"Use '{dotted}' instead of '{netName}' for bundle field access"
                );
                continue;
            }

            if (!validNets.Contains(netName))
            {
                var availableNets = validNets.Take(8).ToList();
                var netList =
                    availableNets.Count < validNets.Count
                        ? string.Join(", ", availableNets) + "..."
                        : string.Join(", ", availableNets);

                result.AddError(
                    "EMIT-002",
                    $"Device '{device.Id}' terminal '{terminal}' references undefined net '{netName}'",
                    $"device {device.Id}.{terminal}--{netName}",
                    $"Available nets: {netList}"
                );
            }
        }

        // EMIT-007: MOSFETs must use size packs
        var sizeName = device.SizeName;
        var sizePack = device.Size;
        var requiredSizeFields = GetRequiredSizeFields(deviceType, primitive);

        if (deviceType is "nmos" or "pmos" && sizeName is null && sizePack is null)
        {
            result.AddError(
                "EMIT-007",
                $"Device '{device.Id}' missing required size reference",
                $"device {device.Id}",
                "MOSFETs must provide a size argument: inline 'size(W=2u, L=180n, M=1)' or a named size pack reference"
            );
        }
        else if (sizePack is not null)
        {
            var missingFields = GetMissingSizeFields(sizePack, requiredSizeFields);
            if (missingFields.Count > 0)
            {
                result.AddError(
                    "EMIT-007",
                    BuildMissingSizeFieldMessage(device, sizeName, missingFields, isInline: true),
                    $"device {device.Id}",
                    BuildMissingSizeFieldSuggestion(missingFields)
                );
            }
        }
        else if (sizeName is not null && requiredSizeFields.Count > 0)
        {
            if (!validSizes.Contains(sizeName))
            {
                result.AddError(
                    "EMIT-007",
                    $"Device '{device.Id}' references undefined size pack '{sizeName}'",
                    $"device {device.Id}",
                    $"Declare size pack '{sizeName}' in the circuit signature or fill block"
                );
            }
            else if (sizeDefaults.TryGetValue(sizeName, out var defaultPack))
            {
                var missingFields = GetMissingSizeFields(defaultPack, requiredSizeFields);
                if (missingFields.Count > 0)
                {
                    result.AddError(
                        "EMIT-007",
                        BuildMissingSizeFieldMessage(
                            device,
                            sizeName,
                            missingFields,
                            isInline: false
                        ),
                        $"device {device.Id}",
                        BuildMissingSizeFieldSuggestion(missingFields)
                    );
                }
            }
        }

        // EMIT-003: Missing required parameters (passives only)
        if (RequiredParams.TryGetValue(deviceType, out var requiredParams) && primitive is not null)
        {
            foreach (var param in requiredParams)
            {
                if (!primitive.Params.ContainsKey(param))
                {
                    var example = param switch
                    {
                        "R" => "R=10k",
                        "C" => "C=1p",
                        "L" => "L=1u",
                        _ => $"{param}=<value>",
                    };

                    result.AddError(
                        "EMIT-003",
                        $"Device '{device.Id}' missing required parameter '{param}'",
                        $"device {device.Id}",
                        $"Add {param} parameter, e.g., {example}"
                    );
                }
            }
        }
    }
}
