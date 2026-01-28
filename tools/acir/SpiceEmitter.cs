using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
        "level1_nmos",
        "level1_pmos",
    };

    private static readonly Regex NumericLiteralPattern = new(
        @"^-?\d+\.?\d*(?:[eE][+\-]?\d+)?[fpnumkMGT]?[A-Za-z]*$",
        RegexOptions.Compiled
    );
    private static readonly Regex SizeFieldReferencePattern = new(
        @"\b(?<size>[A-Za-z_][A-Za-z0-9_]*)\.(?<field>[A-Za-z_][A-Za-z0-9_]*)\b",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Emits a SPICE subcircuit definition for an EL-level circuit.
    /// </summary>
    /// <param name="circuit">The circuit to emit (must be EL level).</param>
    /// <param name="writer">Text writer for output.</param>
    /// <param name="deviceModelMap">Optional map of PDK device names to resolved model definitions.</param>
    /// <param name="document">Optional ACIR document for resolving instance types.</param>
    /// <param name="resolution">Optional attach resolution result for resolved net names.</param>
    /// <param name="backend">Target SPICE backend for SI prefix formatting (default: ngspice).</param>
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
        CircuitResolutionResult? resolution = null,
        BenchBackendType backend = BenchBackendType.Ngspice
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
        // Ports are already desugared to scalar types by BundleDesugarer
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

        IReadOnlyDictionary<string, PrimitiveDefinition>? primitivesByName = null;
        if (document is not null)
        {
            primitivesByName = document.Primitives.ToDictionary(
                p => p.Name,
                StringComparer.Ordinal
            );
        }

        // Build subcircuit parameter defaults.
        // ngspice requires parameters to be declared on the .subckt line (params: ...),
        // even if they will always be overridden at instantiation.
        var paramParts = new List<string>();
        foreach (var param in circuit.Parameters.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            var expr = ParamValueToExpression(param.Default) ?? "0";
            paramParts.Add($"{param.Name}={RenderSpiceExpression(expr, backend)}");
        }

        if (primitivesByName is not null)
        {
            var sizeDefaults = BuildSizeParamDefaults(circuit, primitivesByName, backend);
            foreach (var entry in sizeDefaults.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                paramParts.Add($"{entry.Key}={entry.Value}");
            }
        }

        var paramSuffix = paramParts.Count > 0 ? " params: " + string.Join(" ", paramParts) : "";

        writer.WriteLine($".subckt {circuit.Name} {string.Join(" ", portList)}{paramSuffix}");
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
            if (primitivesByName is null)
            {
                throw new InvalidOperationException(
                    "Primitive definitions are required for device emission. Provide the ACIR document when emitting."
                );
            }

            var localSizeBindings = BuildLocalSizeBindings(circuit);
            foreach (var device in circuit.Fill.Devices.OrderBy(d => d.Id, StringComparer.Ordinal))
            {
                EmitDevice(
                    device,
                    writer,
                    deviceModelMap,
                    primitivesByName,
                    localSizeBindings,
                    backend
                );
            }
        }

        // Emit circuit instances as X-elements or inline expansion
        if (circuit.Fill?.Instances.Count > 0 && document is not null)
        {
            var circuitsByName = document.Circuits.ToDictionary(
                c => c.Name,
                StringComparer.Ordinal
            );
            primitivesByName ??= document.Primitives.ToDictionary(
                p => p.Name,
                StringComparer.Ordinal
            );

            bool hasEmittedCircuitInstancesHeader = false;

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
                            hierarchyPath: new List<string>(),
                            parentNetSubstitutions: new Dictionary<string, string>(
                                StringComparer.Ordinal
                            ),
                            parentParamBindings: new Dictionary<string, string>(
                                StringComparer.Ordinal
                            ),
                            parentSizeBindings: new Dictionary<string, SizePack>(
                                StringComparer.Ordinal
                            ),
                            circuitsByName,
                            resolution,
                            deviceModelMap,
                            primitivesByName,
                            writer,
                            backend
                        );
                    }
                    else
                    {
                        // Non-inline: emit as X-element
                        if (!hasEmittedCircuitInstancesHeader)
                        {
                            writer.WriteLine();
                            writer.WriteLine("* Circuit instances");
                            hasEmittedCircuitInstancesHeader = true;
                        }
                        EmitInstance(instance, targetCircuit, resolution, writer, backend);
                    }
                }
            }
        }

        writer.WriteLine();
        writer.WriteLine($".ends {circuit.Name}");
    }

    /// <summary>
    /// Emits a SPICE testbench for a given bench definition.
    /// </summary>
    /// <param name="circuit">The circuit containing the bench.</param>
    /// <param name="bench">The bench definition.</param>
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
        BenchDefinition bench,
        string designPath,
        TextWriter writer,
        BenchBackendType backend = BenchBackendType.Ngspice,
        ACIRDocument? document = null
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
        var primitivesByName = document?.Primitives.ToDictionary(
            p => p.Name,
            StringComparer.Ordinal
        );
        var genericModels = GetRequiredGenericModels(circuit, primitivesByName);
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
    /// <param name="workspaceRoot">Optional workspace root for include resolution.</param>
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

            // Inline circuits are expanded into their parents and do not emit standalone .subckt files.
            if (circuit.Inline)
            {
                continue;
            }

            var includeResolution = includeResolver?.Resolve(circuit, backend, doc);
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
                    circuitResolution,
                    backend
                );
            }
            result.DesignPaths.Add(designPath);
        }

        // Emit testbenches after all design files are emitted (for hierarchical dependencies)
        foreach (var circuit in orderedCircuits.Where(c => c.Level == ACIRLevel.EL))
        {
            var benchDefinitions = BenchDefinitionResolver.ResolveForCircuit(doc, circuit);
            if (benchDefinitions.Count == 0)
            {
                continue;
            }

            var includeResolution = includeResolver?.Resolve(circuit, backend, doc);
            foreach (var bench in benchDefinitions)
            {
                var files = ACIRBenchAdapter.GenerateTestbench(
                    circuit,
                    bench,
                    backend,
                    outputDir,
                    workspaceRoot,
                    includeResolution,
                    result.DesignPaths,
                    doc
                );
                result.TestbenchPaths.Add(files.NetlistPath);
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
    /// <param name="workspaceRoot">Optional workspace root for include resolution.</param>
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
            var circuitValidation = EmissionValidator.Validate(circuit, doc);
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
    /// Delegates to HierarchyValidator.GetTopologicalOrder with excludeInline=true
    /// since inline circuits are expanded in place rather than emitted as subcircuits.
    /// </remarks>
    internal static List<Circuit> OrderByDependency(ACIRDocument doc)
    {
        return HierarchyValidator.GetTopologicalOrder(doc.Circuits, excludeInline: true);
    }

    /// <summary>
    /// Emits a SPICE element line for a device declaration.
    /// </summary>
    /// <param name="device">Device to emit.</param>
    /// <param name="writer">Text writer for output.</param>
    /// <param name="deviceModelMap">Optional map of PDK device names to resolved model definitions.</param>
    /// <param name="primitivesByName">Primitive definitions keyed by name.</param>
    /// <param name="sizeBindings">Local size bindings for parameter expansion.</param>
    /// <param name="backend">Target SPICE backend for SI prefix formatting.</param>
    /// <exception cref="InvalidOperationException">Thrown if device type is unknown or required terminals are missing.</exception>
    private static void EmitDevice(
        DeviceDeclaration device,
        TextWriter writer,
        IReadOnlyDictionary<string, DeviceModelResolution>? deviceModelMap,
        IReadOnlyDictionary<string, PrimitiveDefinition> primitivesByName,
        IReadOnlyDictionary<string, SizePack> sizeBindings,
        BenchBackendType backend
    )
    {
        if (!primitivesByName.TryGetValue(device.Primitive, out var primitive))
        {
            throw new InvalidOperationException(
                $"Device '{device.Id}' references undefined primitive '{device.Primitive}'."
            );
        }

        var deviceParams = PrimitiveResolver.BuildParamExpressions(device, primitive, sizeBindings);
        var resolvedModel = ResolveDeviceModel(primitive.Device, deviceModelMap);
        var modelName = ResolveDeviceModelName(primitive.Device, resolvedModel);
        var useSubckt = resolvedModel?.IsSubckt ?? false;

        var deviceKind = device.DeviceType.ToLowerInvariant();
        var isBuiltinPassive =
            !useSubckt
            && (deviceKind is "resistor" or "capacitor" or "inductor")
            && modelName.Equals(deviceKind, StringComparison.OrdinalIgnoreCase);
        var paramExpressions = deviceParams;
        string? passiveValue = null;
        if (isBuiltinPassive)
        {
            var key = deviceKind switch
            {
                "resistor" => "R",
                "capacitor" => "C",
                "inductor" => "L",
                _ => null,
            };
            if (key is not null && deviceParams.TryGetValue(key, out var expr))
            {
                passiveValue = RenderSpiceExpression(expr, backend);
                paramExpressions = deviceParams
                    .Where(kvp => !kvp.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal);
            }
        }
        var spiceType = useSubckt
            ? "X"
            : deviceKind switch
            {
                "nmos" or "pmos" => "M",
                "resistor" => "R",
                "capacitor" => "C",
                "inductor" => "L",
                "diode" => "D",
                _ => throw new InvalidOperationException(
                    $"Unknown device type: {device.DeviceType}"
                ),
            };

        var sb = new StringBuilder();
        sb.Append(spiceType);
        sb.Append(device.Id);
        sb.Append(' ');

        switch (deviceKind)
        {
            case "nmos":
            case "pmos":
                sb.Append(GetBinding(device, "D"));
                sb.Append(' ');
                sb.Append(GetBinding(device, "G"));
                sb.Append(' ');
                sb.Append(GetBinding(device, "S"));
                sb.Append(' ');
                sb.Append(GetBinding(device, "B"));
                sb.Append(' ');
                break;
            case "resistor":
            case "capacitor":
            case "inductor":
                sb.Append(GetBinding(device, "P"));
                sb.Append(' ');
                sb.Append(GetBinding(device, "N"));
                sb.Append(' ');
                break;
            case "diode":
                sb.Append(GetBinding(device, "A"));
                sb.Append(' ');
                sb.Append(GetBinding(device, "K"));
                sb.Append(' ');
                break;
        }

        if (isBuiltinPassive)
        {
            if (!string.IsNullOrWhiteSpace(passiveValue))
            {
                sb.Append(passiveValue);
            }
        }
        else
        {
            sb.Append(modelName);
        }

        AppendParamAssignments(sb, paramExpressions, expr => RenderSpiceExpression(expr, backend));

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
        TextWriter writer,
        BenchBackendType backend
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
            else if (
                resolution?.TerminalToNet.TryGetValue(
                    $"{instance.Id}.{portName}",
                    out var resolvedNet
                ) == true
            )
            {
                netName = resolvedNet;
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

        var paramExpressions = BuildInstanceParamExpressions(instance);
        AppendParamAssignments(sb, paramExpressions, expr => RenderSpiceExpression(expr, backend));

        writer.WriteLine(sb.ToString().TrimEnd());
    }

    /// <summary>
    /// Expands an inline circuit by embedding its devices and nested instances with hierarchical naming.
    /// </summary>
    /// <param name="instance">Instance declaration being expanded.</param>
    /// <param name="inlineCircuit">The inline circuit to expand.</param>
    /// <param name="hierarchyPath">Path of instance IDs leading to this expansion (empty at top level).</param>
    /// <param name="parentNetSubstitutions">Net substitutions from parent context (for composing through levels).</param>
    /// <param name="parentParamBindings">Parameter bindings from parent context.</param>
    /// <param name="parentSizeBindings">Size pack bindings from parent context.</param>
    /// <param name="circuitsByName">Dictionary of all circuits for resolving nested instance types.</param>
    /// <param name="resolution">Optional attach resolution for net name mapping.</param>
    /// <param name="deviceModelMap">Optional device model resolution map.</param>
    /// <param name="writer">Text writer for output.</param>
    /// <remarks>
    /// Naming conventions:
    /// - Device IDs: {hierarchy}__{deviceId} (e.g., outer__inner__M1)
    /// - Internal nets: {hierarchy}__{netId}
    /// - Port bindings: substituted with parent-level nets, composed through hierarchy
    ///
    /// Supports recursive expansion of nested inline circuits.
    /// Non-inline instances within inline circuits are emitted as X-elements with hierarchical naming.
    /// </remarks>
    private static void ExpandInlineCircuit(
        InstanceDeclaration instance,
        Circuit inlineCircuit,
        IReadOnlyList<string> hierarchyPath,
        Dictionary<string, string> parentNetSubstitutions,
        IReadOnlyDictionary<string, string> parentParamBindings,
        IReadOnlyDictionary<string, SizePack> parentSizeBindings,
        Dictionary<string, Circuit> circuitsByName,
        CircuitResolutionResult? resolution,
        IReadOnlyDictionary<string, DeviceModelResolution>? deviceModelMap,
        IReadOnlyDictionary<string, PrimitiveDefinition> primitivesByName,
        TextWriter writer,
        BenchBackendType backend
    )
    {
        // Build current hierarchy path by appending this instance's ID
        var currentPath = new List<string>(hierarchyPath) { instance.Id };

        // Build port-to-net substitution map, composing with parent substitutions
        var localSubstitutions = BuildNetSubstitutions(instance, inlineCircuit, resolution);
        var netSubstitutions = ComposeNetSubstitutions(parentNetSubstitutions, localSubstitutions);

        // Build parameter bindings: compose parent bindings with local overrides
        var paramBindings = ComposeParameterBindings(instance, inlineCircuit, parentParamBindings);

        // Build size bindings: compose parent bindings with local overrides
        var sizeBindings = ComposeSizeBindings(instance, inlineCircuit, parentSizeBindings);

        var expressionContext = new ExpressionContext(paramBindings, sizeBindings);

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
                    currentPath,
                    netSubstitutions,
                    internalNets,
                    resolution,
                    deviceModelMap,
                    primitivesByName,
                    expressionContext,
                    sizeBindings,
                    writer,
                    backend
                );
            }
        }

        // Process nested instances
        if (inlineCircuit.Fill?.Instances is not null)
        {
            foreach (
                var nestedInstance in inlineCircuit.Fill.Instances.OrderBy(
                    i => i.Id,
                    StringComparer.Ordinal
                )
            )
            {
                if (!circuitsByName.TryGetValue(nestedInstance.Type, out var nestedCircuit))
                {
                    // Unknown circuit type - skip (validation should catch this)
                    continue;
                }

                if (nestedCircuit.Inline)
                {
                    // Recursively expand nested inline circuit
                    writer.WriteLine();
                    writer.WriteLine(
                        $"* Inline expansion of {BuildHierarchyPrefix(currentPath)}__{nestedInstance.Id} : {nestedInstance.Type}"
                    );
                    ExpandInlineCircuit(
                        nestedInstance,
                        nestedCircuit,
                        currentPath,
                        netSubstitutions,
                        paramBindings,
                        sizeBindings,
                        circuitsByName,
                        resolution,
                        deviceModelMap,
                        primitivesByName,
                        writer,
                        backend
                    );
                }
                else
                {
                    // Emit non-inline instance as X-element with hierarchical naming
                    EmitInlineInstance(
                        nestedInstance,
                        currentPath,
                        nestedCircuit,
                        netSubstitutions,
                        internalNets,
                        resolution,
                        expressionContext,
                        writer,
                        backend
                    );
                }
            }
        }
    }

    /// <summary>
    /// Composes two net substitution maps. Local substitutions are resolved through parent substitutions.
    /// </summary>
    private static Dictionary<string, string> ComposeNetSubstitutions(
        Dictionary<string, string> parentSubstitutions,
        Dictionary<string, string> localSubstitutions
    )
    {
        var composed = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (name, boundNet) in localSubstitutions)
        {
            // If the bound net is itself in parent substitutions, resolve it
            if (parentSubstitutions.TryGetValue(boundNet, out var parentBoundNet))
            {
                composed[name] = parentBoundNet;
            }
            else
            {
                composed[name] = boundNet;
            }
        }

        return composed;
    }

    /// <summary>
    /// Composes parameter bindings by merging parent bindings with local circuit/instance bindings.
    /// </summary>
    private static Dictionary<string, string> ComposeParameterBindings(
        InstanceDeclaration instance,
        Circuit inlineCircuit,
        IReadOnlyDictionary<string, string> parentParamBindings
    )
    {
        var bindings = new Dictionary<string, string>(StringComparer.Ordinal);

        // Start with parent bindings.
        foreach (var (name, value) in parentParamBindings)
        {
            bindings[name] = value;
        }

        // Add circuit parameter defaults
        foreach (var param in inlineCircuit.Parameters)
        {
            var expr = ParamValueToExpression(param.Default);
            if (!string.IsNullOrWhiteSpace(expr))
            {
                bindings[param.Name] = expr;
            }
        }

        // Override with instance parameters
        foreach (var (name, paramValue) in instance.Params)
        {
            var expr = ParamValueToExpression(paramValue);
            if (!string.IsNullOrWhiteSpace(expr))
            {
                bindings[name] = expr;
            }
        }

        return bindings;
    }

    /// <summary>
    /// Composes size pack bindings by merging parent bindings with local circuit/instance bindings.
    /// </summary>
    private static Dictionary<string, SizePack> ComposeSizeBindings(
        InstanceDeclaration instance,
        Circuit inlineCircuit,
        IReadOnlyDictionary<string, SizePack> parentSizeBindings
    )
    {
        var bindings = new Dictionary<string, SizePack>(StringComparer.Ordinal);

        // Start with parent bindings
        foreach (var (name, pack) in parentSizeBindings)
        {
            bindings[name] = pack;
        }

        // Add circuit size pack defaults
        foreach (var size in inlineCircuit.Sizes)
        {
            if (size.Default is not null)
            {
                bindings[size.Name] = size.Default;
            }
        }

        if (inlineCircuit.Fill?.Sizes is { Count: > 0 })
        {
            foreach (var size in inlineCircuit.Fill.Sizes)
            {
                if (size.Default is not null)
                {
                    bindings[size.Name] = size.Default;
                }
            }
        }

        // Override with instance size packs
        foreach (var (name, pack) in instance.Sizes)
        {
            bindings[name] = pack;
        }

        return bindings;
    }

    /// <summary>
    /// Emits a non-inline instance within an inline circuit as an X-element with hierarchical naming.
    /// </summary>
    private static void EmitInlineInstance(
        InstanceDeclaration instance,
        IReadOnlyList<string> hierarchyPath,
        Circuit targetCircuit,
        Dictionary<string, string> netSubstitutions,
        HashSet<string> internalNets,
        CircuitResolutionResult? resolution,
        ExpressionContext expressionContext,
        TextWriter writer,
        BenchBackendType backend
    )
    {
        var sb = new StringBuilder();
        sb.Append('X');
        sb.Append(BuildHierarchyPrefix(hierarchyPath));
        sb.Append("__");
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

        // Emit port bindings in order, substituting nets
        foreach (var portName in portOrder)
        {
            string netName;
            if (instance.Bindings.TryGetValue(portName, out var boundNet))
            {
                netName = boundNet;
            }
            else
            {
                // Port not bound - use the port name itself
                netName = portName;
            }

            // Substitute the net through the hierarchy
            var substitutedNet = SubstituteNet(
                netName,
                hierarchyPath,
                netSubstitutions,
                internalNets,
                resolution
            );
            sb.Append(substitutedNet);
            sb.Append(' ');
        }

        // Subcircuit name
        sb.Append(targetCircuit.Name);

        var paramExpressions = BuildInstanceParamExpressions(instance);
        AppendParamAssignments(
            sb,
            paramExpressions,
            expr => RenderEvaluatedExpression(expressionContext, expr, backend)
        );

        writer.WriteLine(sb.ToString().TrimEnd());
    }

    private static IReadOnlyDictionary<string, SizePack> BuildLocalSizeBindings(Circuit circuit)
    {
        var bindings = new Dictionary<string, SizePack>(StringComparer.Ordinal);
        if (circuit.Fill?.Sizes is { Count: > 0 })
        {
            foreach (var size in circuit.Fill.Sizes)
            {
                if (size.Default is not null)
                {
                    bindings[size.Name] = size.Default;
                }
            }
        }
        return bindings;
    }

    /// <summary>
    /// Builds net name substitution map for inline expansion.
    /// </summary>
    private static Dictionary<string, string> BuildNetSubstitutions(
        InstanceDeclaration instance,
        Circuit inlineCircuit,
        CircuitResolutionResult? resolution
    )
    {
        var substitutions = new Dictionary<string, string>(StringComparer.Ordinal);

        // Map port names to bound nets
        ResolveBindings(
            inlineCircuit.Ports.Select(p => p.Name),
            instance,
            resolution,
            substitutions
        );

        // Map supplies to bound nets
        ResolveBindings(inlineCircuit.Supplies, instance, resolution, substitutions);

        // Map grounds to bound nets
        ResolveBindings(inlineCircuit.Grounds, instance, resolution, substitutions);

        return substitutions;
    }

    /// <summary>
    /// Resolves bindings for a collection of names and adds them to the substitutions map.
    /// </summary>
    private static void ResolveBindings(
        IEnumerable<string> names,
        InstanceDeclaration instance,
        CircuitResolutionResult? resolution,
        Dictionary<string, string> substitutions
    )
    {
        foreach (var name in names)
        {
            if (instance.Bindings.TryGetValue(name, out var boundNet))
            {
                substitutions[name] =
                    resolution?.NetToRepresentative.GetValueOrDefault(boundNet, boundNet)
                    ?? boundNet;
            }
            else if (
                resolution?.TerminalToNet.TryGetValue($"{instance.Id}.{name}", out var resolvedNet)
                == true
            )
            {
                substitutions[name] = resolvedNet;
            }
        }
    }

    /// <summary>
    /// Emits a device from an inline circuit with hierarchical naming.
    /// </summary>
    private static void EmitInlineDevice(
        DeviceDeclaration device,
        IReadOnlyList<string> hierarchyPath,
        Dictionary<string, string> netSubstitutions,
        HashSet<string> internalNets,
        CircuitResolutionResult? resolution,
        IReadOnlyDictionary<string, DeviceModelResolution>? deviceModelMap,
        IReadOnlyDictionary<string, PrimitiveDefinition> primitivesByName,
        ExpressionContext expressionContext,
        IReadOnlyDictionary<string, SizePack> sizeBindings,
        TextWriter writer,
        BenchBackendType backend
    )
    {
        if (!primitivesByName.TryGetValue(device.Primitive, out var primitive))
        {
            throw new InvalidOperationException(
                $"Device '{device.Id}' references undefined primitive '{device.Primitive}'."
            );
        }

        var deviceParams = PrimitiveResolver.BuildParamExpressions(device, primitive, sizeBindings);
        var resolvedModel = ResolveDeviceModel(primitive.Device, deviceModelMap);
        var modelName = ResolveDeviceModelName(primitive.Device, resolvedModel);
        var useSubckt = resolvedModel?.IsSubckt ?? false;

        var deviceKind = device.DeviceType.ToLowerInvariant();
        var isBuiltinPassive =
            !useSubckt
            && (deviceKind is "resistor" or "capacitor" or "inductor")
            && modelName.Equals(deviceKind, StringComparison.OrdinalIgnoreCase);
        var paramExpressions = deviceParams;
        string? passiveValue = null;
        if (isBuiltinPassive)
        {
            var key = deviceKind switch
            {
                "resistor" => "R",
                "capacitor" => "C",
                "inductor" => "L",
                _ => null,
            };
            if (key is not null && deviceParams.TryGetValue(key, out var expr))
            {
                passiveValue = RenderEvaluatedExpression(expressionContext, expr, backend);
                paramExpressions = deviceParams
                    .Where(kvp => !kvp.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal);
            }
        }
        var spiceType = useSubckt
            ? "X"
            : deviceKind switch
            {
                "nmos" or "pmos" => "M",
                "resistor" => "R",
                "capacitor" => "C",
                "inductor" => "L",
                "diode" => "D",
                _ => throw new InvalidOperationException(
                    $"Unknown device type: {device.DeviceType}"
                ),
            };

        var sb = new StringBuilder();
        sb.Append(spiceType);
        sb.Append(BuildHierarchyPrefix(hierarchyPath));
        sb.Append("__");
        sb.Append(device.Id);
        sb.Append(' ');

        switch (deviceKind)
        {
            case "nmos":
            case "pmos":
                sb.Append(
                    SubstituteNet(
                        GetBinding(device, "D"),
                        hierarchyPath,
                        netSubstitutions,
                        internalNets,
                        resolution
                    )
                );
                sb.Append(' ');
                sb.Append(
                    SubstituteNet(
                        GetBinding(device, "G"),
                        hierarchyPath,
                        netSubstitutions,
                        internalNets,
                        resolution
                    )
                );
                sb.Append(' ');
                sb.Append(
                    SubstituteNet(
                        GetBinding(device, "S"),
                        hierarchyPath,
                        netSubstitutions,
                        internalNets,
                        resolution
                    )
                );
                sb.Append(' ');
                sb.Append(
                    SubstituteNet(
                        GetBinding(device, "B"),
                        hierarchyPath,
                        netSubstitutions,
                        internalNets,
                        resolution
                    )
                );
                sb.Append(' ');
                break;
            case "resistor":
            case "capacitor":
            case "inductor":
                sb.Append(
                    SubstituteNet(
                        GetBinding(device, "P"),
                        hierarchyPath,
                        netSubstitutions,
                        internalNets,
                        resolution
                    )
                );
                sb.Append(' ');
                sb.Append(
                    SubstituteNet(
                        GetBinding(device, "N"),
                        hierarchyPath,
                        netSubstitutions,
                        internalNets,
                        resolution
                    )
                );
                sb.Append(' ');
                break;
            case "diode":
                sb.Append(
                    SubstituteNet(
                        GetBinding(device, "A"),
                        hierarchyPath,
                        netSubstitutions,
                        internalNets,
                        resolution
                    )
                );
                sb.Append(' ');
                sb.Append(
                    SubstituteNet(
                        GetBinding(device, "K"),
                        hierarchyPath,
                        netSubstitutions,
                        internalNets,
                        resolution
                    )
                );
                sb.Append(' ');
                break;
        }

        if (isBuiltinPassive)
        {
            if (!string.IsNullOrWhiteSpace(passiveValue))
            {
                sb.Append(passiveValue);
            }
        }
        else
        {
            sb.Append(modelName);
        }

        AppendParamAssignments(
            sb,
            paramExpressions,
            expr => RenderEvaluatedExpression(expressionContext, expr, backend)
        );

        writer.WriteLine(sb.ToString().TrimEnd());
    }

    private static string? ParamValueToExpression(ParamValue? value)
    {
        if (value is null)
        {
            return null;
        }

        return value.Numeric ?? value.Symbolic ?? value.Literal;
    }

    private static IReadOnlyDictionary<string, string> BuildInstanceParamExpressions(
        InstanceDeclaration instance
    )
    {
        var expressions = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (name, value) in instance.Params)
        {
            var expr = ParamValueToExpression(value);
            if (!string.IsNullOrWhiteSpace(expr))
            {
                expressions[name] = expr;
            }
        }

        foreach (var (sizeName, pack) in instance.Sizes)
        {
            foreach (var (field, expr) in pack.Entries)
            {
                if (!string.IsNullOrWhiteSpace(expr))
                {
                    expressions[EncodeSizeParamName(sizeName, field)] = expr;
                }
            }
        }

        return expressions;
    }

    private static void AppendParamAssignments(
        StringBuilder sb,
        IReadOnlyDictionary<string, string> paramExpressions,
        Func<string, string> renderValue
    )
    {
        foreach (var (name, expr) in paramExpressions.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var rendered = renderValue(expr);
            if (string.IsNullOrWhiteSpace(rendered))
            {
                continue;
            }

            sb.Append(' ');
            sb.Append(name);
            sb.Append('=');
            sb.Append(rendered);
        }
    }

    private static string RenderSpiceExpression(string expression, BenchBackendType backend)
    {
        var trimmed = NormalizeSizeFieldReferences(expression.Trim());
        if (trimmed.Length == 0)
        {
            return trimmed;
        }

        if (NumericLiteralPattern.IsMatch(trimmed))
        {
            return ACIRBenchAdapter.TransformValueForBackend(trimmed, backend);
        }

        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            return trimmed;
        }

        return $"{{{trimmed}}}";
    }

    private static string EncodeSizeParamName(string sizeName, string field)
    {
        return $"{sizeName}_{field}";
    }

    private static string NormalizeSizeFieldReferences(string expression)
    {
        return SizeFieldReferencePattern.Replace(
            expression,
            match => $"{match.Groups["size"].Value}_{match.Groups["field"].Value}"
        );
    }

    private static string RenderEvaluatedExpression(
        ExpressionContext context,
        string expression,
        BenchBackendType backend
    )
    {
        var trimmed = expression.Trim();
        if (trimmed.Length == 0)
        {
            return trimmed;
        }

        try
        {
            var evaluated = context.Evaluate(trimmed);
            return ACIRBenchAdapter.TransformValueForBackend(evaluated, backend);
        }
        catch
        {
            return RenderSpiceExpression(trimmed, backend);
        }
    }

    private static IEnumerable<string> EnumerateSizeFieldExpressions(Circuit circuit)
    {
        foreach (var parameter in circuit.Parameters)
        {
            var expr = ParamValueToExpression(parameter.Default);
            if (!string.IsNullOrWhiteSpace(expr))
            {
                yield return expr;
            }
        }

        foreach (var size in circuit.Sizes)
        {
            if (size.Default is null)
            {
                continue;
            }

            foreach (var expr in size.Default.Entries.Values)
            {
                if (!string.IsNullOrWhiteSpace(expr))
                {
                    yield return expr;
                }
            }
        }

        if (circuit.Fill?.Sizes is not null)
        {
            foreach (var size in circuit.Fill.Sizes)
            {
                if (size.Default is null)
                {
                    continue;
                }

                foreach (var expr in size.Default.Entries.Values)
                {
                    if (!string.IsNullOrWhiteSpace(expr))
                    {
                        yield return expr;
                    }
                }
            }
        }

        if (circuit.Fill?.Devices is not null)
        {
            foreach (var device in circuit.Fill.Devices)
            {
                if (device.Size is null)
                {
                    continue;
                }

                foreach (var expr in device.Size.Entries.Values)
                {
                    if (!string.IsNullOrWhiteSpace(expr))
                    {
                        yield return expr;
                    }
                }
            }
        }

        if (circuit.Fill?.Instances is not null)
        {
            foreach (var instance in circuit.Fill.Instances)
            {
                foreach (var pack in instance.Sizes.Values)
                {
                    foreach (var expr in pack.Entries.Values)
                    {
                        if (!string.IsNullOrWhiteSpace(expr))
                        {
                            yield return expr;
                        }
                    }
                }
            }
        }
    }

    private static void AddSizeFieldReferences(
        Dictionary<string, HashSet<string>> fieldsBySize,
        IEnumerable<string> expressions
    )
    {
        foreach (var expression in expressions)
        {
            foreach (Match match in SizeFieldReferencePattern.Matches(expression))
            {
                var sizeName = match.Groups["size"].Value;
                if (!fieldsBySize.TryGetValue(sizeName, out var fields))
                {
                    continue;
                }

                fields.Add(match.Groups["field"].Value);
            }
        }
    }

    private static IReadOnlyDictionary<string, string> BuildSizeParamDefaults(
        Circuit circuit,
        IReadOnlyDictionary<string, PrimitiveDefinition> primitivesByName,
        BenchBackendType backend
    )
    {
        if (circuit.Sizes.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        var fieldsBySize = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var size in circuit.Sizes)
        {
            fieldsBySize[size.Name] = new HashSet<string>(StringComparer.Ordinal);
            if (size.Default is not null)
            {
                foreach (var key in size.Default.Entries.Keys)
                {
                    fieldsBySize[size.Name].Add(key);
                }
            }
        }

        AddSizeFieldReferences(fieldsBySize, EnumerateSizeFieldExpressions(circuit));

        if (circuit.Fill?.Devices is not null)
        {
            foreach (var device in circuit.Fill.Devices)
            {
                if (string.IsNullOrWhiteSpace(device.SizeName))
                {
                    continue;
                }

                if (!fieldsBySize.TryGetValue(device.SizeName, out var fields))
                {
                    continue;
                }

                if (!primitivesByName.TryGetValue(device.Primitive, out var primitive))
                {
                    continue;
                }

                foreach (var field in PrimitiveResolver.GetSizeFields(primitive))
                {
                    fields.Add(field);
                }
            }
        }

        var defaults = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var size in circuit.Sizes)
        {
            if (!fieldsBySize.TryGetValue(size.Name, out var fields) || fields.Count == 0)
            {
                continue;
            }

            foreach (var field in fields.OrderBy(f => f, StringComparer.Ordinal))
            {
                var expr =
                    size.Default?.Entries.TryGetValue(field, out var value) == true ? value : "0";
                defaults[EncodeSizeParamName(size.Name, field)] = RenderSpiceExpression(
                    expr,
                    backend
                );
            }
        }

        return defaults;
    }

    /// <summary>
    /// Composes a hierarchy path into a flat naming prefix.
    /// E.g., ["outer", "inner"] → "outer__inner"
    /// </summary>
    private static string BuildHierarchyPrefix(IReadOnlyList<string> hierarchyPath)
    {
        return string.Join("__", hierarchyPath);
    }

    /// <summary>
    /// Substitutes a net name for inline expansion.
    /// </summary>
    /// <param name="netName">Original net name from inline circuit.</param>
    /// <param name="hierarchyPath">Hierarchy path of instance IDs for hierarchical prefix.</param>
    /// <param name="substitutions">Port/supply/ground substitution map.</param>
    /// <param name="internalNets">Set of internal net names in the inline circuit.</param>
    /// <param name="resolution">Optional attach resolution.</param>
    /// <returns>Substituted net name.</returns>
    private static string SubstituteNet(
        string netName,
        IReadOnlyList<string> hierarchyPath,
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

        // Internal net: prefix with hierarchy path
        if (internalNets.Contains(netName))
        {
            var prefix = BuildHierarchyPrefix(hierarchyPath);
            return $"{prefix}__{netName}";
        }

        // Unknown net - pass through (shouldn't happen in valid circuits)
        return netName;
    }

    private static DeviceModelResolution? ResolveDeviceModel(
        string deviceKey,
        IReadOnlyDictionary<string, DeviceModelResolution>? deviceModelMap
    )
    {
        if (string.IsNullOrWhiteSpace(deviceKey) || deviceModelMap is null)
        {
            return null;
        }

        return deviceModelMap.TryGetValue(deviceKey, out var resolution) ? resolution : null;
    }

    private static string ResolveDeviceModelName(
        string deviceKey,
        DeviceModelResolution? resolution
    )
    {
        if (resolution is not null && !string.IsNullOrWhiteSpace(resolution.ModelName))
        {
            return resolution.ModelName;
        }

        return deviceKey;
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
    /// <param name="bench">Bench definition.</param>
    /// <param name="writer">Text writer for output.</param>
    /// <remarks>
    /// Bench type is inferred from the bench name:
    /// - "AC" → AC sweep (op + ac dec)
    /// - "STEP" or "TRAN" → Transient analysis (op + tran)
    /// - Default → DC operating point only
    /// </remarks>
    private static void EmitAnalysis(BenchDefinition bench, TextWriter writer)
    {
        writer.WriteLine(".control");

        // Determine analysis type from bench name
        var benchName = (bench.Builtin ?? bench.Name).ToUpperInvariant();
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
    private static HashSet<string> GetRequiredGenericModels(
        Circuit circuit,
        IReadOnlyDictionary<string, PrimitiveDefinition>? primitivesByName
    )
    {
        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (circuit.Fill?.Devices is null)
        {
            return required;
        }

        foreach (var device in circuit.Fill.Devices)
        {
            var modelName = device.DeviceType;
            if (
                primitivesByName is not null
                && primitivesByName.TryGetValue(device.Primitive, out var primitive)
            )
            {
                modelName = primitive.Device;
            }
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
                "level1_nmos" =>
                    ".model level1_nmos nmos level=1 vto=0.5 kp=120u gamma=0.4 phi=0.65 lambda=0.04",
                "level1_pmos" =>
                    ".model level1_pmos pmos level=1 vto=-0.5 kp=40u gamma=0.4 phi=0.65 lambda=0.05",
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

    private sealed class ExpressionContext
    {
        private readonly IReadOnlyDictionary<string, string> _paramBindings;
        private readonly IReadOnlyDictionary<string, SizePack> _sizeBindings;
        private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);
        private readonly HashSet<string> _resolving = new(StringComparer.Ordinal);

        public ExpressionContext(
            IReadOnlyDictionary<string, string> paramBindings,
            IReadOnlyDictionary<string, SizePack> sizeBindings
        )
        {
            _paramBindings = paramBindings;
            _sizeBindings = sizeBindings;
        }

        public string Evaluate(string expression)
        {
            return ExpressionEvaluator.Evaluate(expression, ResolveIdentifier);
        }

        private string ResolveIdentifier(string identifier)
        {
            if (_cache.TryGetValue(identifier, out var cached))
            {
                return cached;
            }

            if (!_resolving.Add(identifier))
            {
                throw new ArgumentException($"Circular parameter reference detected: {identifier}");
            }

            if (TryResolveSizeField(identifier, out var sizeExpr))
            {
                var resolved = Evaluate(sizeExpr);
                _cache[identifier] = resolved;
                _resolving.Remove(identifier);
                return resolved;
            }

            if (_paramBindings.TryGetValue(identifier, out var binding) && binding is not null)
            {
                var resolved = Evaluate(binding);
                _cache[identifier] = resolved;
                _resolving.Remove(identifier);
                return resolved;
            }

            _resolving.Remove(identifier);
            throw new ArgumentException($"Undefined parameter reference: {identifier}");
        }

        private bool TryResolveSizeField(string identifier, out string expression)
        {
            var dotIndex = identifier.IndexOf('.');
            if (dotIndex <= 0)
            {
                expression = string.Empty;
                return false;
            }

            var sizeName = identifier[..dotIndex];
            var field = identifier[(dotIndex + 1)..];
            if (
                _sizeBindings.TryGetValue(sizeName, out var pack)
                && pack.Entries.TryGetValue(field, out var expr)
            )
            {
                expression = expr;
                return true;
            }

            expression = string.Empty;
            return false;
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
