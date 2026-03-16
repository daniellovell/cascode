using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language.Validation;

internal static partial class PartSemanticChecker
{
    private static List<EffectivePartEntry> ExpandEntries(EffectivePartDefinition part)
    {
        var seedEntries =
            part.Catalog.Entries.Count == 0
                ?
                [
                    new EffectivePartEntry(
                        null,
                        new Dictionary<string, string>(StringComparer.Ordinal),
                        MergeBody(part.Catalog.Defaults, null),
                        []
                    ),
                ]
                : part
                    .Catalog.Entries.Select(entry => new EffectivePartEntry(
                        entry.Name,
                        new Dictionary<string, string>(StringComparer.Ordinal),
                        MergeBody(part.Catalog.Defaults, entry.Body),
                        []
                    ))
                    .ToList();

        var current = seedEntries;
        foreach (var axis in part.Catalog.Variants)
        {
            var expanded = new List<EffectivePartEntry>();
            foreach (var candidate in current)
            {
                foreach (var option in axis.Options)
                {
                    var selections = new Dictionary<string, string>(
                        candidate.Selection,
                        StringComparer.Ordinal
                    )
                    {
                        [axis.Name] = option.Name,
                    };
                    expanded.Add(
                        new EffectivePartEntry(
                            candidate.EntryName,
                            selections,
                            MergeBody(candidate.Body, option.Body),
                            [.. candidate.Excludes, .. option.Excludes]
                        )
                    );
                }
            }

            current = expanded;
        }

        return current
            .Where(candidate =>
                !candidate.Excludes.Any(exclude =>
                    candidate.Selection.TryGetValue(exclude.Axis!, out var value)
                    && value == exclude.Value
                )
            )
            .ToList();
    }

    private static List<EffectivePartEntry> SelectEntries(
        EffectivePartDefinition part,
        IReadOnlyList<SelectionArgument> selection,
        List<Diagnostic> diagnostics,
        InstanceDeclaration instance
    )
    {
        var positional = selection
            .Where(argument => string.IsNullOrWhiteSpace(argument.Axis))
            .ToList();
        if (positional.Count > 1)
        {
            diagnostics.Add(
                new Diagnostic(
                    $"PART-005: Instance '{instance.Id}' selects multiple catalog entries for part '{part.Name}'.",
                    DiagnosticSeverity.Error,
                    "<semantic>",
                    1,
                    1
                )
            );
            return [];
        }

        var named = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (
            var argument in selection.Where(argument => !string.IsNullOrWhiteSpace(argument.Axis))
        )
        {
            if (!named.TryAdd(argument.Axis!, argument.Value))
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"PART-006: Instance '{instance.Id}' selects axis '{argument.Axis}' more than once for part '{part.Name}'.",
                        DiagnosticSeverity.Error,
                        "<semantic>",
                        1,
                        1
                    )
                );
                return [];
            }
        }

        var filtered = part
            .EffectiveEntries.Where(entry =>
                positional.Count == 0 || entry.EntryName == positional[0].Value
            )
            .Where(entry =>
                named.All(axis =>
                    entry.Selection.TryGetValue(axis.Key, out var option) && option == axis.Value
                )
            )
            .ToList();

        if (filtered.Count == 1)
        {
            return filtered;
        }

        diagnostics.Add(
            new Diagnostic(
                filtered.Count == 0
                    ? $"PART-007: Instance '{instance.Id}' selection does not match any effective entry of part '{part.Name}'."
                    : $"PART-008: Instance '{instance.Id}' selection is incomplete for part '{part.Name}'.",
                DiagnosticSeverity.Error,
                "<semantic>",
                1,
                1
            )
        );
        return filtered;
    }

    private static PartCatalogBody MergeBody(PartCatalogBody? first, PartCatalogBody? second)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var options = new List<CatalogOption>();
        var pins = new List<PinMapEntry>();
        var units = new List<UnitGroup>();
        MetricsBlock? metrics = null;

        foreach (var body in new[] { first, second }.Where(body => body is not null))
        {
            foreach (var (key, value) in body!.Fields)
            {
                fields[key] = value;
            }

            options.AddRange(body.Options);
            pins.AddRange(body.Pins);
            units.AddRange(body.Units);
            metrics = MergeMetrics(metrics, body.Metrics);
        }

        return new PartCatalogBody
        {
            Fields = fields,
            Options = options,
            Pins = pins,
            Units = units,
            Metrics = metrics,
        };
    }

    private static MetricsBlock? MergeMetrics(MetricsBlock? first, MetricsBlock? second)
    {
        if (first is null)
        {
            return second;
        }

        if (second is null)
        {
            return first;
        }

        var assignments = first.Assignments.ToDictionary(assignment =>
            (assignment.Name, assignment.Qualifier, assignment.Corner)
        );
        foreach (var assignment in second.Assignments)
        {
            assignments[(assignment.Name, assignment.Qualifier, assignment.Corner)] = assignment;
        }

        return new MetricsBlock { Assignments = assignments.Values.ToList() };
    }
}
