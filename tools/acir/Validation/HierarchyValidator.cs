using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.ACIR.Validation;

/// <summary>
/// Validates hierarchical ACIR documents for circuit references, parameters, and port coverage.
/// </summary>
/// <remarks>
/// Hierarchy validation rules:
/// - HIER-001: Undefined circuit reference (instance type not in document)
/// - HIER-002: Missing required parameter (no default, not provided at instantiation)
/// - HIER-003: Instance port not covered (not bound directly nor by attach)
/// - HIER-004: Circular instantiation dependency
/// - HIER-005: Duplicate circuit name
/// - HIER-006: Unknown instance in attach statement
/// </remarks>
public static class HierarchyValidator
{
    /// <summary>
    /// Validates a hierarchical ACIR document.
    /// </summary>
    /// <param name="doc">The document to validate.</param>
    /// <returns>Validation result with any errors found.</returns>
    public static ValidationResult Validate(ACIRDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var result = new ValidationResult();

        // HIER-005: Check for duplicate circuit names
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var circuit in doc.Circuits)
        {
            if (!seenNames.Add(circuit.Name))
            {
                result.AddError(
                    "HIER-005",
                    $"Duplicate circuit name '{circuit.Name}'",
                    $"circuit {circuit.Name}",
                    "Each circuit must have a unique name"
                );
            }
        }

        // Build lookup dictionary for O(1) circuit resolution
        var circuitsByName = new Dictionary<string, Circuit>(StringComparer.Ordinal);
        foreach (var circuit in doc.Circuits)
        {
            // Only add first occurrence to avoid dictionary exception
            if (!circuitsByName.ContainsKey(circuit.Name))
            {
                circuitsByName[circuit.Name] = circuit;
            }
        }

        // Validate each circuit's hierarchy
        foreach (var circuit in doc.Circuits)
        {
            var instanceIds = BuildInstanceIds(circuit);
            ValidateCircuitInstances(circuit, circuitsByName, doc.Traits, instanceIds, result);
            ValidateAttachStatements(circuit, instanceIds, result);
        }

        // Detect circular dependencies across the document
        DetectCircularDependencies(doc.Circuits, result);

        return result;
    }

    /// <summary>
    /// Builds a set of instance IDs for a circuit.
    /// </summary>
    private static HashSet<string> BuildInstanceIds(Circuit circuit)
    {
        var instanceIds = new HashSet<string>(StringComparer.Ordinal);
        if (circuit.Fill?.Instances is not null)
        {
            foreach (var inst in circuit.Fill.Instances)
            {
                instanceIds.Add(inst.Id);
            }
        }
        return instanceIds;
    }

    /// <summary>
    /// Validates a single circuit's instances.
    /// </summary>
    private static void ValidateCircuitInstances(
        Circuit circuit,
        Dictionary<string, Circuit> circuitsByName,
        List<TraitDefinition> traits,
        HashSet<string> instanceIds,
        ValidationResult result
    )
    {
        if (circuit.Fill?.Instances is null || circuit.Fill.Instances.Count == 0)
        {
            return;
        }

        foreach (var instance in circuit.Fill.Instances)
        {
            // HIER-001: Validate instance type exists
            if (!circuitsByName.TryGetValue(instance.Type, out var targetCircuit))
            {
                result.AddError(
                    "HIER-001",
                    $"Instance '{instance.Id}' references undefined circuit type '{instance.Type}'",
                    $"circuit {circuit.Name}, inst {instance.Id}",
                    $"Define circuit '{instance.Type}' in this document or check for typos"
                );
                continue; // Cannot validate further without target circuit
            }

            // HIER-002: Validate required parameters
            ValidateInstanceParameters(instance, targetCircuit, circuit.Name, result);

            // HIER-003: Validate port coverage (bindings + attach)
            ValidateInstancePortCoverage(instance, targetCircuit, circuit, traits, result);
        }
    }

    /// <summary>
    /// Validates all required parameters are provided at instantiation.
    /// </summary>
    private static void ValidateInstanceParameters(
        InstanceDeclaration instance,
        Circuit targetCircuit,
        string parentCircuitName,
        ValidationResult result
    )
    {
        foreach (var param in targetCircuit.Parameters)
        {
            // Skip if parameter has a default value
            if (param.Default is not null)
            {
                continue;
            }

            // Required parameter must be provided
            if (!instance.Params.ContainsKey(param.Name))
            {
                result.AddError(
                    "HIER-002",
                    $"Instance '{instance.Id}' missing required parameter '{param.Name}'",
                    $"circuit {parentCircuitName}, inst {instance.Id} : {instance.Type}",
                    $"Add 'param {param.Name} = <value>' to the instance declaration"
                );
            }
        }
    }

    /// <summary>
    /// Validates all ports on an instance are covered by bindings or attach statements.
    /// </summary>
    private static void ValidateInstancePortCoverage(
        InstanceDeclaration instance,
        Circuit targetCircuit,
        Circuit parentCircuit,
        List<TraitDefinition> traits,
        ValidationResult result
    )
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
        foreach (var port in targetCircuit.Ports)
        {
            requiredPorts.Add(port.Name);
        }

        // Remove ports covered by direct bindings
        foreach (var binding in instance.Bindings.Keys)
        {
            // Handle bundle notation: IN.P covers port IN
            var portName = binding.Split('.')[0];
            requiredPorts.Remove(portName);
            requiredPorts.Remove(binding); // Also try exact match
        }

        // Remove ports covered by attach statements
        if (parentCircuit.Fill?.Attaches is not null)
        {
            foreach (var attach in parentCircuit.Fill.Attaches)
            {
                // Check if this attach involves our instance
                if (attach.SourceInstance != instance.Id && attach.TargetInstance != instance.Id)
                {
                    continue;
                }

                // Find the connector and determine which ports are covered
                var coveredPorts = GetPortsCoveredByAttach(attach, instance, targetCircuit, traits);
                foreach (var port in coveredPorts)
                {
                    requiredPorts.Remove(port);
                }
            }
        }

        // Report any remaining uncovered ports
        foreach (var port in requiredPorts)
        {
            result.AddError(
                "HIER-003",
                $"Instance '{instance.Id}' port '{port}' is not bound",
                $"circuit {parentCircuit.Name}, inst {instance.Id} : {instance.Type}",
                $"Add '{port} -> <net>' binding or cover via attach statement"
            );
        }
    }

    /// <summary>
    /// Determines which ports on an instance are covered by an attach statement.
    /// </summary>
    private static HashSet<string> GetPortsCoveredByAttach(
        AttachStatement attach,
        InstanceDeclaration instance,
        Circuit targetCircuit,
        List<TraitDefinition> traits
    )
    {
        var coveredPorts = new HashSet<string>(StringComparer.Ordinal);

        // Parse the via clause: "TraitName::TargetTrait"
        var viaParts = attach.Via.Split("::");
        if (viaParts.Length != 2)
        {
            return coveredPorts; // Invalid via clause, validation handles this elsewhere
        }

        var sourceTraitName = viaParts[0];
        var targetTraitName = viaParts[1];

        // Find the trait with the connector
        var sourceTrait = traits.FirstOrDefault(t =>
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

        // Determine if this instance is source or target in the attach
        if (attach.SourceInstance == instance.Id)
        {
            // Instance is source - connector source ports are covered
            foreach (var mapping in connector.Mappings)
            {
                var portName = mapping.SourcePort.Split('.')[0];
                coveredPorts.Add(portName);
            }
        }
        else if (attach.TargetInstance == instance.Id)
        {
            // Instance is target - connector target ports are covered
            foreach (var mapping in connector.Mappings)
            {
                var portName = mapping.TargetPort.Split('.')[0];
                coveredPorts.Add(portName);
            }
        }

        return coveredPorts;
    }

    /// <summary>
    /// Validates attach statements reference valid instances.
    /// </summary>
    private static void ValidateAttachStatements(
        Circuit circuit,
        HashSet<string> instanceIds,
        ValidationResult result
    )
    {
        if (circuit.Fill?.Attaches is null || circuit.Fill.Attaches.Count == 0)
        {
            return;
        }

        foreach (var attach in circuit.Fill.Attaches)
        {
            // HIER-006: Validate source instance exists
            if (!instanceIds.Contains(attach.SourceInstance))
            {
                result.AddError(
                    "HIER-006",
                    $"Attach references unknown instance '{attach.SourceInstance}'",
                    $"circuit {circuit.Name}, attach {attach.SourceInstance} to {attach.TargetInstance}",
                    $"Check instance name or add 'inst {attach.SourceInstance} : <type>' declaration"
                );
            }

            // HIER-006: Validate target instance exists
            if (!instanceIds.Contains(attach.TargetInstance))
            {
                result.AddError(
                    "HIER-006",
                    $"Attach references unknown instance '{attach.TargetInstance}'",
                    $"circuit {circuit.Name}, attach {attach.SourceInstance} to {attach.TargetInstance}",
                    $"Check instance name or add 'inst {attach.TargetInstance} : <type>' declaration"
                );
            }
        }
    }

    /// <summary>
    /// Detects circular instantiation dependencies using DFS.
    /// </summary>
    private static void DetectCircularDependencies(List<Circuit> circuits, ValidationResult result)
    {
        // Build dependency graph: circuit name -> set of circuit names it instantiates
        var dependencies = BuildDependencyGraph(circuits);

        // Track visited and currently-in-stack for cycle detection
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var inStack = new HashSet<string>(StringComparer.Ordinal);

        foreach (var circuit in circuits)
        {
            if (!visited.Contains(circuit.Name))
            {
                DetectCyclesDfs(circuit.Name, dependencies, visited, inStack, result);
            }
        }
    }

    /// <summary>
    /// Builds dependency graph from circuit instantiations.
    /// </summary>
    private static Dictionary<string, HashSet<string>> BuildDependencyGraph(List<Circuit> circuits)
    {
        var graph = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var circuit in circuits)
        {
            if (!graph.ContainsKey(circuit.Name))
            {
                graph[circuit.Name] = new HashSet<string>(StringComparer.Ordinal);
            }

            if (circuit.Fill?.Instances is null)
            {
                continue;
            }

            foreach (var instance in circuit.Fill.Instances)
            {
                graph[circuit.Name].Add(instance.Type);
            }
        }

        return graph;
    }

    /// <summary>
    /// DFS helper for cycle detection.
    /// </summary>
    private static void DetectCyclesDfs(
        string current,
        Dictionary<string, HashSet<string>> dependencies,
        HashSet<string> visited,
        HashSet<string> inStack,
        ValidationResult result
    )
    {
        visited.Add(current);
        inStack.Add(current);

        if (dependencies.TryGetValue(current, out var deps))
        {
            foreach (var dep in deps)
            {
                if (inStack.Contains(dep))
                {
                    // HIER-004: Cycle detected
                    result.AddError(
                        "HIER-004",
                        $"Circular instantiation dependency: '{current}' -> '{dep}'",
                        $"circuit {current}",
                        "Break the cycle by restructuring the circuit hierarchy"
                    );
                }
                else if (!visited.Contains(dep) && dependencies.ContainsKey(dep))
                {
                    DetectCyclesDfs(dep, dependencies, visited, inStack, result);
                }
            }
        }

        inStack.Remove(current);
    }
}
