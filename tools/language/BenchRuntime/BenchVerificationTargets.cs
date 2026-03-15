using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language.BenchRuntime;

public static class BenchVerificationTargets
{
    public static IReadOnlyList<Circuit> CollectVerifiableCircuits(CascodeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return SpiceEmitter
            .OrderByDependency(document)
            .Where(circuit => circuit.Level == CascodeLevel.EL && !circuit.Inline)
            .Where(circuit =>
                BenchInvocationPlanner.CollectInvocations(document, circuit).Count > 0
            )
            .ToList();
    }
}
