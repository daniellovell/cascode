using System;
using System.Collections.Generic;
using Cascode.Parser;

namespace Cascode.Language;

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
        if (!AreDomainsCompatible(domainA, domainB))
        {
            diagnostics.Add(
                new Diagnostic(
                    $"CAS0024: Incompatible domain merge '{nodeA}' ({domainA}) -> '{nodeB}' ({domainB})",
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

    /// <summary>
    /// Checks if two domains are compatible for connection.
    /// Signal domains (analog, bias) can connect to supply rails (power, ground).
    /// However, different signal types (analog vs bias) remain incompatible with each other,
    /// and power-ground connections are always errors (short circuits).
    /// </summary>
    private static bool AreDomainsCompatible(string domainA, string domainB)
    {
        if (string.Equals(domainA, domainB, StringComparison.Ordinal))
        {
            return true;
        }

        // Normalize to lowercase for comparison
        var a = domainA.ToLowerInvariant();
        var b = domainB.ToLowerInvariant();

        // Direct power-ground connection is always an error (short circuit)
        if ((a == "power" && b == "ground") || (a == "ground" && b == "power"))
        {
            return false;
        }

        // Allow signal domains (analog, bias) to connect to supply rails (power, ground)
        // This is common in analog circuit design (e.g., connecting a transistor source to VDD)
        var signalDomains = new HashSet<string> { "analog", "bias" };
        var railDomains = new HashSet<string> { "power", "ground" };

        // Signal can connect to rail (either direction)
        return (signalDomains.Contains(a) && railDomains.Contains(b))
            || (signalDomains.Contains(b) && railDomains.Contains(a));
    }
}
