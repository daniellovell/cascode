using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cascode.Language.Validation;

internal static partial class PartSemanticChecker
{
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

        for (var index = 0; index < steps; index++)
        {
            var ideal = Math.Pow(10d, index / (double)steps);
            if (Math.Abs(normalized - ideal) / ideal < 0.0125d)
            {
                return true;
            }
        }

        return false;
    }
}
