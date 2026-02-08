using System;
using System.Collections.Generic;
using System.Linq;
using Cascode.Language.BenchRuntime.Netlist;

namespace Cascode.Language.BenchRuntime;

internal sealed record BenchConnectivity(
    BenchUnionFind Uf,
    HashSet<string> InstanceIds,
    IReadOnlyList<InstanceDeclaration> Instances,
    IReadOnlySet<string> BenchTerminalLeaves
);

internal static class BenchConnectivityBuilder
{
    public static BenchConnectivity Build(
        BenchDefinition bench,
        BenchBinding binding,
        IReadOnlyDictionary<string, BundleType> bundlesByName
    )
    {
        var uf = new BenchUnionFind();

        var instances = new List<InstanceDeclaration>();
        if (bench.Fill?.Instances is not null)
        {
            instances.AddRange(bench.Fill.Instances);
        }
        instances.AddRange(
            binding.Statements.OfType<BenchBindingInstance>().Select(i => i.Instance)
        );

        var instanceIds = instances.Select(i => i.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var terminalLeaves = bench
            .Terminals.SelectMany(t => ExpandLeaves(t.Name, t.Type!, bundlesByName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Instance pin bindings: ".P--net" in binding blocks.
        foreach (var inst in instances)
        {
            foreach (var (pin, target) in inst.Bindings)
            {
                uf.Union(
                    BenchNode.InstancePin(inst.Id, pin),
                    BenchNodeRefParser.Parse(target, instanceIds, terminalLeaves)
                );
            }
        }

        // Bench fill connect statements.
        if (bench.Fill?.Connections is not null)
        {
            foreach (var c in bench.Fill.Connections)
            {
                uf.Union(
                    BenchNodeRefParser.Parse(c.From, instanceIds, terminalLeaves),
                    BenchNodeRefParser.Parse(c.To, instanceIds, terminalLeaves)
                );
            }
        }

        // Bench terminal mappings and explicit dut connections.
        foreach (var stmt in binding.Statements)
        {
            if (stmt is BenchTerminalMapping map)
            {
                var term = bench.Terminals.First(t =>
                    t.Name.Equals(map.BenchTerminal, StringComparison.OrdinalIgnoreCase)
                );

                foreach (var leaf in ExpandLeaves(term.Name, term.Type!, bundlesByName))
                {
                    var suffix =
                        leaf.Length > term.Name.Length
                        && leaf.StartsWith(term.Name, StringComparison.OrdinalIgnoreCase)
                            ? leaf[term.Name.Length..]
                            : string.Empty;
                    var dutLeaf = map.DutPinRef + suffix;
                    uf.Union(BenchNode.BenchTerminalLeaf(leaf), BenchNode.DutTerminal(dutLeaf));
                }
            }
            else if (stmt is BenchDutConnection conn)
            {
                uf.Union(
                    BenchNode.DutTerminal(conn.DutPinRef),
                    BenchNodeRefParser.Parse(conn.PinRef, instanceIds, terminalLeaves)
                );
            }
        }

        return new BenchConnectivity(uf, instanceIds, instances, terminalLeaves);
    }

    public static IEnumerable<string> ExpandLeaves(
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

    // NOTE: node parsing lives in BenchNodeRefParser to avoid duplication across compilers.
}
