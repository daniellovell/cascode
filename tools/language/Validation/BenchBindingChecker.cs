using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language.Validation;

public static class BenchBindingChecker
{
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

        foreach (var circuit in document.Circuits)
        {
            var resolved = ResolveBenchBindingsForCircuit(circuit, interfacesByName, diagnostics);

            CheckConstraintBenchReferences(circuit, resolved, benchesByName, diagnostics);

            var dutTerminals = TerminalContractModel.ForCircuit(circuit);

            foreach (var binding in resolved.Values)
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
                    "circuit",
                    circuit.Name,
                    binding,
                    bench,
                    dutTerminals,
                    bundlesByName,
                    diagnostics
                );
            }
        }
    }

    private static Dictionary<string, BenchBinding> ResolveBenchBindingsForCircuit(
        Circuit circuit,
        IReadOnlyDictionary<string, TraitDefinition> interfacesByName,
        List<Diagnostic> diagnostics
    )
    {
        var resolved = new Dictionary<string, BenchBinding>(StringComparer.OrdinalIgnoreCase);

        if (circuit.Traits is { Count: > 0 })
        {
            foreach (var interfaceName in circuit.Traits)
            {
                if (!interfacesByName.TryGetValue(interfaceName, out var interfaceDef))
                {
                    continue;
                }

                foreach (var binding in interfaceDef.BenchBindings)
                {
                    if (!resolved.TryAdd(binding.BindingName, binding))
                    {
                        diagnostics.Add(
                            new Diagnostic(
                                $"CAS3002: Duplicate inherited bench binding name '{binding.BindingName}' on circuit '{circuit.Name}'.",
                                DiagnosticSeverity.Error,
                                "<bench>",
                                1,
                                1
                            )
                        );
                    }
                }
            }
        }

        foreach (var binding in circuit.BenchBindings)
        {
            // Circuit bindings override inherited bindings by binding name.
            resolved[binding.BindingName] = binding;
        }

        return resolved;
    }

    private static void CheckConstraintBenchReferences(
        Circuit circuit,
        IReadOnlyDictionary<string, BenchBinding> resolvedBindings,
        IReadOnlyDictionary<string, BenchDefinition> benchesByName,
        List<Diagnostic> diagnostics
    )
    {
        if (circuit.Constraints?.Numeric is not { Count: > 0 })
        {
            return;
        }

        foreach (var constraint in circuit.Constraints.Numeric)
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
        var benchTerminals = bench.Terminals.ToDictionary(
            t => t.Name,
            StringComparer.OrdinalIgnoreCase
        );

        CheckBindingMeasurementExports(ownerKind, ownerName, binding, bench, diagnostics);

        var mappings = binding.Statements.OfType<BenchTerminalMapping>().ToList();
        var mappedBench = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var m in mappings)
        {
            if (!benchTerminals.TryGetValue(m.BenchTerminal, out var benchTerminal))
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS3003: Binding '{binding.BindingName}' maps unknown bench terminal '{m.BenchTerminal}' (bench '{bench.Name}').",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
                continue;
            }

            var dutRoot = TerminalContractModel.GetRootName(m.DutPinRef);
            if (!dutTerminals.TryGetValue(dutRoot, out var dutInfo))
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS3004: Binding '{binding.BindingName}' maps to unknown dut terminal '{m.DutPinRef}' in {ownerKind} '{ownerName}'.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
                continue;
            }

            // Expand mapping for bundles.
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
            var dutLeaves = TerminalContractModel.SelectLeaves(dutInfo.Leaves, m.DutPinRef);

            if (benchLeaves.Count != dutLeaves.Count)
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS3005: Binding '{binding.BindingName}' terminal mapping 'bench.{benchTerminal.Name}--dut.{m.DutPinRef}' has incompatible shapes ({benchLeaves.Count} vs {dutLeaves.Count}) in {ownerKind} '{ownerName}'.",
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

        foreach (var t in bench.Terminals)
        {
            if (!mappedBench.Contains(t.Name))
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS3007: Binding '{binding.BindingName}' does not map required bench terminal '{t.Name}' (bench '{bench.Name}').",
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
        List<Diagnostic> diagnostics
    )
    {
        var exports = binding.Statements.OfType<BenchBindingMeasurementExport>().ToList();
        if (exports.Count == 0)
        {
            return;
        }

        var byName = new Dictionary<string, BenchBindingMeasurementExport>(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var export in exports)
        {
            if (!byName.TryAdd(export.Name, export))
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
