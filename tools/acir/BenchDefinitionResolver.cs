using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.ACIR;

public static class BenchDefinitionResolver
{
    public static IReadOnlyList<BenchDefinition> ResolveForCircuit(
        ACIRDocument document,
        Circuit circuit
    )
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(circuit);

        if (document.BenchDefinitions.Count == 0)
        {
            return Array.Empty<BenchDefinition>();
        }

        if (circuit.Traits == null || circuit.Traits.Count == 0)
        {
            return Array.Empty<BenchDefinition>();
        }

        var traits = new HashSet<string>(circuit.Traits, StringComparer.OrdinalIgnoreCase);
        return document
            .BenchDefinitions.Where(b =>
                !string.IsNullOrWhiteSpace(b.Trait) && traits.Contains(b.Trait)
            )
            .ToList();
    }
}
