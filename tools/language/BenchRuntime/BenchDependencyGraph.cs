using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language.BenchRuntime;

public sealed class BenchDependencyGraph
{
    public sealed record MetricInvocationArg(string Name, string Text, MeasurementExpr Expr);

    public sealed record BenchMetricInvocation(
        string BenchInstanceName,
        string BenchBindingAlias,
        string MetricName,
        IReadOnlyList<MetricInvocationArg> Args
    )
    {
        public string MetricKey => FormatMetricKey(MetricName, Args);
        public string Id => $"{BenchInstanceName}/{MetricKey}";
    }

    private readonly Dictionary<string, BenchMetricInvocation> _invocationsById;
    private readonly Dictionary<string, HashSet<string>> _depsById;
    private readonly Dictionary<string, HashSet<string>> _dependentsById;

    private BenchDependencyGraph(
        Dictionary<string, BenchMetricInvocation> invocationsById,
        Dictionary<string, HashSet<string>> depsById
    )
    {
        _invocationsById = invocationsById;
        _depsById = depsById;
        _dependentsById = BuildDependentsMap(depsById);
    }

    public IReadOnlyDictionary<string, BenchMetricInvocation> InvocationsById => _invocationsById;

    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> DependenciesById =>
        _depsById.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyCollection<string>)kvp.Value);

    public static bool TryBuild(
        Circuit circuit,
        IReadOnlyList<NumericConstraint> constraints,
        IReadOnlyDictionary<string, BenchDefinition> benchByBindingAlias,
        IReadOnlyDictionary<
            string,
            IReadOnlyDictionary<string, BenchBindingMeasurementExport>
        > bindingMeasurementExportsByBindingAlias,
        out BenchDependencyGraph graph,
        out IReadOnlyList<Diagnostic> diagnostics
    )
    {
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(constraints);
        ArgumentNullException.ThrowIfNull(benchByBindingAlias);
        ArgumentNullException.ThrowIfNull(bindingMeasurementExportsByBindingAlias);

        graph = null!;
        var diags = new List<Diagnostic>();

        var invocationsById = new Dictionary<string, BenchMetricInvocation>(StringComparer.Ordinal);
        var depsById = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        var queue = new Queue<BenchMetricInvocation>();

        foreach (var c in constraints)
        {
            var root = TryCreateInvocationFromConstraint(
                circuit,
                c,
                benchByBindingAlias,
                bindingMeasurementExportsByBindingAlias,
                diags
            );
            if (root is null)
            {
                continue;
            }

            if (invocationsById.TryAdd(root.Id, root))
            {
                queue.Enqueue(root);
            }

            depsById.TryAdd(root.Id, new HashSet<string>(StringComparer.Ordinal));
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!depsById.TryGetValue(current.Id, out var deps))
            {
                deps = new HashSet<string>(StringComparer.Ordinal);
                depsById[current.Id] = deps;
            }

            foreach (var arg in current.Args)
            {
                foreach (var reference in EnumerateBenchMeasurementRefs(arg.Expr))
                {
                    var dep = TryCreateInvocationFromRef(
                        circuit,
                        reference,
                        benchByBindingAlias,
                        bindingMeasurementExportsByBindingAlias,
                        diags
                    );
                    if (dep is null)
                    {
                        continue;
                    }

                    deps.Add(dep.Id);
                    if (invocationsById.TryAdd(dep.Id, dep))
                    {
                        queue.Enqueue(dep);
                    }

                    depsById.TryAdd(dep.Id, new HashSet<string>(StringComparer.Ordinal));
                }
            }

            if (
                current.Args.Count == 0
                && bindingMeasurementExportsByBindingAlias.TryGetValue(
                    current.BenchBindingAlias,
                    out var exports
                )
                && exports.TryGetValue(current.MetricName, out var export)
            )
            {
                if (!export.Target.BindingAlias.Equals("base", StringComparison.OrdinalIgnoreCase))
                {
                    diags.Add(
                        new Diagnostic(
                            $"CAS3026: Binding '{current.BenchBindingAlias}' exported measurement '{export.Name}' must forward to 'base::<measurement>(...)'.",
                            DiagnosticSeverity.Error,
                            "<bench>",
                            1,
                            1
                        )
                    );
                    continue;
                }

                foreach (var forwardedArg in export.Target.Args)
                {
                    foreach (var reference in EnumerateBenchMeasurementRefs(forwardedArg.Expr))
                    {
                        var dep = TryCreateInvocationFromRef(
                            circuit,
                            reference,
                            benchByBindingAlias,
                            bindingMeasurementExportsByBindingAlias,
                            diags
                        );
                        if (dep is null)
                        {
                            continue;
                        }

                        deps.Add(dep.Id);
                        if (invocationsById.TryAdd(dep.Id, dep))
                        {
                            queue.Enqueue(dep);
                        }

                        depsById.TryAdd(dep.Id, new HashSet<string>(StringComparer.Ordinal));
                    }
                }
            }
        }

        ValidateNoCycles(circuit, invocationsById, depsById, diags);

        diagnostics = diags;
        if (diags.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return false;
        }

        graph = new BenchDependencyGraph(invocationsById, depsById);
        return true;
    }

    public IReadOnlyList<IReadOnlyList<string>> GetExecutionLevels()
    {
        var inDegree = _depsById.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Count);
        var ready = new Queue<string>(inDegree.Where(kvp => kvp.Value == 0).Select(kvp => kvp.Key));

        var levels = new List<IReadOnlyList<string>>();
        var remaining = new HashSet<string>(inDegree.Keys, StringComparer.Ordinal);

        while (ready.Count > 0)
        {
            var level = new List<string>();
            var count = ready.Count;
            for (var i = 0; i < count; i++)
            {
                var n = ready.Dequeue();
                if (!remaining.Remove(n))
                {
                    continue;
                }
                level.Add(n);
            }

            foreach (var n in level)
            {
                if (!_dependentsById.TryGetValue(n, out var dependents))
                {
                    continue;
                }

                foreach (var d in dependents)
                {
                    if (!inDegree.ContainsKey(d))
                    {
                        continue;
                    }

                    inDegree[d]--;
                    if (inDegree[d] == 0)
                    {
                        ready.Enqueue(d);
                    }
                }
            }

            if (level.Count > 0)
            {
                levels.Add(level);
            }
        }

        if (remaining.Count > 0)
        {
            // Should already be diagnosed by TryBuild.
            throw new InvalidOperationException(
                $"Circular dependency detected among bench measurements: {string.Join(", ", remaining.OrderBy(x => x, StringComparer.Ordinal))}"
            );
        }

        return levels;
    }

    private static BenchMetricInvocation? TryCreateInvocationFromConstraint(
        Circuit circuit,
        NumericConstraint c,
        IReadOnlyDictionary<string, BenchDefinition> benchByBindingAlias,
        IReadOnlyDictionary<
            string,
            IReadOnlyDictionary<string, BenchBindingMeasurementExport>
        > bindingMeasurementExportsByBindingAlias,
        List<Diagnostic> diagnostics
    )
    {
        if (string.IsNullOrWhiteSpace(c.BenchBase) || string.IsNullOrWhiteSpace(c.Bench))
        {
            return null;
        }

        if (!benchByBindingAlias.TryGetValue(c.BenchBase, out var bench))
        {
            diagnostics.Add(
                new Diagnostic(
                    $"CAS3012: Constraint '{c.Id}' references unknown bench binding '{c.BenchBase}' in circuit '{circuit.Name}'.",
                    DiagnosticSeverity.Error,
                    "<constraints>",
                    1,
                    1
                )
            );
            return null;
        }

        if (
            !HasMeasurementOrExport(
                bench,
                bindingMeasurementExportsByBindingAlias,
                c.BenchBase,
                c.Metric
            )
        )
        {
            diagnostics.Add(
                new Diagnostic(
                    $"CAS3013: Constraint '{c.Id}' references unknown measurement '{c.Metric}' on bench binding '{c.BenchBase}' (bench '{bench.Name}') in circuit '{circuit.Name}'.",
                    DiagnosticSeverity.Error,
                    "<constraints>",
                    1,
                    1
                )
            );
            return null;
        }

        var args = new List<MetricInvocationArg>();
        foreach (var a in c.MetricArgs)
        {
            if (
                !CascodeAstBuilder.TryParseMeasurementExprText(a.Value, out var expr, out var diags)
            )
            {
                diagnostics.AddRange(diags);
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS3014: Failed to parse constraint argument expression '{a.Name}={a.Value}' in constraint '{c.Id}'.",
                        DiagnosticSeverity.Error,
                        "<constraints>",
                        1,
                        1
                    )
                );
                return null;
            }

            args.Add(new MetricInvocationArg(a.Name, a.Value, expr!));
        }

        return new BenchMetricInvocation(c.Bench, c.BenchBase, c.Metric, args);
    }

    private static BenchMetricInvocation? TryCreateInvocationFromRef(
        Circuit circuit,
        MeasurementBenchMeasurementRef r,
        IReadOnlyDictionary<string, BenchDefinition> benchByBindingAlias,
        IReadOnlyDictionary<
            string,
            IReadOnlyDictionary<string, BenchBindingMeasurementExport>
        > bindingMeasurementExportsByBindingAlias,
        List<Diagnostic> diagnostics
    )
    {
        if (!benchByBindingAlias.TryGetValue(r.BindingAlias, out var bench))
        {
            diagnostics.Add(
                new Diagnostic(
                    $"CAS3015: Cross-bench reference targets unknown bench binding '{r.BindingAlias}' in circuit '{circuit.Name}'.",
                    DiagnosticSeverity.Error,
                    "<constraints>",
                    1,
                    1
                )
            );
            return null;
        }

        if (
            !HasMeasurementOrExport(
                bench,
                bindingMeasurementExportsByBindingAlias,
                r.BindingAlias,
                r.MeasurementName
            )
        )
        {
            diagnostics.Add(
                new Diagnostic(
                    $"CAS3016: Cross-bench reference targets unknown measurement '{r.MeasurementName}' on bench binding '{r.BindingAlias}' (bench '{bench.Name}') in circuit '{circuit.Name}'.",
                    DiagnosticSeverity.Error,
                    "<constraints>",
                    1,
                    1
                )
            );
            return null;
        }

        var args = new List<MetricInvocationArg>();
        foreach (var a in r.Args)
        {
            if (a.Name is null)
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS3017: Cross-bench reference '{r.BindingAlias}::{r.MeasurementName}(...)' requires named arguments.",
                        DiagnosticSeverity.Error,
                        "<constraints>",
                        1,
                        1
                    )
                );
                return null;
            }

            args.Add(new MetricInvocationArg(a.Name, a.Text, a.Expr));
        }

        // Cross-bench references select the default bench invocation (no bench invocation args).
        return new BenchMetricInvocation(
            BenchInstanceName: r.BindingAlias,
            BenchBindingAlias: r.BindingAlias,
            MetricName: r.MeasurementName,
            Args: args
        );
    }

    private static bool HasMeasurementOrExport(
        BenchDefinition bench,
        IReadOnlyDictionary<
            string,
            IReadOnlyDictionary<string, BenchBindingMeasurementExport>
        > bindingMeasurementExportsByBindingAlias,
        string bindingAlias,
        string metricName
    )
    {
        if (
            bench.Measurements.Any(m =>
                m.Name.Equals(metricName, StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            return true;
        }

        return bindingMeasurementExportsByBindingAlias.TryGetValue(bindingAlias, out var exports)
            && exports.ContainsKey(metricName);
    }

    private static IEnumerable<MeasurementBenchMeasurementRef> EnumerateBenchMeasurementRefs(
        MeasurementExpr expr
    )
    {
        switch (expr)
        {
            case MeasurementBenchMeasurementRef r:
                yield return r;
                foreach (var a in r.Args)
                {
                    foreach (var nested in EnumerateBenchMeasurementRefs(a.Expr))
                    {
                        yield return nested;
                    }
                }
                yield break;

            case MeasurementUnary u:
                foreach (var nested in EnumerateBenchMeasurementRefs(u.Operand))
                {
                    yield return nested;
                }
                yield break;

            case MeasurementBinary b:
                foreach (var nested in EnumerateBenchMeasurementRefs(b.Left))
                {
                    yield return nested;
                }
                foreach (var nested in EnumerateBenchMeasurementRefs(b.Right))
                {
                    yield return nested;
                }
                yield break;

            case MeasurementConditional c:
                foreach (var nested in EnumerateBenchMeasurementRefs(c.ThenExpr))
                {
                    yield return nested;
                }
                foreach (var nested in EnumerateBenchMeasurementRefs(c.ElseExpr))
                {
                    yield return nested;
                }
                yield break;

            case MeasurementMethodCall m:
                foreach (var nested in EnumerateBenchMeasurementRefs(m.Receiver))
                {
                    yield return nested;
                }
                foreach (var a in m.Args)
                {
                    foreach (var nested in EnumerateBenchMeasurementRefs(a.Value))
                    {
                        yield return nested;
                    }
                }
                yield break;

            case MeasurementCall call:
                foreach (var a in call.Args)
                {
                    foreach (var nested in EnumerateBenchMeasurementRefs(a.Value))
                    {
                        yield return nested;
                    }
                }
                yield break;

            case MeasurementNew constructor:
                foreach (var a in constructor.Args)
                {
                    foreach (var nested in EnumerateBenchMeasurementRefs(a.Value))
                    {
                        yield return nested;
                    }
                }
                yield break;
        }
    }

    private static string FormatMetricKey(string metric, IReadOnlyList<MetricInvocationArg> args)
    {
        if (args.Count == 0)
        {
            return metric;
        }

        var parts = args.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(a => $"{a.Name}={a.Text}");
        return $"{metric}({string.Join(", ", parts)})";
    }

    private static Dictionary<string, HashSet<string>> BuildDependentsMap(
        IReadOnlyDictionary<string, HashSet<string>> depsById
    )
    {
        var dependents = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (node, deps) in depsById)
        {
            dependents.TryAdd(node, new HashSet<string>(StringComparer.Ordinal));
            foreach (var dep in deps)
            {
                if (!dependents.TryGetValue(dep, out var list))
                {
                    list = new HashSet<string>(StringComparer.Ordinal);
                    dependents[dep] = list;
                }
                list.Add(node);
            }
        }
        return dependents;
    }

    private static void ValidateNoCycles(
        Circuit circuit,
        IReadOnlyDictionary<string, BenchMetricInvocation> invocationsById,
        IReadOnlyDictionary<string, HashSet<string>> depsById,
        List<Diagnostic> diagnostics
    )
    {
        var benchDeps = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, deps) in depsById)
        {
            if (!invocationsById.TryGetValue(id, out var inv))
            {
                continue;
            }

            var fromBench = inv.BenchInstanceName;
            if (!benchDeps.TryGetValue(fromBench, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                benchDeps[fromBench] = set;
            }

            foreach (var depId in deps)
            {
                if (!invocationsById.TryGetValue(depId, out var depInv))
                {
                    continue;
                }

                if (!fromBench.Equals(depInv.BenchInstanceName, StringComparison.OrdinalIgnoreCase))
                {
                    set.Add(depInv.BenchInstanceName);
                }
            }
        }

        var visitedBenches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visitingBenches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var benchPath = new List<string>();

        foreach (var bench in benchDeps.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (visitedBenches.Contains(bench))
            {
                continue;
            }

            if (
                TryFindCycle(
                    bench,
                    benchDeps,
                    visitedBenches,
                    visitingBenches,
                    benchPath,
                    out var cycle
                )
            )
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS3018: Circular cross-bench dependency detected in circuit '{circuit.Name}': {string.Join(" -> ", cycle)}",
                        DiagnosticSeverity.Error,
                        "<constraints>",
                        1,
                        1
                    )
                );
                return;
            }
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var path = new List<string>();

        foreach (var node in depsById.Keys.OrderBy(x => x, StringComparer.Ordinal))
        {
            if (visited.Contains(node))
            {
                continue;
            }

            if (TryFindCycle(node, depsById, visited, visiting, path, out var cycle))
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS3019: Circular cross-bench measurement invocation dependency detected in circuit '{circuit.Name}': {string.Join(" -> ", cycle)}",
                        DiagnosticSeverity.Error,
                        "<constraints>",
                        1,
                        1
                    )
                );
                return;
            }
        }
    }

    private static bool TryFindCycle(
        string current,
        IReadOnlyDictionary<string, HashSet<string>> deps,
        HashSet<string> visited,
        HashSet<string> visiting,
        List<string> path,
        out IReadOnlyList<string> cycle
    )
    {
        cycle = Array.Empty<string>();
        if (visited.Contains(current))
        {
            return false;
        }

        if (!visiting.Add(current))
        {
            var idx = path.FindIndex(p => p.Equals(current, StringComparison.Ordinal));
            cycle =
                idx >= 0 ? path.Skip(idx).Concat(new[] { current }).ToArray() : new[] { current };
            return true;
        }

        path.Add(current);
        if (deps.TryGetValue(current, out var nexts))
        {
            foreach (var next in nexts)
            {
                if (TryFindCycle(next, deps, visited, visiting, path, out cycle))
                {
                    return true;
                }
            }
        }

        path.RemoveAt(path.Count - 1);
        visiting.Remove(current);
        visited.Add(current);
        return false;
    }
}
