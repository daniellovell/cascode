using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language;

/// <summary>
/// Desugars bundle-typed constructs in an Cascode document into their canonical expanded form.
/// </summary>
/// <remarks>
/// After desugaring:
/// - All bundle-typed ports are expanded to individual ports (e.g., "IN : Diff" → "IN.P", "IN.N")
/// - Device bindings preserve dot notation (e.g., "IN.P" stays "IN.P")
/// - All connections are expanded (e.g., "dp.IN--IN" -> "dp.IN.P--IN.P", "dp.IN.N--IN.N")
/// - All interface connectors are expanded (e.g., "DRAIN--OUT" -> "DRAIN.P--OUT.P", "DRAIN.N--OUT.N")
///
/// Downstream code (validation, emission, resolution) operates on the desugared representation
/// and never needs bundle context.
/// </remarks>
public static class BundleDesugarer
{
    /// <summary>
    /// Desugars all bundle-typed constructs in the document.
    /// Returns a new document with expanded ports, connections, and preserved dot notation.
    /// </summary>
    /// <param name="document">The Cascode document to desugar.</param>
    /// <returns>A new document with all bundle types expanded.</returns>
    public static CascodeDocument Desugar(CascodeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var bundlesByName = BundleExpander.GetBundlesByName(document);

        // If no bundle types defined, return the document unchanged
        if (bundlesByName.Count == 0)
        {
            return document;
        }

        // Build circuit lookup for resolving instance port types
        var circuitsByName = document.Circuits.ToDictionary(
            c => c.Name,
            c => c,
            StringComparer.Ordinal
        );

        return new CascodeDocument
        {
            VersionMajor = document.VersionMajor,
            VersionMinor = document.VersionMinor,
            Includes = document.Includes,
            FileLibrary = document.FileLibrary,
            Functions = document.Functions,
            BundleTypes = document.BundleTypes, // Preserve for documentation/round-trip
            Traits = document.Traits.Select(t => DesugarTrait(t, bundlesByName)).ToList(),
            BenchDefinitions = document.BenchDefinitions,
            Primitives = document.Primitives,
            Circuits = document
                .Circuits.Select(c => DesugarCircuit(c, bundlesByName, circuitsByName))
                .ToList(),
        };
    }

    /// <summary>
    /// Desugars an interface definition by expanding bundle-typed ports and connector mappings.
    /// </summary>
    private static TraitDefinition DesugarTrait(
        TraitDefinition interfaceDef,
        IReadOnlyDictionary<string, BundleType> bundlesByName
    )
    {
        // Build a map of port name -> type for this interface
        var portTypes = interfaceDef.Ports.ToDictionary(
            p => p.Name,
            p => p.Type,
            StringComparer.Ordinal
        );

        return new TraitDefinition
        {
            Name = interfaceDef.Name,
            Ports = ExpandPorts(interfaceDef.Ports, bundlesByName),
            Connectors = interfaceDef
                .Connectors.Select(c => DesugarConnector(c, portTypes, bundlesByName))
                .ToList(),
            BenchBindings = interfaceDef.BenchBindings,
        };
    }

    /// <summary>
    /// Desugars a connector by expanding bundle-to-bundle mappings.
    /// </summary>
    private static TraitConnector DesugarConnector(
        TraitConnector connector,
        IReadOnlyDictionary<string, string> sourcePortTypes,
        IReadOnlyDictionary<string, BundleType> bundlesByName
    )
    {
        var expandedMappings = new List<ConnectorMapping>();

        foreach (var mapping in connector.Mappings)
        {
            expandedMappings.AddRange(
                ExpandConnectorMapping(mapping, sourcePortTypes, bundlesByName)
            );
        }

        return new TraitConnector
        {
            TargetTrait = connector.TargetTrait,
            Mappings = expandedMappings,
        };
    }

    /// <summary>
    /// Expands a single connector mapping, handling bundle-to-bundle references.
    /// </summary>
    private static IEnumerable<ConnectorMapping> ExpandConnectorMapping(
        ConnectorMapping mapping,
        IReadOnlyDictionary<string, string> sourcePortTypes,
        IReadOnlyDictionary<string, BundleType> bundlesByName
    )
    {
        var sourcePath = mapping.SourcePort;
        var targetPath = mapping.TargetPort;

        // Check if source is a bundle-typed port (no dot = potential bundle reference)
        var sourceBaseName = GetBaseName(sourcePath);
        if (
            !sourcePath.Contains('.')
            && sourcePortTypes.TryGetValue(sourceBaseName, out var sourceType)
            && bundlesByName.TryGetValue(sourceType, out var bundle)
        )
        {
            // Bundle-to-bundle expansion
            foreach (var field in bundle.Fields.OrderBy(f => f.Key, StringComparer.Ordinal))
            {
                var expandedSourcePath = $"{sourcePath}.{field.Key}";
                var expandedTargetPath = $"{targetPath}.{field.Key}";

                // Recursively expand nested bundles
                foreach (
                    var nested in ExpandConnectorMapping(
                        new ConnectorMapping
                        {
                            SourcePort = expandedSourcePath,
                            TargetPort = expandedTargetPath,
                        },
                        sourcePortTypes,
                        bundlesByName
                    )
                )
                {
                    yield return nested;
                }
            }
        }
        else
        {
            // Leaf mapping - preserve dot notation
            yield return new ConnectorMapping
            {
                SourcePort = NormalizePath(sourcePath),
                TargetPort = NormalizePath(targetPath),
            };
        }
    }

    /// <summary>
    /// Desugars a circuit by expanding bundle-typed ports, connections, and device bindings.
    /// <summary>
    /// Desugars a circuit by expanding bundle-typed ports, instance bindings, connections, and related blocks into their canonical, leaf-level form.
    /// </summary>
    /// <param name="circuit">The input circuit to desugar.</param>
    /// <param name="bundlesByName">Lookup of bundle type definitions keyed by bundle name, used to expand bundle-typed ports and bindings.</param>
    /// <param name="circuitsByName">Lookup of circuit definitions keyed by circuit name, used to resolve child instance port types during expansion.</param>
    /// <returns>A new <see cref="Circuit"/> with bundle-typed ports, slot/fill/harness contents, and instance bindings expanded to terminal paths while preserving other circuit properties.</returns>
    private static Circuit DesugarCircuit(
        Circuit circuit,
        IReadOnlyDictionary<string, BundleType> bundlesByName,
        IReadOnlyDictionary<string, Circuit> circuitsByName
    )
    {
        // Build a map of port name -> type for this circuit
        var portTypes = circuit.Ports.ToDictionary(
            p => p.Name,
            p => p.Type,
            StringComparer.Ordinal
        );

        // Build instance type lookup for resolving instance.port references
        var instanceTypes =
            circuit.Fill?.Instances.ToDictionary(i => i.Id, i => i.Type, StringComparer.Ordinal)
            ?? new Dictionary<string, string>();

        return new Circuit
        {
            Name = circuit.Name,
            Traits = circuit.Traits,
            Level = circuit.Level,
            Inline = circuit.Inline,
            Package = circuit.Package,
            Parameters = circuit.Parameters,
            Sizes = circuit.Sizes,
            Supplies = circuit.Supplies,
            Grounds = circuit.Grounds,
            Ports = ExpandPorts(circuit.Ports, bundlesByName),
            Slot = circuit.Slot is not null
                ? DesugarSlotBlock(circuit.Slot, bundlesByName, circuitsByName)
                : null,
            Fill = circuit.Fill is not null
                ? DesugarFillBlock(
                    circuit.Fill,
                    portTypes,
                    bundlesByName,
                    circuitsByName,
                    instanceTypes
                )
                : null,
            Constraints = circuit.Constraints,
            Harness = circuit.Harness is not null
                ? DesugarHarness(circuit.Harness, portTypes, bundlesByName)
                : null,
            Env = circuit.Env,
            Render = circuit.Render,
            BenchBindings = circuit.BenchBindings,
            BenchBindingExtensions = circuit.BenchBindingExtensions,
            Synth = circuit.Synth,
            Provenance = circuit.Provenance,
        };
    }

    /// <summary>
    /// Expands bundle-typed ports to individual ports with dot notation preserved.
    /// </summary>
    private static List<PortDeclaration> ExpandPorts(
        List<PortDeclaration> ports,
        IReadOnlyDictionary<string, BundleType> bundlesByName
    )
    {
        var expanded = new List<PortDeclaration>();

        foreach (var port in ports)
        {
            foreach (
                var terminalPath in BundleExpander.ExpandToTerminalPaths(
                    port.Name,
                    port.Type,
                    bundlesByName
                )
            )
            {
                // Get the leaf type (the domain after full expansion)
                var leafType = GetLeafType(port.Name, port.Type, terminalPath, bundlesByName);

                expanded.Add(
                    new PortDeclaration
                    {
                        Direction = port.Direction,
                        Name = NormalizePath(terminalPath),
                        Type = leafType,
                    }
                );
            }
        }

        return expanded;
    }

    /// <summary>
    /// Gets the leaf type for an expanded terminal path.
    /// </summary>
    private static string GetLeafType(
        string baseName,
        string baseType,
        string terminalPath,
        IReadOnlyDictionary<string, BundleType> bundlesByName
    )
    {
        // If it's not a bundle type, the leaf type is the base type
        if (!bundlesByName.TryGetValue(baseType, out var bundle))
        {
            return baseType;
        }

        // Navigate through the path to find the leaf type
        var suffix =
            terminalPath.Length > baseName.Length
                ? terminalPath[(baseName.Length + 1)..] // Skip the base name and dot
                : string.Empty;

        if (string.IsNullOrEmpty(suffix))
        {
            return baseType;
        }

        var parts = suffix.Split('.');
        var currentType = baseType;

        foreach (var part in parts)
        {
            if (bundlesByName.TryGetValue(currentType, out var currentBundle))
            {
                if (currentBundle.Fields.TryGetValue(part, out var fieldType))
                {
                    currentType = fieldType;
                }
            }
        }

        return currentType;
    }

    /// <summary>
    /// Desugars a slot block by expanding bundle-typed instance bindings.
    /// </summary>
    private static SlotBlock DesugarSlotBlock(
        SlotBlock slot,
        IReadOnlyDictionary<string, BundleType> bundlesByName,
        IReadOnlyDictionary<string, Circuit> circuitsByName
    )
    {
        if (slot.Nets.Count == 0 && slot.Instances.Count == 0 && slot.Connections.Count == 0)
        {
            return slot;
        }

        return new SlotBlock
        {
            Nets = slot.Nets,
            Instances = slot
                .Instances.Select(i => DesugarInstance(i, bundlesByName, circuitsByName))
                .ToList(),
            Connections = slot.Connections,
        };
    }

    /// <summary>
    /// Desugars a fill block by expanding connections and normalizing device bindings.
    /// </summary>
    private static FillBlock DesugarFillBlock(
        FillBlock fill,
        IReadOnlyDictionary<string, string> portTypes,
        IReadOnlyDictionary<string, BundleType> bundlesByName,
        IReadOnlyDictionary<string, Circuit> circuitsByName,
        IReadOnlyDictionary<string, string> instanceTypes
    )
    {
        // Desugar instances and expand their connects
        var desugaredInstances = fill
            .Instances.Select(i =>
            {
                var desugared = DesugarInstance(i, bundlesByName, circuitsByName);
                // Expand instance-level connects if any
                if (desugared.Connects.Count > 0)
                {
                    return new InstanceDeclaration
                    {
                        Id = desugared.Id,
                        Type = desugared.Type,
                        DeclaredType = desugared.DeclaredType,
                        Bindings = desugared.Bindings,
                        Params = desugared.Params,
                        Sizes = desugared.Sizes,
                        Connects = ExpandConnections(
                            desugared.Connects,
                            portTypes,
                            bundlesByName,
                            circuitsByName,
                            instanceTypes
                        ),
                    };
                }
                return desugared;
            })
            .ToList();

        return new FillBlock
        {
            Nets = fill.Nets,
            Sizes = fill.Sizes,
            Instances = desugaredInstances,
            Devices = fill.Devices.Select(d => DesugarDevice(d)).ToList(),
            Attaches = fill.Attaches,
            Connections = ExpandConnections(
                fill.Connections,
                portTypes,
                bundlesByName,
                circuitsByName,
                instanceTypes
            ),
        };
    }

    /// <summary>
    /// Desugars an instance by expanding bundle-typed bindings.
    /// </summary>
    private static InstanceDeclaration DesugarInstance(
        InstanceDeclaration instance,
        IReadOnlyDictionary<string, BundleType> bundlesByName,
        IReadOnlyDictionary<string, Circuit> circuitsByName
    )
    {
        var expandedBindings = new Dictionary<string, string>(StringComparer.Ordinal);

        // Get the child circuit's port types if available
        IReadOnlyDictionary<string, string>? childPortTypes = null;
        if (circuitsByName.TryGetValue(instance.Type, out var childCircuit))
        {
            childPortTypes = childCircuit.Ports.ToDictionary(
                p => p.Name,
                p => p.Type,
                StringComparer.Ordinal
            );
        }

        foreach (var (port, net) in instance.Bindings)
        {
            // Check if this port is bundle-typed on the child circuit
            if (
                childPortTypes is not null
                && childPortTypes.TryGetValue(port, out var portType)
                && bundlesByName.TryGetValue(portType, out var bundle)
            )
            {
                // Expand bundle-typed binding to individual bindings
                foreach (
                    var terminalPath in BundleExpander.ExpandToTerminalPaths(
                        port,
                        portType,
                        bundlesByName
                    )
                )
                {
                    var suffix =
                        terminalPath.Length > port.Length
                            ? terminalPath[port.Length..] // e.g., ".P" or ".N"
                            : string.Empty;
                    var expandedNet = net + suffix;
                    expandedBindings[NormalizePath(terminalPath)] = NormalizePath(expandedNet);
                }
            }
            else
            {
                // Not a bundle - preserve dot notation
                expandedBindings[NormalizePath(port)] = NormalizePath(net);
            }
        }

        return new InstanceDeclaration
        {
            Id = instance.Id,
            Type = instance.Type,
            DeclaredType = instance.DeclaredType,
            Bindings = expandedBindings,
            Params = instance.Params,
            Sizes = instance.Sizes,
            Connects = instance.Connects, // Preserve for later expansion
        };
    }

    /// <summary>
    /// Desugars a device by normalizing its terminal bindings.
    /// </summary>
    private static DeviceDeclaration DesugarDevice(DeviceDeclaration device)
    {
        var bindings = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (terminal, net) in device.Bindings)
        {
            bindings[terminal] = NormalizePath(net);
        }

        return new DeviceDeclaration
        {
            DeviceType = device.DeviceType,
            Id = device.Id,
            Bindings = bindings,
            Primitive = device.Primitive,
            SizeName = device.SizeName,
            Size = device.Size,
        };
    }

    /// <summary>
    /// Expands connections, handling bundle-to-bundle connections.
    /// </summary>
    private static List<ConnectionStatement> ExpandConnections(
        List<ConnectionStatement> connections,
        IReadOnlyDictionary<string, string> portTypes,
        IReadOnlyDictionary<string, BundleType> bundlesByName,
        IReadOnlyDictionary<string, Circuit> circuitsByName,
        IReadOnlyDictionary<string, string> instanceTypes
    )
    {
        var expanded = new List<ConnectionStatement>();

        foreach (var conn in connections)
        {
            expanded.AddRange(
                ExpandConnection(conn, portTypes, bundlesByName, circuitsByName, instanceTypes)
            );
        }

        return expanded;
    }

    /// <summary>
    /// Expands a single connection, handling bundle-to-bundle references.
    /// </summary>
    private static IEnumerable<ConnectionStatement> ExpandConnection(
        ConnectionStatement conn,
        IReadOnlyDictionary<string, string> portTypes,
        IReadOnlyDictionary<string, BundleType> bundlesByName,
        IReadOnlyDictionary<string, Circuit> circuitsByName,
        IReadOnlyDictionary<string, string> instanceTypes
    )
    {
        var fromPath = conn.From;
        var toPath = conn.To;

        // Try to determine the expansion from 'from' side (instance.port or local port)
        string? bundleType = null;
        List<string>? dottedPorts = null; // For child circuits with dotted port names

        // Check if 'from' is an instance.port reference
        var dotIndex = fromPath.IndexOf('.');
        if (dotIndex >= 0)
        {
            var instanceId = fromPath[..dotIndex];
            var portName = fromPath[(dotIndex + 1)..];

            // Look up the instance type and its port
            if (
                instanceTypes.TryGetValue(instanceId, out var instanceTypeName)
                && circuitsByName.TryGetValue(instanceTypeName, out var instanceCircuit)
            )
            {
                var instancePortTypes = instanceCircuit.Ports.ToDictionary(
                    p => p.Name,
                    p => p.Type,
                    StringComparer.Ordinal
                );

                // Check if the port on the instance is bundle-typed
                if (
                    instancePortTypes.TryGetValue(portName, out var portType)
                    && bundlesByName.ContainsKey(portType)
                )
                {
                    bundleType = portType;
                }
                else
                {
                    // Check if there are dotted ports with this prefix (e.g., "IN.P", "IN.N" for "IN")
                    var prefix = portName + ".";
                    dottedPorts = instanceCircuit
                        .Ports.Where(p => p.Name.StartsWith(prefix, StringComparison.Ordinal))
                        .Select(p => p.Name[prefix.Length..]) // Get the suffix (P, N, etc.)
                        .OrderBy(s => s, StringComparer.Ordinal)
                        .ToList();

                    if (dottedPorts.Count == 0)
                    {
                        dottedPorts = null;
                    }
                }
            }
        }
        else
        {
            // Check if 'from' is a local bundle-typed port
            if (
                portTypes.TryGetValue(fromPath, out var fromType)
                && bundlesByName.ContainsKey(fromType)
            )
            {
                bundleType = fromType;
            }
        }

        // Also check 'to' side for bundle type (it may be a local port or instance.port)
        string? toBundleType = null;
        if (portTypes.TryGetValue(toPath, out var toType) && bundlesByName.ContainsKey(toType))
        {
            toBundleType = toType;
            // If 'from' side doesn't have a bundle type, use 'to' side's type for expansion
            if (bundleType is null && dottedPorts is null)
            {
                bundleType = toType;
            }
        }
        else
        {
            // Check if 'to' is an instance.port reference with bundle type
            var toDotIndex = toPath.IndexOf('.');
            if (toDotIndex >= 0)
            {
                var toInstanceId = toPath[..toDotIndex];
                var toPortName = toPath[(toDotIndex + 1)..];
                if (
                    instanceTypes.TryGetValue(toInstanceId, out var toInstanceTypeName)
                    && circuitsByName.TryGetValue(toInstanceTypeName, out var toInstanceCircuit)
                )
                {
                    var toInstancePortTypes = toInstanceCircuit.Ports.ToDictionary(
                        p => p.Name,
                        p => p.Type,
                        StringComparer.Ordinal
                    );
                    if (
                        toInstancePortTypes.TryGetValue(toPortName, out var toPortType)
                        && bundlesByName.ContainsKey(toPortType)
                    )
                    {
                        toBundleType = toPortType;
                    }
                }
            }
        }

        // If we found a bundle type, expand
        if (bundleType is not null && bundlesByName.TryGetValue(bundleType, out var bundle))
        {
            // Determine if 'to' side is bundle-typed (should expand) or scalar (should not expand)
            bool toIsBundleTyped = toBundleType is not null;

            foreach (var field in bundle.Fields.OrderBy(f => f.Key, StringComparer.Ordinal))
            {
                var expandedFrom = $"{fromPath}.{field.Key}";
                // Only append field suffix to 'to' if it's also bundle-typed
                var expandedTo = toIsBundleTyped ? $"{toPath}.{field.Key}" : toPath;

                // Recursively expand nested bundles
                foreach (
                    var nested in ExpandConnection(
                        new ConnectionStatement { From = expandedFrom, To = expandedTo },
                        portTypes,
                        bundlesByName,
                        circuitsByName,
                        instanceTypes
                    )
                )
                {
                    yield return nested;
                }
            }
        }
        // If we found dotted ports on the child circuit, expand to match them
        else if (dottedPorts is not null)
        {
            // Determine if 'to' side also has dotted ports or is a scalar
            bool toHasDottedPorts = false;
            var toDotIndex = toPath.IndexOf('.');
            if (toDotIndex >= 0)
            {
                var toInstanceId = toPath[..toDotIndex];
                var toPortName = toPath[(toDotIndex + 1)..];
                if (
                    instanceTypes.TryGetValue(toInstanceId, out var toInstanceTypeName)
                    && circuitsByName.TryGetValue(toInstanceTypeName, out var toInstanceCircuit)
                )
                {
                    var prefix = toPortName + ".";
                    toHasDottedPorts = toInstanceCircuit.Ports.Any(p =>
                        p.Name.StartsWith(prefix, StringComparison.Ordinal)
                    );
                }
            }
            else
            {
                // 'to' is a local port - check if it has dotted sub-ports or is bundle-typed
                toHasDottedPorts = toBundleType is not null;
            }

            foreach (var suffix in dottedPorts)
            {
                var expandedFrom = $"{fromPath}.{suffix}";
                // Only append suffix to 'to' if it also has dotted ports / is bundle-typed
                var expandedTo = toHasDottedPorts ? $"{toPath}.{suffix}" : toPath;

                // Recursively expand (in case of nested dotted ports)
                foreach (
                    var nested in ExpandConnection(
                        new ConnectionStatement { From = expandedFrom, To = expandedTo },
                        portTypes,
                        bundlesByName,
                        circuitsByName,
                        instanceTypes
                    )
                )
                {
                    yield return nested;
                }
            }
        }
        else
        {
            // Leaf connection - preserve dot notation while keeping instance.port structure
            yield return new ConnectionStatement
            {
                From = NormalizeConnectionPath(fromPath, instanceTypes),
                To = NormalizeConnectionPath(toPath, instanceTypes),
            };
        }
    }

    /// <summary>
    /// Normalizes a connection path, preserving instance.port structure where applicable.
    /// </summary>
    private static string NormalizeConnectionPath(
        string path,
        IReadOnlyDictionary<string, string> instanceTypes
    )
    {
        var firstDot = path.IndexOf('.');
        if (firstDot < 0)
        {
            // No dots - return as-is
            return path;
        }

        var firstPart = path[..firstDot];
        var rest = path[(firstDot + 1)..];

        // Check if first part is an instance name
        if (instanceTypes.ContainsKey(firstPart))
        {
            // Instance.port reference: preserve dot notation for the port portion
            return $"{firstPart}.{NormalizePath(rest)}";
        }
        else
        {
            // Local port reference: preserve dot notation
            return NormalizePath(path);
        }
    }

    /// <summary>
    /// Desugars harness block while preserving dot notation in loads, sources, etc.
    /// </summary>
    private static HarnessBlock DesugarHarness(
        HarnessBlock harness,
        IReadOnlyDictionary<string, string> portTypes,
        IReadOnlyDictionary<string, BundleType> bundlesByName
    )
    {
        return new HarnessBlock
        {
            Grounds = harness
                .Grounds.Select(g => new GroundValue
                {
                    Net = NormalizePath(g.Net),
                    Value = g.Value,
                })
                .ToList(),
            Supplies = harness.Supplies,
            Biases = harness.Biases,
            Sources = harness
                .Sources.Select(s => new SourceValue { Net = NormalizePath(s.Net), Z = s.Z })
                .ToList(),
            Loads = ExpandHarnessLoads(harness.Loads, portTypes, bundlesByName),
            Sweeps = harness.Sweeps,
            Icmr = harness.Icmr,
            Pvt = harness.Pvt,
        };
    }

    /// <summary>
    /// Expands harness loads, handling bundle-typed port references.
    /// </summary>
    private static List<LoadValue> ExpandHarnessLoads(
        List<LoadValue> loads,
        IReadOnlyDictionary<string, string> portTypes,
        IReadOnlyDictionary<string, BundleType> bundlesByName
    )
    {
        var expanded = new List<LoadValue>();

        foreach (var load in loads)
        {
            var netBaseName = GetBaseName(load.Net);

            // Check if load references a bundle-typed port
            if (
                !load.Net.Contains('.')
                && portTypes.TryGetValue(netBaseName, out var portType)
                && bundlesByName.TryGetValue(portType, out var bundle)
            )
            {
                // Expand to individual loads for each bundle field
                var terminalPaths = BundleExpander
                    .ExpandToTerminalPaths(load.Net, portType, bundlesByName)
                    .ToList();

                // Split load evenly across bundle terminals
                var splitElements = SplitLoadElements(load.Elements, terminalPaths.Count);

                foreach (var terminalPath in terminalPaths)
                {
                    expanded.Add(
                        new LoadValue
                        {
                            Net = NormalizePath(terminalPath),
                            Elements = splitElements,
                        }
                    );
                }
            }
            else
            {
                // Not a bundle - preserve dot notation
                expanded.Add(
                    new LoadValue { Net = NormalizePath(load.Net), Elements = load.Elements }
                );
            }
        }

        return expanded;
    }

    /// <summary>
    /// Splits load elements evenly across multiple terminals.
    /// For example, a 1pF load split across 2 terminals becomes 500f per terminal.
    /// </summary>
    private static List<LoadElement> SplitLoadElements(
        List<LoadElement> elements,
        int terminalCount
    )
    {
        if (terminalCount <= 1)
            return elements;

        var splitElements = new List<LoadElement>();

        foreach (var elem in elements)
        {
            // Parse the numeric value and SI prefix
            if (TryParseValueWithUnit(elem.Value, out var numericValue, out var unit))
            {
                var splitValue = numericValue / terminalCount;
                splitElements.Add(
                    new LoadElement(elem.Type, FormatValueWithUnit(splitValue, unit))
                );
            }
            else
            {
                // Can't parse - keep original (shouldn't happen with valid input)
                splitElements.Add(elem);
            }
        }

        return splitElements;
    }

    /// <summary>
    /// Parses a value string with optional SI unit suffix.
    /// </summary>
    private static bool TryParseValueWithUnit(string valueStr, out double value, out string unit)
    {
        value = 0;
        unit = "";

        if (string.IsNullOrWhiteSpace(valueStr))
            return false;

        // Try to parse as plain number
        if (double.TryParse(valueStr, out value))
        {
            unit = "";
            return true;
        }

        // Extract numeric part and unit
        var match = System.Text.RegularExpressions.Regex.Match(
            valueStr,
            @"^([-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?)\s*(.*)$"
        );

        if (!match.Success)
            return false;

        if (!double.TryParse(match.Groups[1].Value, out value))
            return false;

        unit = match.Groups[2].Value.Trim();
        return true;
    }

    /// <summary>
    /// Formats a value with its unit suffix.
    /// </summary>
    private static string FormatValueWithUnit(double value, string unit)
    {
        return string.IsNullOrEmpty(unit) ? value.ToString() : $"{value}{unit}";
    }

    /// <summary>
    /// Gets the base name from a potentially dotted path.
    /// E.g., "dp.IN.P" → "dp", "IN" → "IN"
    /// </summary>
    private static string GetBaseName(string path)
    {
        var dotIndex = path.IndexOf('.');
        return dotIndex >= 0 ? path[..dotIndex] : path;
    }

    /// <summary>
    /// Normalizes a path while preserving dot notation.
    /// </summary>
    private static string NormalizePath(string path) => path;
}