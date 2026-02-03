using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language.BenchRuntime.Netlist;

internal sealed class BenchNetNamer
{
    private readonly BenchUnionFind _uf;
    private readonly Dictionary<BenchNode, string> _canonicalByRep;

    public BenchNetNamer(BenchUnionFind uf)
    {
        _uf = uf;
        _canonicalByRep = BuildCanonicalMap();
    }

    public string Canonical(BenchNode node)
    {
        var rep = _uf.Find(node);
        return _canonicalByRep[rep];
    }

    public string ToSpiceNet(BenchNode node)
    {
        return ToSpiceNet(Canonical(node));
    }

    public static string ToSpiceNet(string canonical)
    {
        return canonical.Replace('.', '_');
    }

    public IEnumerable<(string Pin, string SpiceNet)> EnumerateInstancePins(string instanceId)
    {
        foreach (var group in _uf.Groups.Values)
        {
            foreach (var node in group)
            {
                if (node.Kind != BenchNodeKind.InstancePin)
                    continue;
                if (!node.A.Equals(instanceId, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.IsNullOrWhiteSpace(node.B))
                    continue;
                yield return (node.B!, ToSpiceNet(Canonical(node)));
            }
        }
    }

    private Dictionary<BenchNode, string> BuildCanonicalMap()
    {
        var map = new Dictionary<BenchNode, string>();
        foreach (var (rep, members) in _uf.Groups)
        {
            if (members.Contains(BenchNode.Spice0))
            {
                map[rep] = "0";
                continue;
            }

            var namedCandidates = members
                .Where(m =>
                    m.Kind == BenchNodeKind.BenchNet || m.Kind == BenchNodeKind.BenchTerminalLeaf
                )
                .Select(m => m.A)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();

            string canonical;
            if (namedCandidates.Count > 0)
            {
                canonical = namedCandidates[0];
            }
            else
            {
                var dut = members.FirstOrDefault(m => m.Kind == BenchNodeKind.DutTerminal);
                canonical = !dut.Equals(default(BenchNode)) ? dut.A : members[0].DebugName;
            }

            map[rep] = canonical;
        }

        return map;
    }
}
