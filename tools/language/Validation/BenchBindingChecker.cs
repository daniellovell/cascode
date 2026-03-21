using System;
using System.Collections.Generic;
using System.Linq;
using Cascode.Language;

namespace Cascode.Language.Validation;

public static class BenchBindingChecker
{
    /// <summary>
    /// Validates bench bindings against their declared bench terminals and DUT/interface
    /// terminal contracts.
    /// </summary>
    /// <remarks>
    /// Validation intentionally happens in two phases:
    /// - Interface bindings are checked against the interface contract once.
    /// - Circuit validation uses the resolved binding view for constraint lookup and local
    ///   overrides, but skips unchanged inherited bindings so diagnostics are not reported
    ///   twice for the same interface-authored mistake.
    ///
    /// If a circuit <c>extend</c>s an inherited binding, only the extension-added
    /// statements are revalidated in circuit context. That preserves the "validate
    /// interface bindings once" rule while still checking new circuit-local behavior.
    /// </remarks>
    public static void Check(CascodeDocument document, List<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var benchesByName = document.BenchDefinitions.ToDictionary(
            b => b.Name,
            StringComparer.Ordinal
        );
        var bundlesByName = BundleExpander.GetBundlesByName(document);
        var interfacesByName = document.Traits.ToDictionary(
            t => t.Name,
            StringComparer.OrdinalIgnoreCase
        );

        // Interface bindings are part of the interface contract and are validated exactly
        // once against the interface terminals here.
        foreach (var interfaceDef in document.Traits)
        {
            var interfaceTerminals = TerminalContractModel.ForInterface(interfaceDef);
            foreach (var binding in interfaceDef.BenchBindings)
            {
                if (!benchesByName.TryGetValue(binding.BenchName, out var bench))
                {
                    diagnostics.Add(
                        new Diagnostic(
                            $"CAS3001: Bench binding '{binding.BindingName}' references unknown bench '{binding.BenchName}' in interface '{interfaceDef.Name}'.",
                            DiagnosticSeverity.Error,
                            "<bench>",
                            1,
                            1
                        )
                    );
                    continue;
                }

                if (bench.IsAbstract)
                {
                    diagnostics.Add(
                        new Diagnostic(
                            $"CAS2022: Abstract bench '{bench.Name}' cannot appear in bind statements.",
                            DiagnosticSeverity.Error,
                            "<bench>",
                            1,
                            1
                        )
                    );
                    continue;
                }

                CheckBinding(
                    "interface",
                    interfaceDef.Name,
                    binding,
                    bench,
                    interfaceTerminals,
                    bundlesByName,
                    diagnostics
                );
            }
        }

        // Circuits resolve inherited + local bindings for constraint lookup, but only
        // bindings with circuit-local authorship or circuit-local extensions get checked in
        // circuit context. Unchanged inherited bindings were already checked above.
        foreach (var circuit in document.Circuits)
        {
            var resolution = BenchBindingResolver.ResolveForCircuit(circuit, interfacesByName);
            foreach (var bindingName in resolution.DuplicateInheritedBindingNames)
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS3002: Duplicate inherited bench binding name '{bindingName}' on circuit '{circuit.Name}'.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
            }

            CheckConstraintBenchReferences(
                circuit,
                resolution.Bindings,
                benchesByName,
                diagnostics
            );

            var dutTerminals = TerminalContractModel.ForCircuit(circuit);

            foreach (
                var resolvedBinding in resolution.Bindings.Values.OrderBy(
                    binding => binding.Binding.BindingName,
                    StringComparer.OrdinalIgnoreCase
                )
            )
            {
                CheckCircuitBinding(
                    circuit,
                    resolvedBinding,
                    benchesByName,
                    dutTerminals,
                    bundlesByName,
                    diagnostics
                );
            }
        }
    }

    private static void CheckConstraintBenchReferences(
        Circuit circuit,
        IReadOnlyDictionary<string, ResolvedBenchBinding> resolvedBindings,
        IReadOnlyDictionary<string, BenchDefinition> benchesByName,
        List<Diagnostic> diagnostics
    )
    {
        if (circuit.Constraints?.Bench is not { Count: > 0 })
        {
            return;
        }

        foreach (var constraint in circuit.Constraints.Bench)
        {
            // Validate against BenchBase (the user-written binding alias), not the computed
            // instance name (Bench) which includes arg-set hashing.
            var benchBinding = constraint.BenchBase;
            if (string.IsNullOrEmpty(benchBinding))
            {
                continue;
            }

            if (!resolvedBindings.ContainsKey(benchBinding))
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS3008: Constraint '{constraint.Id}' references unknown bench binding '{benchBinding}' in circuit '{circuit.Name}'.",
                        DiagnosticSeverity.Error,
                        "<constraints>",
                        1,
                        1
                    )
                );
            }
        }
    }

    private static void CheckCircuitBinding(
        Circuit circuit,
        ResolvedBenchBinding resolvedBinding,
        IReadOnlyDictionary<string, BenchDefinition> benchesByName,
        IReadOnlyDictionary<string, TerminalContract> dutTerminals,
        IReadOnlyDictionary<string, BundleType> bundlesByName,
        List<Diagnostic> diagnostics
    )
    {
        // The resolved layer tells us whether this binding needs circuit-context
        // validation at all. Purely inherited bindings were already validated in the
        // interface pass, so we skip them here to avoid duplicate diagnostics.
        if (!resolvedBinding.Resolution.RequiresCircuitValidation)
        {
            return;
        }

        var binding = resolvedBinding.Binding;
        if (resolvedBinding.Resolution.Origin.Kind == BenchBindingOriginKind.Circuit)
        {
            if (!benchesByName.TryGetValue(binding.BenchName, out var bench))
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS3001: Bench binding '{binding.BindingName}' references unknown bench '{binding.BenchName}' in circuit '{circuit.Name}'.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
                return;
            }

            if (bench.IsAbstract)
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS2022: Abstract bench '{bench.Name}' cannot appear in bind statements.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
                return;
            }

            CheckBinding(
                "circuit",
                circuit.Name,
                binding,
                bench,
                dutTerminals,
                bundlesByName,
                diagnostics
            );
            return;
        }

        if (
            !benchesByName.TryGetValue(binding.BenchName, out var inheritedBench)
            || inheritedBench.IsAbstract
        )
        {
            return;
        }

        CheckInheritedBindingExtensions(
            circuit.Name,
            binding,
            inheritedBench,
            dutTerminals,
            bundlesByName,
            resolvedBinding.Resolution.ExtensionStatementCount,
            diagnostics
        );
    }

    private static void CheckBinding(
        string ownerKind,
        string ownerName,
        BenchBinding binding,
        BenchDefinition bench,
        IReadOnlyDictionary<string, TerminalContract> dutTerminals,
        IReadOnlyDictionary<string, BundleType> bundlesByName,
        List<Diagnostic> diagnostics
    )
    {
        var mappings = binding.Statements.OfType<BenchTerminalMapping>().ToList();
        CheckBindingMeasurementExports(
            ownerKind,
            ownerName,
            binding,
            bench,
            binding.Statements.OfType<BenchBindingMeasurementExport>().ToList(),
            diagnostics
        );
        CheckBindingMappings(
            ownerKind,
            ownerName,
            binding,
            bench,
            dutTerminals,
            bundlesByName,
            mappings,
            requireAllBenchTerminalsMapped: true,
            diagnostics
        );
    }

    private static void CheckInheritedBindingExtensions(
        string circuitName,
        BenchBinding binding,
        BenchDefinition bench,
        IReadOnlyDictionary<string, TerminalContract> dutTerminals,
        IReadOnlyDictionary<string, BundleType> bundlesByName,
        int extensionStatementCount,
        List<Diagnostic> diagnostics
    )
    {
        var extensionStatements = GetExtensionStatements(binding, extensionStatementCount);
        if (extensionStatements.Count == 0)
        {
            return;
        }

        // The base inherited body was already validated in interface context. Only check
        // statements appended by circuit-local `extend` blocks here; in particular, do not
        // re-run required-terminal coverage checks because the inherited base body already
        // owns those obligations.
        CheckBindingMeasurementExports(
            "circuit",
            circuitName,
            binding,
            bench,
            extensionStatements.OfType<BenchBindingMeasurementExport>().ToList(),
            diagnostics
        );
        CheckBindingMappings(
            "circuit",
            circuitName,
            binding,
            bench,
            dutTerminals,
            bundlesByName,
            extensionStatements.OfType<BenchTerminalMapping>().ToList(),
            requireAllBenchTerminalsMapped: false,
            diagnostics
        );
    }

    private static void CheckBindingMappings(
        string ownerKind,
        string ownerName,
        BenchBinding binding,
        BenchDefinition bench,
        IReadOnlyDictionary<string, TerminalContract> dutTerminals,
        IReadOnlyDictionary<string, BundleType> bundlesByName,
        IReadOnlyList<BenchTerminalMapping> mappings,
        bool requireAllBenchTerminalsMapped,
        List<Diagnostic> diagnostics
    )
    {
        var benchTerminals = bench.Terminals.ToDictionary(
            terminal => terminal.Name,
            StringComparer.OrdinalIgnoreCase
        );
        var mappedBench = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in mappings)
        {
            if (!benchTerminals.TryGetValue(mapping.BenchTerminal, out var benchTerminal))
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS3003: Binding '{binding.BindingName}' maps unknown bench terminal '{mapping.BenchTerminal}' (bench '{bench.Name}').",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
                continue;
            }

            var dutRoot = TerminalContractModel.GetRootName(mapping.DutPinRef);
            if (!dutTerminals.TryGetValue(dutRoot, out var dutInfo))
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS3004: Binding '{binding.BindingName}' maps to unknown dut terminal '{mapping.DutPinRef}' in {ownerKind} '{ownerName}'.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
                continue;
            }

            if (benchTerminal.Type is null)
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS2024: Concrete bench '{bench.Name}' has terminal '{benchTerminal.Name}' without a type.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
                continue;
            }

            var benchLeaves = ExpandLeaves(benchTerminal.Name, benchTerminal.Type, bundlesByName)
                .ToList();
            var dutLeaves = TerminalContractModel.SelectLeaves(dutInfo.Leaves, mapping.DutPinRef);

            if (benchLeaves.Count != dutLeaves.Count)
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS3005: Binding '{binding.BindingName}' terminal mapping 'bench.{benchTerminal.Name}--dut.{mapping.DutPinRef}' has incompatible shapes ({benchLeaves.Count} vs {dutLeaves.Count}) in {ownerKind} '{ownerName}'.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
                continue;
            }

            for (var i = 0; i < benchLeaves.Count; i++)
            {
                if (
                    !string.Equals(
                        benchLeaves[i].LeafType,
                        dutLeaves[i].LeafType,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    diagnostics.Add(
                        new Diagnostic(
                            $"CAS3006: Binding '{binding.BindingName}' terminal mapping has incompatible leaf types: '{benchLeaves[i].Path}:{benchLeaves[i].LeafType}' vs '{dutLeaves[i].Path}:{dutLeaves[i].LeafType}' in {ownerKind} '{ownerName}'.",
                            DiagnosticSeverity.Error,
                            "<bench>",
                            1,
                            1
                        )
                    );
                }
            }

            mappedBench.Add(benchTerminal.Name);
        }

        if (!requireAllBenchTerminalsMapped)
        {
            return;
        }

        foreach (var terminal in bench.Terminals)
        {
            if (!mappedBench.Contains(terminal.Name))
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS3007: Binding '{binding.BindingName}' does not map required bench terminal '{terminal.Name}' (bench '{bench.Name}').",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
            }
        }
    }

    private static void CheckBindingMeasurementExports(
        string ownerKind,
        string ownerName,
        BenchBinding binding,
        BenchDefinition bench,
        IReadOnlyList<BenchBindingMeasurementExport> exportsToValidate,
        List<Diagnostic> diagnostics
    )
    {
        var exports = binding.Statements.OfType<BenchBindingMeasurementExport>().ToList();
        if (exports.Count == 0 || exportsToValidate.Count == 0)
        {
            return;
        }

        var byName = new Dictionary<string, BenchBindingMeasurementExport>(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var export in exports)
        {
            var shouldValidate = exportsToValidate.Any(candidate =>
                ReferenceEquals(candidate, export)
            );
            if (!byName.TryAdd(export.Name, export))
            {
                if (shouldValidate)
                {
                    diagnostics.Add(
                        new Diagnostic(
                            $"CAS3020: Binding '{binding.BindingName}' defines duplicate exported measurement '{export.Name}' in {ownerKind} '{ownerName}'.",
                            DiagnosticSeverity.Error,
                            "<bench>",
                            1,
                            1
                        )
                    );
                }
            }

            if (!shouldValidate)
            {
                continue;
            }

            if (export.Parameters.Count != 0)
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS3021: Binding '{binding.BindingName}' exported measurement '{export.Name}' must not declare parameters (binding exports are adapters, not overrides).",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
            }

            if (!export.Target.BindingAlias.Equals("base", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS3023: Binding '{binding.BindingName}' exported measurement '{export.Name}' must forward to 'base::<measurement>(...)'.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
                continue;
            }

            if (export.Target.Args.Any(a => a.Name is null))
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS3022: Binding '{binding.BindingName}' exported measurement '{export.Name}' forwarding call requires named arguments.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
            }

            var target = bench.Measurements.FirstOrDefault(m =>
                m.Name.Equals(export.Target.MeasurementName, StringComparison.OrdinalIgnoreCase)
            );
            if (target is null)
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS3024: Binding '{binding.BindingName}' exported measurement '{export.Name}' forwards to unknown measurement '{export.Target.MeasurementName}' on bench '{bench.Name}' in {ownerKind} '{ownerName}'.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
                continue;
            }

            if (
                export.Name.Equals(target.Name, StringComparison.OrdinalIgnoreCase)
                && target.Parameters.Count == 0
            )
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS3027: Binding '{binding.BindingName}' exported measurement '{export.Name}' would override a non-parameterized bench measurement. Use a different exported name if you intend to expose an adapter.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
            }

            if (!string.Equals(export.Unit, target.Unit, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS3025: Binding '{binding.BindingName}' exported measurement '{export.Name}' unit '{export.Unit}' does not match bench measurement '{target.Name}' unit '{target.Unit}' on bench '{bench.Name}'.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
            }
        }
    }

    private static IReadOnlyList<BenchBindingStatement> GetExtensionStatements(
        BenchBinding binding,
        int extensionStatementCount
    )
    {
        if (extensionStatementCount <= 0)
        {
            return Array.Empty<BenchBindingStatement>();
        }

        var startIndex = Math.Max(0, binding.Statements.Count - extensionStatementCount);
        return binding.Statements.Skip(startIndex).ToList();
    }

    private static IEnumerable<TerminalLeaf> ExpandLeaves(
        string basePath,
        string typeName,
        IReadOnlyDictionary<string, BundleType> bundlesByName
    )
    {
        if (!bundlesByName.TryGetValue(typeName, out var bundle))
        {
            yield return new TerminalLeaf(basePath, string.Empty, typeName);
            yield break;
        }

        foreach (var field in bundle.Fields)
        {
            var fieldPath = $"{basePath}.{field.Key}";
            foreach (var leaf in ExpandLeaves(fieldPath, field.Value, bundlesByName))
            {
                var relativePath = string.IsNullOrEmpty(leaf.RelativePath)
                    ? field.Key
                    : $"{field.Key}.{leaf.RelativePath}";
                yield return new TerminalLeaf(leaf.Path, relativePath, leaf.LeafType);
            }
        }
    }
}
