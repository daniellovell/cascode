using System;
using System.Collections.Generic;
using System.Linq;
using Cascode.Language.BenchRuntime.Netlist;

namespace Cascode.Language.BenchRuntime;

internal sealed record BenchTerminalCompilation(
    BenchNetlist Netlist,
    IReadOnlyDictionary<string, BenchTerminalRef> Terminals,
    IReadOnlyList<string> DutOrderedNets,
    IReadOnlyList<string> AcNodeKeys,
    IReadOnlyList<string> DutAcNodeKeys
);

internal static class BenchTerminalCompiler
{
    public static BenchTerminalCompilation Compile(
        BenchDefinition bench,
        Circuit circuit,
        IReadOnlyDictionary<string, BundleType> bundlesByName,
        BenchUnionFind uf,
        IReadOnlyList<InstanceDeclaration> instances
    )
    {
        ArgumentNullException.ThrowIfNull(bench);
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(bundlesByName);
        ArgumentNullException.ThrowIfNull(uf);
        ArgumentNullException.ThrowIfNull(instances);

        // Force existence of DUT terminals and bench terminal leaves so they get stable naming.
        foreach (var p in circuit.Ports)
        {
            uf.Ensure(BenchNode.DutTerminal(p.Name));
        }
        foreach (var s in circuit.Supplies)
        {
            uf.Ensure(BenchNode.DutTerminal(s));
        }
        foreach (var g in circuit.Grounds)
        {
            uf.Ensure(BenchNode.DutTerminal(g));
        }
        foreach (var t in bench.Terminals)
        {
            foreach (
                var leaf in BenchConnectivityBuilder.ExpandLeaves(t.Name, t.Type, bundlesByName)
            )
            {
                uf.Ensure(BenchNode.BenchTerminalLeaf(leaf));
            }
        }

        var netlist = BenchNetlistCompiler.Compile(uf, instances);

        var terminals = BuildBenchTerminals(bench, bundlesByName, netlist);
        var dutOrderedNets = BuildDutOrderedNets(circuit, netlist);

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

        return new BenchTerminalCompilation(
            netlist,
            terminals,
            dutOrderedNets,
            acNodeKeys,
            dutAcNodeKeys
        );
    }

    private static IReadOnlyDictionary<string, BenchTerminalRef> BuildBenchTerminals(
        BenchDefinition bench,
        IReadOnlyDictionary<string, BundleType> bundlesByName,
        BenchNetlist netlist
    )
    {
        var terminals = new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in bench.Terminals)
        {
            var leaves = BenchConnectivityBuilder
                .ExpandLeaves(t.Name, t.Type, bundlesByName)
                .ToList();
            var nodes = leaves
                .Select(l => netlist.GetSpiceNet(BenchNode.BenchTerminalLeaf(l)))
                .ToList();
            terminals[t.Name] = new BenchTerminalRef(t.Name, nodes);
        }

        return terminals;
    }

    private static IReadOnlyList<string> BuildDutOrderedNets(Circuit circuit, BenchNetlist netlist)
    {
        var nets = new List<string>();
        foreach (var p in circuit.Ports)
        {
            nets.Add(netlist.GetSpiceNet(BenchNode.DutTerminal(p.Name)));
        }
        foreach (var s in circuit.Supplies)
        {
            nets.Add(netlist.GetSpiceNet(BenchNode.DutTerminal(s)));
        }
        foreach (var g in circuit.Grounds)
        {
            nets.Add(netlist.GetSpiceNet(BenchNode.DutTerminal(g)));
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
}
