using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cascode.Workspace;

public sealed class DeviceFilterOptions
{
    public static readonly DeviceFilterOptions Empty = new();

    public DeviceFilterOptions(
        IEnumerable<string>? classes = null,
        IEnumerable<string>? vts = null,
        IEnumerable<string>? vdds = null,
        bool? infra = null,
        bool? matched = null,
        IEnumerable<string>? nameContains = null,
        IEnumerable<string>? nameExcludes = null
    )
    {
        Classes = new HashSet<string>(
            Normalize(classes, s => s.ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase
        );
        Vts = new HashSet<string>(
            Normalize(vts, s => s.ToUpperInvariant()),
            StringComparer.OrdinalIgnoreCase
        );
        Vdds = new HashSet<string>(Normalize(vdds, s => s), StringComparer.OrdinalIgnoreCase);
        Infra = infra;
        Matched = matched;
        NameContains = Normalize(nameContains, s => s).ToArray();
        NameExcludes = Normalize(nameExcludes, s => s).ToArray();
    }

    public HashSet<string> Classes { get; }
    public HashSet<string> Vts { get; }
    public HashSet<string> Vdds { get; }
    public bool? Infra { get; }
    public bool? Matched { get; }
    public IReadOnlyList<string> NameContains { get; }
    public IReadOnlyList<string> NameExcludes { get; }

    private static IEnumerable<string> Normalize(
        IEnumerable<string>? input,
        Func<string, string> normalize
    )
    {
        if (input is null)
            yield break;
        foreach (var s in input)
        {
            if (string.IsNullOrWhiteSpace(s))
                continue;
            yield return normalize(s.Trim());
        }
    }
}

public static class DeviceFilterEvaluator
{
    public static bool Matches(
        Device device,
        DeviceFilterOptions filters,
        HashSet<string>? matchedKeys = null
    )
    {
        if (
            filters.Classes.Count > 0
            && !filters.Classes.Contains(device.Class.ToString().ToLowerInvariant())
        )
            return false;
        if (filters.Vts.Count > 0 && !device.VtTags.Any(t => filters.Vts.Contains(t)))
            return false;
        if (filters.Vdds.Count > 0 && !MatchesVddFilters(device.VddTags, filters.Vdds))
            return false;
        if (filters.Infra.HasValue)
        {
            var isInfra = device.Tags.Any(t =>
                t.Equals("infra", StringComparison.OrdinalIgnoreCase)
            );
            if (filters.Infra.Value != isInfra)
                return false;
        }
        if (filters.Matched.HasValue)
        {
            if (matchedKeys is null)
                return false;
            var isMatched = matchedKeys.Contains(device.CanonicalName);
            if (filters.Matched.Value != isMatched)
                return false;
        }
        if (
            filters.NameContains.Count > 0
            && !filters.NameContains.Any(tok =>
                device.CellName.Contains(tok, StringComparison.OrdinalIgnoreCase)
            )
        )
            return false;
        if (
            filters.NameExcludes.Count > 0
            && filters.NameExcludes.Any(tok =>
                device.CellName.Contains(tok, StringComparison.OrdinalIgnoreCase)
            )
        )
            return false;
        return true;
    }

    public static bool TryNormalizeVddFilter(string raw, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var trimmed = raw.Trim();
        var lower = trimmed.ToLowerInvariant();
        if (VddFormatting.TryTokenToVolts(lower, out var fromToken))
        {
            normalized = VddFormatting.PrettyFromVolts(fromToken);
            return true;
        }

        if (lower.EndsWith("v", StringComparison.Ordinal))
        {
            lower = lower[..^1];
        }

        if (double.TryParse(lower, NumberStyles.Float, CultureInfo.InvariantCulture, out var volts))
        {
            normalized = VddFormatting.PrettyFromVolts(volts);
            return true;
        }

        return false;
    }

    private static bool MatchesVddFilters(
        IReadOnlyList<string> deviceVddTags,
        HashSet<string> filters
    )
    {
        if (filters.Count == 0)
            return true;
        if (deviceVddTags is null || deviceVddTags.Count == 0)
            return false;

        foreach (var tag in deviceVddTags)
        {
            if (TryNormalizeVddFilter(tag, out var normalized) && filters.Contains(normalized))
                return true;
            if (filters.Contains(tag))
                return true;
        }

        return false;
    }
}
