using System;
using System.Collections.Generic;
using Cascode.Parser;

namespace Cascode.ACIR;

public sealed partial class AttachResolver
{
    private static void AddExplicitNetNode(
        ResolutionContext context,
        string netName,
        string domain,
        NetTier tier
    )
    {
        AddNode(context, netName, domain, isNet: true, isExplicit: true, tier);
    }

    private static void AddAutoNetNode(ResolutionContext context, string netName, string domain)
    {
        AddNode(context, netName, domain, isNet: true, isExplicit: false, tier: null);
    }

    private static void AddEndpointNode(ResolutionContext context, string endpointId, string domain)
    {
        AddNode(context, endpointId, domain, isNet: false, isExplicit: false, tier: null);
    }

    private static void AddNode(
        ResolutionContext context,
        string node,
        string domain,
        bool isNet,
        bool isExplicit,
        NetTier? tier
    )
    {
        if (context.UnionFind.Contains(node))
        {
            return;
        }

        context.UnionFind.MakeSet(node);
        context.NodeDomains[node] = domain;
        context.DomainByRoot[node] = domain;
        context.NetCountByRoot[node] = isNet ? 1 : 0;

        if (isNet)
        {
            context.NetNodes.Add(node);
        }
        else
        {
            context.EndpointNodes.Add(node);
        }

        if (isExplicit)
        {
            context.ExplicitNetNodes.Add(node);
            context.NetTiers[node] = tier ?? NetTier.Declared;
            context.ExplicitNetNamesByRoot[node] = new SortedSet<string>(StringComparer.Ordinal)
            {
                node,
            };
        }
    }

    private static void EnsureNetNode(ResolutionContext context, string netName, string domain)
    {
        if (context.UnionFind.Contains(netName))
        {
            return;
        }

        AddExplicitNetNode(context, netName, domain, NetTier.Declared);
    }

    private static bool TryUnion(
        ResolutionContext context,
        string nodeA,
        string nodeB,
        List<Diagnostic> diagnostics,
        string circuitName
    )
    {
        var rootA = context.UnionFind.Find(nodeA);
        var rootB = context.UnionFind.Find(nodeB);
        if (rootA == rootB)
        {
            return true;
        }

        var domainA = context.DomainByRoot[rootA];
        var domainB = context.DomainByRoot[rootB];
        if (!string.Equals(domainA, domainB, StringComparison.Ordinal))
        {
            diagnostics.Add(
                new Diagnostic(
                    $"ACIR0024: Incompatible domain merge '{nodeA}' ({domainA}) -> '{nodeB}' ({domainB})",
                    DiagnosticSeverity.Error,
                    circuitName,
                    1,
                    1
                )
            );
            return false;
        }

        context.UnionFind.Union(rootA, rootB);
        var newRoot = context.UnionFind.Find(rootA);
        var oldRoot = newRoot == rootA ? rootB : rootA;
        MergeComponentInfo(context, newRoot, oldRoot);
        return true;
    }

    private static void MergeComponentInfo(
        ResolutionContext context,
        string newRoot,
        string oldRoot
    )
    {
        if (context.NetCountByRoot.TryGetValue(oldRoot, out var oldCount))
        {
            context.NetCountByRoot[newRoot] = context.NetCountByRoot[newRoot] + oldCount;
            context.NetCountByRoot.Remove(oldRoot);
        }

        if (context.ExplicitNetNamesByRoot.TryGetValue(oldRoot, out var oldNames))
        {
            if (!context.ExplicitNetNamesByRoot.TryGetValue(newRoot, out var newNames))
            {
                newNames = new SortedSet<string>(StringComparer.Ordinal);
                context.ExplicitNetNamesByRoot[newRoot] = newNames;
            }

            foreach (var name in oldNames)
            {
                newNames.Add(name);
            }

            context.ExplicitNetNamesByRoot.Remove(oldRoot);
        }

        context.DomainByRoot.Remove(oldRoot);
    }
}
