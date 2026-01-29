using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language;

public sealed partial class AttachResolver
{
    private void ApplyAttachStatements(
        Circuit circuit,
        ResolutionContext context,
        List<Diagnostic> diagnostics
    )
    {
        if (circuit.Fill!.Attaches.Count == 0)
        {
            return;
        }

        var instancesById = circuit.Fill.Instances.ToDictionary(
            inst => inst.Id,
            StringComparer.Ordinal
        );

        for (var attachIndex = 0; attachIndex < circuit.Fill.Attaches.Count; attachIndex++)
        {
            var attach = circuit.Fill.Attaches[attachIndex];
            var attachInfo = ResolveConnector(attach, circuit, diagnostics);
            if (attachInfo is null)
            {
                continue;
            }

            context.ConnectorByAttach[attach] = attachInfo;
            ProcessAttach(
                circuit,
                attach,
                attachInfo,
                attachIndex,
                instancesById,
                context,
                diagnostics
            );
        }
    }

    private AttachResolutionInfo? ResolveConnector(
        AttachStatement attach,
        Circuit circuit,
        List<Diagnostic> diagnostics
    )
    {
        var viaParts = attach.Via.Split("::");
        if (viaParts.Length != 2)
        {
            diagnostics.Add(
                new Diagnostic(
                    $"CAS0022: Malformed via clause '{attach.Via}'",
                    DiagnosticSeverity.Error,
                    circuit.Name,
                    1,
                    1
                )
            );
            return null;
        }

        var sourceTraitName = viaParts[0];
        var targetTraitName = viaParts[1];

        if (!_traitsByName.TryGetValue(sourceTraitName, out var sourceTrait))
        {
            diagnostics.Add(
                new Diagnostic(
                    $"CAS0021: Undefined trait '{sourceTraitName}' in attach via clause",
                    DiagnosticSeverity.Error,
                    circuit.Name,
                    1,
                    1
                )
            );
            return null;
        }

        var connector = sourceTrait.Connectors.FirstOrDefault(c =>
            c.TargetTrait.Equals(targetTraitName, StringComparison.Ordinal)
        );
        if (connector is null)
        {
            diagnostics.Add(
                new Diagnostic(
                    $"CAS0023: No connector from '{sourceTraitName}' to '{targetTraitName}'",
                    DiagnosticSeverity.Error,
                    circuit.Name,
                    1,
                    1
                )
            );
            return null;
        }

        _traitsByName.TryGetValue(targetTraitName, out var targetTrait);

        return new AttachResolutionInfo(connector, sourceTrait, targetTrait);
    }

    private void ProcessAttach(
        Circuit circuit,
        AttachStatement attach,
        AttachResolutionInfo attachInfo,
        int attachIndex,
        Dictionary<string, InstanceDeclaration> instancesById,
        ResolutionContext context,
        List<Diagnostic> diagnostics
    )
    {
        var connector = attachInfo.Connector;
        var sourceTrait = attachInfo.SourceTrait;
        var targetTrait = attachInfo.TargetTrait;

        var createdAutoNets = new List<string>();
        var instanceChain = BuildInstanceChain(attach);

        // Validate override source ports exist in connector
        if (attach.Overrides is { Count: > 0 })
        {
            var connectorSourcePorts = connector
                .Mappings.Select(m => m.SourcePort)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var ov in attach.Overrides)
            {
                if (!connectorSourcePorts.Contains(ov.SourcePort))
                {
                    diagnostics.Add(
                        new Diagnostic(
                            $"Override source port '{ov.SourcePort}' not found in connector {attach.Via}",
                            DiagnosticSeverity.Warning,
                            circuit.Name,
                            1,
                            1
                        )
                    );
                }
            }
        }

        // Validate domain compatibility based on trait port definitions
        foreach (var (sourcePort, targetPort) in EnumerateConnectorMappings(attach, connector))
        {
            var sourcePortDomain =
                sourceTrait?.Ports.FirstOrDefault(p => p.Name == sourcePort)?.Type ?? DefaultDomain;
            var targetPortDomain =
                targetTrait?.Ports.FirstOrDefault(p => p.Name == targetPort)?.Type ?? DefaultDomain;

            if (!string.Equals(sourcePortDomain, targetPortDomain, StringComparison.Ordinal))
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS0024: Domain mismatch in attach: {sourcePort} ({sourcePortDomain}) vs {targetPort} ({targetPortDomain})",
                        DiagnosticSeverity.Error,
                        circuit.Name,
                        1,
                        1
                    )
                );
            }
        }

        for (var pairIndex = 0; pairIndex < instanceChain.Count - 1; pairIndex++)
        {
            var fromInstance = instanceChain[pairIndex];
            var toInstance = instanceChain[pairIndex + 1];

            foreach (var (sourcePort, targetPort) in EnumerateConnectorMappings(attach, connector))
            {
                ProcessAttachMapping(
                    circuit,
                    attachIndex,
                    createdAutoNets,
                    instancesById,
                    context,
                    diagnostics,
                    fromInstance,
                    toInstance,
                    sourcePort,
                    targetPort
                );
            }
        }

        ApplyAnchorNames(attach, createdAutoNets, context);
    }

    private void ProcessAttachMapping(
        Circuit circuit,
        int attachIndex,
        List<string> createdAutoNets,
        Dictionary<string, InstanceDeclaration> instancesById,
        ResolutionContext context,
        List<Diagnostic> diagnostics,
        string fromInstance,
        string toInstance,
        string sourcePort,
        string targetPort
    )
    {
        var fromEndpoint = EnsureAttachEndpoint(instancesById, fromInstance, sourcePort, context);
        var toEndpoint = EnsureAttachEndpoint(instancesById, toInstance, targetPort, context);

        if (HasExplicitNetConflict(context, fromEndpoint, toEndpoint, out var conflict))
        {
            diagnostics.Add(
                new Diagnostic(
                    $"CAS0020: Attach would merge distinct named nets '{conflict.FromNet}' and '{conflict.ToNet}'; use explicit 'connect' to unify",
                    DiagnosticSeverity.Error,
                    circuit.Name,
                    1,
                    1
                )
            );
            return;
        }

        if (ShouldCreateAutoNet(context, fromEndpoint, toEndpoint))
        {
            CreateAutoNetOrReportDiagnostic(
                circuit,
                attachIndex,
                createdAutoNets,
                context,
                diagnostics,
                fromEndpoint,
                toEndpoint
            );
            return;
        }

        TryUnion(context, fromEndpoint, toEndpoint, diagnostics, circuit.Name);
    }

    private static void CreateAutoNetOrReportDiagnostic(
        Circuit circuit,
        int attachIndex,
        List<string> createdAutoNets,
        ResolutionContext context,
        List<Diagnostic> diagnostics,
        string fromEndpoint,
        string toEndpoint
    )
    {
        var domain = context.DomainByRoot[context.UnionFind.Find(fromEndpoint)];
        if (!TryEnsureDomainMatch(domain, toEndpoint, context, diagnostics, circuit.Name))
        {
            return;
        }

        if (domain is PowerDomain or GroundDomain)
        {
            diagnostics.Add(
                new Diagnostic(
                    "CAS0025: Cannot auto-create supply/ground net; bind rails explicitly",
                    DiagnosticSeverity.Error,
                    circuit.Name,
                    1,
                    1
                )
            );
            return;
        }

        var autoNetId = $"__auto_attach{attachIndex}_net{createdAutoNets.Count}";
        AddAutoNetNode(context, autoNetId, domain);
        createdAutoNets.Add(autoNetId);
        TryUnion(context, fromEndpoint, autoNetId, diagnostics, circuit.Name);
        TryUnion(context, toEndpoint, autoNetId, diagnostics, circuit.Name);
    }

    private static bool TryEnsureDomainMatch(
        string expectedDomain,
        string node,
        ResolutionContext context,
        List<Diagnostic> diagnostics,
        string circuitName
    )
    {
        var nodeDomain = context.DomainByRoot[context.UnionFind.Find(node)];
        if (string.Equals(expectedDomain, nodeDomain, StringComparison.Ordinal))
        {
            return true;
        }

        diagnostics.Add(
            new Diagnostic(
                $"CAS0024: Incompatible domain merge '{expectedDomain}' -> '{nodeDomain}'",
                DiagnosticSeverity.Error,
                circuitName,
                1,
                1
            )
        );
        return false;
    }

    private static void ApplyAnchorNames(
        AttachStatement attach,
        List<string> autoNets,
        ResolutionContext context
    )
    {
        if (attach.Anchor is null || autoNets.Count == 0)
        {
            return;
        }

        if (autoNets.Count == 1)
        {
            context.AutoNetNameOverrides[autoNets[0]] = attach.Anchor;
            return;
        }

        for (var index = 0; index < autoNets.Count; index++)
        {
            context.AutoNetNameOverrides[autoNets[index]] = $"{attach.Anchor}_{index}";
        }
    }

    private bool HasExplicitNetConflict(
        ResolutionContext context,
        string fromEndpoint,
        string toEndpoint,
        out (string FromNet, string ToNet) conflict
    )
    {
        conflict = default;
        var fromRoot = context.UnionFind.Find(fromEndpoint);
        var toRoot = context.UnionFind.Find(toEndpoint);
        if (fromRoot == toRoot)
        {
            return false;
        }

        if (!HasExplicitNet(context, fromRoot) || !HasExplicitNet(context, toRoot))
        {
            return false;
        }

        var fromNet = context.ExplicitNetNamesByRoot[fromRoot].Min!;
        var toNet = context.ExplicitNetNamesByRoot[toRoot].Min!;
        conflict = (fromNet, toNet);
        return true;
    }

    private static bool ShouldCreateAutoNet(
        ResolutionContext context,
        string fromEndpoint,
        string toEndpoint
    )
    {
        var fromRoot = context.UnionFind.Find(fromEndpoint);
        var toRoot = context.UnionFind.Find(toEndpoint);
        return !HasNet(context, fromRoot) && !HasNet(context, toRoot);
    }

    private static bool HasNet(ResolutionContext context, string root)
    {
        return context.NetCountByRoot.TryGetValue(root, out var count) && count > 0;
    }

    private static bool HasExplicitNet(ResolutionContext context, string root)
    {
        return context.ExplicitNetNamesByRoot.TryGetValue(root, out var names) && names.Count > 0;
    }

    private string EnsureAttachEndpoint(
        Dictionary<string, InstanceDeclaration> instancesById,
        string instanceId,
        string terminalPath,
        ResolutionContext context
    )
    {
        var endpointId = $"{instanceId}.{terminalPath}";
        if (context.UnionFind.Contains(endpointId))
        {
            return endpointId;
        }

        if (instancesById.TryGetValue(instanceId, out var instance))
        {
            EnsureEndpointNode(instance, terminalPath, endpointId, context);
            return endpointId;
        }

        AddEndpointNode(context, endpointId, DefaultDomain);
        return endpointId;
    }

    private static IEnumerable<(string SourcePort, string TargetPort)> EnumerateConnectorMappings(
        AttachStatement attach,
        TraitConnector connector
    )
    {
        Dictionary<string, string>? overrides = null;
        if (attach.Overrides is { Count: > 0 })
        {
            overrides = attach.Overrides.ToDictionary(
                mapping => mapping.SourcePort,
                mapping => mapping.TargetPort,
                StringComparer.Ordinal
            );
        }

        foreach (var mapping in connector.Mappings)
        {
            var targetPort = mapping.TargetPort;
            if (
                overrides is not null
                && overrides.TryGetValue(mapping.SourcePort, out var overridePort)
            )
            {
                targetPort = overridePort;
            }

            yield return (mapping.SourcePort, targetPort);
        }
    }
}
