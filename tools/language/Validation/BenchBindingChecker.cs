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

        foreach (var circuit in document.Circuits)
        {
            var resolved = ResolveBenchBindingsForCircuit(circuit, interfacesByName, diagnostics);

            CheckConstraintBenchReferences(circuit, resolved, benchesByName, diagnostics);

            var dutTerminals = BuildDutTerminalMap(circuit, bundlesByName);

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

                CheckBinding(circuit, binding, bench, dutTerminals, bundlesByName, diagnostics);
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
            if (string.IsNullOrEmpty(constraint.Bench))
            {
                continue;
            }

            if (!resolvedBindings.ContainsKey(constraint.Bench))
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS3008: Constraint '{constraint.Id}' references unknown bench binding '{constraint.Bench}' in circuit '{circuit.Name}'.",
                        DiagnosticSeverity.Error,
                        "<constraints>",
                        1,
                        1
                    )
                );
            }
        }
    }

    private static Dictionary<
        string,
        (string Type, IReadOnlyList<(string Path, string LeafType)> Leaves)
    > BuildDutTerminalMap(Circuit circuit, IReadOnlyDictionary<string, BundleType> bundlesByName)
    {
        // Circuits are bundle-desugared before semantic checks run, so bundle ports like
        // "IN : Diff" appear as scalar leaf ports "IN.P", "IN.N". For bench bindings, we
        // still want to allow mapping at the bundle root ("dut.IN"), so we group leaf ports
        // by their root prefix.
        var map = new Dictionary<string, (string, IReadOnlyList<(string, string)>)>(
            StringComparer.OrdinalIgnoreCase
        );

        foreach (
            var group in circuit.Ports.GroupBy(
                p => GetRootName(p.Name),
                StringComparer.OrdinalIgnoreCase
            )
        )
        {
            var leaves = group
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(p => (Path: p.Name, LeafType: p.Type))
                .ToList();

            // If a root has a single leaf (no dot), preserve its scalar type.
            // If it has multiple leaves, the root is effectively a bundle.
            var rootType =
                leaves.Count == 1 && !leaves[0].Path.Contains('.') ? leaves[0].LeafType : group.Key;

            map[group.Key] = (rootType, leaves);
        }

        foreach (var s in circuit.Supplies)
        {
            map[s] = ("supply", new List<(string, string)> { (s, "supply") });
        }

        foreach (var g in circuit.Grounds)
        {
            map[g] = ("ground", new List<(string, string)> { (g, "ground") });
        }

        return map;
    }

    private static void CheckBinding(
        Circuit circuit,
        BenchBinding binding,
        BenchDefinition bench,
        IReadOnlyDictionary<
            string,
            (string Type, IReadOnlyList<(string Path, string LeafType)> Leaves)
        > dutTerminals,
        IReadOnlyDictionary<string, BundleType> bundlesByName,
        List<Diagnostic> diagnostics
    )
    {
        var benchTerminals = bench.Terminals.ToDictionary(
            t => t.Name,
            StringComparer.OrdinalIgnoreCase
        );

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

            var dutRoot = GetRootName(m.DutPinRef);
            if (!dutTerminals.TryGetValue(dutRoot, out var dutInfo))
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS3004: Binding '{binding.BindingName}' maps to unknown dut terminal '{m.DutPinRef}' in circuit '{circuit.Name}'.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
                continue;
            }

            // Expand mapping for bundles.
            var benchLeaves = ExpandLeaves(benchTerminal.Name, benchTerminal.Type, bundlesByName)
                .ToList();
            var dutLeaves = SelectLeaves(dutInfo.Leaves, m.DutPinRef);

            if (benchLeaves.Count != dutLeaves.Count)
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS3005: Binding '{binding.BindingName}' terminal mapping 'bench.{benchTerminal.Name}--dut.{m.DutPinRef}' has incompatible shapes ({benchLeaves.Count} vs {dutLeaves.Count}).",
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
                            $"CAS3006: Binding '{binding.BindingName}' terminal mapping has incompatible leaf types: '{benchLeaves[i].Path}:{benchLeaves[i].LeafType}' vs '{dutLeaves[i].Path}:{dutLeaves[i].LeafType}'.",
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

    private static string GetRootName(string pinRef)
    {
        var dot = pinRef.IndexOf('.', StringComparison.Ordinal);
        return dot < 0 ? pinRef : pinRef[..dot];
    }

    private static IReadOnlyList<(string Path, string LeafType)> SelectLeaves(
        IReadOnlyList<(string Path, string LeafType)> leaves,
        string pinRef
    )
    {
        // If the mapping targets a leaf (e.g. "IN.P"), constrain to that leaf.
        // Otherwise, use the full set under the root.
        if (!pinRef.Contains('.', StringComparison.Ordinal))
        {
            return leaves;
        }

        return leaves
            .Where(l =>
                l.Path.Equals(pinRef, StringComparison.OrdinalIgnoreCase)
                || l.Path.StartsWith(pinRef + ".", StringComparison.OrdinalIgnoreCase)
            )
            .ToList();
    }

    private static IEnumerable<(string Path, string LeafType)> ExpandLeaves(
        string basePath,
        string typeName,
        IReadOnlyDictionary<string, BundleType> bundlesByName
    )
    {
        if (!bundlesByName.TryGetValue(typeName, out var bundle))
        {
            yield return (basePath, typeName);
            yield break;
        }

        foreach (var field in bundle.Fields)
        {
            var fieldPath = $"{basePath}.{field.Key}";
            foreach (var leaf in ExpandLeaves(fieldPath, field.Value, bundlesByName))
            {
                yield return leaf;
            }
        }
    }
}
