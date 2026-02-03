using System;
using System.Collections.Generic;
using System.Linq;
using Cascode.Language;

namespace Cascode.Language.BenchRuntime;

/// <summary>
/// Compiles declarative benches/bindings into concrete <see cref="BenchPlan"/>s.
/// </summary>
public static class BenchCompiler
{
    public static IReadOnlyList<BenchPlan> CompileAllPlans(CascodeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var interfacesByName = document.Traits.ToDictionary(
            t => t.Name,
            StringComparer.OrdinalIgnoreCase
        );
        var benchesByName = document.BenchDefinitions.ToDictionary(
            b => b.Name,
            StringComparer.OrdinalIgnoreCase
        );

        var plans = new List<BenchPlan>();

        foreach (
            var circuit in document.Circuits.Where(c => c.Level == CascodeLevel.EL && !c.Inline)
        )
        {
            var bindings = ResolveBenchBindings(circuit, interfacesByName);
            var invocations = CollectBenchInvocations(circuit, bindings);

            foreach (var (binding, instanceName, args) in invocations)
            {
                if (!benchesByName.ContainsKey(binding.BenchName))
                {
                    // Reported by bench binding checker; skip emission.
                    continue;
                }

                plans.Add(BenchPlanBuilder.Build(document, circuit, binding, instanceName, args));
            }
        }

        return plans;
    }

    /// <summary>
    /// Collects unique bench invocations for a circuit by examining constraints.
    /// Each unique (bindingName, args) pair produces a separate bench instance.
    /// Bindings not referenced by any constraint get a single instance with empty args.
    /// </summary>
    private static IReadOnlyList<(
        BenchBinding Binding,
        string InstanceName,
        IReadOnlyList<MetricCallArg> Args
    )> CollectBenchInvocations(Circuit circuit, IReadOnlyList<BenchBinding> bindings)
    {
        var bindingsByName = bindings.ToDictionary(
            b => b.BindingName,
            StringComparer.OrdinalIgnoreCase
        );

        // Collect unique invocations from constraints, keyed by computed instance name.
        var invocationsByInstance = new Dictionary<
            string,
            (BenchBinding, string, IReadOnlyList<MetricCallArg>)
        >(StringComparer.OrdinalIgnoreCase);

        if (circuit.Constraints?.Numeric is { Count: > 0 })
        {
            foreach (var constraint in circuit.Constraints.Numeric)
            {
                if (!bindingsByName.TryGetValue(constraint.BenchBase, out var binding))
                {
                    continue; // Unknown binding, reported by validation.
                }

                var instanceName = constraint.Bench; // Already computed by AST builder.
                if (!invocationsByInstance.ContainsKey(instanceName))
                {
                    invocationsByInstance[instanceName] = (
                        binding,
                        instanceName,
                        constraint.BenchArgs
                    );
                }
            }
        }

        // For bindings not referenced by any constraint, add a default invocation with empty args.
        foreach (var binding in bindings)
        {
            if (
                !invocationsByInstance.Values.Any(i =>
                    i.Item1.BindingName.Equals(
                        binding.BindingName,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            )
            {
                invocationsByInstance[binding.BindingName] = (
                    binding,
                    binding.BindingName,
                    Array.Empty<MetricCallArg>()
                );
            }
        }

        return invocationsByInstance
            .Values.OrderBy(i => i.Item2, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<BenchBinding> ResolveBenchBindings(
        Circuit circuit,
        IReadOnlyDictionary<string, TraitDefinition> interfacesByName
    )
    {
        var map = new Dictionary<string, BenchBinding>(StringComparer.OrdinalIgnoreCase);

        if (circuit.Traits is { Count: > 0 })
        {
            foreach (var iface in circuit.Traits)
            {
                if (!interfacesByName.TryGetValue(iface, out var interfaceDef))
                {
                    continue;
                }

                foreach (var b in interfaceDef.BenchBindings)
                {
                    map.TryAdd(b.BindingName, b);
                }
            }
        }

        foreach (var b in circuit.BenchBindings)
        {
            map[b.BindingName] = b;
        }

        return map.Values.OrderBy(b => b.BindingName, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
