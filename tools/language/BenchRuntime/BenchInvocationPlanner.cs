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
    public static IReadOnlyList<BenchInvocationPlan> CollectInvocations(
        CascodeDocument document,
        Circuit circuit
    )
    {
        var interfacesByName = document.Traits.ToDictionary(
            t => t.Name,
            StringComparer.OrdinalIgnoreCase
        );
        var map = new Dictionary<string, BenchBinding>(StringComparer.OrdinalIgnoreCase);
        if (circuit.Traits is { Count: > 0 })
        {
            foreach (var iface in circuit.Traits)
            {
                if (!interfacesByName.TryGetValue(iface, out var interfaceDef))
                {
                    continue;
                }

                foreach (var binding in interfaceDef.BenchBindings)
                {
                    map.TryAdd(binding.BindingName, binding);
                }
            }
        }

        foreach (var binding in circuit.BenchBindings)
        {
            map[binding.BindingName] = binding;
        }

        var bindings = map
            .Values.OrderBy(b => b.BindingName, StringComparer.OrdinalIgnoreCase)
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
        if (circuit.Constraints?.Numeric is not { Count: > 0 })
        {
            return roots;
        }

        foreach (var constraint in circuit.Constraints.Numeric)
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

    private static IReadOnlyList<BenchInvocationPlan> CollectDependencyInvocations(
        CascodeDocument document,
        Circuit circuit,
        IReadOnlyList<BenchBinding> bindings,
        IReadOnlyDictionary<string, BenchInvocationPlan> rootsByInstance
    )
    {
        if (circuit.Constraints?.Numeric is not { Count: > 0 })
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
                circuit.Constraints.Numeric,
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
                    Array.Empty<MetricCallArg>()
                )
            );
        }

        return deps.Values.ToList();
    }
}
