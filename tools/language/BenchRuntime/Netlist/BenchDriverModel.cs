using System;
using System.Collections.Generic;
using System.Linq;
using Cascode.Language;

namespace Cascode.Language.BenchRuntime.Netlist;

internal sealed class BenchDriverModel
{
    private static readonly HashSet<string> IndependentVoltageSources = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "VDC",
        "VAC",
        "VSIN",
    };

    private static readonly HashSet<string> GroundTies = new(StringComparer.OrdinalIgnoreCase)
    {
        "GND",
    };

    private static readonly HashSet<string> LoadLike = new(StringComparer.OrdinalIgnoreCase)
    {
        "Impedor",
        "Impedance",
    };

    private readonly BenchUnionFind _uf;
    private readonly Dictionary<BenchNode, BenchNetDriveState> _stateByRep;

    public BenchDriverModel(BenchUnionFind uf, IReadOnlyList<InstanceDeclaration> instances)
    {
        _uf = uf;
        _stateByRep = BuildStateByRep(instances);
    }

    public bool ShouldInjectGroundTie(BenchNode dutNet)
    {
        var s = GetState(dutNet);
        return !(s.IsSpice0 || s.HasGroundTieElement || s.HasSourceReferencingSpice0);
    }

    public bool ShouldInjectSupplyOrBias(BenchNode dutNet)
    {
        var s = GetState(dutNet);
        return !(s.IsSpice0 || s.HasGroundTieElement || s.HasIndependentVoltageSource);
    }

    public bool ShouldInjectLoad(BenchNode dutNet)
    {
        return !GetState(dutNet).HasLoadElement;
    }

    private BenchNetDriveState GetState(BenchNode node)
    {
        var rep = _uf.Find(node);
        if (_stateByRep.TryGetValue(rep, out var s))
        {
            return s;
        }

        return new BenchNetDriveState();
    }

    private Dictionary<BenchNode, BenchNetDriveState> BuildStateByRep(
        IReadOnlyList<InstanceDeclaration> instances
    )
    {
        var groups = _uf.Groups;
        var stateByRep = new Dictionary<BenchNode, BenchNetDriveState>();

        foreach (var (rep, members) in groups)
        {
            stateByRep[rep] = new BenchNetDriveState(IsSpice0: members.Contains(BenchNode.Spice0));
        }

        foreach (var inst in instances)
        {
            var type = inst.Type.Equals("Impedor", StringComparison.OrdinalIgnoreCase)
                ? "Impedance"
                : inst.Type;

            var pinNames = EnumeratePinsForInstance(inst.Id, groups).ToList();
            var touched = new HashSet<BenchNode>();
            foreach (var pin in pinNames)
            {
                touched.Add(_uf.Find(BenchNode.InstancePin(inst.Id, pin)));
            }

            if (IndependentVoltageSources.Contains(type))
            {
                foreach (var rep in touched)
                {
                    stateByRep[rep] = stateByRep[rep] with { HasIndependentVoltageSource = true };
                }

                // Tighten the semantics for ground tie detection: a V source only counts as a
                // Spice0 reference when it actually connects to net 0.
                if (!pinNames.Contains("P") || !pinNames.Contains("N"))
                {
                    continue;
                }

                var pRep = _uf.Find(BenchNode.InstancePin(inst.Id, "P"));
                var nRep = _uf.Find(BenchNode.InstancePin(inst.Id, "N"));
                var pIs0 = stateByRep.TryGetValue(pRep, out var ps) && ps.IsSpice0;
                var nIs0 = stateByRep.TryGetValue(nRep, out var ns) && ns.IsSpice0;
                if (pIs0 && !nIs0)
                {
                    stateByRep[nRep] = stateByRep[nRep] with { HasSourceReferencingSpice0 = true };
                }
                else if (nIs0 && !pIs0)
                {
                    stateByRep[pRep] = stateByRep[pRep] with { HasSourceReferencingSpice0 = true };
                }
            }

            if (GroundTies.Contains(type))
            {
                foreach (var rep in touched)
                {
                    stateByRep[rep] = stateByRep[rep] with { HasGroundTieElement = true };
                }
            }

            if (LoadLike.Contains(type))
            {
                foreach (var rep in touched)
                {
                    stateByRep[rep] = stateByRep[rep] with { HasLoadElement = true };
                }
            }
        }

        return stateByRep;
    }

    private static IEnumerable<string> EnumeratePinsForInstance(
        string instanceId,
        IReadOnlyDictionary<BenchNode, List<BenchNode>> groups
    )
    {
        var pins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var members in groups.Values)
        {
            foreach (var node in members)
            {
                if (node.Kind != BenchNodeKind.InstancePin)
                    continue;
                if (!node.A.Equals(instanceId, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.IsNullOrWhiteSpace(node.B))
                    continue;
                pins.Add(node.B!);
            }
        }

        return pins.OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
    }

    private sealed record BenchNetDriveState(
        bool IsSpice0 = false,
        bool HasIndependentVoltageSource = false,
        bool HasGroundTieElement = false,
        bool HasLoadElement = false,
        bool HasSourceReferencingSpice0 = false
    );
}
