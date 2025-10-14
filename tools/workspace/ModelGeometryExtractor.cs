using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cascode.Workspace;

public sealed class ModelGeometry
{
    public string ModelName { get; init; } = string.Empty;
    public double? WMin { get; init; }
    public double? WMax { get; init; }
    public double? LMin { get; init; }
    public double? LMax { get; init; }
    public int? NfMin { get; init; }
    public int? NfMax { get; init; }
    public double? WDefault { get; init; }
    public double? LDefault { get; init; }
    public int? NfDefault { get; init; }
    public string Source { get; init; } = string.Empty; // subckt|model|mixed
    public string? Notes { get; init; }
}

public static class ModelGeometryExtractor
{
    public static List<ModelGeometry> Extract(IReadOnlyList<SpectreModel> models)
    {
        // Cache normalized file contents to avoid redundant reads
        var fileCache = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        var list = new List<ModelGeometry>();
        foreach (var m in models)
        {
            try
            {
                var paths = (m.SourceFiles ?? Array.Empty<string>()).Concat(m.Decks ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (paths.Count == 0) continue;
                double? wmin = null, wmax = null, lmin = null, lmax = null, wdef = null, ldef = null;
                int? nfmin = null, nfmax = null, nfdef = null;
                var gotModel = false; var gotSubckt = false;

                foreach (var path in paths)
                {
                    if (!File.Exists(path)) continue;

                    // Check cache first to avoid redundant file reads
                    if (!fileCache.TryGetValue(path, out var normalized))
                    {
                        normalized = ReadAndNormalizeFile(path);
                        fileCache[path] = normalized;
                    }

                    // Parse accumulated lines
                    bool inTargetSubckt = false;
                    foreach (var line in normalized)
                    {
                        var t = line.Trim();
                        if (string.IsNullOrWhiteSpace(t)) continue;
                        if (TryParseModelParams(t, m.Name, ref wmin, ref wmax, ref lmin, ref lmax)) gotModel = true;
                        if (IsSubcktStart(t, m.Name)) { inTargetSubckt = true; gotSubckt = true; TryParseSubcktDefaults(t, m.Name, ref wdef, ref ldef, ref nfdef); continue; }
                        if (inTargetSubckt)
                        {
                            if (IsSubcktEnd(t)) { inTargetSubckt = false; continue; }
                            if (t.StartsWith("param", StringComparison.OrdinalIgnoreCase) || t.StartsWith("parameters", StringComparison.OrdinalIgnoreCase))
                            {
                                TryParseParamLine(t, ref wdef, ref ldef, ref nfdef);
                            }
                        }
                    }
                }

                if (gotModel || gotSubckt)
                {
                    list.Add(new ModelGeometry
                    {
                        ModelName = m.Name,
                        WMin = wmin,
                        WMax = wmax,
                        LMin = lmin,
                        LMax = lmax,
                        NfMin = nfmin,
                        NfMax = nfmax,
                        WDefault = wdef,
                        LDefault = ldef,
                        NfDefault = nfdef,
                        Source = gotModel && gotSubckt ? "mixed" : (gotModel ? "model" : "subckt"),
                        Notes = null
                    });
                }
            }
            catch
            {
                // ignore per-model errors; best effort
            }
        }
        return list;
    }

    private static List<string> ReadAndNormalizeFile(string path)
    {
        var lines = File.ReadAllLines(path);
        // Join continuation lines ending with '\' or starting with '+'
        var normalized = new List<string>();
        var acc = new StringBuilder();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("*")) continue;
            if (line.StartsWith("+")) { acc.Append(' ').Append(line[1..].Trim()); continue; }
            if (acc.Length > 0) { normalized.Add(acc.ToString()); acc.Clear(); }
            if (line.EndsWith("\\")) { acc.Append(line[..^1].Trim()); continue; }
            normalized.Add(line);
        }
        if (acc.Length > 0) { normalized.Add(acc.ToString()); acc.Clear(); }
        return normalized;
    }

    private static bool TryParseModelParams(string line, string modelName, ref double? wmin, ref double? wmax, ref double? lmin, ref double? lmax)
    {
        // .model <name> <type> params...
        if (!Regex.IsMatch(line, @$"^\.?model\s+{Regex.Escape(modelName)}\b", RegexOptions.IgnoreCase)) return false;
        var idx = line.IndexOf(modelName, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return false;
        var rest = line[(idx + modelName.Length)..];
        var tokens = SplitArgs(rest);
        foreach (var tok in tokens)
        {
            if (TryParseAssign(tok, "wmin", out var v)) wmin = ParseSi(v);
            else if (TryParseAssign(tok, "wmax", out v)) wmax = ParseSi(v);
            else if (TryParseAssign(tok, "lmin", out v)) lmin = ParseSi(v);
            else if (TryParseAssign(tok, "lmax", out v)) lmax = ParseSi(v);
        }
        return true;
    }

    private static bool TryParseSubcktDefaults(string line, string subcktName, ref double? wdef, ref double? ldef, ref int? nfdef)
    {
        if (!Regex.IsMatch(line, @$"^\.?subckt\s+{Regex.Escape(subcktName)}\b", RegexOptions.IgnoreCase)) return false;
        var idx = line.IndexOf(subcktName, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return false;
        var rest = line[(idx + subcktName.Length)..];
        var tokens = SplitArgs(rest);
        foreach (var tok in tokens)
        {
            if (TryParseAssign(tok, "w", out var w)) wdef = ParseSi(w);
            else if (TryParseAssign(tok, "l", out var l)) ldef = ParseSi(l);
            else if (TryParseAssign(tok, "nf", out var nf)) nfdef = ParseInt(nf);
        }
        return true;
    }

    private static bool IsSubcktStart(string line, string subcktName)
        => Regex.IsMatch(line, @$"^\.?subckt\s+{Regex.Escape(subcktName)}\b", RegexOptions.IgnoreCase);

    private static bool IsSubcktEnd(string line)
        => Regex.IsMatch(line, @"^\.(ends|end)\b", RegexOptions.IgnoreCase);

    private static void TryParseParamLine(string line, ref double? wdef, ref double? ldef, ref int? nfdef)
    {
        foreach (var tok in SplitArgs(line))
        {
            if (TryParseAssign(tok, "w", out var w)) wdef = ParseSi(w) ?? wdef;
            else if (TryParseAssign(tok, "l", out var l)) ldef = ParseSi(l) ?? ldef;
            else if (TryParseAssign(tok, "nf", out var nf)) nfdef = ParseInt(nf) ?? nfdef;
        }
    }

    private static IEnumerable<string> SplitArgs(string s)
    {
        return s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool TryParseAssign(string token, string key, out string value)
    {
        value = string.Empty;
        var eq = token.IndexOf('=');
        if (eq <= 0) return false;
        var lhs = token[..eq].Trim();
        if (!lhs.Equals(key, StringComparison.OrdinalIgnoreCase)) return false;
        value = token[(eq + 1)..].Trim().Trim('"', '\'');
        return value.Length > 0;
    }

    private static double? ParseSi(string raw)
    {
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var val)) return val;
        var lower = raw.Trim().ToLowerInvariant();
        double scale = 1;
        if (lower.EndsWith("m")) { scale = 1e-3; lower = lower[..^1]; }
        else if (lower.EndsWith("u")) { scale = 1e-6; lower = lower[..^1]; }
        else if (lower.EndsWith("n")) { scale = 1e-9; lower = lower[..^1]; }
        else if (lower.EndsWith("p")) { scale = 1e-12; lower = lower[..^1]; }
        else if (lower.EndsWith("f")) { scale = 1e-15; lower = lower[..^1]; }
        if (double.TryParse(lower, NumberStyles.Float, CultureInfo.InvariantCulture, out var baseVal)) return baseVal * scale;
        return null;
    }

    private static int? ParseInt(string raw)
    {
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return v;
        if (double.TryParse(raw, NumberStyles.Float, CultureBox, out var vf)) return (int)Math.Round(vf);
        return null;
    }

    private static readonly CultureInfo CultureBox = CultureInfo.InvariantCulture;
}
