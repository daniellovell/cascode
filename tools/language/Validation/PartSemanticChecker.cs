using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cascode.Language.Validation;

internal static class PartSemanticChecker
{
    private static readonly Regex PlaceholderPattern = new(@"\{(?<name>[^{}]+)\}");

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
            basePart.Parameters.Select(p => p.Name),
            derivedPart.Parameters.Select(p => p.Name),
            derivedPart.Name,
            "parameter",
            diagnostics
        );
        ValidateUnique(
            basePart.Ports.Select(p => p.Name),
            derivedPart.Ports.Select(p => p.Name),
            derivedPart.Name,
            "port",
            diagnostics
        );
        ValidateUnique(
            basePart.Corners.Select(c => c.Name),
            derivedPart.Corners.Select(c => c.Name),
            derivedPart.Name,
            "corner",
            diagnostics
        );
        ValidateUnique(
            basePart.Catalog.Entries.Select(e => e.Name),
            derivedPart.Catalog.Entries.Select(e => e.Name),
            derivedPart.Name,
            "catalog entry",
            diagnostics
        );
        ValidateUnique(
            basePart.Catalog.Variants.Select(v => v.Name),
            derivedPart.Catalog.Variants.Select(v => v.Name),
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
        var positional = selection.Where(s => string.IsNullOrWhiteSpace(s.Axis)).ToList();
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
        foreach (var arg in selection.Where(s => !string.IsNullOrWhiteSpace(s.Axis)))
        {
            if (!named.TryAdd(arg.Axis!, arg.Value))
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"PART-006: Instance '{instance.Id}' selects axis '{arg.Axis}' more than once for part '{part.Name}'.",
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

            for (var i = 0; i < padList.Count; i++)
            {
                if (!pads.Add(padList[i]) || !targets.Add(targetList[i]))
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
                if (part.Parameters.Any(param => param.Name == token))
                {
                    continue;
                }

                var pieces = token.Split('.', StringSplitOptions.RemoveEmptyEntries);
                if (
                    pieces.Length == 2
                    && entry.Selection.TryGetValue(pieces[0], out var optionName)
                    && part.Catalog.Variants.First(v => v.Name == pieces[0])
                        .Options.First(o => o.Name == optionName)
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

        var assignments = first.Assignments.ToDictionary(a => (a.Name, a.Qualifier, a.Corner));
        foreach (var assignment in second.Assignments)
        {
            assignments[(assignment.Name, assignment.Qualifier, assignment.Corner)] = assignment;
        }

        return new MetricsBlock { Assignments = assignments.Values.ToList() };
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

    private static IEnumerable<string> ExpandTargets(EffectivePartDefinition part) =>
        part
            .Supplies.Concat(part.Grounds)
            .Concat(part.Ports.SelectMany(port => ExpandSequence(port.Name)));

    private static List<string> ExpandSequence(string value)
    {
        var rangeMatch = Regex.Match(
            value,
            @"^(?<prefix>[A-Za-z_][A-Za-z0-9_]*)(?<start>\d+):(?<prefix2>[A-Za-z_][A-Za-z0-9_]*)(?<end>\d+)$"
        );
        if (
            rangeMatch.Success
            && rangeMatch.Groups["prefix"].Value == rangeMatch.Groups["prefix2"].Value
        )
        {
            var prefix = rangeMatch.Groups["prefix"].Value;
            var start = int.Parse(rangeMatch.Groups["start"].Value, CultureInfo.InvariantCulture);
            var end = int.Parse(rangeMatch.Groups["end"].Value, CultureInfo.InvariantCulture);
            return ExpandNumericRange(prefix, start, end);
        }

        var arrayMatch = Regex.Match(
            value,
            @"^(?<name>[A-Za-z_][A-Za-z0-9_]*?)\[(?<start>\d+):(?<end>\d+)\]$"
        );
        if (arrayMatch.Success)
        {
            return ExpandNumericRange(
                arrayMatch.Groups["name"].Value + "[",
                int.Parse(arrayMatch.Groups["start"].Value, CultureInfo.InvariantCulture),
                int.Parse(arrayMatch.Groups["end"].Value, CultureInfo.InvariantCulture),
                "]"
            );
        }

        return [value];
    }

    private static List<string> ExpandNumericRange(
        string prefix,
        int start,
        int end,
        string suffix = ""
    )
    {
        var step = start <= end ? 1 : -1;
        var values = new List<string>();
        for (var current = start; ; current += step)
        {
            values.Add($"{prefix}{current}{suffix}");
            if (current == end)
            {
                return values;
            }
        }
    }

    private static IReadOnlyList<string> ParseTuple(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value
                .Trim('(', ')')
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool BelongsToESeries(string series, string numeric)
    {
        if (
            !int.TryParse(
                series[1..],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var steps
            )
        )
        {
            return false;
        }

        var normalized = Math.Abs(ParameterEvaluator.ParseNumeric(numeric));
        if (normalized <= 0)
        {
            return false;
        }

        while (normalized >= 10)
        {
            normalized /= 10;
        }

        while (normalized < 1)
        {
            normalized *= 10;
        }

        for (var i = 0; i < steps; i++)
        {
            var ideal = Math.Pow(10d, i / (double)steps);
            if (Math.Abs(normalized - ideal) / ideal < 0.0125d)
            {
                return true;
            }
        }

        return false;
    }

    private sealed record EffectivePartDefinition(
        string Name,
        bool IsAbstract,
        List<string> Implements,
        List<CircuitParameter> Parameters,
        List<string> Supplies,
        List<string> Grounds,
        List<PortDeclaration> Ports,
        List<PartCorner> Corners,
        PartCatalog Catalog,
        List<EffectivePartEntry> EffectiveEntries
    );

    private sealed record EffectivePartEntry(
        string? EntryName,
        Dictionary<string, string> Selection,
        PartCatalogBody Body,
        List<SelectionArgument> Excludes
    );
}
