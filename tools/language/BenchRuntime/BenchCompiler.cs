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
            foreach (var binding in bindings)
            {
                if (!benchesByName.ContainsKey(binding.BenchName))
                {
                    // Reported by bench binding checker; skip emission.
                    continue;
                }

                plans.Add(BenchPlanBuilder.Build(document, circuit, binding));
            }
        }

        return plans;
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
