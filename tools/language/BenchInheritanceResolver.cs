using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language;

public static partial class BenchInheritanceResolver
{
    public static CascodeDocument Resolve(CascodeDocument document, List<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (document.BenchDefinitions.Count == 0)
        {
            return document;
        }

        ReportAbstractBenchBindings(document, diagnostics);

        var resolver = new Resolver(document.BenchDefinitions, diagnostics);
        var resolved = resolver.ResolveAll();
        var concrete = resolved.Where(b => !b.IsAbstract).ToList();

        return new CascodeDocument
        {
            VersionMajor = document.VersionMajor,
            VersionMinor = document.VersionMinor,
            Includes = document.Includes,
            FileLibrary = document.FileLibrary,
            Functions = document.Functions,
            BundleTypes = document.BundleTypes,
            Traits = document.Traits,
            BenchDefinitions = concrete,
            Primitives = document.Primitives,
            Circuits = document.Circuits,
        };
    }

    private static void ReportAbstractBenchBindings(
        CascodeDocument document,
        List<Diagnostic> diagnostics
    )
    {
        var abstractBenches = document
            .BenchDefinitions.Where(b => b.IsAbstract)
            .Select(b => b.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var trait in document.Traits)
        {
            foreach (var binding in trait.BenchBindings)
            {
                if (!abstractBenches.Contains(binding.BenchName))
                {
                    continue;
                }

                diagnostics.Add(
                    new Diagnostic(
                        $"CAS2022: Abstract bench '{binding.BenchName}' cannot appear in bind statements.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
            }
        }

        foreach (var circuit in document.Circuits)
        {
            foreach (var binding in circuit.BenchBindings)
            {
                if (!abstractBenches.Contains(binding.BenchName))
                {
                    continue;
                }

                diagnostics.Add(
                    new Diagnostic(
                        $"CAS2022: Abstract bench '{binding.BenchName}' cannot appear in bind statements.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
            }
        }
    }

    private sealed partial class Resolver
    {
        private readonly IReadOnlyList<BenchDefinition> _sourceOrder;
        private readonly IReadOnlyDictionary<string, BenchDefinition> _benchesByName;
        private readonly List<Diagnostic> _diagnostics;
        private readonly Dictionary<string, BenchDefinition> _resolved = new(
            StringComparer.Ordinal
        );
        private readonly HashSet<string> _visiting = new(StringComparer.Ordinal);
        private readonly List<string> _stack = new();

        public Resolver(IReadOnlyList<BenchDefinition> benches, List<Diagnostic> diagnostics)
        {
            _sourceOrder = benches;
            _diagnostics = diagnostics;
            _benchesByName = benches.ToDictionary(b => b.Name, StringComparer.Ordinal);
        }

        public IReadOnlyList<BenchDefinition> ResolveAll()
        {
            foreach (var bench in _sourceOrder)
            {
                _ = ResolveBench(bench.Name);
            }

            return _sourceOrder
                .Select(b =>
                    _resolved.TryGetValue(b.Name, out var resolved) ? resolved : CloneBench(b)
                )
                .ToList();
        }

        private BenchDefinition? ResolveBench(string name)
        {
            if (_resolved.TryGetValue(name, out var cached))
            {
                return cached;
            }

            if (!_benchesByName.TryGetValue(name, out var bench))
            {
                return null;
            }

            if (_visiting.Contains(name))
            {
                ReportCycle(name);
                return null;
            }

            _visiting.Add(name);
            _stack.Add(name);

            var resolved = ResolveBenchCore(bench);

            _stack.RemoveAt(_stack.Count - 1);
            _visiting.Remove(name);

            if (resolved is not null)
            {
                _resolved[name] = resolved;
            }

            return resolved;
        }

        private BenchDefinition ResolveBenchCore(BenchDefinition bench)
        {
            if (bench.BaseBench is null)
            {
                ValidateBenchWithoutBase(bench);
                return CloneBench(bench);
            }

            if (!_benchesByName.TryGetValue(bench.BaseBench, out var _))
            {
                _diagnostics.Add(
                    new Diagnostic(
                        $"CAS2020: 'extends' references unknown bench '{bench.BaseBench}'.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
                return CloneBench(bench, clearBaseBench: true);
            }

            var resolvedBase = ResolveBench(bench.BaseBench);
            if (resolvedBase is null)
            {
                return CloneBench(bench, clearBaseBench: true);
            }

            if (!resolvedBase.IsAbstract)
            {
                _diagnostics.Add(
                    new Diagnostic(
                        $"CAS2021: 'extends' targets non-abstract bench '{bench.BaseBench}'.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
            }

            return FlattenBench(resolvedBase, bench);
        }

        private void ValidateBenchWithoutBase(BenchDefinition bench)
        {
            if (bench.IsAbstract && bench.Fill is not null)
            {
                _diagnostics.Add(
                    new Diagnostic(
                        $"CAS2027: Abstract bench '{bench.Name}' must not have a fill block.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
            }

            ValidateTerminalKinds(bench);
        }

        private BenchDefinition FlattenBench(BenchDefinition baseBench, BenchDefinition child)
        {
            if (child.IsAbstract && child.Fill is not null)
            {
                _diagnostics.Add(
                    new Diagnostic(
                        $"CAS2027: Abstract bench '{child.Name}' must not have a fill block.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
            }

            if (!child.IsAbstract && child.Fill is null)
            {
                _diagnostics.Add(
                    new Diagnostic(
                        $"CAS2028: Extending bench '{child.Name}' must have a fill block.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
            }

            var terminals = MergeTerminals(baseBench, child);
            var parameters = MergeParameters(baseBench, child);
            var analyses = MergeAnalyses(baseBench, child);
            var measurements = MergeMeasurements(baseBench, child);
            var functions = MergeFunctions(baseBench, child);

            var flattened = new BenchDefinition
            {
                Name = child.Name,
                IsAbstract = child.IsAbstract,
                BaseBench = null,
                OverrideAnalysis = false,
                Parameters = parameters,
                Terminals = terminals,
                Fill = child.Fill,
                Functions = functions,
                Analyses = analyses,
                Measurements = measurements,
            };

            ValidateTerminalKinds(flattened);
            return flattened;
        }

        private List<BenchTerminal> MergeTerminals(BenchDefinition baseBench, BenchDefinition child)
        {
            var merged = child.Terminals.Select(CloneTerminal).ToList();
            var byName = child.Terminals.ToDictionary(t => t.Name, StringComparer.Ordinal);
            var mergedIndexByName = merged
                .Select((terminal, index) => (terminal.Name, index))
                .ToDictionary(t => t.Name, t => t.index, StringComparer.Ordinal);

            foreach (var inherited in baseBench.Terminals)
            {
                if (!byName.TryGetValue(inherited.Name, out var local))
                {
                    if (!child.IsAbstract)
                    {
                        _diagnostics.Add(
                            new Diagnostic(
                                $"CAS2025: Extending bench '{child.Name}' missing terminal for abstract terminal '{inherited.Name}' from '{baseBench.Name}'.",
                                DiagnosticSeverity.Error,
                                "<bench>",
                                1,
                                1
                            )
                        );
                    }

                    merged.Add(CloneTerminal(inherited));
                    continue;
                }

                if (inherited.Role != local.Role)
                {
                    _diagnostics.Add(
                        new Diagnostic(
                            $"CAS2026: Terminal '{local.Name}' role mismatch with base '{baseBench.Name}'.",
                            DiagnosticSeverity.Error,
                            "<bench>",
                            1,
                            1
                        )
                    );
                }

                if (inherited.Type is null)
                {
                    if (local.Type is null)
                    {
                        if (child.IsAbstract)
                        {
                            continue;
                        }

                        _diagnostics.Add(
                            new Diagnostic(
                                $"CAS2025: Extending bench '{child.Name}' missing terminal for abstract terminal '{local.Name}' from '{baseBench.Name}'.",
                                DiagnosticSeverity.Error,
                                "<bench>",
                                1,
                                1
                            )
                        );
                    }
                    continue;
                }

                if (local.Type is null)
                {
                    if (
                        child.IsAbstract
                        && mergedIndexByName.TryGetValue(local.Name, out var inheritedIndex)
                    )
                    {
                        merged[inheritedIndex] = CloneTerminal(inherited);
                        continue;
                    }

                    _diagnostics.Add(
                        new Diagnostic(
                            $"CAS2024: Concrete bench '{child.Name}' has terminal '{local.Name}' without a type.",
                            DiagnosticSeverity.Error,
                            "<bench>",
                            1,
                            1
                        )
                    );
                    continue;
                }

                if (!string.Equals(inherited.Type, local.Type, StringComparison.Ordinal))
                {
                    _diagnostics.Add(
                        new Diagnostic(
                            $"CAS2031: Concrete terminal '{local.Name}' type mismatch: base has '{inherited.Type}', extending has '{local.Type}'.",
                            DiagnosticSeverity.Error,
                            "<bench>",
                            1,
                            1
                        )
                    );
                }
            }

            return merged;
        }
    }
}
