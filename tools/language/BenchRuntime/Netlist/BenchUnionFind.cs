using System;
using System.Collections.Generic;

namespace Cascode.Language.BenchRuntime.Netlist;

internal sealed class BenchUnionFind
{
    private readonly Dictionary<BenchNode, BenchNode> _parent = new();

    public IEnumerable<BenchNode> Nodes => _parent.Keys;

    public IReadOnlyDictionary<BenchNode, List<BenchNode>> Groups
    {
        get
        {
            var groups = new Dictionary<BenchNode, List<BenchNode>>();
            foreach (var node in _parent.Keys)
            {
                var rep = Find(node);
                if (!groups.TryGetValue(rep, out var list))
                {
                    list = new List<BenchNode>();
                    groups[rep] = list;
                }
                list.Add(node);
            }
            return groups;
        }
    }

    public void Ensure(BenchNode node)
    {
        if (!_parent.ContainsKey(node))
        {
            _parent[node] = node;
        }
    }

    public BenchNode Find(BenchNode node)
    {
        Ensure(node);
        var p = _parent[node];
        if (p.Equals(node))
        {
            return node;
        }

        var root = Find(p);
        _parent[node] = root;
        return root;
    }

    public void Union(BenchNode a, BenchNode b)
    {
        var ra = Find(a);
        var rb = Find(b);
        if (ra.Equals(rb))
        {
            return;
        }

        _parent[ra] = rb;
    }
}
