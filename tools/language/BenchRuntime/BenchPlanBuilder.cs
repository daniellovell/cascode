using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Cascode.Language;

namespace Cascode.Language.BenchRuntime;

public static class BenchPlanBuilder
{
    public static BenchPlan Build(CascodeDocument document, Circuit circuit, BenchBinding binding)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(binding);

        var bench = document.BenchDefinitions.FirstOrDefault(b =>
            b.Name.Equals(binding.BenchName, StringComparison.OrdinalIgnoreCase)
        );
        if (bench is null)
        {
            throw new InvalidOperationException(
                $"Unknown bench '{binding.BenchName}' for binding '{binding.BindingName}'."
            );
        }

        var bundlesByName = BundleExpander.GetBundlesByName(document);

        var functions = BuildFunctions(document, bench);
        var env = BuildEnv(circuit);
        var harness = new Dictionary<string, BenchValue>(env, StringComparer.Ordinal);
        var constraints = BuildConstraints(circuit, binding.BindingName);

        var evalTerminals = new Dictionary<string, BenchTerminalRef>(
            StringComparer.OrdinalIgnoreCase
        );

        // Used for evaluating analysis params and harness instance arguments.
        var evalRunner = new BenchMeasurementRunner(
            bench,
            functions,
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            ),
            terminals: evalTerminals,
            env,
            harness,
            constraints
        );

        var (unionFind, instanceIds, harnessInstances, dutMappings, dutConnections) =
            BuildConnectivity(circuit, bench, binding, bundlesByName);

        // Force existence of DUT terminals in the union-find so they get stable naming.
        foreach (var p in circuit.Ports)
        {
            unionFind.Ensure("dut." + p.Name);
        }
        foreach (var s in circuit.Supplies)
        {
            unionFind.Ensure("dut." + s);
        }
        foreach (var g in circuit.Grounds)
        {
            unionFind.Ensure("dut." + g);
        }

        // Apply mappings/connections.
        foreach (var (a, b) in dutMappings)
        {
            unionFind.Union(a, b);
        }
        foreach (var (a, b) in dutConnections)
        {
            unionFind.Union(a, b);
        }

        var netNamer = new NetNamer(unionFind, instanceIds);

        var terminals = BuildBenchTerminals(bench, bundlesByName, netNamer);
        foreach (var kvp in terminals)
        {
            evalTerminals[kvp.Key] = kvp.Value;
        }
        var dutSubcktName = SpiceEmitter.GetDefaultVariantName(circuit);
        var dutOrderedNets = BuildDutOrderedNets(circuit, netNamer);

        var dutAccessPinRefs = CollectDutAccessPinRefs(bench);
        var dutAcNodeKeys = dutAccessPinRefs
            .Select(p => "XDUT:" + p.Replace('.', '_'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var acNodeKeys = terminals
            .Values.SelectMany(t => t.LeafNodes)
            .Concat(dutAcNodeKeys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var analyses = BuildAnalyses(bench, evalRunner, harnessInstances);

        var harnessElements = EmitHarnessElements(harnessInstances, netNamer, evalRunner);

        return new BenchPlan(
            circuit.Name,
            binding.BindingName,
            bench.Name,
            bench,
            binding,
            functions,
            analyses,
            terminals,
            env,
            harness,
            constraints,
            harnessElements,
            dutOrderedNets,
            dutSubcktName,
            acNodeKeys,
            dutAcNodeKeys
        );
    }

    private static IReadOnlyDictionary<string, FunctionDefinition> BuildFunctions(
        CascodeDocument document,
        BenchDefinition bench
    )
    {
        // Bench-local functions override file-level functions by name.
        var map = new Dictionary<string, FunctionDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var fn in document.Functions)
        {
            map[fn.Name] = fn;
        }
        foreach (var fn in bench.Functions)
        {
            map[fn.Name] = fn;
        }
        return map;
    }

    private static Dictionary<string, BenchValue> BuildEnv(Circuit circuit)
    {
        var env = new Dictionary<string, BenchValue>(StringComparer.Ordinal);
        if (circuit.Env is null)
        {
            return env;
        }

        foreach (var (key, raw) in circuit.Env.Entries)
        {
            if (raw.Contains("||", StringComparison.Ordinal))
            {
                env[key] = new BenchSymbol(raw.Trim());
                continue;
            }

            try
            {
                env[key] = BenchQuantity.Parse(raw);
            }
            catch
            {
                env[key] = new BenchSymbol(raw.Trim());
            }
        }

        return env;
    }

    private static Dictionary<string, BenchValue> BuildConstraints(
        Circuit circuit,
        string bindingName
    )
    {
        var constraints = new Dictionary<string, BenchValue>(StringComparer.Ordinal);
        if (circuit.Constraints?.Numeric is null)
        {
            return constraints;
        }

        foreach (var c in circuit.Constraints.Numeric)
        {
            if (!c.Bench.Equals(bindingName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var raw = c.Value + c.Unit;
            try
            {
                constraints[c.Metric] = BenchQuantity.Parse(raw);
            }
            catch
            {
                constraints[c.Metric] = new BenchSymbol(raw);
            }
        }

        return constraints;
    }

    private static IReadOnlyList<BenchPlanAnalysis> BuildAnalyses(
        BenchDefinition bench,
        BenchMeasurementRunner evalRunner,
        IReadOnlyList<InstanceDeclaration> harnessInstances
    )
    {
        var analyses = new List<BenchPlanAnalysis>();
        var noiseInputSource = FindNoiseInputSource(harnessInstances);

        foreach (var a in bench.Analyses)
        {
            var space = "dec";
            if (a.Parameters.TryGetValue("space", out var spaceExpr))
            {
                var v = evalRunner.EvaluateExpressionForPlan(spaceExpr);
                if (v is BenchSymbol sym)
                {
                    space = sym.Name.Equals("lin", StringComparison.OrdinalIgnoreCase)
                        ? "lin"
                        : "dec";
                    if (sym.Name.Equals("log", StringComparison.OrdinalIgnoreCase))
                    {
                        space = "dec";
                    }
                }
            }

            var samples = 100;
            if (a.Parameters.TryGetValue("samples", out var samplesExpr))
            {
                var v = evalRunner.EvaluateExpressionForPlan(samplesExpr);
                if (v is BenchNumber n && n.Kind == BenchNumericKind.Scalar)
                {
                    samples = Math.Max(1, (int)Math.Round(n.Value));
                }
            }

            if (a.Type == BenchValueType.ACAnalysis)
            {
                if (!a.Parameters.TryGetValue("start", out var startExpr))
                {
                    throw new InvalidOperationException(
                        $"ACAnalysis '{a.Name}' missing required parameter 'start'."
                    );
                }
                if (!a.Parameters.TryGetValue("stop", out var stopExpr))
                {
                    throw new InvalidOperationException(
                        $"ACAnalysis '{a.Name}' missing required parameter 'stop'."
                    );
                }

                var startV =
                    evalRunner.EvaluateExpressionForPlan(startExpr) as BenchNumber
                    ?? throw new InvalidOperationException(
                        $"ACAnalysis '{a.Name}' start did not evaluate to a number."
                    );
                var stopV =
                    evalRunner.EvaluateExpressionForPlan(stopExpr) as BenchNumber
                    ?? throw new InvalidOperationException(
                        $"ACAnalysis '{a.Name}' stop did not evaluate to a number."
                    );

                if (
                    startV.Kind != BenchNumericKind.FrequencyHz
                    || stopV.Kind != BenchNumericKind.FrequencyHz
                )
                {
                    throw new InvalidOperationException(
                        $"ACAnalysis '{a.Name}' start/stop must be Frequency values."
                    );
                }

                analyses.Add(
                    new BenchPlanAnalysis(a.Type, a.Name, space, samples, startV.Value, stopV.Value)
                );
                continue;
            }

            if (a.Type == BenchValueType.NoiseAnalysis)
            {
                if (!a.Parameters.TryGetValue("start", out var startExpr))
                {
                    throw new InvalidOperationException(
                        $"NoiseAnalysis '{a.Name}' missing required parameter 'start'."
                    );
                }
                if (!a.Parameters.TryGetValue("stop", out var stopExpr))
                {
                    throw new InvalidOperationException(
                        $"NoiseAnalysis '{a.Name}' missing required parameter 'stop'."
                    );
                }
                if (!a.Parameters.TryGetValue("output", out var outputExpr))
                {
                    throw new InvalidOperationException(
                        $"NoiseAnalysis '{a.Name}' missing required parameter 'output'."
                    );
                }

                if (noiseInputSource is null)
                {
                    throw new InvalidOperationException(
                        $"NoiseAnalysis '{a.Name}' requires at least one VAC source in the bench/binding fill to use as the noise input source."
                    );
                }

                var startV =
                    evalRunner.EvaluateExpressionForPlan(startExpr) as BenchNumber
                    ?? throw new InvalidOperationException(
                        $"NoiseAnalysis '{a.Name}' start did not evaluate to a number."
                    );
                var stopV =
                    evalRunner.EvaluateExpressionForPlan(stopExpr) as BenchNumber
                    ?? throw new InvalidOperationException(
                        $"NoiseAnalysis '{a.Name}' stop did not evaluate to a number."
                    );
                var output =
                    evalRunner.EvaluateExpressionForPlan(outputExpr) as BenchTerminalRef
                    ?? throw new InvalidOperationException(
                        $"NoiseAnalysis '{a.Name}' output did not evaluate to a terminal."
                    );

                if (
                    startV.Kind != BenchNumericKind.FrequencyHz
                    || stopV.Kind != BenchNumericKind.FrequencyHz
                )
                {
                    throw new InvalidOperationException(
                        $"NoiseAnalysis '{a.Name}' start/stop must be Frequency values."
                    );
                }

                analyses.Add(
                    new BenchPlanAnalysis(
                        a.Type,
                        a.Name,
                        space,
                        samples,
                        startV.Value,
                        stopV.Value,
                        OutputTerminal: output,
                        NoiseInputSource: noiseInputSource
                    )
                );
            }
        }

        return analyses;
    }

    private static string? FindNoiseInputSource(IReadOnlyList<InstanceDeclaration> harnessInstances)
    {
        var vac = harnessInstances
            .Where(i => i.Type.Equals("VAC", StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return vac is null ? null : "V" + vac.Id;
    }

    private static IReadOnlyDictionary<string, BenchTerminalRef> BuildBenchTerminals(
        BenchDefinition bench,
        IReadOnlyDictionary<string, BundleType> bundlesByName,
        NetNamer netNamer
    )
    {
        var terminals = new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in bench.Terminals)
        {
            var leaves = ExpandLeaves(t.Name, t.Type, bundlesByName).ToList();
            var nodes = leaves.Select(l => netNamer.ToSpiceNet(netNamer.Canonical(l))).ToList();
            terminals[t.Name] = new BenchTerminalRef(t.Name, nodes);
        }

        return terminals;
    }

    private static IReadOnlyList<string> BuildDutOrderedNets(Circuit circuit, NetNamer netNamer)
    {
        var nets = new List<string>();
        foreach (var p in circuit.Ports)
        {
            nets.Add(netNamer.ToSpiceNet(netNamer.Canonical("dut." + p.Name)));
        }
        foreach (var s in circuit.Supplies)
        {
            nets.Add(netNamer.ToSpiceNet(netNamer.Canonical("dut." + s)));
        }
        foreach (var g in circuit.Grounds)
        {
            nets.Add(netNamer.ToSpiceNet(netNamer.Canonical("dut." + g)));
        }

        return nets;
    }

    private static HashSet<string> CollectDutAccessPinRefs(BenchDefinition bench)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var a in bench.Analyses)
        {
            foreach (var expr in a.Parameters.Values)
            {
                Collect(expr, set);
            }
        }

        foreach (var m in bench.Measurements)
        {
            foreach (var stmt in m.Body)
            {
                Collect(stmt, set);
            }
        }

        foreach (var fn in bench.Functions)
        {
            foreach (var stmt in fn.Body)
            {
                Collect(stmt, set);
            }
        }

        return set;
    }

    private static void Collect(BenchStatement stmt, HashSet<string> set)
    {
        switch (stmt)
        {
            case BenchVarDecl v:
                Collect(v.Expr, set);
                break;
            case BenchReturn r:
                Collect(r.Expr, set);
                break;
            case BenchIf i:
                Collect(i.Condition, set);
                foreach (var s in i.ThenBody)
                    Collect(s, set);
                if (i.ElseBody is not null)
                    foreach (var s in i.ElseBody)
                        Collect(s, set);
                break;
        }
    }

    private static void Collect(BoolExpr expr, HashSet<string> set)
    {
        switch (expr)
        {
            case BoolCompare c:
                Collect(c.Left, set);
                Collect(c.Right, set);
                break;
        }
    }

    private static void Collect(MeasurementExpr expr, HashSet<string> set)
    {
        switch (expr)
        {
            case MeasurementDutAccess d:
                set.Add(d.PinRef);
                break;
            case MeasurementBinary b:
                Collect(b.Left, set);
                Collect(b.Right, set);
                break;
            case MeasurementUnary u:
                Collect(u.Operand, set);
                break;
            case MeasurementCall c:
                foreach (var a in c.Args)
                    Collect(a.Value, set);
                break;
            case MeasurementConditional c:
                Collect(c.Condition, set);
                Collect(c.ThenExpr, set);
                Collect(c.ElseExpr, set);
                break;
        }
    }

    private static IEnumerable<string> ExpandLeaves(
        string basePath,
        string typeName,
        IReadOnlyDictionary<string, BundleType> bundlesByName
    )
    {
        if (!bundlesByName.TryGetValue(typeName, out var bundle))
        {
            yield return basePath;
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

    private static (
        UnionFind Uf,
        HashSet<string> InstanceIds,
        IReadOnlyList<InstanceDeclaration> Instances,
        IReadOnlyList<(string A, string B)> DutMappings,
        IReadOnlyList<(string A, string B)> DutConnections
    ) BuildConnectivity(
        Circuit circuit,
        BenchDefinition bench,
        BenchBinding binding,
        IReadOnlyDictionary<string, BundleType> bundlesByName
    )
    {
        var uf = new UnionFind();

        var instances = new List<InstanceDeclaration>();
        if (bench.Fill?.Instances is not null)
        {
            instances.AddRange(bench.Fill.Instances);
        }
        instances.AddRange(
            binding.Statements.OfType<BenchBindingInstance>().Select(i => i.Instance)
        );

        var instanceIds = instances.Select(i => i.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Instance pin bindings: ".P--net" in binding blocks.
        foreach (var inst in instances)
        {
            foreach (var (pin, target) in inst.Bindings)
            {
                uf.Union(inst.Id + "." + pin, target);
            }
        }

        // Bench fill connect statements.
        if (bench.Fill?.Connections is not null)
        {
            foreach (var c in bench.Fill.Connections)
            {
                uf.Union(c.From, c.To);
            }
        }

        // Bench terminal mappings and explicit dut connections.
        var dutMappings = new List<(string, string)>();
        var dutConnections = new List<(string, string)>();

        foreach (var stmt in binding.Statements)
        {
            if (stmt is BenchTerminalMapping map)
            {
                var term = bench.Terminals.First(t =>
                    t.Name.Equals(map.BenchTerminal, StringComparison.OrdinalIgnoreCase)
                );

                foreach (var leaf in ExpandLeaves(term.Name, term.Type, bundlesByName))
                {
                    var suffix =
                        leaf.Length > term.Name.Length
                        && leaf.StartsWith(term.Name, StringComparison.OrdinalIgnoreCase)
                            ? leaf[term.Name.Length..]
                            : string.Empty;
                    var dutLeaf = map.DutPinRef + suffix;
                    dutMappings.Add((leaf, "dut." + dutLeaf));
                }
            }
            else if (stmt is BenchDutConnection conn)
            {
                dutConnections.Add(("dut." + conn.DutPinRef, conn.PinRef));
            }
        }

        return (uf, instanceIds, instances, dutMappings, dutConnections);
    }

    private static IReadOnlyList<BenchHarnessElement> EmitHarnessElements(
        IReadOnlyList<InstanceDeclaration> instances,
        NetNamer netNamer,
        BenchMeasurementRunner evalRunner
    )
    {
        var elements = new List<BenchHarnessElement>();

        foreach (var inst in instances)
        {
            // Only a small set of harness primitives are supported initially.
            var type = inst.Type;
            if (
                !type.Equals("GND", StringComparison.OrdinalIgnoreCase)
                && !type.Equals("VDC", StringComparison.OrdinalIgnoreCase)
                && !type.Equals("VAC", StringComparison.OrdinalIgnoreCase)
                && !type.Equals("Impedance", StringComparison.OrdinalIgnoreCase)
            )
            {
                continue;
            }

            var pins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pin in inst.Bindings.Keys)
            {
                pins[pin] = netNamer.ToSpiceNet(netNamer.Canonical(inst.Id + "." + pin));
            }

            // Include pins referenced by connect statements (instance-id qualified).
            foreach (var member in netNamer.UfMembersForInstance(inst.Id))
            {
                if (!member.StartsWith(inst.Id + ".", StringComparison.OrdinalIgnoreCase))
                    continue;
                var pin = member[(inst.Id.Length + 1)..];
                if (!pins.ContainsKey(pin))
                {
                    pins[pin] = netNamer.ToSpiceNet(netNamer.Canonical(member));
                }
            }

            var parameters = new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, value) in inst.Params)
            {
                parameters[name] = EvaluateInstanceParam(value, evalRunner);
            }

            elements.Add(new BenchHarnessElement(type, inst.Id, pins, parameters));
        }

        return elements;
    }

    private static BenchValue EvaluateInstanceParam(ParamValue v, BenchMeasurementRunner evalRunner)
    {
        if (!string.IsNullOrWhiteSpace(v.Numeric))
        {
            // Try quantity first; fallback to scalar number.
            try
            {
                return BenchQuantity.Parse(v.Numeric);
            }
            catch
            {
                if (
                    double.TryParse(
                        v.Numeric,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var d
                    )
                )
                {
                    return new BenchNumber(BenchNumericKind.Scalar, d);
                }
                return new BenchSymbol(v.Numeric);
            }
        }

        if (!string.IsNullOrWhiteSpace(v.Symbolic))
        {
            if (CascodeAstBuilder.TryParseMeasurementExprText(v.Symbolic, out var expr, out _))
            {
                return evalRunner.EvaluateExpressionForPlan(expr!);
            }

            return new BenchSymbol(v.Symbolic);
        }

        if (!string.IsNullOrWhiteSpace(v.Literal))
        {
            return new BenchSymbol(v.Literal);
        }

        return new BenchSymbol(string.Empty);
    }

    private sealed class NetNamer
    {
        private readonly UnionFind _uf;
        private readonly HashSet<string> _instanceIds;
        private readonly Dictionary<string, string> _canonicalByRep;

        public NetNamer(UnionFind uf, HashSet<string> instanceIds)
        {
            _uf = uf;
            _instanceIds = instanceIds;
            _canonicalByRep = BuildCanonicalMap();
        }

        public string Canonical(string node)
        {
            var rep = _uf.Find(node);
            return _canonicalByRep[rep];
        }

        public string ToSpiceNet(string canonical)
        {
            // Strip any internal prefixes and sanitize dots for SPICE identifiers.
            if (canonical.StartsWith("dut.", StringComparison.OrdinalIgnoreCase))
            {
                canonical = canonical["dut.".Length..];
            }
            return canonical.Replace('.', '_');
        }

        public IEnumerable<string> UfMembersForInstance(string instanceId)
        {
            var prefix = instanceId + ".";
            foreach (var group in _uf.Groups.Values)
            {
                foreach (var node in group)
                {
                    if (node.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        yield return node;
                    }
                }
            }
        }

        private Dictionary<string, string> BuildCanonicalMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (rep, members) in _uf.Groups)
            {
                var candidates = members
                    .Where(m => !IsDutNode(m) && !IsInstanceTerminal(m))
                    .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                string canonical;
                if (candidates.Count > 0)
                {
                    canonical = candidates[0];
                }
                else
                {
                    var dut = members.FirstOrDefault(IsDutNode);
                    canonical = dut is not null ? dut["dut.".Length..] : members[0];
                }

                map[rep] = canonical;
            }

            return map;
        }

        private bool IsDutNode(string node) =>
            node.StartsWith("dut.", StringComparison.OrdinalIgnoreCase);

        private bool IsInstanceTerminal(string node)
        {
            var dot = node.IndexOf('.', StringComparison.Ordinal);
            if (dot <= 0)
            {
                return false;
            }

            var root = node[..dot];
            return _instanceIds.Contains(root);
        }
    }

    private sealed class UnionFind
    {
        private readonly Dictionary<string, string> _parent = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, List<string>> Groups
        {
            get
            {
                var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var node in _parent.Keys)
                {
                    var rep = Find(node);
                    if (!groups.TryGetValue(rep, out var list))
                    {
                        list = new List<string>();
                        groups[rep] = list;
                    }
                    list.Add(node);
                }

                return groups;
            }
        }

        public void Ensure(string node)
        {
            if (!_parent.ContainsKey(node))
            {
                _parent[node] = node;
            }
        }

        public string Find(string node)
        {
            Ensure(node);
            var p = _parent[node];
            if (p.Equals(node, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            var root = Find(p);
            _parent[node] = root;
            return root;
        }

        public void Union(string a, string b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra.Equals(rb, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _parent[ra] = rb;
        }
    }
}
