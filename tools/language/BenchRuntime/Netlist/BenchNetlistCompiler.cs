using System;
using System.Collections.Generic;
using System.Linq;
using Cascode.Language;

namespace Cascode.Language.BenchRuntime.Netlist;

internal static class BenchNetlistCompiler
{
    private static readonly HashSet<string> IndependentVoltageSources = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "VDC",
        "VAC",
        "VSIN",
    };

    private static readonly HashSet<string> LoadLike = new(StringComparer.OrdinalIgnoreCase)
    {
        "Impedor",
        "Impedance",
    };

    private static readonly HashSet<string> GroundTies = new(StringComparer.OrdinalIgnoreCase)
    {
        "GND",
    };

    public static BenchNetlist Compile(
        BenchUnionFind uf,
        IReadOnlyList<InstanceDeclaration> instances
    )
    {
        ArgumentNullException.ThrowIfNull(uf);
        ArgumentNullException.ThrowIfNull(instances);

        var namer = new BenchNetNamer(uf);

        var repList = uf
            .Groups.Keys.Select(r => (Rep: r, Spice: namer.ToSpiceNet(r), IsSpice0: false))
            .ToList();

        // Ensure the SPICE ground net is always named "0", even if the group contains other names.
        for (var i = 0; i < repList.Count; i++)
        {
            var rep = repList[i].Rep;
            if (uf.Groups.TryGetValue(rep, out var members) && members.Contains(BenchNode.Spice0))
            {
                repList[i] = (rep, "0", true);
            }
        }

        repList = repList
            .OrderBy(r => r.Spice, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Rep.DebugName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var repToId = new Dictionary<BenchNode, BenchNetId>();
        var netIdByNode = new Dictionary<BenchNode, BenchNetId>();
        var nets = new List<BenchNet>();
        for (var i = 0; i < repList.Count; i++)
        {
            var (rep, spice, isSpice0) = repList[i];
            var id = new BenchNetId(i);
            repToId[rep] = id;
            nets.Add(new BenchNet(id, spice, isSpice0));

            if (uf.Groups.TryGetValue(rep, out var members))
            {
                foreach (var node in members)
                {
                    netIdByNode[node] = id;
                }
            }
        }

        var components = new List<BenchComponent>();
        foreach (var inst in instances.OrderBy(i => i.Id, StringComparer.OrdinalIgnoreCase))
        {
            var pins = new Dictionary<string, BenchNetId>(StringComparer.OrdinalIgnoreCase);
            foreach (var pin in EnumeratePins(uf, inst))
            {
                var node = BenchNode.InstancePin(inst.Id, pin);
                var rep = uf.Find(node);
                if (!repToId.TryGetValue(rep, out var netId))
                {
                    continue;
                }

                pins[pin] = netId;
            }

            components.Add(new BenchComponent(inst.Id, inst.Type, pins));
        }

        var hasVsrc = new bool[nets.Count];
        var hasLoad = new bool[nets.Count];
        var hasGndTie = new bool[nets.Count];
        foreach (var c in components)
        {
            foreach (var netId in c.Pins.Values.Distinct())
            {
                if (IndependentVoltageSources.Contains(c.Type))
                {
                    hasVsrc[netId.Value] = true;
                }

                if (LoadLike.Contains(c.Type))
                {
                    hasLoad[netId.Value] = true;
                }

                if (GroundTies.Contains(c.Type))
                {
                    hasGndTie[netId.Value] = true;
                }
            }
        }

        var attrs = new Dictionary<BenchNetId, BenchNetAttributes>();
        foreach (var n in nets)
        {
            attrs[n.Id] = new BenchNetAttributes(
                n.IsSpice0,
                hasVsrc[n.Id.Value],
                hasLoad[n.Id.Value],
                hasGndTie[n.Id.Value]
            );
        }

        return new BenchNetlist(nets, components, netIdByNode, attrs);
    }

    private static IEnumerable<string> EnumeratePins(BenchUnionFind uf, InstanceDeclaration inst)
    {
        var pins = new HashSet<string>(inst.Bindings.Keys, StringComparer.OrdinalIgnoreCase);

        // Include pins referenced by connect statements (instance-id qualified).
        foreach (var node in uf.Nodes)
        {
            if (node.Kind != BenchNodeKind.InstancePin)
                continue;
            if (!node.A.Equals(inst.Id, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(node.B))
                continue;
            pins.Add(node.B!);
        }

        return pins.OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
    }
}
