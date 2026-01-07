using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Cascode.ACIR.Validation;
using Cascode.Bench;

namespace Cascode.ACIR;

/// <summary>
/// Emits SPICE netlists from ACIR EL documents.
/// </summary>
/// <remarks>
/// The emitter generates ngspice-compatible SPICE netlists from EL-level ACIR circuits.
/// It produces:
/// - Design subcircuit files (.sp) with device instantiations
/// - Testbench files that instantiate the design with harness elements
///
/// Device terminal ordering follows SPICE conventions:
/// - MOSFETs: D G S B (drain, gate, source, bulk)
/// - Two-terminal devices (R, C, L): P N (positive, negative)
/// - Diodes: A K (anode, cathode)
///
/// When devices use generic model names (nmos, pmos) without PDK-specific models,
/// the emitter includes Level-1 MOSFET model definitions for ngspice simulation.
/// </remarks>
public static class SpiceEmitter
{
    private static readonly HashSet<string> GenericMosfetModels = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "nmos",
        "pmos",
    };

    /// <summary>
    /// Emits a SPICE subcircuit definition for an EL-level circuit.
    /// </summary>
    /// <param name="circuit">The circuit to emit (must be EL level).</param>
    /// <param name="writer">Text writer for output.</param>
    /// <param name="deviceModelMap">Optional map of PDK device names to resolved model definitions.</param>
    /// <param name="document">Optional ACIR document for resolving instance types.</param>
    /// <param name="resolution">Optional attach resolution result for resolved net names.</param>
    /// <exception cref="InvalidOperationException">Thrown if circuit is not EL level.</exception>
    /// <remarks>
    /// Output format:
    /// <code>
    /// * CircuitName - Generated from ACIR EL
    /// .subckt CircuitName port1 port2 ... supply1 ... ground1 ...
    /// * Internal nets: net1, net2, ...
    /// M... (device instances)
    /// X... (circuit instances)
    /// .ends CircuitName
    /// </code>
    /// Port ordering: declared ports, then supplies, then grounds.
    /// </remarks>
    public static void EmitDesign(
        Circuit circuit,
        TextWriter writer,
        IReadOnlyDictionary<string, DeviceModelResolution>? deviceModelMap = null,
        ACIRDocument? document = null,
        CircuitResolutionResult? resolution = null
    )
    {
        if (circuit.Level != ACIRLevel.EL)
        {
            throw new InvalidOperationException(
                $"SpiceEmitter requires EL-level circuit, but '{circuit.Name}' is {circuit.Level}."
            );
        }

        // Header comment
        writer.WriteLine($"* {circuit.Name} - Generated from ACIR EL");
        writer.WriteLine();

        // Build port list: ports first, then supplies, then grounds
        var portList = new List<string>();
        foreach (var port in circuit.Ports)
        {
            portList.Add(port.Name);
        }
        foreach (var supply in circuit.Supplies)
        {
            portList.Add(supply);
        }
        foreach (var ground in circuit.Grounds)
        {
            portList.Add(ground);
        }

        writer.WriteLine($".subckt {circuit.Name} {string.Join(" ", portList)}");
        writer.WriteLine();

        // Internal nets comment
        if (circuit.Fill?.Nets.Count > 0)
        {
            var netNames = circuit
                .Fill.Nets.OrderBy(n => n.Id, StringComparer.Ordinal)
                .Select(n => n.Id);
            writer.WriteLine($"* Internal nets: {string.Join(", ", netNames)}");
            writer.WriteLine();
        }

        // Emit devices
        if (circuit.Fill?.Devices.Count > 0)
        {
            foreach (var device in circuit.Fill.Devices.OrderBy(d => d.Id, StringComparer.Ordinal))
            {
                EmitDevice(device, writer, deviceModelMap);
            }
        }

        // Emit circuit instances as X-elements or inline expansion
        if (circuit.Fill?.Instances.Count > 0 && document is not null)
        {
            var circuitsByName = document.Circuits.ToDictionary(
                c => c.Name,
                StringComparer.Ordinal
            );

            foreach (
                var instance in circuit.Fill.Instances.OrderBy(i => i.Id, StringComparer.Ordinal)
            )
            {
                if (circuitsByName.TryGetValue(instance.Type, out var targetCircuit))
                {
                    if (targetCircuit.Inline)
                    {
                        // Inline expansion: embed devices with hierarchical naming
                        writer.WriteLine();
                        writer.WriteLine($"* Inline expansion of {instance.Id} : {instance.Type}");
                        ExpandInlineCircuit(
                            instance,
                            targetCircuit,
                            resolution,
                            deviceModelMap,
                            writer
                        );
                    }
                    else
                    {
                        // Non-inline: emit as X-element
                        writer.WriteLine();
                        writer.WriteLine("* Circuit instances");
                        EmitInstance(instance, targetCircuit, resolution, writer);
                    }
                }
            }
        }

        writer.WriteLine();
        writer.WriteLine($".ends {circuit.Name}");
    }

    /// <summary>
    /// Emits a SPICE testbench for a given bench configuration.
    /// </summary>
    /// <param name="circuit">The circuit containing the bench.</param>
    /// <param name="bench">The bench configuration.</param>
    /// <param name="designPath">Path to the design .sp file to include.</param>
    /// <param name="writer">Text writer for output.</param>
    /// <param name="backend">Backend type for testbench generation.</param>
    /// <exception cref="InvalidOperationException">Thrown if circuit is not EL level.</exception>
    /// <remarks>
    /// This method is deprecated. Use Emit() with backend parameter instead.
    /// The testbench is now generated using templates via TestbenchGenerator.
    /// </remarks>
    [Obsolete("Use Emit() method with backend parameter instead")]
    public static void EmitTestbench(
        Circuit circuit,
        BenchConfig bench,
        string designPath,
        TextWriter writer,
        BenchBackendType backend = BenchBackendType.Ngspice
    )
    {
        if (circuit.Level != ACIRLevel.EL)
        {
            throw new InvalidOperationException(
                $"SpiceEmitter requires EL-level circuit, but '{circuit.Name}' is {circuit.Level}."
            );
        }

        var title = $"{circuit.Name}_{bench.Name}";

        // Header
        writer.WriteLine($"* {title} - Generated from ACIR EL");
        writer.WriteLine($".title {title}");
        writer.WriteLine();

        // Emit generic model definitions if circuit uses generic devices
        var genericModels = GetRequiredGenericModels(circuit);
        if (genericModels.Count > 0)
        {
            EmitGenericModels(genericModels, writer);
            writer.WriteLine();
        }

        // Include design
        writer.WriteLine($".include \"{designPath}\"");
        writer.WriteLine();

        // Harness section
        writer.WriteLine("* Harness");
        if (circuit.Harness is not null)
        {
            EmitHarness(circuit, writer);
        }
        writer.WriteLine();

        // DUT instantiation
        writer.WriteLine("* DUT");
        EmitDutInstantiation(circuit, writer);
        writer.WriteLine();

        // Analysis commands based on bench type
        EmitAnalysis(bench, writer);

        writer.WriteLine(".end");
    }

    /// <summary>
    /// Emits all outputs for an ACIR document: design netlist and testbenches.
    /// </summary>
    /// <param name="doc">The ACIR document.</param>
    /// <param name="outputDir">Output directory for generated files.</param>
    /// <param name="backend">Backend type for testbench generation (default: ngspice).</param>
    /// <param name="workspaceRoot">Optional workspace root for template discovery.</param>
    /// <returns>Result containing paths to generated files.</returns>
    /// <remarks>
    /// Processes all EL-level circuits in the document:
    /// - Generates {CircuitName}.sp for each circuit in dependency order
    /// - Generates {CircuitName}_{BenchName}.sp for each bench using templates
    /// Output directory is created if it doesn't exist.
    /// Non-EL circuits are silently skipped.
    /// Dependency order ensures .subckt definitions appear before X-element references.
    /// </remarks>
    public static SpiceEmitResult Emit(
        ACIRDocument doc,
        string outputDir,
        BenchBackendType backend = BenchBackendType.Ngspice,
        string? workspaceRoot = null,
        IBenchIncludeResolver? includeResolver = null
    )
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(outputDir);

        var result = new SpiceEmitResult();
        Directory.CreateDirectory(outputDir);

        // Resolve attach statements for net connectivity
        var attachResolver = new AttachResolver(doc);
        var attachResult = attachResolver.Resolve();

        // Order circuits by dependency (leaves first, top-level last)
        var orderedCircuits = OrderByDependency(doc);

        foreach (var circuit in orderedCircuits)
        {
            if (circuit.Level != ACIRLevel.EL)
            {
                continue;
            }

            var includeResolution = includeResolver?.Resolve(circuit, backend);
            var circuitResolution = attachResult.CircuitResults.GetValueOrDefault(circuit.Name);

            // Emit design netlist
            var designPath = Path.Combine(outputDir, $"{circuit.Name}.sp");
            using (var writer = File.CreateText(designPath))
            {
                EmitDesign(
                    circuit,
                    writer,
                    includeResolution?.DeviceModelMap,
                    doc,
                    circuitResolution
                );
            }
            result.DesignPaths.Add(designPath);

            // Emit testbenches using template-based generation
            if (circuit.Benches?.Benches.Count > 0)
            {
                foreach (var bench in circuit.Benches.Benches)
                {
                    var files = ACIRBenchAdapter.GenerateTestbench(
                        circuit,
                        bench,
                        backend,
                        outputDir,
                        workspaceRoot,
                        includeResolution
                    );
                    result.TestbenchPaths.Add(files.NetlistPath);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Validates and emits all outputs for an ACIR document with pre-flight validation.
    /// </summary>
    /// <param name="doc">The ACIR document.</param>
    /// <param name="outputDir">Output directory for generated files.</param>
    /// <param name="backend">Backend type for testbench generation (default: ngspice).</param>
    /// <param name="workspaceRoot">Optional workspace root for template discovery.</param>
    /// <returns>Result containing paths to generated files and validation result.</returns>
    /// <remarks>
    /// Runs hierarchy validation and emission validation before attempting SPICE generation.
    /// If validation fails, no files are written and the validation errors are returned.
    /// </remarks>
    public static ValidatedEmitResult ValidateAndEmit(
        ACIRDocument doc,
        string outputDir,
        BenchBackendType backend = BenchBackendType.Ngspice,
        string? workspaceRoot = null,
        IBenchIncludeResolver? includeResolver = null
    )
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(outputDir);

        var validationResult = new ValidationResult();

        // Validate hierarchy first (circuit references, parameters, ports, cycles)
        var hierarchyValidation = HierarchyValidator.Validate(doc);
        validationResult.Merge(hierarchyValidation);

        // Validate all EL circuits for emission requirements
        var elCircuits = doc.Circuits.Where(c => c.Level == ACIRLevel.EL).ToList();
        foreach (var circuit in elCircuits)
        {
            var circuitValidation = EmissionValidator.Validate(circuit);
            validationResult.Merge(circuitValidation);
        }

        // If validation failed, return early without emitting
        if (!validationResult.IsValid)
        {
            return new ValidatedEmitResult
            {
                Validation = validationResult,
                Emit = new SpiceEmitResult(),
            };
        }

        // Validation passed, proceed with emission
        var emitResult = Emit(doc, outputDir, backend, workspaceRoot, includeResolver);

        return new ValidatedEmitResult { Validation = validationResult, Emit = emitResult };
    }

    /// <summary>
    /// Orders circuits by dependency using topological sort.
    /// </summary>
    /// <param name="doc">The ACIR document.</param>
    /// <returns>Circuits ordered with leaf circuits first, top-level last.</returns>
    /// <remarks>
    /// Required for SPICE: .subckt must be defined before X-element reference.
    /// Uses Kahn's algorithm for topological sort.
    /// </remarks>
    private static List<Circuit> OrderByDependency(ACIRDocument doc)
    {
        var circuits = doc.Circuits;
        var circuitsByName = circuits.ToDictionary(c => c.Name, StringComparer.Ordinal);

        // Build dependency graph: circuit -> circuits it depends on (instantiates)
        var dependencies = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var circuit in circuits)
        {
            dependencies[circuit.Name] = new HashSet<string>(StringComparer.Ordinal);
            if (circuit.Fill?.Instances is not null)
            {
                foreach (var instance in circuit.Fill.Instances)
                {
                    // Only add dependency if the type exists in document and is not inline
                    if (circuitsByName.TryGetValue(instance.Type, out var target) && !target.Inline)
                    {
                        dependencies[circuit.Name].Add(instance.Type);
                    }
                }
            }
        }

        // Compute in-degrees (how many circuits depend on each circuit)
        var inDegree = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var circuit in circuits)
        {
            inDegree[circuit.Name] = 0;
        }
        foreach (var deps in dependencies.Values)
        {
            foreach (var dep in deps)
            {
                if (inDegree.ContainsKey(dep))
                {
                    inDegree[dep]++;
                }
            }
        }

        // Kahn's algorithm: start with nodes that have no dependents
        var queue = new Queue<string>();
        foreach (var circuit in circuits)
        {
            if (inDegree[circuit.Name] == 0)
            {
                queue.Enqueue(circuit.Name);
            }
        }

        var result = new List<Circuit>();
        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            result.Add(circuitsByName[name]);

            // Reduce in-degree for circuits this one depends on
            foreach (var dep in dependencies[name])
            {
                if (inDegree.ContainsKey(dep))
                {
                    inDegree[dep]--;
                    if (inDegree[dep] == 0)
                    {
                        queue.Enqueue(dep);
                    }
                }
            }
        }

        // If not all circuits were processed, there's a cycle (already caught by HierarchyValidator)
        // Just return circuits in original order as fallback
        if (result.Count < circuits.Count)
        {
            return circuits;
        }

        // Reverse to get dependency order (leaves first)
        result.Reverse();
        return result;
    }

    /// <summary>
    /// Emits a SPICE element line for a device declaration.
    /// </summary>
    /// <param name="device">Device to emit.</param>
    /// <param name="writer">Text writer for output.</param>
    /// <exception cref="InvalidOperationException">Thrown if device type is unknown or required terminals are missing.</exception>
    private static void EmitDevice(
        DeviceDeclaration device,
        TextWriter writer,
        IReadOnlyDictionary<string, DeviceModelResolution>? deviceModelMap
    )
    {
        var resolvedModel = ResolveDeviceModel(device, deviceModelMap);
        var useSubckt = resolvedModel?.IsSubckt ?? false;

        var spiceType = device.DeviceType.ToLowerInvariant() switch
        {
            "nmos" or "pmos" => useSubckt ? "X" : "M",
            "resistor" => "R",
            "capacitor" => "C",
            "inductor" => "L",
            "diode" => "D",
            _ => throw new InvalidOperationException($"Unknown device type: {device.DeviceType}"),
        };

        var sb = new StringBuilder();
        sb.Append(spiceType);
        sb.Append(device.Id);
        sb.Append(' ');

        // Terminal ordering and parameters depend on device type
        if (spiceType is "M" or "X")
        {
            EmitMosfetTerminalsAndParams(device, sb, deviceModelMap, useSubckt);
        }
        else if (spiceType is "R" or "C" or "L")
        {
            // Two-terminal: P N
            sb.Append(GetBinding(device, "P"));
            sb.Append(' ');
            sb.Append(GetBinding(device, "N"));
            sb.Append(' ');

            // Value parameter
            var valueKey = spiceType switch
            {
                "R" => "R",
                "C" => "C",
                "L" => "L",
                _ => throw new InvalidOperationException(),
            };
            if (device.Params.TryGetValue(valueKey, out var value))
            {
                sb.Append(value);
            }
        }
        else if (spiceType == "D")
        {
            // Diode: A K
            sb.Append(GetBinding(device, "A"));
            sb.Append(' ');
            sb.Append(GetBinding(device, "K"));
            sb.Append(' ');
            sb.Append(ResolveDeviceModelName(device, deviceModelMap, defaultModel: "D"));
        }

        writer.WriteLine(sb.ToString().TrimEnd());
    }

    /// <summary>
    /// Emits a circuit instance as a SPICE X-element.
    /// </summary>
    /// <param name="instance">Instance declaration.</param>
    /// <param name="targetCircuit">The circuit being instantiated.</param>
    /// <param name="resolution">Optional attach resolution for net name mapping.</param>
    /// <param name="writer">Text writer for output.</param>
    /// <remarks>
    /// Output format: X{id} {port1} {port2} ... {supply1} ... {ground1} ... {subckt_name}
    /// Port ordering matches subcircuit declaration: ports, supplies, grounds.
    /// </remarks>
    private static void EmitInstance(
        InstanceDeclaration instance,
        Circuit targetCircuit,
        CircuitResolutionResult? resolution,
        TextWriter writer
    )
    {
        var sb = new StringBuilder();
        sb.Append('X');
        sb.Append(instance.Id);
        sb.Append(' ');

        // Build port order: ports, supplies, grounds (matching subcircuit declaration)
        var portOrder = new List<string>();
        foreach (var port in targetCircuit.Ports)
        {
            portOrder.Add(port.Name);
        }
        foreach (var supply in targetCircuit.Supplies)
        {
            portOrder.Add(supply);
        }
        foreach (var ground in targetCircuit.Grounds)
        {
            portOrder.Add(ground);
        }

        // Emit port bindings in order
        foreach (var portName in portOrder)
        {
            string netName;
            if (instance.Bindings.TryGetValue(portName, out var boundNet))
            {
                // Use resolved net name if available
                netName =
                    resolution?.NetToRepresentative.GetValueOrDefault(boundNet, boundNet)
                    ?? boundNet;
            }
            else
            {
                // Port not bound - use the port name itself (may be auto-connected)
                netName = portName;
            }
            sb.Append(netName);
            sb.Append(' ');
        }

        // Subcircuit name
        sb.Append(targetCircuit.Name);

        writer.WriteLine(sb.ToString().TrimEnd());
    }

    /// <summary>
    /// Expands an inline circuit by embedding its devices with hierarchical naming.
    /// </summary>
    /// <param name="instance">Instance declaration being expanded.</param>
    /// <param name="inlineCircuit">The inline circuit to expand.</param>
    /// <param name="resolution">Optional attach resolution for net name mapping.</param>
    /// <param name="deviceModelMap">Optional device model resolution map.</param>
    /// <param name="writer">Text writer for output.</param>
    /// <remarks>
    /// Naming conventions:
    /// - Device IDs: {instanceId}__{deviceId} (e.g., dp__M_N becomes M_dp__M_N)
    /// - Internal nets: {instanceId}__{netId}
    /// - Port bindings: substituted with parent-level nets
    /// </remarks>
    private static void ExpandInlineCircuit(
        InstanceDeclaration instance,
        Circuit inlineCircuit,
        CircuitResolutionResult? resolution,
        IReadOnlyDictionary<string, DeviceModelResolution>? deviceModelMap,
        TextWriter writer
    )
    {
        // Build port-to-net substitution map
        var netSubstitutions = BuildNetSubstitutions(instance, inlineCircuit);

        // Build set of internal nets (not ports, supplies, or grounds)
        var internalNets = new HashSet<string>(StringComparer.Ordinal);
        if (inlineCircuit.Fill?.Nets is not null)
        {
            foreach (var net in inlineCircuit.Fill.Nets)
            {
                internalNets.Add(net.Id);
            }
        }

        // Emit devices with hierarchical naming
        if (inlineCircuit.Fill?.Devices is not null)
        {
            foreach (
                var device in inlineCircuit.Fill.Devices.OrderBy(d => d.Id, StringComparer.Ordinal)
            )
            {
                EmitInlineDevice(
                    device,
                    instance.Id,
                    netSubstitutions,
                    internalNets,
                    resolution,
                    deviceModelMap,
                    writer
                );
            }
        }
    }

    /// <summary>
    /// Builds net name substitution map for inline expansion.
    /// </summary>
    private static Dictionary<string, string> BuildNetSubstitutions(
        InstanceDeclaration instance,
        Circuit inlineCircuit
    )
    {
        var substitutions = new Dictionary<string, string>(StringComparer.Ordinal);

        // Map port names to bound nets
        foreach (var port in inlineCircuit.Ports)
        {
            if (instance.Bindings.TryGetValue(port.Name, out var boundNet))
            {
                substitutions[port.Name] = boundNet;
            }
        }

        // Map supplies to bound nets
        foreach (var supply in inlineCircuit.Supplies)
        {
            if (instance.Bindings.TryGetValue(supply, out var boundNet))
            {
                substitutions[supply] = boundNet;
            }
        }

        // Map grounds to bound nets
        foreach (var ground in inlineCircuit.Grounds)
        {
            if (instance.Bindings.TryGetValue(ground, out var boundNet))
            {
                substitutions[ground] = boundNet;
            }
        }

        return substitutions;
    }

    /// <summary>
    /// Emits a device from an inline circuit with hierarchical naming.
    /// </summary>
    private static void EmitInlineDevice(
        DeviceDeclaration device,
        string instanceId,
        Dictionary<string, string> netSubstitutions,
        HashSet<string> internalNets,
        CircuitResolutionResult? resolution,
        IReadOnlyDictionary<string, DeviceModelResolution>? deviceModelMap,
        TextWriter writer
    )
    {
        var resolvedModel = ResolveDeviceModel(device, deviceModelMap);
        var useSubckt = resolvedModel?.IsSubckt ?? false;

        var spiceType = device.DeviceType.ToLowerInvariant() switch
        {
            "nmos" or "pmos" => useSubckt ? "X" : "M",
            "resistor" => "R",
            "capacitor" => "C",
            "inductor" => "L",
            "diode" => "D",
            _ => throw new InvalidOperationException($"Unknown device type: {device.DeviceType}"),
        };

        var sb = new StringBuilder();
        sb.Append(spiceType);
        sb.Append(instanceId);
        sb.Append("__");
        sb.Append(device.Id);
        sb.Append(' ');

        // Emit terminals with net substitution
        if (spiceType is "M" or "X")
        {
            EmitInlineMosfetTerminalsAndParams(
                device,
                instanceId,
                netSubstitutions,
                internalNets,
                resolution,
                sb,
                deviceModelMap,
                useSubckt
            );
        }
        else if (spiceType is "R" or "C" or "L")
        {
            // Two-terminal: P N
            sb.Append(
                SubstituteNet(
                    GetBinding(device, "P"),
                    instanceId,
                    netSubstitutions,
                    internalNets,
                    resolution
                )
            );
            sb.Append(' ');
            sb.Append(
                SubstituteNet(
                    GetBinding(device, "N"),
                    instanceId,
                    netSubstitutions,
                    internalNets,
                    resolution
                )
            );
            sb.Append(' ');

            var valueKey = spiceType switch
            {
                "R" => "R",
                "C" => "C",
                "L" => "L",
                _ => throw new InvalidOperationException(),
            };
            if (device.Params.TryGetValue(valueKey, out var value))
            {
                sb.Append(value);
            }
        }
        else if (spiceType == "D")
        {
            // Diode: A K
            sb.Append(
                SubstituteNet(
                    GetBinding(device, "A"),
                    instanceId,
                    netSubstitutions,
                    internalNets,
                    resolution
                )
            );
            sb.Append(' ');
            sb.Append(
                SubstituteNet(
                    GetBinding(device, "K"),
                    instanceId,
                    netSubstitutions,
                    internalNets,
                    resolution
                )
            );
            sb.Append(' ');
            sb.Append(ResolveDeviceModelName(device, deviceModelMap, defaultModel: "D"));
        }

        writer.WriteLine(sb.ToString().TrimEnd());
    }

    /// <summary>
    /// Emits MOSFET terminals and params for inline expansion.
    /// </summary>
    private static void EmitInlineMosfetTerminalsAndParams(
        DeviceDeclaration device,
        string instanceId,
        Dictionary<string, string> netSubstitutions,
        HashSet<string> internalNets,
        CircuitResolutionResult? resolution,
        StringBuilder sb,
        IReadOnlyDictionary<string, DeviceModelResolution>? deviceModelMap,
        bool useSubckt
    )
    {
        // MOSFET terminal ordering: D G S B
        sb.Append(
            SubstituteNet(
                GetBinding(device, "D"),
                instanceId,
                netSubstitutions,
                internalNets,
                resolution
            )
        );
        sb.Append(' ');
        sb.Append(
            SubstituteNet(
                GetBinding(device, "G"),
                instanceId,
                netSubstitutions,
                internalNets,
                resolution
            )
        );
        sb.Append(' ');
        sb.Append(
            SubstituteNet(
                GetBinding(device, "S"),
                instanceId,
                netSubstitutions,
                internalNets,
                resolution
            )
        );
        sb.Append(' ');
        sb.Append(
            SubstituteNet(
                GetBinding(device, "B"),
                instanceId,
                netSubstitutions,
                internalNets,
                resolution
            )
        );
        sb.Append(' ');

        // Model name
        sb.Append(ResolveDeviceModelName(device, deviceModelMap, defaultModel: device.DeviceType));
        sb.Append(' ');

        // Parameters: W, L, m
        if (device.Params.TryGetValue("W", out var w))
        {
            sb.Append(useSubckt ? $"w={w} " : $"W={w} ");
        }
        if (device.Params.TryGetValue("L", out var l))
        {
            sb.Append(useSubckt ? $"l={l} " : $"L={l} ");
        }
        if (device.Params.TryGetValue("M", out var m))
        {
            sb.Append(useSubckt ? $"mult={m}" : $"m={m}");
        }
    }

    /// <summary>
    /// Substitutes a net name for inline expansion.
    /// </summary>
    /// <param name="netName">Original net name from inline circuit.</param>
    /// <param name="instanceId">Instance ID for hierarchical prefix.</param>
    /// <param name="substitutions">Port/supply/ground substitution map.</param>
    /// <param name="internalNets">Set of internal net names in the inline circuit.</param>
    /// <param name="resolution">Optional attach resolution.</param>
    /// <returns>Substituted net name.</returns>
    private static string SubstituteNet(
        string netName,
        string instanceId,
        Dictionary<string, string> substitutions,
        HashSet<string> internalNets,
        CircuitResolutionResult? resolution
    )
    {
        // Check if this is a port/supply/ground that should be substituted
        if (substitutions.TryGetValue(netName, out var boundNet))
        {
            // Use resolved net name if available
            return resolution?.NetToRepresentative.GetValueOrDefault(boundNet, boundNet)
                ?? boundNet;
        }

        // Internal net: prefix with instance ID
        if (internalNets.Contains(netName))
        {
            return $"{instanceId}__{netName}";
        }

        // Unknown net - pass through (shouldn't happen in valid circuits)
        return netName;
    }

    /// <summary>
    /// Emits MOSFET terminal connections and parameters.
    /// </summary>
    /// <param name="device">MOSFET device declaration.</param>
    /// <param name="sb">StringBuilder to append to.</param>
    /// <param name="deviceModelMap">Optional map of PDK device names to resolved model names.</param>
    private static void EmitMosfetTerminalsAndParams(
        DeviceDeclaration device,
        StringBuilder sb,
        IReadOnlyDictionary<string, DeviceModelResolution>? deviceModelMap,
        bool useSubckt
    )
    {
        // MOSFET terminal ordering: D G S B
        sb.Append(GetBinding(device, "D"));
        sb.Append(' ');
        sb.Append(GetBinding(device, "G"));
        sb.Append(' ');
        sb.Append(GetBinding(device, "S"));
        sb.Append(' ');
        sb.Append(GetBinding(device, "B"));
        sb.Append(' ');

        // Model name (resolved PDK model or generic)
        sb.Append(ResolveDeviceModelName(device, deviceModelMap, defaultModel: device.DeviceType));
        sb.Append(' ');

        // Parameters: W, L, m
        if (device.Params.TryGetValue("W", out var w))
        {
            sb.Append(useSubckt ? $"w={w} " : $"W={w} ");
        }
        if (device.Params.TryGetValue("L", out var l))
        {
            sb.Append(useSubckt ? $"l={l} " : $"L={l} ");
        }
        if (device.Params.TryGetValue("M", out var m))
        {
            sb.Append(useSubckt ? $"mult={m}" : $"m={m}");
        }
    }

    private static DeviceModelResolution? ResolveDeviceModel(
        DeviceDeclaration device,
        IReadOnlyDictionary<string, DeviceModelResolution>? deviceModelMap
    )
    {
        if (string.IsNullOrWhiteSpace(device.PdkDevice) || deviceModelMap is null)
        {
            return null;
        }

        return deviceModelMap.TryGetValue(device.PdkDevice, out var resolution) ? resolution : null;
    }

    private static string ResolveDeviceModelName(
        DeviceDeclaration device,
        IReadOnlyDictionary<string, DeviceModelResolution>? deviceModelMap,
        string defaultModel
    )
    {
        var resolved = ResolveDeviceModel(device, deviceModelMap);
        if (resolved is not null && !string.IsNullOrWhiteSpace(resolved.ModelName))
        {
            return resolved.ModelName;
        }

        if (!string.IsNullOrWhiteSpace(device.PdkDevice))
        {
            return device.PdkDevice;
        }

        return defaultModel;
    }

    /// <summary>
    /// Gets the net name bound to a device terminal.
    /// </summary>
    /// <param name="device">Device declaration.</param>
    /// <param name="terminal">Terminal name (D, G, S, B for MOSFETs; P, N for passives).</param>
    /// <returns>Net name bound to the terminal.</returns>
    /// <exception cref="InvalidOperationException">Thrown if terminal is not bound.</exception>
    private static string GetBinding(DeviceDeclaration device, string terminal)
    {
        if (device.Bindings.TryGetValue(terminal, out var net))
        {
            return net;
        }
        throw new InvalidOperationException(
            $"Device '{device.Id}' missing required terminal '{terminal}'."
        );
    }

    /// <summary>
    /// Emits harness elements (voltage sources, loads, source impedances).
    /// </summary>
    /// <param name="circuit">Circuit containing harness block.</param>
    /// <param name="writer">Text writer for output.</param>
    private static void EmitHarness(Circuit circuit, TextWriter writer)
    {
        var harness = circuit.Harness!;

        // Supply voltage sources
        foreach (var supply in harness.Supplies)
        {
            writer.WriteLine($"V{supply.Net} {supply.Net} 0 DC {supply.Value}");
        }

        // Bias voltage sources (DC only, no AC)
        foreach (var bias in harness.Biases)
        {
            writer.WriteLine($"V{bias.Net} {bias.Net} 0 DC {bias.Value}");
        }

        // Input sources - simplified: DC bias with AC stimulus
        foreach (var source in harness.Sources)
        {
            // Default to mid-supply bias with AC stimulus
            writer.WriteLine($"V{source.Net} {source.Net} 0 DC 0.9 AC 1");
            if (source.Z is not null)
            {
                writer.WriteLine($"R{source.Net}_Z {source.Net}_int {source.Net} {source.Z}");
            }
        }

        // Load elements
        foreach (var load in harness.Loads)
        {
            for (int i = 0; i < load.Elements.Count; i++)
            {
                var element = load.Elements[i];
                var suffix = load.Elements.Count > 1 ? $"_{i}" : "";

                if (element.Type == "C")
                {
                    writer.WriteLine($"C{load.Net}_load{suffix} {load.Net} 0 {element.Value}");
                }
                else if (element.Type == "R")
                {
                    writer.WriteLine($"R{load.Net}_load{suffix} {load.Net} 0 {element.Value}");
                }
            }
        }
    }

    /// <summary>
    /// Emits the DUT instantiation line (X-element).
    /// </summary>
    /// <param name="circuit">Circuit to instantiate.</param>
    /// <param name="writer">Text writer for output.</param>
    private static void EmitDutInstantiation(Circuit circuit, TextWriter writer)
    {
        // Build port list for subcircuit instantiation
        var portList = new List<string>();
        foreach (var port in circuit.Ports)
        {
            portList.Add(port.Name);
        }
        foreach (var supply in circuit.Supplies)
        {
            portList.Add(supply);
        }
        foreach (var ground in circuit.Grounds)
        {
            portList.Add(ground);
        }

        writer.WriteLine($"XDUT {string.Join(" ", portList)} {circuit.Name}");
    }

    /// <summary>
    /// Emits analysis commands based on bench type.
    /// </summary>
    /// <param name="bench">Bench configuration.</param>
    /// <param name="writer">Text writer for output.</param>
    /// <remarks>
    /// Bench type is inferred from the bench name:
    /// - "AC" → AC sweep (op + ac dec)
    /// - "STEP" or "TRAN" → Transient analysis (op + tran)
    /// - Default → DC operating point only
    /// </remarks>
    private static void EmitAnalysis(BenchConfig bench, TextWriter writer)
    {
        writer.WriteLine(".control");

        // Determine analysis type from bench name
        var benchName = bench.Name.ToUpperInvariant();
        if (benchName.Contains("AC"))
        {
            writer.WriteLine("op");
            writer.WriteLine("ac dec 100 1 10G");
        }
        else if (benchName.Contains("STEP") || benchName.Contains("TRAN"))
        {
            writer.WriteLine("op");
            writer.WriteLine("tran 1n 100n");
        }
        else if (benchName.Contains("DC"))
        {
            writer.WriteLine("op");
        }
        else
        {
            // Default to DC operating point
            writer.WriteLine("op");
        }

        writer.WriteLine("quit");
        writer.WriteLine(".endc");
    }

    /// <summary>
    /// Determines which generic MOSFET models are needed based on device declarations.
    /// </summary>
    /// <param name="circuit">The circuit to analyze.</param>
    /// <returns>Set of generic model names that need definitions.</returns>
    private static HashSet<string> GetRequiredGenericModels(Circuit circuit)
    {
        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (circuit.Fill?.Devices is null)
        {
            return required;
        }

        foreach (var device in circuit.Fill.Devices)
        {
            var modelName = device.PdkDevice ?? device.DeviceType;
            if (GenericMosfetModels.Contains(modelName))
            {
                required.Add(modelName.ToLowerInvariant());
            }
        }

        return required;
    }

    /// <summary>
    /// Emits Level-1 MOSFET model definitions for ngspice simulation.
    /// </summary>
    /// <param name="models">Set of model names to emit (nmos, pmos).</param>
    /// <param name="writer">Text writer for output.</param>
    private static void EmitGenericModels(HashSet<string> models, TextWriter writer)
    {
        writer.WriteLine("* Generic MOSFET models for simulation");
        foreach (var model in models.OrderBy(m => m, StringComparer.Ordinal))
        {
            var modelLine = model switch
            {
                "nmos" => ".model nmos nmos level=1 vto=0.5 kp=120u gamma=0.4 phi=0.65 lambda=0.04",
                "pmos" => ".model pmos pmos level=1 vto=-0.5 kp=40u gamma=0.4 phi=0.65 lambda=0.05",
                _ => null,
            };
            if (modelLine is not null)
            {
                writer.WriteLine(modelLine);
            }
        }
    }
}

/// <summary>
/// Result of SPICE emission containing paths to generated files.
/// </summary>
public sealed class SpiceEmitResult
{
    /// <summary>
    /// Paths to generated design netlist files.
    /// </summary>
    public List<string> DesignPaths { get; } = new();

    /// <summary>
    /// Paths to generated testbench files.
    /// </summary>
    public List<string> TestbenchPaths { get; } = new();
}

/// <summary>
/// Result of validated SPICE emission containing validation result and generated files.
/// </summary>
public sealed class ValidatedEmitResult
{
    /// <summary>
    /// Validation result containing any errors or warnings.
    /// </summary>
    public required ValidationResult Validation { get; init; }

    /// <summary>
    /// Emission result containing paths to generated files (empty if validation failed).
    /// </summary>
    public required SpiceEmitResult Emit { get; init; }

    /// <summary>
    /// True if validation passed and files were emitted.
    /// </summary>
    public bool Success => Validation.IsValid;
}
