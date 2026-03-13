using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Cascode.Bench;
using Cascode.Language.BenchRuntime;
using Cascode.Language.Validation;

namespace Cascode.Language;

/// <summary>
/// Emits SPICE netlists from Cascode EL documents.
/// </summary>
/// <remarks>
/// The emitter generates ngspice-compatible SPICE netlists from EL-level Cascode circuits.
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
        "nmos_level1",
        "pmos_level1",
    };

    private static readonly Dictionary<string, string> QFactorPassiveDevices = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        { "capacitor_q", "C" },
        { "inductor_q", "L" },
    };

    private static string SanitizeNetName(string netName)
    {
        return netName.Replace('.', '_');
    }

    internal sealed class CircuitVariant
    {
        public required Circuit Circuit { get; init; }
        public required string CanonicalName { get; init; }
        public required IReadOnlyDictionary<string, string> ResolvedParams { get; init; }
        public required IReadOnlyDictionary<string, SizePack> ResolvedSizes { get; init; }
    }

    /// <summary>
    /// Emits a SPICE subcircuit definition for an EL-level circuit.
    /// </summary>
    /// <param name="circuit">The circuit to emit (must be EL level).</param>
    /// <param name="writer">Text writer for output.</param>
    /// <param name="deviceModelMap">Optional map of PDK device names to resolved model definitions.</param>
    /// <param name="document">Optional Cascode document for resolving instance types.</param>
    /// <param name="resolution">Optional attach resolution result for resolved net names.</param>
    /// <param name="backend">Target SPICE backend for SI prefix formatting (default: ngspice).</param>
    /// <exception cref="InvalidOperationException">Thrown if circuit is not EL level.</exception>
    /// <remarks>
    /// Output format:
    /// <code>
    /// * CircuitName - Generated from Cascode EL
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
        CascodeDocument? document = null,
        CircuitResolutionResult? resolution = null,
        BenchBackendType backend = BenchBackendType.Ngspice
    )
    {
        EmitDesignInternal(circuit, writer, deviceModelMap, document, resolution, backend, null);
    }

    private static void EmitDesignInternal(
        Circuit circuit,
        TextWriter writer,
        IReadOnlyDictionary<string, DeviceModelResolution>? deviceModelMap,
        CascodeDocument? document,
        CircuitResolutionResult? resolution,
        BenchBackendType backend,
        IReadOnlyDictionary<string, List<CircuitVariant>>? variantMap
    )
    {
        if (circuit.Level != CascodeLevel.EL)
        {
            throw new InvalidOperationException(
                $"SpiceEmitter requires EL-level circuit, but '{circuit.Name}' is {circuit.Level}."
            );
        }

        // Header comment
        writer.WriteLine($"* {circuit.Name} - Generated from Cascode EL");
        writer.WriteLine();

        // Build port list: ports first, then supplies, then grounds
        // Ports are already desugared to scalar types by BundleDesugarer
        var portList = BuildPortList(circuit);

        IReadOnlyDictionary<string, PrimitiveDefinition>? primitivesByName = null;
        IReadOnlyDictionary<string, Circuit>? circuitsByName = null;
        if (document is not null)
        {
            primitivesByName = document.Primitives.ToDictionary(
                p => p.Name,
                StringComparer.Ordinal
            );
            circuitsByName = document.Circuits.ToDictionary(c => c.Name, StringComparer.Ordinal);
        }

        // If the design uses generic MOS model names (e.g. nmos_level1), emit model cards so
        // ngspice can simulate without a PDK model include.
        if (backend == BenchBackendType.Ngspice)
        {
            var requiredModels = GetRequiredGenericModels(circuit, primitivesByName);
            if (requiredModels.Count != 0)
            {
                EmitGenericModels(requiredModels, writer);
                writer.WriteLine();
            }
        }

        if (document is not null && variantMap is null)
        {
            variantMap = CollectAllVariants(document);
        }

        var variants = ResolveVariantsForCircuit(circuit, variantMap);
        for (var index = 0; index < variants.Count; index++)
        {
            if (index > 0)
            {
                writer.WriteLine();
            }

            EmitVariant(
                variants[index],
                portList,
                writer,
                deviceModelMap,
                circuitsByName,
                primitivesByName,
                variantMap,
                resolution,
                backend
            );
        }
    }

    /// <summary>
    /// Emits all outputs for an Cascode document: design netlist and testbenches.
    /// </summary>
    /// <param name="doc">The Cascode document.</param>
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
        CascodeDocument doc,
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
        var variantMap = CollectAllVariants(doc);

        foreach (var circuit in orderedCircuits)
        {
            if (circuit.Level != CascodeLevel.EL)
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
                EmitDesignInternal(
                    circuit,
                    writer,
                    includeResolution?.DeviceModelMap,
                    doc,
                    circuitResolution,
                    backend,
                    variantMap
                );
            }
            result.DesignPaths.Add(designPath);
        }

        // Emit declarative bench testbenches (if any bindings exist on EL circuits).
        var benchPlans = BenchCompiler.CompileAllPlans(doc);
        result.TestbenchPaths.AddRange(
            BenchTestbenchEmitter.EmitPlans(
                doc,
                benchPlans,
                outputDir,
                backend,
                result.DesignPaths,
                includeResolver
            )
        );

        return result;
    }

    /// <summary>
    /// Validates and emits all outputs for an Cascode document with pre-flight validation.
    /// </summary>
    /// <param name="doc">The Cascode document.</param>
    /// <param name="outputDir">Output directory for generated files.</param>
    /// <param name="backend">Backend type for testbench generation (default: ngspice).</param>
    /// <param name="workspaceRoot">Optional workspace root for include resolution.</param>
    /// <returns>Result containing paths to generated files and validation result.</returns>
    /// <remarks>
    /// Runs hierarchy validation and emission validation before attempting SPICE generation.
    /// If validation fails, no files are written and the validation errors are returned.
    /// </remarks>
    public static ValidatedEmitResult ValidateAndEmit(
        CascodeDocument doc,
        string outputDir,
        BenchBackendType backend = BenchBackendType.Ngspice,
        string? workspaceRoot = null,
        IBenchIncludeResolver? includeResolver = null
    )
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(outputDir);

        var validationResult = new ValidationResult();

        var semanticValidation = CompleteDocumentSemanticValidator.Validate(doc);
        validationResult.Merge(semanticValidation);
        if (!validationResult.IsValid)
        {
            return new ValidatedEmitResult
            {
                Validation = validationResult,
                Emit = new SpiceEmitResult(),
            };
        }

        // Validate hierarchy first (circuit references, parameters, ports, cycles)
        var hierarchyValidation = HierarchyValidator.Validate(doc);
        validationResult.Merge(hierarchyValidation);

        // Validate all EL circuits for emission requirements
        var elCircuits = doc.Circuits.Where(c => c.Level == CascodeLevel.EL).ToList();
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
    /// <param name="doc">The Cascode document.</param>
    /// <returns>Circuits ordered with leaf circuits first, top-level last.</returns>
    /// <remarks>
    /// Required for SPICE: .subckt must be defined before X-element reference.
    /// Delegates to HierarchyValidator.GetTopologicalOrder with excludeInline=true
    /// since inline circuits are expanded in place rather than emitted as subcircuits.
    /// </remarks>
    internal static List<Circuit> OrderByDependency(CascodeDocument doc)
    {
        return HierarchyValidator.GetTopologicalOrder(doc.Circuits, excludeInline: true);
    }

    internal static string GetDefaultVariantName(Circuit circuit)
    {
        var variant = BuildVariant(
            circuit,
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, SizePack>(StringComparer.Ordinal),
            instance: null
        );
        return variant.CanonicalName;
    }

    private static List<string> BuildPortList(Circuit circuit)
    {
        var portList = new List<string>();
        foreach (var port in circuit.Ports)
        {
            portList.Add(SanitizeNetName(port.Name));
        }
        foreach (var supply in circuit.Supplies)
        {
            portList.Add(SanitizeNetName(supply));
        }
        foreach (var ground in circuit.Grounds)
        {
            portList.Add(SanitizeNetName(ground));
        }

        return portList;
    }

    private static IReadOnlyList<CircuitVariant> ResolveVariantsForCircuit(
        Circuit circuit,
        IReadOnlyDictionary<string, List<CircuitVariant>>? variantMap
    )
    {
        if (variantMap is not null && variantMap.TryGetValue(circuit.Name, out var variants))
        {
            return variants;
        }

        return
        [
            BuildVariant(
                circuit,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, SizePack>(StringComparer.Ordinal),
                instance: null
            ),
        ];
    }

    private static IReadOnlyDictionary<string, List<CircuitVariant>> CollectAllVariants(
        CascodeDocument doc
    )
    {
        var instanced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var circuit in doc.Circuits)
        {
            if (circuit.Fill?.Instances is null)
            {
                continue;
            }

            foreach (var instance in circuit.Fill.Instances)
            {
                if (!string.IsNullOrWhiteSpace(instance.Type))
                {
                    instanced.Add(instance.Type);
                }
            }
        }

        var merged = new Dictionary<string, Dictionary<string, CircuitVariant>>(
            StringComparer.Ordinal
        );

        foreach (var root in doc.Circuits.Where(c => c.Level == CascodeLevel.EL))
        {
            if (instanced.Contains(root.Name))
            {
                continue;
            }

            var rootVariants = CollectVariants(doc, root);
            foreach (var (name, variants) in rootVariants)
            {
                if (!merged.TryGetValue(name, out var entries))
                {
                    entries = new Dictionary<string, CircuitVariant>(StringComparer.Ordinal);
                    merged[name] = entries;
                }

                foreach (var variant in variants)
                {
                    entries.TryAdd(variant.CanonicalName, variant);
                }
            }
        }

        foreach (var circuit in doc.Circuits.Where(c => c.Level == CascodeLevel.EL && !c.Inline))
        {
            if (merged.ContainsKey(circuit.Name))
            {
                continue;
            }

            var variant = BuildVariant(
                circuit,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, SizePack>(StringComparer.Ordinal),
                instance: null
            );
            merged[circuit.Name] = new Dictionary<string, CircuitVariant>(StringComparer.Ordinal)
            {
                [variant.CanonicalName] = variant,
            };
        }

        return merged.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Values.OrderBy(v => v.CanonicalName, StringComparer.Ordinal).ToList(),
            StringComparer.Ordinal
        );
    }

    private static Dictionary<string, List<CircuitVariant>> CollectVariants(
        CascodeDocument doc,
        Circuit topLevel
    )
    {
        var circuitsByName = doc.Circuits.ToDictionary(c => c.Name, StringComparer.Ordinal);
        var variantsByCircuit = new Dictionary<string, Dictionary<string, CircuitVariant>>(
            StringComparer.Ordinal
        );

        void VisitCircuit(
            Circuit circuit,
            IReadOnlyDictionary<string, string> parentParams,
            IReadOnlyDictionary<string, SizePack> parentSizes,
            InstanceDeclaration? instance
        )
        {
            var variant = BuildVariant(circuit, parentParams, parentSizes, instance);
            if (!circuit.Inline)
            {
                if (!variantsByCircuit.TryGetValue(circuit.Name, out var entries))
                {
                    entries = new Dictionary<string, CircuitVariant>(StringComparer.Ordinal);
                    variantsByCircuit[circuit.Name] = entries;
                }

                entries.TryAdd(variant.CanonicalName, variant);
            }

            if (circuit.Fill?.Instances is null)
            {
                return;
            }

            foreach (var child in circuit.Fill.Instances)
            {
                if (!circuitsByName.TryGetValue(child.Type, out var targetCircuit))
                {
                    continue;
                }

                VisitCircuit(targetCircuit, variant.ResolvedParams, variant.ResolvedSizes, child);
            }
        }

        VisitCircuit(
            topLevel,
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, SizePack>(StringComparer.Ordinal),
            instance: null
        );

        return variantsByCircuit.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Values.OrderBy(v => v.CanonicalName, StringComparer.Ordinal).ToList(),
            StringComparer.Ordinal
        );
    }

    private static CircuitVariant BuildVariant(
        Circuit circuit,
        IReadOnlyDictionary<string, string> parentParams,
        IReadOnlyDictionary<string, SizePack> parentSizes,
        InstanceDeclaration? instance
    )
    {
        var paramBindings = BuildParameterBindings(circuit, parentParams, instance);
        var sizeBindings = BuildSizeBindings(circuit, parentSizes, instance);
        var context = new ExpressionContext(paramBindings, sizeBindings);

        var resolvedParams = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var param in circuit.Parameters)
        {
            if (!paramBindings.TryGetValue(param.Name, out var expr))
            {
                throw new InvalidOperationException(
                    $"Missing required parameter '{param.Name}' for circuit '{circuit.Name}'."
                );
            }

            resolvedParams[param.Name] = ResolveParameterValue(circuit, param, context, expr);
        }

        var resolvedSizes = ResolveSizeBindings(circuit, sizeBindings, context);
        var nameSizes = SelectNamingSizes(circuit, resolvedSizes);
        var canonicalName = VariantNaming.BuildCanonicalName(
            circuit.Name,
            resolvedParams,
            nameSizes
        );

        return new CircuitVariant
        {
            Circuit = circuit,
            CanonicalName = canonicalName,
            ResolvedParams = resolvedParams,
            ResolvedSizes = resolvedSizes,
        };
    }

    private static Dictionary<string, string> BuildParameterBindings(
        Circuit circuit,
        IReadOnlyDictionary<string, string> parentParamBindings,
        InstanceDeclaration? instance
    )
    {
        var bindings = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (name, value) in parentParamBindings)
        {
            bindings[name] = value;
        }

        foreach (var param in circuit.Parameters)
        {
            var expr = ParamValueToExpression(param.Default);
            if (!string.IsNullOrWhiteSpace(expr))
            {
                bindings[param.Name] = expr;
            }
        }

        if (instance is not null)
        {
            foreach (var (name, paramValue) in instance.Params)
            {
                var expr = ParamValueToExpression(paramValue);
                if (!string.IsNullOrWhiteSpace(expr))
                {
                    bindings[name] = expr;
                }
            }
        }

        return bindings;
    }

    private static Dictionary<string, SizePack> BuildSizeBindings(
        Circuit circuit,
        IReadOnlyDictionary<string, SizePack> parentSizeBindings,
        InstanceDeclaration? instance
    )
    {
        var bindings = new Dictionary<string, SizePack>(StringComparer.Ordinal);

        foreach (var (name, pack) in parentSizeBindings)
        {
            bindings[name] = pack;
        }

        foreach (var size in circuit.Sizes)
        {
            if (size.Default is not null)
            {
                bindings[size.Name] = size.Default;
            }
        }

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

        if (instance is not null)
        {
            foreach (var (name, pack) in instance.Sizes)
            {
                bindings[name] = pack;
            }
        }

        return bindings;
    }

    private static string ResolveParameterValue(
        Circuit circuit,
        CircuitParameter param,
        ExpressionContext context,
        string expression
    )
    {
        var trimmed = expression.Trim();
        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException(
                $"Empty value for parameter '{param.Name}' in circuit '{circuit.Name}'."
            );
        }

        var type = param.Type.ToLowerInvariant();
        if (type == "bool")
        {
            if (trimmed.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return "true";
            }

            if (trimmed.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                return "false";
            }

            var numeric = context.Evaluate(trimmed);
            var parsed = ParameterEvaluator.ParseNumeric(numeric);
            return Math.Abs(parsed) > 0 ? "true" : "false";
        }

        if (type == "polarity")
        {
            if (trimmed.Equals("NMOS", StringComparison.OrdinalIgnoreCase))
            {
                return "NMOS";
            }

            if (trimmed.Equals("PMOS", StringComparison.OrdinalIgnoreCase))
            {
                return "PMOS";
            }

            return trimmed.ToUpperInvariant();
        }

        var evaluated = context.Evaluate(trimmed);
        if (type == "int")
        {
            var value = ParameterEvaluator.ParseNumeric(evaluated);
            var rounded = Math.Round(value, 0);
            if (Math.Abs(value - rounded) > 1e-9)
            {
                throw new InvalidOperationException(
                    $"Parameter '{param.Name}' in circuit '{circuit.Name}' is not an integer."
                );
            }

            return ((long)rounded).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return ParameterEvaluator.FormatNumeric(ParameterEvaluator.ParseNumeric(evaluated));
    }

    private static Dictionary<string, SizePack> ResolveSizeBindings(
        Circuit circuit,
        IReadOnlyDictionary<string, SizePack> sizeBindings,
        ExpressionContext context
    )
    {
        var resolved = new Dictionary<string, SizePack>(StringComparer.Ordinal);

        void ResolveSizeDeclaration(SizeDeclaration size)
        {
            if (!sizeBindings.TryGetValue(size.Name, out var pack))
            {
                throw new InvalidOperationException(
                    $"Missing required size pack '{size.Name}' for circuit '{circuit.Name}'."
                );
            }

            var resolvedPack = new SizePack();
            foreach (var (field, expr) in pack.Entries)
            {
                var evaluated = context.Evaluate(expr);
                resolvedPack.Entries[field] = ParameterEvaluator.FormatNumeric(
                    ParameterEvaluator.ParseNumeric(evaluated)
                );
            }

            resolved[size.Name] = resolvedPack;
        }

        foreach (var size in circuit.Sizes)
        {
            ResolveSizeDeclaration(size);
        }

        if (circuit.Fill?.Sizes is { Count: > 0 })
        {
            foreach (var size in circuit.Fill.Sizes)
            {
                ResolveSizeDeclaration(size);
            }
        }

        return resolved;
    }

    private static IReadOnlyDictionary<string, SizePack> SelectNamingSizes(
        Circuit circuit,
        IReadOnlyDictionary<string, SizePack> resolvedSizes
    )
    {
        if (circuit.Sizes.Count == 0)
        {
            return new Dictionary<string, SizePack>(StringComparer.Ordinal);
        }

        var namingSizes = new Dictionary<string, SizePack>(StringComparer.Ordinal);
        foreach (var size in circuit.Sizes)
        {
            if (resolvedSizes.TryGetValue(size.Name, out var pack))
            {
                namingSizes[size.Name] = pack;
            }
        }

        return namingSizes;
    }

    private static string BuildVariantName(
        Circuit circuit,
        IReadOnlyDictionary<string, string> parentParams,
        IReadOnlyDictionary<string, SizePack> parentSizes,
        InstanceDeclaration instance,
        IReadOnlyDictionary<string, List<CircuitVariant>>? variantMap
    )
    {
        var variant = BuildVariant(circuit, parentParams, parentSizes, instance);
        if (variantMap is not null && variantMap.TryGetValue(circuit.Name, out var variants))
        {
            if (!variants.Any(v => v.CanonicalName == variant.CanonicalName))
            {
                throw new InvalidOperationException(
                    $"Variant '{variant.CanonicalName}' for circuit '{circuit.Name}' was not collected."
                );
            }
        }

        return variant.CanonicalName;
    }

    // Long method: keeps variant emission steps contiguous for spec-ordered output.
    private static void EmitVariant(
        CircuitVariant variant,
        IReadOnlyList<string> portList,
        TextWriter writer,
        IReadOnlyDictionary<string, DeviceModelResolution>? deviceModelMap,
        IReadOnlyDictionary<string, Circuit>? circuitsByName,
        IReadOnlyDictionary<string, PrimitiveDefinition>? primitivesByName,
        IReadOnlyDictionary<string, List<CircuitVariant>>? variantMap,
        CircuitResolutionResult? resolution,
        BenchBackendType backend
    )
    {
        var circuit = variant.Circuit;

        writer.WriteLine($".subckt {variant.CanonicalName} {string.Join(" ", portList)}");
        writer.WriteLine();

        if (circuit.Fill?.Nets.Count > 0)
        {
            var netNames = circuit
                .Fill.Nets.OrderBy(n => n.Id, StringComparer.Ordinal)
                .Select(n => SanitizeNetName(n.Id));
            writer.WriteLine($"* Internal nets: {string.Join(", ", netNames)}");
            writer.WriteLine();
        }

        var expressionContext = new ExpressionContext(
            variant.ResolvedParams,
            variant.ResolvedSizes
        );

        if (circuit.Fill?.Devices.Count > 0)
        {
            if (primitivesByName is null)
            {
                throw new InvalidOperationException(
                    "Primitive definitions are required for device emission. Provide the Cascode document when emitting."
                );
            }

            foreach (var device in circuit.Fill.Devices.OrderBy(d => d.Id, StringComparer.Ordinal))
            {
                EmitDevice(
                    device,
                    writer,
                    deviceModelMap,
                    primitivesByName,
                    variant.ResolvedSizes,
                    expressionContext,
                    backend
                );
            }
        }

        if (circuit.Fill?.Instances.Count > 0 && circuitsByName is not null)
        {
            bool hasEmittedCircuitInstancesHeader = false;

            foreach (
                var instance in circuit.Fill.Instances.OrderBy(i => i.Id, StringComparer.Ordinal)
            )
            {
                if (!circuitsByName.TryGetValue(instance.Type, out var targetCircuit))
                {
                    continue;
                }

                if (targetCircuit.Inline)
                {
                    if (primitivesByName is null)
                    {
                        throw new InvalidOperationException(
                            "Primitive definitions are required for device emission. Provide the Cascode document when emitting."
                        );
                    }

                    writer.WriteLine();
                    writer.WriteLine($"* Inline expansion of {instance.Id} : {instance.Type}");
                    ExpandInlineCircuit(
                        instance,
                        targetCircuit,
                        hierarchyPath: new List<string>(),
                        parentNetSubstitutions: new Dictionary<string, string>(
                            StringComparer.Ordinal
                        ),
                        parentParamBindings: variant.ResolvedParams,
                        parentSizeBindings: variant.ResolvedSizes,
                        circuitsByName,
                        resolution,
                        deviceModelMap,
                        primitivesByName,
                        variantMap,
                        writer,
                        backend
                    );
                }
                else
                {
                    if (!hasEmittedCircuitInstancesHeader)
                    {
                        writer.WriteLine();
                        writer.WriteLine("* Circuit instances");
                        hasEmittedCircuitInstancesHeader = true;
                    }

                    EmitInstance(
                        instance,
                        targetCircuit,
                        resolution,
                        variant.ResolvedParams,
                        variant.ResolvedSizes,
                        variantMap,
                        writer
                    );
                }
            }
        }

        writer.WriteLine();
        writer.WriteLine($".ends {variant.CanonicalName}");
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
        ExpressionContext expressionContext,
        BenchBackendType backend
    )
    {
        var info = BuildSpiceDeviceInfo(
            device,
            primitivesByName,
            deviceModelMap,
            sizeBindings,
            expressionContext,
            backend
        );

        var hasTwoTerminals = info.TerminalBindings.Count == 2;
        var terminalNets = hasTwoTerminals ? info.TerminalBindings.Values.ToArray() : null;
        if (
            info.IsBuiltinPassive
            && !string.IsNullOrWhiteSpace(info.SeriesResistanceValue)
            && terminalNets is not null
        )
        {
            var positiveNet = terminalNets[0];
            var negativeNet = terminalNets[1];
            var seriesNode = SanitizeNetName($"{device.Id}__esr_n");

            var passiveSb = new StringBuilder();
            passiveSb.Append(info.SpiceType);
            passiveSb.Append(device.Id);
            passiveSb.Append(' ');
            passiveSb.Append(SanitizeNetName(positiveNet));
            passiveSb.Append(' ');
            passiveSb.Append(seriesNode);
            passiveSb.Append(' ');
            if (!string.IsNullOrWhiteSpace(info.PassiveValue))
            {
                passiveSb.Append(info.PassiveValue);
            }
            AppendParamAssignments(
                passiveSb,
                info.ParamExpressions,
                expr => RenderEvaluatedExpression(expressionContext, expr, backend)
            );
            writer.WriteLine(passiveSb.ToString().TrimEnd());

            writer.WriteLine(
                $"R{device.Id}__esr {seriesNode} {SanitizeNetName(negativeNet)} {info.SeriesResistanceValue}"
            );
            return;
        }

        var sb = new StringBuilder();
        sb.Append(info.SpiceType);
        sb.Append(device.Id);
        sb.Append(' ');

        foreach (var (_, net) in info.TerminalBindings)
        {
            sb.Append(SanitizeNetName(net));
            sb.Append(' ');
        }

        if (info.IsBuiltinPassive)
        {
            if (!string.IsNullOrWhiteSpace(info.PassiveValue))
            {
                sb.Append(info.PassiveValue);
            }
        }
        else
        {
            sb.Append(info.ModelName);
        }

        AppendParamAssignments(
            sb,
            info.ParamExpressions,
            expr => RenderEvaluatedExpression(expressionContext, expr, backend)
        );

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
        IReadOnlyDictionary<string, string> parentParamBindings,
        IReadOnlyDictionary<string, SizePack> parentSizeBindings,
        IReadOnlyDictionary<string, List<CircuitVariant>>? variantMap,
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
            sb.Append(SanitizeNetName(netName));
            sb.Append(' ');
        }

        var variantName = BuildVariantName(
            targetCircuit,
            parentParamBindings,
            parentSizeBindings,
            instance,
            variantMap
        );
        sb.Append(variantName);

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
    // Long method: inline expansion is kept together to preserve hierarchy and substitution flow.
    private static void ExpandInlineCircuit(
        InstanceDeclaration instance,
        Circuit inlineCircuit,
        IReadOnlyList<string> hierarchyPath,
        Dictionary<string, string> parentNetSubstitutions,
        IReadOnlyDictionary<string, string> parentParamBindings,
        IReadOnlyDictionary<string, SizePack> parentSizeBindings,
        IReadOnlyDictionary<string, Circuit> circuitsByName,
        CircuitResolutionResult? resolution,
        IReadOnlyDictionary<string, DeviceModelResolution>? deviceModelMap,
        IReadOnlyDictionary<string, PrimitiveDefinition> primitivesByName,
        IReadOnlyDictionary<string, List<CircuitVariant>>? variantMap,
        TextWriter writer,
        BenchBackendType backend
    )
    {
        // Build current hierarchy path by appending this instance's ID
        var currentPath = new List<string>(hierarchyPath) { instance.Id };

        // Build port-to-net substitution map, composing with parent substitutions
        var localSubstitutions = BuildNetSubstitutions(instance, inlineCircuit, resolution);
        var netSubstitutions = ComposeNetSubstitutions(parentNetSubstitutions, localSubstitutions);

        var paramBindings = BuildParameterBindings(inlineCircuit, parentParamBindings, instance);
        var sizeBindings = BuildSizeBindings(inlineCircuit, parentSizeBindings, instance);

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
                        variantMap,
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
                        paramBindings,
                        sizeBindings,
                        variantMap,
                        writer
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
    /// Emits a non-inline instance within an inline circuit as an X-element with hierarchical naming.
    /// </summary>
    private static void EmitInlineInstance(
        InstanceDeclaration instance,
        IReadOnlyList<string> hierarchyPath,
        Circuit targetCircuit,
        Dictionary<string, string> netSubstitutions,
        HashSet<string> internalNets,
        CircuitResolutionResult? resolution,
        IReadOnlyDictionary<string, string> parentParamBindings,
        IReadOnlyDictionary<string, SizePack> parentSizeBindings,
        IReadOnlyDictionary<string, List<CircuitVariant>>? variantMap,
        TextWriter writer
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
            sb.Append(SanitizeNetName(substitutedNet));
            sb.Append(' ');
        }

        var variantName = BuildVariantName(
            targetCircuit,
            parentParamBindings,
            parentSizeBindings,
            instance,
            variantMap
        );
        sb.Append(variantName);

        writer.WriteLine(sb.ToString().TrimEnd());
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
        var info = BuildSpiceDeviceInfo(
            device,
            primitivesByName,
            deviceModelMap,
            sizeBindings,
            expressionContext,
            backend
        );

        var hasTwoTerminals = info.TerminalBindings.Count == 2;
        var terminalNets = hasTwoTerminals ? info.TerminalBindings.Values.ToArray() : null;
        if (
            info.IsBuiltinPassive
            && !string.IsNullOrWhiteSpace(info.SeriesResistanceValue)
            && terminalNets is not null
        )
        {
            var positiveNet = terminalNets[0];
            var negativeNet = terminalNets[1];
            var prefix = BuildHierarchyPrefix(hierarchyPath);
            var devicePrefix = $"{prefix}__{device.Id}";
            var seriesNode = SanitizeNetName($"{devicePrefix}__esr_n");

            var passiveSb = new StringBuilder();
            passiveSb.Append(info.SpiceType);
            passiveSb.Append(devicePrefix);
            passiveSb.Append(' ');
            passiveSb.Append(
                SanitizeNetName(
                    SubstituteNet(
                        positiveNet,
                        hierarchyPath,
                        netSubstitutions,
                        internalNets,
                        resolution
                    )
                )
            );
            passiveSb.Append(' ');
            passiveSb.Append(seriesNode);
            passiveSb.Append(' ');
            if (!string.IsNullOrWhiteSpace(info.PassiveValue))
            {
                passiveSb.Append(info.PassiveValue);
            }
            AppendParamAssignments(
                passiveSb,
                info.ParamExpressions,
                expr => RenderEvaluatedExpression(expressionContext, expr, backend)
            );
            writer.WriteLine(passiveSb.ToString().TrimEnd());

            writer.WriteLine(
                $"R{devicePrefix}__esr {seriesNode} {SanitizeNetName(SubstituteNet(negativeNet, hierarchyPath, netSubstitutions, internalNets, resolution))} {info.SeriesResistanceValue}"
            );
            return;
        }

        var sb = new StringBuilder();
        sb.Append(info.SpiceType);
        sb.Append(BuildHierarchyPrefix(hierarchyPath));
        sb.Append("__");
        sb.Append(device.Id);
        sb.Append(' ');

        foreach (var (_, net) in info.TerminalBindings)
        {
            var substitutedNet = SubstituteNet(
                net,
                hierarchyPath,
                netSubstitutions,
                internalNets,
                resolution
            );
            sb.Append(SanitizeNetName(substitutedNet));
            sb.Append(' ');
        }

        if (info.IsBuiltinPassive)
        {
            if (!string.IsNullOrWhiteSpace(info.PassiveValue))
            {
                sb.Append(info.PassiveValue);
            }
        }
        else
        {
            sb.Append(info.ModelName);
        }

        AppendParamAssignments(
            sb,
            info.ParamExpressions,
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
        var evaluated = context.Evaluate(trimmed);
        return SiValue.TransformForBackend(evaluated, backend);
    }

    private static double EvaluateNumericParam(
        IReadOnlyDictionary<string, string> paramExpressions,
        string paramName,
        ExpressionContext context
    )
    {
        if (!paramExpressions.TryGetValue(paramName, out var expr))
        {
            throw new InvalidOperationException($"Missing required parameter '{paramName}'.");
        }

        return ParameterEvaluator.ParseNumeric(context.Evaluate(expr.Trim()));
    }

    private static void EnsurePositiveFiniteParameter(double value, string paramName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0.0)
        {
            throw new InvalidOperationException(
                $"Parameter '{paramName}' must be a finite positive value."
            );
        }
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
    /// Information about a resolved device for SPICE emission.
    /// </summary>
    private readonly struct SpiceDeviceInfo
    {
        public required PrimitiveDefinition Primitive { get; init; }
        public required DeviceModelResolution? ResolvedModel { get; init; }
        public required string ModelName { get; init; }
        public required bool UseSubckt { get; init; }
        public required bool IsBuiltinPassive { get; init; }
        public required string? PassiveValue { get; init; }
        public required string? SeriesResistanceValue { get; init; }
        public required string SpiceType { get; init; }
        public required IReadOnlyDictionary<string, string> TerminalBindings { get; init; }
        public required IReadOnlyDictionary<string, string> ParamExpressions { get; init; }
    }

    /// <summary>
    /// Builds SPICE device information from a device declaration.
    /// </summary>
    /// <param name="device">Device declaration (regular or inline).</param>
    /// <param name="primitivesByName">Primitive definitions keyed by name.</param>
    /// <param name="deviceModelMap">Optional map of PDK device names to resolved model definitions.</param>
    /// <param name="sizeBindings">Local size bindings for parameter expansion.</param>
    /// <param name="expressionContext">Expression context for evaluating passive values.</param>
    /// <param name="backend">Target SPICE backend for SI prefix formatting.</param>
    /// <returns>SpiceDeviceInfo containing resolved device information.</returns>
    /// <exception cref="InvalidOperationException">Thrown if device type is unknown or primitive is undefined.</exception>
    private static SpiceDeviceInfo BuildSpiceDeviceInfo(
        DeviceDeclaration device,
        IReadOnlyDictionary<string, PrimitiveDefinition> primitivesByName,
        IReadOnlyDictionary<string, DeviceModelResolution>? deviceModelMap,
        IReadOnlyDictionary<string, SizePack> sizeBindings,
        ExpressionContext expressionContext,
        BenchBackendType backend
    )
    {
        if (!primitivesByName.TryGetValue(device.Primitive, out var primitive))
        {
            throw new InvalidOperationException(
                $"Device '{device.Id}' references undefined primitive '{device.Primitive}'."
            );
        }

        var deviceParams = PrimitiveResolver
            .BuildParamExpressions(device, primitive, sizeBindings)
            .Where(kvp => !IsReservedPrimitiveMetaParam(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal);
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
        string? seriesResistanceValue = null;
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

        if (
            !isBuiltinPassive
            && !useSubckt
            && QFactorPassiveDevices.TryGetValue(modelName, out var valueKey)
            && deviceParams.TryGetValue(valueKey, out var valueExpr)
        )
        {
            isBuiltinPassive = true;
            passiveValue = RenderEvaluatedExpression(expressionContext, valueExpr, backend);
            var reactiveVal = ParameterEvaluator.ParseNumeric(
                expressionContext.Evaluate(valueExpr.Trim())
            );
            var qVal = EvaluateNumericParam(deviceParams, "Q", expressionContext);
            var freqVal = EvaluateNumericParam(deviceParams, "freq", expressionContext);
            EnsurePositiveFiniteParameter(reactiveVal, valueKey);
            EnsurePositiveFiniteParameter(qVal, "Q");
            EnsurePositiveFiniteParameter(freqVal, "freq");
            var rser = valueKey switch
            {
                "C" => 1.0 / (2.0 * Math.PI * freqVal * reactiveVal * qVal),
                "L" => (2.0 * Math.PI * freqVal * reactiveVal) / qVal,
                _ => throw new InvalidOperationException(
                    $"Unsupported Q-factor passive value key '{valueKey}'."
                ),
            };
            seriesResistanceValue = SiValue.FormatForBackend(rser, backend);
            paramExpressions = deviceParams
                .Where(kvp =>
                    !kvp.Key.Equals(valueKey, StringComparison.OrdinalIgnoreCase)
                    && !kvp.Key.Equals("Q", StringComparison.OrdinalIgnoreCase)
                    && !kvp.Key.Equals("freq", StringComparison.OrdinalIgnoreCase)
                )
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal);
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

        var terminalBindings = BuildTerminalBindings(device, deviceKind);

        return new SpiceDeviceInfo
        {
            Primitive = primitive,
            ResolvedModel = resolvedModel,
            ModelName = modelName,
            UseSubckt = useSubckt,
            IsBuiltinPassive = isBuiltinPassive,
            PassiveValue = passiveValue,
            SeriesResistanceValue = seriesResistanceValue,
            SpiceType = spiceType,
            TerminalBindings = terminalBindings,
            ParamExpressions = paramExpressions,
        };
    }

    private static bool IsReservedPrimitiveMetaParam(string name)
    {
        return name.StartsWith("__op_", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds terminal bindings for a device based on its kind.
    /// </summary>
    private static Dictionary<string, string> BuildTerminalBindings(
        DeviceDeclaration device,
        string deviceKind
    )
    {
        var terminals = deviceKind switch
        {
            "nmos" or "pmos" => new[] { "D", "G", "S", "B" },
            "resistor" or "capacitor" or "inductor" => new[] { "P", "N" },
            "diode" => new[] { "A", "K" },
            _ => Array.Empty<string>(),
        };

        var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var terminal in terminals)
        {
            bindings[terminal] = GetBinding(device, terminal);
        }

        return bindings;
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
            var net = SanitizeNetName(supply.Net);
            writer.WriteLine($"V{net} {net} 0 DC {supply.Value}");
        }

        // Bias voltage sources (DC only, no AC)
        foreach (var bias in harness.Biases)
        {
            var net = SanitizeNetName(bias.Net);
            writer.WriteLine($"V{net} {net} 0 DC {bias.Value}");
        }

        // Input sources - simplified: DC bias with AC stimulus
        foreach (var source in harness.Sources)
        {
            // Default to mid-supply bias with AC stimulus
            var net = SanitizeNetName(source.Net);
            writer.WriteLine($"V{net} {net} 0 DC 0.9 AC 1");
            if (source.Z is not null)
            {
                writer.WriteLine($"R{net}_Z {net}_int {net} {source.Z}");
            }
        }

        // Load elements
        foreach (var load in harness.Loads)
        {
            var net = SanitizeNetName(load.Net);
            for (int i = 0; i < load.Elements.Count; i++)
            {
                var element = load.Elements[i];
                var suffix = load.Elements.Count > 1 ? $"_{i}" : "";

                if (element.Type == "C")
                {
                    writer.WriteLine($"C{net}_load{suffix} {net} 0 {element.Value}");
                }
                else if (element.Type == "R")
                {
                    writer.WriteLine($"R{net}_load{suffix} {net} 0 {element.Value}");
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
            portList.Add(SanitizeNetName(port.Name));
        }
        foreach (var supply in circuit.Supplies)
        {
            portList.Add(SanitizeNetName(supply));
        }
        foreach (var ground in circuit.Grounds)
        {
            portList.Add(SanitizeNetName(ground));
        }

        var subcktName = GetDefaultVariantName(circuit);
        writer.WriteLine($"XDUT {string.Join(" ", portList)} {subcktName}");
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
                "nmos_level1" =>
                    ".model nmos_level1 nmos level=1 vto=0.5 kp=120u gamma=0.4 phi=0.65 lambda=0.04",
                "pmos_level1" =>
                    ".model pmos_level1 pmos level=1 vto=-0.5 kp=40u gamma=0.4 phi=0.65 lambda=0.05",
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
            if (_sizeBindings.TryGetValue(sizeName, out var pack))
            {
                if (pack.Entries.TryGetValue(field, out var expr))
                {
                    expression = expr;
                    return true;
                }

                if (field.Equals("M", StringComparison.OrdinalIgnoreCase))
                {
                    expression = "1";
                    return true;
                }
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
