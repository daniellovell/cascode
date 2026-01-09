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
/// - HIER-007: Missing required size pack (no default, not provided at instantiation)
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

            // HIER-007: Validate required size packs
            ValidateInstanceSizes(instance, targetCircuit, circuit.Name, result);

            // HIER-003: Validate port coverage (bindings + attach)
            var portAnalysis = new PortCoverageAnalysis(circuit, traits, result);
            portAnalysis.ValidateInstancePortCoverage(instance, targetCircuit);
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
    /// Validates all required size packs are provided at instantiation.
    /// </summary>
    private static void ValidateInstanceSizes(
        InstanceDeclaration instance,
        Circuit targetCircuit,
        string parentCircuitName,
        ValidationResult result
    )
    {
        foreach (var size in targetCircuit.Sizes)
        {
            // Skip if size pack has a default value
            if (size.Default is not null)
            {
                continue;
            }

            if (!instance.Sizes.ContainsKey(size.Name))
            {
                result.AddError(
                    "HIER-007",
                    $"Instance '{instance.Id}' missing required size pack '{size.Name}'",
                    $"circuit {parentCircuitName}, inst {instance.Id} : {instance.Type}",
                    $"Add 'size {size.Name} = (k=v, ...)' to the instance declaration"
                );
            }
        }
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
            var attachChain = FormatAttachChain(attach);
            // HIER-006: Validate source instance exists
            if (!instanceIds.Contains(attach.SourceInstance))
            {
                result.AddError(
                    "HIER-006",
                    $"Attach references unknown instance '{attach.SourceInstance}'",
                    $"circuit {circuit.Name}, attach {attachChain}",
                    $"Check instance name or add 'inst {attach.SourceInstance} : <type>' declaration"
                );
            }

            // HIER-006: Validate target instance exists
            foreach (var targetInstance in attach.TargetInstances)
            {
                if (!instanceIds.Contains(targetInstance))
                {
                    result.AddError(
                        "HIER-006",
                        $"Attach references unknown instance '{targetInstance}'",
                        $"circuit {circuit.Name}, attach {attachChain}",
                        $"Check instance name or add 'inst {targetInstance} : <type>' declaration"
                    );
                }
            }
        }
    }

    private static string FormatAttachChain(AttachStatement attach)
    {
        var instanceChain = AttachResolver.BuildInstanceChain(attach);
        return string.Join(" to ", instanceChain);
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
