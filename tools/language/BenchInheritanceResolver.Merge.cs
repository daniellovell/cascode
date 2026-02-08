using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language;

public static partial class BenchInheritanceResolver
{
    private sealed partial class Resolver
    {
        private List<BenchParameter> MergeParameters(
            BenchDefinition baseBench,
            BenchDefinition child
        )
        {
            var merged = baseBench
                .Parameters.Select(p => new BenchParameter(p.Type, p.Name, p.Default))
                .ToList();
            var indexByName = merged
                .Select((param, index) => (param.Name, index))
                .ToDictionary(p => p.Name, p => p.index, StringComparer.Ordinal);

            foreach (var param in child.Parameters)
            {
                if (!indexByName.TryGetValue(param.Name, out var index))
                {
                    indexByName[param.Name] = merged.Count;
                    merged.Add(new BenchParameter(param.Type, param.Name, param.Default));
                    continue;
                }

                if (merged[index].Type != param.Type)
                {
                    _diagnostics.Add(
                        new Diagnostic(
                            $"CAS2034: Parameter '{param.Name}' in bench '{child.Name}' must keep type '{merged[index].Type}' when overriding defaults.",
                            DiagnosticSeverity.Error,
                            "<bench>",
                            1,
                            1
                        )
                    );
                    continue;
                }

                merged[index] = new BenchParameter(param.Type, param.Name, param.Default);
            }

            return merged;
        }

        private List<AnalysisDeclaration> MergeAnalyses(
            BenchDefinition baseBench,
            BenchDefinition child
        )
        {
            if (child.OverrideAnalysis)
            {
                if (baseBench.Analyses.Count == 0)
                {
                    _diagnostics.Add(
                        new Diagnostic(
                            $"CAS2033: 'override analysis' used but base bench has no analysis block.",
                            DiagnosticSeverity.Error,
                            "<bench>",
                            1,
                            1
                        )
                    );
                }

                return CloneAnalyses(child.Analyses);
            }

            if (baseBench.Analyses.Count == 0)
            {
                return CloneAnalyses(child.Analyses);
            }

            if (child.Analyses.Count > 0)
            {
                _diagnostics.Add(
                    new Diagnostic(
                        $"CAS2035: Bench '{child.Name}' defines analysis while inheriting one. Use 'override analysis' to replace it.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
            }

            return CloneAnalyses(baseBench.Analyses);
        }

        private List<MeasurementDefinition> MergeMeasurements(
            BenchDefinition baseBench,
            BenchDefinition child
        )
        {
            var merged = baseBench.Measurements.Select(CloneMeasurement).ToList();
            var indexByName = merged
                .Select((measurement, index) => (measurement.Name, index))
                .ToDictionary(m => m.Name, m => m.index, StringComparer.Ordinal);

            foreach (var measurement in child.Measurements)
            {
                if (measurement.IsOverride)
                {
                    if (!indexByName.TryGetValue(measurement.Name, out var index))
                    {
                        _diagnostics.Add(
                            new Diagnostic(
                                $"CAS2032: 'override measurement {measurement.Name}' targets nonexistent base measurement.",
                                DiagnosticSeverity.Error,
                                "<bench>",
                                1,
                                1
                            )
                        );
                        continue;
                    }

                    merged[index] = CloneMeasurement(measurement);
                    continue;
                }

                if (indexByName.ContainsKey(measurement.Name))
                {
                    _diagnostics.Add(
                        new Diagnostic(
                            $"CAS2029: Measurement '{measurement.Name}' duplicates inherited measurement (use 'override' to replace).",
                            DiagnosticSeverity.Error,
                            "<bench>",
                            1,
                            1
                        )
                    );
                    continue;
                }

                indexByName[measurement.Name] = merged.Count;
                merged.Add(CloneMeasurement(measurement));
            }

            return merged;
        }

        private static List<FunctionDefinition> MergeFunctions(
            BenchDefinition baseBench,
            BenchDefinition child
        )
        {
            var merged = baseBench.Functions.ToList();
            var indexByName = merged
                .Select((fn, index) => (fn.Name, index))
                .ToDictionary(f => f.Name, f => f.index, StringComparer.Ordinal);

            foreach (var fn in child.Functions)
            {
                if (!indexByName.TryGetValue(fn.Name, out var index))
                {
                    indexByName[fn.Name] = merged.Count;
                    merged.Add(fn);
                    continue;
                }

                merged[index] = fn;
            }

            return merged;
        }

        private void ValidateTerminalKinds(BenchDefinition bench)
        {
            foreach (var terminal in bench.Terminals)
            {
                if (!bench.IsAbstract && terminal.IsAbstract)
                {
                    _diagnostics.Add(
                        new Diagnostic(
                            $"CAS2023: Abstract terminal '{terminal.Name}' in non-abstract bench '{bench.Name}'.",
                            DiagnosticSeverity.Error,
                            "<bench>",
                            1,
                            1
                        )
                    );
                }

                if (!terminal.IsAbstract && terminal.Type is null)
                {
                    _diagnostics.Add(
                        new Diagnostic(
                            $"CAS2024: Concrete bench '{bench.Name}' has terminal '{terminal.Name}' without a type.",
                            DiagnosticSeverity.Error,
                            "<bench>",
                            1,
                            1
                        )
                    );
                }
            }
        }

        private void ReportCycle(string repeatedName)
        {
            var index = _stack.FindIndex(s => s.Equals(repeatedName, StringComparison.Ordinal));
            var cycle =
                index >= 0 ? _stack.Skip(index).Concat(new[] { repeatedName }) : [repeatedName];
            var chain = string.Join(" -> ", cycle);

            _diagnostics.Add(
                new Diagnostic(
                    $"CAS2030: Inheritance cycle detected: '{chain}'.",
                    DiagnosticSeverity.Error,
                    "<bench>",
                    1,
                    1
                )
            );
        }

        private static BenchDefinition CloneBench(
            BenchDefinition bench,
            bool clearBaseBench = false
        )
        {
            return new BenchDefinition
            {
                Name = bench.Name,
                IsAbstract = bench.IsAbstract,
                BaseBench = clearBaseBench ? null : bench.BaseBench,
                OverrideAnalysis = bench.OverrideAnalysis,
                Parameters = bench
                    .Parameters.Select(p => new BenchParameter(p.Type, p.Name, p.Default))
                    .ToList(),
                Terminals = bench.Terminals.Select(CloneTerminal).ToList(),
                Fill = bench.Fill,
                Functions = bench.Functions.ToList(),
                Analyses = CloneAnalyses(bench.Analyses),
                Measurements = bench.Measurements.Select(CloneMeasurement).ToList(),
            };
        }

        private static BenchTerminal CloneTerminal(BenchTerminal terminal)
        {
            return new BenchTerminal(
                terminal.Role,
                terminal.Name,
                terminal.Type,
                terminal.IsAbstract
            );
        }

        private static List<AnalysisDeclaration> CloneAnalyses(
            IReadOnlyList<AnalysisDeclaration> analyses
        )
        {
            return analyses
                .Select(a => new AnalysisDeclaration
                {
                    Type = a.Type,
                    Name = a.Name,
                    Parameters = new Dictionary<string, MeasurementExpr>(
                        a.Parameters,
                        StringComparer.Ordinal
                    ),
                })
                .ToList();
        }

        private static MeasurementDefinition CloneMeasurement(MeasurementDefinition measurement)
        {
            return new MeasurementDefinition
            {
                Name = measurement.Name,
                IsOverride = false,
                Parameters = measurement.Parameters.ToList(),
                Unit = measurement.Unit,
                Body = measurement.Body.ToList(),
            };
        }
    }
}
