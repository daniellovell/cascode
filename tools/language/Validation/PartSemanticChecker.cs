using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language.Validation;

internal static partial class PartSemanticChecker
{
    public static void Check(CascodeDocument document, List<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var effectiveParts = BuildEffectiveParts(document, diagnostics);
        ValidateInstances(document, effectiveParts, diagnostics);
    }

    private static Dictionary<string, EffectivePartDefinition> BuildEffectiveParts(
        CascodeDocument document,
        List<Diagnostic> diagnostics
    )
    {
        var partsByName = document.Parts.ToDictionary(p => p.Name, StringComparer.Ordinal);
        var cache = new Dictionary<string, EffectivePartDefinition>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in document.Parts)
        {
            BuildEffectivePart(part, partsByName, cache, visiting, diagnostics);
        }

        return cache;
    }

    private static EffectivePartDefinition BuildEffectivePart(
        PartDefinition part,
        IReadOnlyDictionary<string, PartDefinition> partsByName,
        IDictionary<string, EffectivePartDefinition> cache,
        ISet<string> visiting,
        List<Diagnostic> diagnostics
    )
    {
        if (cache.TryGetValue(part.Name, out var cached))
        {
            return cached;
        }

        if (!visiting.Add(part.Name))
        {
            diagnostics.Add(
                new Diagnostic(
                    $"PART-001: Part '{part.Name}' participates in an inheritance cycle.",
                    DiagnosticSeverity.Error,
                    "<semantic>",
                    1,
                    1
                )
            );
            return cache[part.Name] = CreateEffectivePart(part);
        }

        var effective = CreateEffectivePart(part);
        if (!string.IsNullOrWhiteSpace(part.BasePart))
        {
            if (!partsByName.TryGetValue(part.BasePart, out var basePart))
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"PART-002: Part '{part.Name}' extends unknown base part '{part.BasePart}'.",
                        DiagnosticSeverity.Error,
                        "<semantic>",
                        1,
                        1
                    )
                );
            }
            else
            {
                effective = MergeParts(
                    BuildEffectivePart(basePart, partsByName, cache, visiting, diagnostics),
                    effective,
                    diagnostics
                );
            }
        }

        effective = effective with { EffectiveEntries = ExpandEntries(effective) };
        ValidatePartShape(effective, diagnostics);
        visiting.Remove(part.Name);
        cache[part.Name] = effective;
        return effective;
    }
}
