using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language.Validation;

internal static partial class PartSemanticChecker
{
    private static EffectivePartDefinition CreateEffectivePart(PartDefinition part) =>
        new(
            part.Name,
            part.IsAbstract,
            [.. part.Implements],
            [.. part.Parameters],
            [.. part.Supplies],
            [.. part.Grounds],
            [.. part.Ports],
            [.. part.Corners],
            new PartCatalog
            {
                Defaults = part.Catalog.Defaults,
                Entries = [.. part.Catalog.Entries],
                Variants = [.. part.Catalog.Variants],
            },
            []
        );

    private static EffectivePartDefinition MergeParts(
        EffectivePartDefinition basePart,
        EffectivePartDefinition derivedPart,
        List<Diagnostic> diagnostics
    )
    {
        ValidateUnique(
            basePart.Parameters.Select(parameter => parameter.Name),
            derivedPart.Parameters.Select(parameter => parameter.Name),
            derivedPart.Name,
            "parameter",
            diagnostics
        );
        ValidateUnique(
            basePart.Ports.Select(port => port.Name),
            derivedPart.Ports.Select(port => port.Name),
            derivedPart.Name,
            "port",
            diagnostics
        );
        ValidateUnique(
            basePart.Corners.Select(corner => corner.Name),
            derivedPart.Corners.Select(corner => corner.Name),
            derivedPart.Name,
            "corner",
            diagnostics
        );
        ValidateUnique(
            basePart.Catalog.Entries.Select(entry => entry.Name),
            derivedPart.Catalog.Entries.Select(entry => entry.Name),
            derivedPart.Name,
            "catalog entry",
            diagnostics
        );
        ValidateUnique(
            basePart.Catalog.Variants.Select(variant => variant.Name),
            derivedPart.Catalog.Variants.Select(variant => variant.Name),
            derivedPart.Name,
            "variant axis",
            diagnostics
        );

        return derivedPart with
        {
            Implements = basePart
                .Implements.Concat(derivedPart.Implements)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            Parameters = basePart.Parameters.Concat(derivedPart.Parameters).ToList(),
            Supplies = basePart
                .Supplies.Concat(derivedPart.Supplies)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            Grounds = basePart
                .Grounds.Concat(derivedPart.Grounds)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            Ports = basePart.Ports.Concat(derivedPart.Ports).ToList(),
            Corners = basePart.Corners.Concat(derivedPart.Corners).ToList(),
            Catalog = new PartCatalog
            {
                Defaults = MergeBody(basePart.Catalog.Defaults, derivedPart.Catalog.Defaults),
                Entries = basePart.Catalog.Entries.Concat(derivedPart.Catalog.Entries).ToList(),
                Variants = basePart.Catalog.Variants.Concat(derivedPart.Catalog.Variants).ToList(),
            },
        };
    }

    private static void ValidateUnique(
        IEnumerable<string> inherited,
        IEnumerable<string> local,
        string partName,
        string kind,
        List<Diagnostic> diagnostics
    )
    {
        foreach (var duplicate in inherited.Intersect(local, StringComparer.Ordinal))
        {
            diagnostics.Add(
                new Diagnostic(
                    $"PART-016: Part '{partName}' redeclares inherited {kind} '{duplicate}'.",
                    DiagnosticSeverity.Error,
                    "<semantic>",
                    1,
                    1
                )
            );
        }
    }
}
