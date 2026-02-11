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

        var benchesByName = document.BenchDefinitions.ToDictionary(
            b => b.Name,
            StringComparer.OrdinalIgnoreCase
        );

        var plans = new List<BenchPlan>();

        foreach (
            var circuit in document.Circuits.Where(c => c.Level == CascodeLevel.EL && !c.Inline)
        )
        {
            var invocations = BenchInvocationPlanner.CollectInvocations(document, circuit);
            foreach (var invocation in invocations)
            {
                var binding = invocation.Binding;
                if (!benchesByName.ContainsKey(binding.BenchName))
                {
                    // Reported by bench binding checker; skip emission.
                    continue;
                }

                plans.Add(
                    BenchPlanBuilder.Build(
                        document,
                        circuit,
                        binding,
                        invocation.InstanceName,
                        invocation.InvocationArgs
                    )
                );
            }
        }

        return plans;
    }
}
