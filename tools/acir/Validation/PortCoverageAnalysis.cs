using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.ACIR.Validation;

/// <summary>
/// Encapsulates port coverage validation logic for hierarchical instances.
/// </summary>
internal sealed class PortCoverageAnalysis
{
    private readonly Circuit _parentCircuit;
    private readonly List<TraitDefinition> _traits;
    private readonly ValidationResult _result;

    public PortCoverageAnalysis(
        Circuit parentCircuit,
        List<TraitDefinition> traits,
        ValidationResult result
    )
    {
        _parentCircuit = parentCircuit;
        _traits = traits;
        _result = result;
    }

    /// <summary>
    /// Validates all ports on an instance are covered by bindings or attach statements.
    /// </summary>
    public void ValidateInstancePortCoverage(InstanceDeclaration instance, Circuit targetCircuit)
    {
        // Build set of ports that need coverage
        var requiredPorts = new HashSet<string>(StringComparer.Ordinal);

        // Add supply ports
        foreach (var supply in targetCircuit.Supplies)
        {
            requiredPorts.Add(supply);
        }

        // Add ground ports - they also need explicit binding
        foreach (var ground in targetCircuit.Grounds)
        {
            requiredPorts.Add(ground);
        }

        // Add declared ports
        var declaredPortNames = new HashSet<string>(
            targetCircuit.Ports.Select(p => p.Name),
            StringComparer.Ordinal
        );
        foreach (var port in targetCircuit.Ports)
        {
            requiredPorts.Add(port.Name);
        }

        // Remove ports covered by direct bindings
        foreach (var binding in instance.Bindings.Keys)
        {
            requiredPorts.Remove(binding);

            // Handle bundle notation: IN.P covers port IN (but avoid treating '.' as bundle syntax
            // when the circuit explicitly declares a port with a '.' in its name).
            if (binding.Contains('.') && !declaredPortNames.Contains(binding))
            {
                var portName = binding.Split('.')[0];
                requiredPorts.Remove(portName);
            }
        }

        // Remove ports covered by connect statements (both fill-level and instance-level)
        if (_parentCircuit.Fill?.Connections is not null)
        {
            var connectedPorts = GetPortsCoveredByConnects(
                _parentCircuit.Fill.Connections,
                instance,
                declaredPortNames
            );
            foreach (var port in connectedPorts)
            {
                requiredPorts.Remove(port);
            }
        }

        // Also check instance-level connects
        if (instance.Connects.Count > 0)
        {
            var connectedPorts = GetPortsCoveredByConnects(
                instance.Connects,
                instance,
                declaredPortNames
            );
            foreach (var port in connectedPorts)
            {
                requiredPorts.Remove(port);
            }
        }

        // Remove ports covered by attach statements
        if (_parentCircuit.Fill?.Attaches is not null)
        {
            foreach (var attach in _parentCircuit.Fill.Attaches)
            {
                var coveredPorts = GetPortsCoveredByAttach(attach, instance, targetCircuit);
                foreach (var port in coveredPorts)
                {
                    requiredPorts.Remove(port);
                }
            }
        }

        // Report any remaining uncovered ports
        foreach (var port in requiredPorts)
        {
            _result.AddError(
                "HIER-003",
                $"Instance '{instance.Id}' port '{port}' is not bound",
                $"circuit {_parentCircuit.Name}, inst {instance.Id} : {instance.Type}",
                $"Add '{port} -> <net>' binding or cover via attach statement"
            );
        }
    }

    /// <summary>
    /// Determines which ports on an instance are covered by an attach statement.
    /// </summary>
    private HashSet<string> GetPortsCoveredByAttach(
        AttachStatement attach,
        InstanceDeclaration instance,
        Circuit targetCircuit
    )
    {
        var coveredPorts = new HashSet<string>(StringComparer.Ordinal);
        var declaredPortNames = new HashSet<string>(
            targetCircuit.Ports.Select(p => p.Name),
            StringComparer.Ordinal
        );

        // Parse the via clause: "TraitName::TargetTrait"
        var viaParts = attach.Via.Split("::");
        if (viaParts.Length != 2)
        {
            return coveredPorts; // Invalid via clause, validation handles this elsewhere
        }

        var sourceTraitName = viaParts[0];
        var targetTraitName = viaParts[1];

        // Find the trait with the connector
        var sourceTrait = _traits.FirstOrDefault(t =>
            t.Name.Equals(sourceTraitName, StringComparison.Ordinal)
        );
        if (sourceTrait is null)
        {
            return coveredPorts;
        }

        // Find the connector to the target trait
        var connector = sourceTrait.Connectors.FirstOrDefault(c =>
            c.TargetTrait.Equals(targetTraitName, StringComparison.Ordinal)
        );
        if (connector is null)
        {
            return coveredPorts;
        }

        var instanceChain = new List<string> { attach.SourceInstance };
        instanceChain.AddRange(attach.TargetInstances);

        for (var pairIndex = 0; pairIndex < instanceChain.Count - 1; pairIndex++)
        {
            var fromInstance = instanceChain[pairIndex];
            var toInstance = instanceChain[pairIndex + 1];

            foreach (var mapping in connector.Mappings)
            {
                var sourcePort = mapping.SourcePort;
                var targetPort = mapping.TargetPort;

                if (attach.Overrides is not null)
                {
                    var overrideMapping = attach.Overrides.FirstOrDefault(o =>
                        o.SourcePort == sourcePort
                    );
                    if (overrideMapping is not null)
                    {
                        targetPort = overrideMapping.TargetPort;
                    }
                }

                if (fromInstance == instance.Id)
                {
                    coveredPorts.Add(
                        declaredPortNames.Contains(sourcePort)
                            ? sourcePort
                            : sourcePort.Split('.')[0]
                    );
                }

                if (toInstance == instance.Id)
                {
                    coveredPorts.Add(
                        declaredPortNames.Contains(targetPort)
                            ? targetPort
                            : targetPort.Split('.')[0]
                    );
                }
            }
        }

        return coveredPorts;
    }

    /// <summary>
    /// Determines which ports on an instance are covered by connect statements.
    /// </summary>
    private HashSet<string> GetPortsCoveredByConnects(
        IEnumerable<ConnectionStatement> connections,
        InstanceDeclaration instance,
        HashSet<string> declaredPortNames
    )
    {
        var coveredPorts = new HashSet<string>(StringComparer.Ordinal);
        var instancePrefix = $"{instance.Id}.";

        foreach (var conn in connections)
        {
            CheckEndpoint(conn.From);
            CheckEndpoint(conn.To);
        }

        return coveredPorts;

        void CheckEndpoint(string endpoint)
        {
            if (!endpoint.StartsWith(instancePrefix, StringComparison.Ordinal))
            {
                return;
            }

            var portPath = endpoint[instancePrefix.Length..];

            // If the exact port path is declared, use it
            if (declaredPortNames.Contains(portPath))
            {
                coveredPorts.Add(portPath);
                return;
            }

            // Check for bundle expansion: dp.IN should cover IN.P, IN.N if those ports exist
            var bundlePrefix = $"{portPath}.";
            var matchingPorts = declaredPortNames
                .Where(p => p.StartsWith(bundlePrefix, StringComparison.Ordinal))
                .ToList();

            if (matchingPorts.Count > 0)
            {
                // Bundle connection covers all matching ports
                foreach (var matchedPort in matchingPorts)
                {
                    coveredPorts.Add(matchedPort);
                }
            }
            else
            {
                // Fallback: extract the root port name (for bundle notation)
                var rootPort = portPath.Split('.')[0];
                coveredPorts.Add(rootPort);
            }
        }
    }
}
