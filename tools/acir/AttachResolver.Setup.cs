using System;
using System.Collections.Generic;
using System.Linq;
using Cascode.Parser;

namespace Cascode.ACIR;

public sealed partial class AttachResolver
{
    private void InitializeNetAtoms(Circuit circuit, ResolutionContext context)
    {
        foreach (var net in circuit.Fill!.Nets)
        {
            AddExplicitNetNode(context, net.Id, net.Domain, NetTier.Declared);
        }

        foreach (var supply in circuit.Supplies)
        {
            AddExplicitNetNode(context, supply, PowerDomain, NetTier.Supply);
        }

        foreach (var ground in circuit.Grounds)
        {
            AddExplicitNetNode(context, ground, GroundDomain, NetTier.Ground);
        }

        foreach (var port in circuit.Ports)
        {
            foreach (var (terminalPath, domain) in ExpandPortTerminalPaths(port))
            {
                var netName = ToNetName(terminalPath);
                AddExplicitNetNode(context, netName, domain, NetTier.PortExpansion);
            }
        }
    }

    private void InitializeInstanceEndpoints(Circuit circuit, ResolutionContext context)
    {
        foreach (var instance in circuit.Fill!.Instances)
        {
            if (!_circuitsByName.TryGetValue(instance.Type, out var targetCircuit))
            {
                continue;
            }

            foreach (var (terminalPath, domain) in EnumerateCircuitTerminals(targetCircuit))
            {
                var endpointId = $"{instance.Id}.{terminalPath}";
                AddEndpointNode(context, endpointId, domain);
            }
        }
    }

    private IEnumerable<(string TerminalPath, string Domain)> EnumerateCircuitTerminals(
        Circuit circuit
    )
    {
        foreach (var supply in circuit.Supplies)
        {
            yield return (supply, PowerDomain);
        }

        foreach (var ground in circuit.Grounds)
        {
            yield return (ground, GroundDomain);
        }

        foreach (var port in circuit.Ports)
        {
            foreach (var entry in ExpandPortTerminalPaths(port))
            {
                yield return entry;
            }
        }
    }

    private void ApplyDeviceBindings(Circuit circuit, ResolutionContext context)
    {
        foreach (var device in circuit.Fill!.Devices)
        {
            foreach (var netName in device.Bindings.Values)
            {
                EnsureNetNode(context, netName, DefaultDomain);
            }
        }
    }

    private void ApplyInstanceBindings(
        Circuit circuit,
        ResolutionContext context,
        List<Diagnostic> diagnostics
    )
    {
        foreach (var instance in circuit.Fill!.Instances)
        {
            foreach (var binding in instance.Bindings)
            {
                var endpointId = $"{instance.Id}.{binding.Key}";
                EnsureEndpointNode(instance, binding.Key, endpointId, context);
                EnsureNetNode(context, binding.Value, DefaultDomain);
                TryUnion(context, endpointId, binding.Value, diagnostics, circuit.Name);
            }
        }
    }

    private void ApplyConnectStatements(
        Circuit circuit,
        ResolutionContext context,
        List<Diagnostic> diagnostics
    )
    {
        if (circuit.Fill!.Connections.Count == 0)
        {
            return;
        }

        var instancesById = circuit.Fill.Instances.ToDictionary(
            inst => inst.Id,
            StringComparer.Ordinal
        );

        foreach (var conn in circuit.Fill.Connections)
        {
            // Expand bundle connections (e.g., "dp.IN -> IN" expands to "dp.IN.P -> IN_P", "dp.IN.N -> IN_N")
            var expandedConnections = ExpandBundleConnection(
                circuit,
                instancesById,
                conn.From,
                conn.To
            );

            foreach (var (from, to) in expandedConnections)
            {
                var fromNode = ResolveEndpointNode(circuit, instancesById, from, context);
                var toNode = ResolveEndpointNode(circuit, instancesById, to, context);
                TryUnion(context, fromNode, toNode, diagnostics, circuit.Name);
            }
        }
    }

    /// <summary>
    /// Expands a bundle connection to individual field connections.
    /// For example, "dp.IN -> IN" where IN is a Diff bundle expands to:
    /// - "dp.IN.P -> IN_P"
    /// - "dp.IN.N -> IN_N"
    /// </summary>
    private IEnumerable<(string From, string To)> ExpandBundleConnection(
        Circuit parentCircuit,
        Dictionary<string, InstanceDeclaration> instancesById,
        string from,
        string to
    )
    {
        // Get bundle type for each endpoint
        var fromBundle = GetEndpointBundleInfo(parentCircuit, instancesById, from);
        var toBundle = GetEndpointBundleInfo(parentCircuit, instancesById, to);

        // If neither is a bundle, return the original connection
        if (fromBundle.Fields is null && toBundle.Fields is null)
        {
            yield return (from, to);
            yield break;
        }

        // If only one side is a bundle, we still expand (the other side may have dot-named ports)
        var fields = fromBundle.Fields ?? toBundle.Fields;
        if (fields is null)
        {
            yield return (from, to);
            yield break;
        }

        // Expand each field
        foreach (var field in fields)
        {
            var expandedFrom = fromBundle.IsInstanceEndpoint
                ? $"{from}.{field}"
                : ToNetName($"{from}.{field}");

            var expandedTo = toBundle.IsInstanceEndpoint
                ? $"{to}.{field}"
                : ToNetName($"{to}.{field}");

            yield return (expandedFrom, expandedTo);
        }
    }

    /// <summary>
    /// Gets bundle information for an endpoint.
    /// </summary>
    private (bool IsInstanceEndpoint, List<string>? Fields) GetEndpointBundleInfo(
        Circuit parentCircuit,
        Dictionary<string, InstanceDeclaration> instancesById,
        string endpoint
    )
    {
        var dotIndex = endpoint.IndexOf('.', StringComparison.Ordinal);

        // Check if this is an instance endpoint (e.g., "dp.IN")
        if (dotIndex > 0)
        {
            var instanceId = endpoint[..dotIndex];
            if (instancesById.TryGetValue(instanceId, out var instance))
            {
                var terminalPath = endpoint[(dotIndex + 1)..];
                var fields = GetInstancePortBundleFields(instance, terminalPath);
                return (true, fields);
            }
        }

        // Circuit-level endpoint - check if it's a bundle port
        var port = parentCircuit.Ports.FirstOrDefault(p =>
            p.Name.Equals(endpoint, StringComparison.Ordinal)
        );

        if (port is not null && _bundleTypesByName.TryGetValue(port.Type, out var bundle))
        {
            var fields = bundle.Fields.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            return (false, fields);
        }

        return (false, null);
    }

    /// <summary>
    /// Gets the bundle fields for an instance terminal by looking at the target circuit's ports.
    /// Handles both explicit bundle types and implicit bundles via dot-naming (e.g., ports IN.P, IN.N).
    /// </summary>
    private List<string>? GetInstancePortBundleFields(
        InstanceDeclaration instance,
        string terminalPath
    )
    {
        if (!_circuitsByName.TryGetValue(instance.Type, out var targetCircuit))
        {
            return null;
        }

        // First check if the terminal is a bundle-typed port
        var port = targetCircuit.Ports.FirstOrDefault(p =>
            p.Name.Equals(terminalPath, StringComparison.Ordinal)
        );

        if (port is not null && _bundleTypesByName.TryGetValue(port.Type, out var bundle))
        {
            return bundle.Fields.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        }

        // Check for implicit bundle via dot-named ports (e.g., IN.P, IN.N for terminal IN)
        var prefix = $"{terminalPath}.";
        var matchingPorts = targetCircuit
            .Ports.Where(p => p.Name.StartsWith(prefix, StringComparison.Ordinal))
            .Select(p => p.Name[prefix.Length..])
            .Where(suffix => !suffix.Contains('.')) // Only direct children
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        return matchingPorts.Count > 0 ? matchingPorts : null;
    }

    private string ResolveEndpointNode(
        Circuit parentCircuit,
        Dictionary<string, InstanceDeclaration> instancesById,
        string endpoint,
        ResolutionContext context
    )
    {
        var dotIndex = endpoint.IndexOf('.', StringComparison.Ordinal);
        if (dotIndex > 0)
        {
            var instanceId = endpoint[..dotIndex];
            if (instancesById.TryGetValue(instanceId, out var instance))
            {
                var terminalPath = endpoint[(dotIndex + 1)..];
                var endpointId = endpoint;
                EnsureEndpointNode(instance, terminalPath, endpointId, context);
                return endpointId;
            }
        }

        EnsureNetNode(context, endpoint, DefaultDomain);
        return endpoint;
    }
}
