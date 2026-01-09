using System;
using System.Collections.Generic;

namespace Cascode.ACIR;

/// <summary>
/// Union-find data structure for net connectivity.
/// </summary>
internal sealed class UnionFind<T>
    where T : notnull
{
    private readonly Dictionary<T, T> _parent = new();
    private readonly Dictionary<T, int> _rank = new();

    public void MakeSet(T item)
    {
        if (_parent.ContainsKey(item))
        {
            return;
        }

        _parent[item] = item;
        _rank[item] = 0;
    }

    public bool Contains(T item) => _parent.ContainsKey(item);

    public T Find(T item)
    {
        if (!_parent.TryGetValue(item, out var parent))
        {
            throw new ArgumentException($"Item '{item}' not in union-find");
        }

        if (!EqualityComparer<T>.Default.Equals(parent, item))
        {
            _parent[item] = Find(parent); // Path compression
        }

        return _parent[item];
    }

    public void Union(T a, T b)
    {
        var rootA = Find(a);
        var rootB = Find(b);

        if (EqualityComparer<T>.Default.Equals(rootA, rootB))
        {
            return;
        }

        // Union by rank
        if (_rank[rootA] < _rank[rootB])
        {
            _parent[rootA] = rootB;
        }
        else if (_rank[rootA] > _rank[rootB])
        {
            _parent[rootB] = rootA;
        }
        else
        {
            _parent[rootB] = rootA;
            _rank[rootA]++;
        }
    }

    public IEnumerable<T> GetAllElements() => _parent.Keys;
}
