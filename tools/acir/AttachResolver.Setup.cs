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

        // Ports are already desugared to scalar types by BundleDesugarer
        foreach (var port in circuit.Ports)
        {
            AddExplicitNetNode(context, port.Name, port.Type, NetTier.PortExpansion);
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

        // Ports are already desugared to scalar types by BundleDesugarer
        foreach (var port in circuit.Ports)
        {
            yield return (port.Name, port.Type);
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
        var instancesById = circuit.Fill!.Instances.ToDictionary(
            inst => inst.Id,
            StringComparer.Ordinal
        );

        // Process fill-level connections (already expanded by BundleDesugarer)
        foreach (var conn in circuit.Fill.Connections)
        {
            var fromNode = ResolveEndpointNode(circuit, instancesById, conn.From, context);
            var toNode = ResolveEndpointNode(circuit, instancesById, conn.To, context);
            TryUnion(context, fromNode, toNode, diagnostics, circuit.Name);
        }

        // Process instance-level connects
        foreach (var instance in circuit.Fill.Instances)
        {
            foreach (var conn in instance.Connects)
            {
                var fromNode = ResolveEndpointNode(circuit, instancesById, conn.From, context);
                var toNode = ResolveEndpointNode(circuit, instancesById, conn.To, context);
                TryUnion(context, fromNode, toNode, diagnostics, circuit.Name);
            }
        }
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
