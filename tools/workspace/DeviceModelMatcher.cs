using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cascode.Workspace;

public static class DeviceModelMatcher
{
    public static List<DeviceModelMatchRecord> Match(IReadOnlyList<Device> devices, IReadOnlyList<SpectreModel> models)
    {
        var result = new List<DeviceModelMatchRecord>();

        if (devices.Count == 0 || models.Count == 0) return result;

        var index = BuildModelIndex(models);

        foreach (var d in devices)
        {
            if (!d.HasLayout || !d.HasSymbol) continue;

            var dNorm = NormalizeDeviceName(d.CellName);
            var dBase = StripVtVddTokens(dNorm);
            var dVt = new HashSet<string>(d.VtTags ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var dVdd = new HashSet<string>(d.VddTags ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var dClass = d.Class;
            var dInfra = (d.Tags ?? Array.Empty<string>()).Any(t => t.Equals("infra", StringComparison.OrdinalIgnoreCase));

            var candidates = new Dictionary<ModelRef, int>(ModelRefComparer.Instance);

            // exact normalized name
            if (index.NameIndex.TryGetValue(dNorm, out var exactList)) foreach (var m in exactList) candidates[m] = ScoreCandidate(d, m, exact: true, baseMatch: true, vt: dVt, vdd: dVdd);

            // base match
            if (index.BaseIndex.TryGetValue(dBase, out var baseList)) foreach (var m in baseList) candidates[m] = ScoreCandidate(d, m, exact: false, baseMatch: true, vt: dVt, vdd: dVdd);

            // class fallback
            if (index.ClassIndex.TryGetValue(dClass, out var classList)) foreach (var m in classList) if (!candidates.ContainsKey(m)) candidates[m] = ScoreCandidate(d, m, exact: false, baseMatch: false, vt: dVt, vdd: dVdd);

            if (dInfra)
            {
                // lightly penalize non-ESD models for infra-tagged devices
                foreach (var kv in candidates.ToList())
                {
                    if (!kv.Key.Name.Contains("esd", StringComparison.OrdinalIgnoreCase)) candidates[kv.Key] = kv.Value - 5;
                }
            }

            var ordered = candidates
                .Where(kv => kv.Value >= 30) // threshold for acceptance
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (ordered.Count == 0)
            {
                continue; // unmatched; store absence in DB by not inserting
            }

            var top = ordered[0].Value;
            var ambiguousCut = top - 3;
            var topGroup = ordered.Where(kv => kv.Value >= ambiguousCut).ToList();

            var quality = ordered[0].Key.NormalizedName.Equals(dNorm, StringComparison.OrdinalIgnoreCase)
                ? "normalized_name"
                : (ordered[0].Key.BaseName.Equals(dBase, StringComparison.OrdinalIgnoreCase) ? "normalized_name" : "class_tags");

            for (var i = 0; i < topGroup.Count; i++)
            {
                var mr = topGroup[i].Key;
                var score = topGroup[i].Value;
                result.Add(new DeviceModelMatchRecord
                {
                    DeviceCanonicalName = d.CanonicalName,
                    ModelName = mr.Name,
                    Quality = topGroup.Count > 1 ? "ambiguous" : quality,
                    Rank = i,
                    Notes = $"score={score}; {(mr.IsSubckt ? "subckt" : "model")}; base={(mr.BaseName)}; norm={(mr.NormalizedName)}"
                });
            }
        }

        return result;
    }

    private static int ScoreCandidate(Device d, ModelRef m, bool exact, bool baseMatch, HashSet<string> vt, HashSet<string> vdd)
    {
        var score = 0;
        if (exact) score += 100;
        if (baseMatch && !exact) score += 50;
        if (d.Class == m.Class) score += 10;
        if (m.IsSubckt) score += 5;

        if (vt.Count > 0 && !string.IsNullOrWhiteSpace(m.Vt) && vt.Contains(m.Vt)) score += 20;
        if (vdd.Count > 0 && !string.IsNullOrWhiteSpace(m.Vdd) && vdd.Contains(m.VddToken)) score += 20;

        return score;
    }

    private static string NormalizeDeviceName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;
        var n = name.Trim().ToLowerInvariant();
        n = StripVendorPrefix(n);
        n = CollapseUnderscores(n);
        return n;
    }

    private static string NormalizeModelName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;
        var n = name.Trim().ToLowerInvariant();
        n = Regex.Replace(n, @"(?:__|_)(?:model(?:_base)?|base)(?:\.\d+)?$", "");
        n = StripVendorPrefix(n);
        n = CollapseUnderscores(n);
        return n;
    }

    private static string StripVtVddTokens(string value)
    {
        var n = value;
        n = Regex.Replace(n, @"_(ulvt|llvt|slvt|lvt|rvt|svt|nvt|hvt|mvt)\b", "");
        n = Regex.Replace(n, @"_\d+v\d+\b", "");
        n = CollapseUnderscores(n);
        return n;
    }

    private static string StripVendorPrefix(string n)
    {
        if (n.StartsWith("sky130_fd_pr__")) return n["sky130_fd_pr__".Length..];
        return n;
    }

    private static string CollapseUnderscores(string n)
    {
        while (n.Contains("__")) n = n.Replace("__", "_");
        return n.Trim('_');
    }

    private sealed class ModelIndex
    {
        public Dictionary<string, List<ModelRef>> NameIndex { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<ModelRef>> BaseIndex { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<DeviceClass, List<ModelRef>> ClassIndex { get; } = new();
    }

    private sealed record ModelRef(string Name, string NormalizedName, string BaseName, DeviceClass Class, string? Vt, string? Vdd, bool IsSubckt)
    {
        public string VddToken => ExtractVddToken(Vdd);

        private static string ExtractVddToken(string? v)
        {
            if (string.IsNullOrWhiteSpace(v)) return string.Empty;
            // normalize "1.8V" → "01v8" token form for comparison with device tags
            var lower = v.ToLowerInvariant();
            var m = Regex.Match(lower, @"(?<n>\d+)(?:\.(?<f>\d+))?v");
            if (!m.Success) return lower;
            var n = m.Groups["n"].Value;
            var f = m.Groups["f"].Success ? m.Groups["f"].Value : "0";
            return $"{n.PadLeft(2, '0')}v{f}";
        }
    }

    private sealed class ModelRefComparer : IEqualityComparer<ModelRef>
    {
        public static readonly ModelRefComparer Instance = new();
        public bool Equals(ModelRef? x, ModelRef? y) => string.Equals(x?.Name, y?.Name, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode(ModelRef obj) => StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name);
    }

    private static ModelIndex BuildModelIndex(IReadOnlyList<SpectreModel> models)
    {
        var index = new ModelIndex();
        foreach (var m in models)
        {
            var norm = NormalizeModelName(m.Name);
            var @base = StripVtVddTokens(norm);
            var mr = new ModelRef(m.Name, norm, @base, m.DeviceClass, m.ThresholdFlavor, m.VoltageDomain, string.Equals(m.ModelType, "subckt", StringComparison.OrdinalIgnoreCase));
            Add(index.NameIndex, norm, mr);
            Add(index.BaseIndex, @base, mr);
            if (!index.ClassIndex.TryGetValue(m.DeviceClass, out var list)) index.ClassIndex[m.DeviceClass] = list = new List<ModelRef>();
            list.Add(mr);
        }
        return index;
    }

    private static void Add(Dictionary<string, List<ModelRef>> map, string key, ModelRef value)
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = new List<ModelRef>();
        list.Add(value);
    }
}

