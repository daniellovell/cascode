using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.ACIR;

public sealed partial class AttachResolver
{
    private void FinalizeResolution(ResolutionContext context, CircuitResolutionResult result)
    {
        var netNodesByRoot = GroupNodesByRoot(context.NetNodes, context);
        var endpointNodesByRoot = GroupNodesByRoot(context.EndpointNodes, context);
        var representatives = DetermineRepresentatives(
            context,
            netNodesByRoot,
            endpointNodesByRoot
        );

        PopulateNetResults(context, result, netNodesByRoot, representatives);
        PopulateTerminalToNet(context, result, representatives);
    }

    private static Dictionary<string, List<string>> GroupNodesByRoot(
        IEnumerable<string> nodes,
        ResolutionContext context
    )
    {
        var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            var root = context.UnionFind.Find(node);
            if (!groups.TryGetValue(root, out var list))
            {
                list = new List<string>();
                groups[root] = list;
            }

            list.Add(node);
        }

        return groups;
    }

    private Dictionary<string, string> DetermineRepresentatives(
        ResolutionContext context,
        Dictionary<string, List<string>> netNodesByRoot,
        Dictionary<string, List<string>> endpointNodesByRoot
    )
    {
        var representatives = new Dictionary<string, string>(StringComparer.Ordinal);
        var roots = new HashSet<string>(netNodesByRoot.Keys, StringComparer.Ordinal);
        roots.UnionWith(endpointNodesByRoot.Keys);

        foreach (var root in roots)
        {
            var explicitNets = netNodesByRoot.TryGetValue(root, out var netNodes)
                ? netNodes.Where(context.ExplicitNetNodes.Contains).ToList()
                : new List<string>();

            string representative;
            if (explicitNets.Count > 0)
            {
                representative = SelectRepresentative(explicitNets, context.NetTiers);
            }
            else if (TryGetAnchorName(root, netNodesByRoot, context, out var anchorName))
            {
                representative = anchorName;
            }
            else
            {
                var endpoints = endpointNodesByRoot.GetValueOrDefault(root, new List<string>());
                representative = ComputeAutoNetName(endpoints);
            }

            representatives[root] = representative;
        }

        return representatives;
    }

    private static bool TryGetAnchorName(
        string root,
        Dictionary<string, List<string>> netNodesByRoot,
        ResolutionContext context,
        out string anchorName
    )
    {
        anchorName = string.Empty;
        if (!netNodesByRoot.TryGetValue(root, out var netNodes))
        {
            return false;
        }

        var anchorNames = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var node in netNodes)
        {
            if (context.AutoNetNameOverrides.TryGetValue(node, out var name))
            {
                anchorNames.Add(name);
            }
        }

        if (anchorNames.Count == 0)
        {
            return false;
        }

        anchorName = anchorNames.Min!;
        return true;
    }

    private static string SelectRepresentative(List<string> nets, Dictionary<string, NetTier> tiers)
    {
        if (nets is null || nets.Count == 0)
        {
            throw new ArgumentException("nets must be non-empty", nameof(nets));
        }

        var grouped = nets.GroupBy(net => tiers[net]).OrderBy(group => group.Key);
        var bestGroup = grouped.First();
        return bestGroup.Min(StringComparer.Ordinal)!;
    }

    private static string ComputeAutoNetName(IReadOnlyList<string> endpoints)
    {
        if (endpoints.Count == 0)
        {
            return "_auto";
        }

        var ordered = endpoints.OrderBy(endpoint => endpoint, StringComparer.Ordinal).ToList();
        var first = ordered[0];
        var second = ordered.Count > 1 ? ordered[1] : ordered[0];
        return $"_auto_{SanitizeEndpoint(first)}__{SanitizeEndpoint(second)}";
    }

    private static string SanitizeEndpoint(string endpoint)
    {
        return endpoint.Replace('.', '_');
    }

    private static string ToNetName(string terminalPath)
    {
        return terminalPath.Replace('.', '_');
    }

    private static void PopulateNetResults(
        ResolutionContext context,
        CircuitResolutionResult result,
        Dictionary<string, List<string>> netNodesByRoot,
        Dictionary<string, string> representatives
    )
    {
        foreach (var net in context.ExplicitNetNodes)
        {
            var root = context.UnionFind.Find(net);
            result._netToRepresentative[net] = representatives[root];
        }

        foreach (var (root, nets) in netNodesByRoot)
        {
            var representative = representatives[root];
            var names = BuildEquivalenceNames(nets, representative, context);
            if (names.Count > 0)
            {
                result._netEquivalences[representative] = names;
            }
        }
    }

    private static List<string> BuildEquivalenceNames(
        List<string> netNodes,
        string representative,
        ResolutionContext context
    )
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in netNodes)
        {
            if (context.ExplicitNetNodes.Contains(node))
            {
                names.Add(node);
                continue;
            }

            if (context.AutoNetNameOverrides.TryGetValue(node, out var anchorName))
            {
                names.Add(anchorName);
                continue;
            }

            names.Add(representative);
        }

        return names.OrderBy(name => name, StringComparer.Ordinal).ToList();
    }

    private static void PopulateTerminalToNet(
        ResolutionContext context,
        CircuitResolutionResult result,
        Dictionary<string, string> representatives
    )
    {
        foreach (var endpoint in context.EndpointNodes)
        {
            var root = context.UnionFind.Find(endpoint);
            result._terminalToNet[endpoint] = representatives[root];
        }
    }

    private void PopulateAttachBindings(
        Circuit circuit,
        ResolutionContext context,
        CircuitResolutionResult result
    )
    {
        if (circuit.Fill?.Attaches is null || circuit.Fill.Attaches.Count == 0)
        {
            return;
        }

        foreach (var attach in circuit.Fill.Attaches)
        {
            if (!context.ConnectorByAttach.TryGetValue(attach, out var attachInfo))
            {
                continue;
            }

            var connector = attachInfo.Connector;
            var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
            var instanceChain = BuildInstanceChain(attach);

            for (var pairIndex = 0; pairIndex < instanceChain.Count - 1; pairIndex++)
            {
                var fromInstance = instanceChain[pairIndex];
                var toInstance = instanceChain[pairIndex + 1];

                foreach (
                    var (sourcePort, targetPort) in EnumerateConnectorMappings(attach, connector)
                )
                {
                    var fromEndpoint = $"{fromInstance}.{sourcePort}";
                    var toEndpoint = $"{toInstance}.{targetPort}";

                    if (result.TerminalToNet.TryGetValue(fromEndpoint, out var netName))
                    {
                        var key =
                            attach.TargetInstances.Count == 1
                                ? sourcePort
                                : $"{fromInstance}.{sourcePort}";
                        bindings[key] = netName;
                    }

                    if (result.TerminalToNet.TryGetValue(toEndpoint, out var targetNet))
                    {
                        bindings[$"{toInstance}.{targetPort}"] = targetNet;
                    }
                }
            }

            if (bindings.Count > 0)
            {
                result._attachBindings[attach] = bindings;
            }
        }
    }
}
