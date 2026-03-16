using System;
using System.Collections.Generic;
using System.Linq;
using Cascode.Language;

namespace Cascode.Language.BenchRuntime;

public sealed record BenchInvocationPlan(
    BenchBinding Binding,
    string InstanceName,
    IReadOnlyList<MetricCallArg> InvocationArgs
);

public static class BenchInvocationPlanner
{
    /// <summary>
    /// Collects the bench invocations required to satisfy a circuit's bench constraints.
    /// </summary>
    /// <remarks>
    /// Planner resolution intentionally shares <see cref="BenchBindingResolver"/> with the
    /// validator and extension folder so all three stages agree on inheritance, circuit
    /// overrides, and extension-applied bindings. This avoids a class of bugs where
    /// validation and runtime would otherwise reason about different effective bindings.
    /// </remarks>
    public static IReadOnlyList<BenchInvocationPlan> CollectInvocations(
        CascodeDocument document,
        Circuit circuit
    )
    {
        var interfacesByName = document.Traits.ToDictionary(
            t => t.Name,
            StringComparer.OrdinalIgnoreCase
        );
        // Use the shared resolved view rather than rebuilding inheritance rules here.
        var resolution = BenchBindingResolver.ResolveForCircuit(circuit, interfacesByName);
        var bindings = resolution
            .Bindings.Values.Select(binding => binding.Binding)
            .OrderBy(binding => binding.BindingName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (bindings.Count == 0)
        {
            return Array.Empty<BenchInvocationPlan>();
        }

        var rootsByInstance = CollectConstraintRoots(circuit, bindings);
        var plansByInstance = new Dictionary<string, BenchInvocationPlan>(
            rootsByInstance,
            StringComparer.OrdinalIgnoreCase
        );

        foreach (
            var dependency in CollectDependencyInvocations(
                document,
                circuit,
                bindings,
                rootsByInstance
            )
        )
        {
            plansByInstance.TryAdd(dependency.InstanceName, dependency);
        }

        return plansByInstance
            .Values.OrderBy(i => i.InstanceName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, BenchInvocationPlan> CollectConstraintRoots(
        Circuit circuit,
        IReadOnlyList<BenchBinding> bindings
    )
    {
        var bindingsByName = bindings.ToDictionary(
            b => b.BindingName,
            StringComparer.OrdinalIgnoreCase
        );

        var roots = new Dictionary<string, BenchInvocationPlan>(StringComparer.OrdinalIgnoreCase);
        if (circuit.Constraints?.Bench is not { Count: > 0 })
        {
            return roots;
        }

        foreach (var constraint in circuit.Constraints.Bench)
        {
            if (!bindingsByName.TryGetValue(constraint.BenchBase, out var binding))
            {
                continue;
            }

            if (roots.ContainsKey(constraint.Bench))
            {
                continue;
            }

            roots[constraint.Bench] = new BenchInvocationPlan(
                binding,
                constraint.Bench,
                constraint.BenchArgs
            );
        }

        return roots;
    }

    /// <summary>
    /// Computes additional bench invocation plans required by dependency resolution for the given circuit constraints.
    /// </summary>
    /// <param name="document">The CascodeDocument containing bench definitions used to resolve bench aliases.</param>
    /// <param name="circuit">The Circuit whose bench constraints drive dependency graph construction.</param>
    /// <param name="bindings">The available bench bindings to map binding aliases to concrete bench configurations.</param>
    /// <param name="rootsByInstance">Existing root invocation plans keyed by bench instance name; instances present here are excluded from dependency results.</param>
    /// <returns>
    /// A list of BenchInvocationPlan objects for dependency-driven bench invocations; each plan uses a resolved binding, the target instance name, and invocation arguments derived from the dependency graph. Returns an empty list if no bench constraints exist or if dependency graph construction fails.
    /// </returns>
    private static IReadOnlyList<BenchInvocationPlan> CollectDependencyInvocations(
        CascodeDocument document,
        Circuit circuit,
        IReadOnlyList<BenchBinding> bindings,
        IReadOnlyDictionary<string, BenchInvocationPlan> rootsByInstance
    )
    {
        if (circuit.Constraints?.Bench is not { Count: > 0 })
        {
            return Array.Empty<BenchInvocationPlan>();
        }

        var bindingsByAlias = bindings.ToDictionary(
            b => b.BindingName,
            StringComparer.OrdinalIgnoreCase
        );
        var benchByName = document.BenchDefinitions.ToDictionary(
            b => b.Name,
            StringComparer.OrdinalIgnoreCase
        );
        var benchByAlias = new Dictionary<string, BenchDefinition>(
            StringComparer.OrdinalIgnoreCase
        );
        var exportsByAlias = new Dictionary<
            string,
            IReadOnlyDictionary<string, BenchBindingMeasurementExport>
        >(StringComparer.OrdinalIgnoreCase);
        foreach (var (alias, binding) in bindingsByAlias)
        {
            if (benchByName.TryGetValue(binding.BenchName, out var bench))
            {
                benchByAlias[alias] = bench;
            }

            var exports = binding.Statements.OfType<BenchBindingMeasurementExport>().ToList();
            if (exports.Count == 0)
            {
                continue;
            }

            var byName = new Dictionary<string, BenchBindingMeasurementExport>(
                StringComparer.OrdinalIgnoreCase
            );
            foreach (var export in exports)
            {
                byName[export.Name] = export;
            }

            exportsByAlias[alias] = byName;
        }

        if (
            !BenchDependencyGraph.TryBuild(
                circuit,
                circuit.Constraints.Bench,
                benchByAlias,
                exportsByAlias,
                out var graph,
                out _
            )
        )
        {
            return Array.Empty<BenchInvocationPlan>();
        }

        var deps = new Dictionary<string, BenchInvocationPlan>(StringComparer.OrdinalIgnoreCase);
        foreach (var invocation in graph.InvocationsById.Values)
        {
            if (rootsByInstance.ContainsKey(invocation.BenchInstanceName))
            {
                continue;
            }

            if (!bindingsByAlias.TryGetValue(invocation.BenchBindingAlias, out var binding))
            {
                continue;
            }

            deps.TryAdd(
                invocation.BenchInstanceName,
                new BenchInvocationPlan(
                    binding,
                    invocation.BenchInstanceName,
                    invocation.Args.Select(arg => new MetricCallArg(arg.Name, arg.Text)).ToList()
                )
            );
        }

        return deps.Values.ToList();
    }
}
