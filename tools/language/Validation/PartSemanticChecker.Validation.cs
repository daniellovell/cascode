using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cascode.Language.Validation;

internal static partial class PartSemanticChecker
{
    private static readonly Regex PlaceholderPattern = new(@"\{(?<name>[^{}]+)\}");

    private static void ValidatePartShape(
        EffectivePartDefinition part,
        List<Diagnostic> diagnostics
    )
    {
        if (!part.IsAbstract && part.EffectiveEntries.Count == 0)
        {
            diagnostics.Add(
                new Diagnostic(
                    $"PART-003: Part '{part.Name}' must produce at least one effective catalog entry.",
                    DiagnosticSeverity.Error,
                    "<semantic>",
                    1,
                    1
                )
            );
        }

        foreach (var entry in part.EffectiveEntries)
        {
            ValidatePlaceholders(part, entry, diagnostics);
            ValidatePins(part, entry, diagnostics);
            ValidateUnits(part, entry, diagnostics);
        }
    }

    private static void ValidateInstances(
        CascodeDocument document,
        IReadOnlyDictionary<string, EffectivePartDefinition> effectiveParts,
        List<Diagnostic> diagnostics
    )
    {
        foreach (var circuit in document.Circuits)
        {
            foreach (
                var instance in circuit.Fill?.Instances ?? Enumerable.Empty<InstanceDeclaration>()
            )
            {
                var partName = InstanceTargetResolver.GetReferenceName(instance.Type);
                if (!effectiveParts.TryGetValue(partName, out var part))
                {
                    continue;
                }

                if (part.IsAbstract)
                {
                    diagnostics.Add(
                        new Diagnostic(
                            $"PART-004: Instance '{instance.Id}' cannot instantiate abstract part '{part.Name}'.",
                            DiagnosticSeverity.Error,
                            "<semantic>",
                            1,
                            1
                        )
                    );
                    continue;
                }

                var selected = SelectEntries(part, instance.Selection, diagnostics, instance);
                if (selected.Count != 1)
                {
                    continue;
                }

                ValidateESeries(part, instance, diagnostics);
            }
        }
    }

    private static void ValidatePins(
        EffectivePartDefinition part,
        EffectivePartEntry entry,
        List<Diagnostic> diagnostics
    )
    {
        var expected = ExpandTargets(part).ToHashSet(StringComparer.Ordinal);
        var pads = new HashSet<string>(StringComparer.Ordinal);
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pin in entry.Body.Pins)
        {
            var padList = ExpandSequence(pin.Pad);
            var targetList = ExpandSequence(pin.Target);
            if (padList.Count != targetList.Count)
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"PART-009: Part '{part.Name}' pin map '{pin.Pad} = {pin.Target}' has mismatched range lengths.",
                        DiagnosticSeverity.Error,
                        "<semantic>",
                        1,
                        1
                    )
                );
                continue;
            }

            for (var index = 0; index < padList.Count; index++)
            {
                if (!pads.Add(padList[index]) || !targets.Add(targetList[index]))
                {
                    diagnostics.Add(
                        new Diagnostic(
                            $"PART-010: Part '{part.Name}' pin map contains a duplicate pad or terminal assignment.",
                            DiagnosticSeverity.Error,
                            "<semantic>",
                            1,
                            1
                        )
                    );
                }
            }
        }

        if (!expected.SetEquals(targets))
        {
            diagnostics.Add(
                new Diagnostic(
                    $"PART-011: Part '{part.Name}' effective entry does not cover every terminal leaf exactly once.",
                    DiagnosticSeverity.Error,
                    "<semantic>",
                    1,
                    1
                )
            );
        }
    }

    private static void ValidateUnits(
        EffectivePartDefinition part,
        EffectivePartEntry entry,
        List<Diagnostic> diagnostics
    )
    {
        var padSet = entry
            .Body.Pins.SelectMany(pin => ExpandSequence(pin.Pad))
            .ToHashSet(StringComparer.Ordinal);
        var targetSet = entry
            .Body.Pins.SelectMany(pin => ExpandSequence(pin.Target))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var unit in entry.Body.Units)
        {
            foreach (var pad in ParseTuple(unit.Fields.GetValueOrDefault("pads")))
            {
                if (!padSet.Contains(pad))
                {
                    diagnostics.Add(
                        new Diagnostic(
                            $"PART-012: Part '{part.Name}' unit '{unit.Name}' references unknown pad '{pad}'.",
                            DiagnosticSeverity.Error,
                            "<semantic>",
                            1,
                            1
                        )
                    );
                }
            }

            foreach (var terminal in ParseTuple(unit.Fields.GetValueOrDefault("terminals")))
            {
                if (!targetSet.Contains(terminal))
                {
                    diagnostics.Add(
                        new Diagnostic(
                            $"PART-013: Part '{part.Name}' unit '{unit.Name}' references unknown terminal '{terminal}'.",
                            DiagnosticSeverity.Error,
                            "<semantic>",
                            1,
                            1
                        )
                    );
                }
            }
        }
    }

    private static void ValidatePlaceholders(
        EffectivePartDefinition part,
        EffectivePartEntry entry,
        List<Diagnostic> diagnostics
    )
    {
        foreach (
            var value in entry.Body.Fields.Values.Concat(
                entry.Body.Options.SelectMany(option => option.Fields.Values)
            )
        )
        {
            foreach (Match match in PlaceholderPattern.Matches(value))
            {
                var token = match.Groups["name"].Value;
                if (part.Parameters.Any(parameter => parameter.Name == token))
                {
                    continue;
                }

                var pieces = token.Split('.', StringSplitOptions.RemoveEmptyEntries);
                if (
                    pieces.Length == 2
                    && entry.Selection.TryGetValue(pieces[0], out var optionName)
                    && part.Catalog.Variants.First(variant => variant.Name == pieces[0])
                        .Options.First(option => option.Name == optionName)
                        .Body.Fields.ContainsKey(pieces[1])
                )
                {
                    continue;
                }

                diagnostics.Add(
                    new Diagnostic(
                        $"PART-014: Part '{part.Name}' uses unresolved placeholder '{{{token}}}'.",
                        DiagnosticSeverity.Error,
                        "<semantic>",
                        1,
                        1
                    )
                );
            }
        }
    }

    private static void ValidateESeries(
        EffectivePartDefinition part,
        InstanceDeclaration instance,
        List<Diagnostic> diagnostics
    )
    {
        foreach (
            var parameter in part.Parameters.Where(parameter =>
                parameter.Type.StartsWith("e", StringComparison.Ordinal)
            )
        )
        {
            if (
                !instance.Params.TryGetValue(parameter.Name, out var value)
                || string.IsNullOrWhiteSpace(value.Numeric)
            )
            {
                continue;
            }

            if (!BelongsToESeries(parameter.Type, value.Numeric!))
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"PART-015: Instance '{instance.Id}' value '{value.Numeric}' does not belong to series '{parameter.Type}' for part '{part.Name}'.",
                        DiagnosticSeverity.Error,
                        "<semantic>",
                        1,
                        1
                    )
                );
            }
        }
    }
}
