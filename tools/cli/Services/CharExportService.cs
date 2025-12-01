using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Cascode.Cli.Services;

internal static class CharExportService
{
    // Exports derived.csv from results.csv (or attempts to build results from Spectre outputs when missing).
    // Returns true on success. Provides an output message and the derived file path.
    internal static bool ExportDerived(string jobDir, HashSet<string>? metricFilter, out string outFile, out string message)
    {
        outFile = Path.Combine(jobDir, "derived.csv");
        try
        {
            var csv = Path.Combine(jobDir, "results.csv");
            if (!File.Exists(csv))
            {
                // Attempt to synthesize results.csv from oppoint.* or Spectre nutascii/raw
                if (!TryExportFromOppointFiles(jobDir, out var buildMsg))
                {
                    // Fallback: try to parse Spectre outputs (nutascii/raw)
                    TryBuildResultsCsvFromNutascii(jobDir, out buildMsg);
                }
                message = buildMsg;
            }

            csv = Path.Combine(jobDir, "results.csv");
            if (!File.Exists(csv))
            {
                message = $"Results file not found: {csv}";
                return false;
            }

            var lines = File.ReadAllLines(csv);
            if (lines.Length == 0)
            {
                message = "Empty results file.";
                return false;
            }

            static bool TryParseInvariant(string? text, out double value)
            {
                value = double.NaN;
                if (string.IsNullOrWhiteSpace(text)) return false;
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }

            static string Format(double value)
            {
                if (double.IsNaN(value)) return string.Empty;
                return value.ToString("G", CultureInfo.InvariantCulture);
            }

            // Column map
            var header = lines[0].Split(',', StringSplitOptions.TrimEntries);
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < header.Length; i++) map[header[i]] = i;

            string controlLabel = FindFirstColumn(map, "vgs", "vsg") ?? "vgs";
            var iControl = map.TryGetValue(controlLabel, out var idx) ? idx : -1;
            var iVd = map.TryGetValue("vd", out var iv) ? iv : (map.TryGetValue("vds", out iv) ? iv : -1);
            var iId = map.TryGetValue("id", out var ii) ? ii : (map.TryGetValue("ids", out ii) ? ii : -1);
            var iGm = map.TryGetValue("gm", out var igm) ? igm : -1;
            var iGmbs = map.TryGetValue("gmbs", out var igmbs) ? igmbs : -1;
            var iGds = map.TryGetValue("gds", out var igds) ? igds : -1;
            var iVth = map.TryGetValue("vth", out var ivth) ? ivth : -1;
            var iVdsat = map.TryGetValue("vdsat", out var ivsat) ? ivsat : -1;
            var iCgs = map.TryGetValue("cgs", out var icgs) ? icgs : -1;
            var iCgd = map.TryGetValue("cgd", out var icgd) ? icgd : -1;
            var iCgg = map.TryGetValue("cgg", out var icgg) ? icgg : -1;
            var iGmOverId = map.TryGetValue("gmoverid", out var igmoi) ? igmoi : (map.TryGetValue("gm_over_id", out igmoi) ? igmoi : -1);
            var iUeff = map.TryGetValue("ueff", out var iueff) ? iueff : -1;
            var iRon = map.TryGetValue("ron", out var iron) ? iron : -1;
            var iRseff = map.TryGetValue("rseff", out var irseff) ? irseff : -1;
            var iRdeff = map.TryGetValue("rdeff", out var irdeff) ? irdeff : -1;
            var iWeff = map.TryGetValue("w_eff", out var iweff) ? iweff : -1;

            var exportRows = new List<(double Control, double Vds, double Id, double Gm, double Gmbs, double Gds, double Vth, double Vdsat, double Cgs, double Cgd, double Cgg, double GmOverIdRaw, double Ueff, double Ron, double RsEff, double RdEff, double Weff)>();
            for (int i = 1; i < lines.Length; i++)
            {
                var parts = lines[i].Split(',', StringSplitOptions.None);
                var control = (iControl >= 0 && iControl < parts.Length && TryParseInvariant(parts[iControl], out var vControl)) ? vControl : double.NaN;
                var vds = (iVd >= 0 && iVd < parts.Length && TryParseInvariant(parts[iVd], out var vVds)) ? vVds : double.NaN;
                var id = (iId >= 0 && iId < parts.Length && TryParseInvariant(parts[iId], out var vId)) ? vId : double.NaN;
                var gm = (iGm >= 0 && iGm < parts.Length && TryParseInvariant(parts[iGm], out var vGm)) ? vGm : double.NaN;
                var gmbs = (iGmbs >= 0 && iGmbs < parts.Length && TryParseInvariant(parts[iGmbs], out var vGmbs)) ? vGmbs : double.NaN;
                var gds = (iGds >= 0 && iGds < parts.Length && TryParseInvariant(parts[iGds], out var vGds)) ? vGds : double.NaN;
                var vth = (iVth >= 0 && iVth < parts.Length && TryParseInvariant(parts[iVth], out var vVth)) ? vVth : double.NaN;
                var vdsat = (iVdsat >= 0 && iVdsat < parts.Length && TryParseInvariant(parts[iVdsat], out var vVdsat)) ? vVdsat : double.NaN;
                var cgs = (iCgs >= 0 && iCgs < parts.Length && TryParseInvariant(parts[iCgs], out var vCgs)) ? vCgs : double.NaN;
                var cgd = (iCgd >= 0 && iCgd < parts.Length && TryParseInvariant(parts[iCgd], out var vCgd)) ? vCgd : double.NaN;
                var cgg = (iCgg >= 0 && iCgg < parts.Length && TryParseInvariant(parts[iCgg], out var vCgg)) ? vCgg : double.NaN;
                var gmOverId = (iGmOverId >= 0 && iGmOverId < parts.Length && TryParseInvariant(parts[iGmOverId], out var vGmOverId)) ? vGmOverId : double.NaN;
                var ueff = (iUeff >= 0 && iUeff < parts.Length && TryParseInvariant(parts[iUeff], out var vUeff)) ? vUeff : double.NaN;
                var ron = (iRon >= 0 && iRon < parts.Length && TryParseInvariant(parts[iRon], out var vRon)) ? vRon : double.NaN;
                var rseff = (iRseff >= 0 && iRseff < parts.Length && TryParseInvariant(parts[iRseff], out var vRsEff)) ? vRsEff : double.NaN;
                var rdeff = (iRdeff >= 0 && iRdeff < parts.Length && TryParseInvariant(parts[iRdeff], out var vRdEff)) ? vRdEff : double.NaN;
                var weff = (iWeff >= 0 && iWeff < parts.Length && TryParseInvariant(parts[iWeff], out var vWeff)) ? vWeff : double.NaN;
                exportRows.Add((control, vds, id, gm, gmbs, gds, vth, vdsat, cgs, cgd, cgg, gmOverId, ueff, ron, rseff, rdeff, weff));
            }

            if (exportRows.Count == 0)
            {
                message = "No numeric samples parsed from results.";
                return false;
            }

            // Derivations
            ExportRow? FindNeighbor(int index, int step)
            {
                for (int j = index + step; j >= 0 && j < exportRows.Count; j += step)
                {
                    var candidate = exportRows[j];
                    if (!double.IsNaN(candidate.Control) && !double.IsNaN(candidate.Id)) return new ExportRow(candidate);
                }
                return null;
            }

            var rows = exportRows.Select(er => new ExportRow(er)).ToList();
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (double.IsNaN(row.Gm))
                {
                    if (!double.IsNaN(row.GmOverIdRaw) && !double.IsNaN(row.Id)) row.Gm = row.GmOverIdRaw * row.Id;
                    else
                    {
                        var prev = FindNeighbor(i, -1);
                        var next = FindNeighbor(i, +1);
                        if (prev is not null && next is not null)
                        {
                            var dv = next.Control - prev.Control;
                            if (Math.Abs(dv) > 1e-30) row.Gm = (next.Id - prev.Id) / dv;
                        }
                    }
                }
                if (double.IsNaN(row.GmOverIdRaw) && !double.IsNaN(row.Gm) && Math.Abs(row.Id) > 0) row.GmOverIdRaw = row.Gm / row.Id;
            }

            bool Wants(string metric) => metricFilter is null || metricFilter.Count == 0 || metricFilter.Contains(metric);
            var optionalMetricOrder = new[] { "gm", "gmbs", "gds", "ro", "gm_over_id", "gm_ro", "vstar", "cgs", "cgd", "cgg", "gm_per_w", "id_per_w", "ft", "vth", "vdsat", "gmoverid", "ueff", "ron", "rseff", "rdeff", "w_eff" };
            var headerOut = new List<string> { controlLabel, "vds", "id" };
            foreach (var metric in optionalMetricOrder) if (Wants(metric)) headerOut.Add(metric);
            var outLines = new List<string> { string.Join(',', headerOut) };

            foreach (var row in rows)
            {
                var gm = row.Gm;
                var gds = row.Gds;
                var gmOverId = !double.IsNaN(row.GmOverIdRaw) ? row.GmOverIdRaw : (!double.IsNaN(row.Id) && Math.Abs(row.Id) > 0 ? row.Gm / row.Id : double.NaN);
                var ro = (!double.IsNaN(gds) && Math.Abs(gds) > 1e-30) ? 1.0 / gds : (!double.IsNaN(row.Ron) ? row.Ron : double.NaN);
                var gmRo = (!double.IsNaN(gm) && !double.IsNaN(ro)) ? gm * ro : double.NaN;
                var vstar = (!double.IsNaN(gm) && Math.Abs(gm) > 1e-30) ? (2.0 * row.Id) / gm : double.NaN;

                double totalCap = 0.0;
                if (!double.IsNaN(row.Cgs)) totalCap += Math.Abs(row.Cgs);
                if (!double.IsNaN(row.Cgd)) totalCap += Math.Abs(row.Cgd);
                var ft = (totalCap > 0 && !double.IsNaN(gm)) ? Math.Abs(gm) / (2.0 * Math.PI * totalCap) : double.NaN;
                var gmPerW = (!double.IsNaN(gm) && row.Weff > 0) ? gm / row.Weff : double.NaN;
                var idPerW = (!double.IsNaN(row.Id) && row.Weff > 0) ? row.Id / row.Weff : double.NaN;

                var metrics = new Dictionary<string, double>
                {
                    ["gm"] = gm,
                    ["gmbs"] = row.Gmbs,
                    ["gds"] = gds,
                    ["ro"] = ro,
                    ["gm_over_id"] = gmOverId,
                    ["gm_ro"] = gmRo,
                    ["vstar"] = vstar,
                    ["cgs"] = row.Cgs,
                    ["cgd"] = row.Cgd,
                    ["cgg"] = row.Cgg,
                    ["gm_per_w"] = gmPerW,
                    ["id_per_w"] = idPerW,
                    ["ft"] = ft,
                    ["vth"] = row.Vth,
                    ["vdsat"] = row.Vdsat,
                    ["gmoverid"] = row.GmOverIdRaw,
                    ["ueff"] = row.Ueff,
                    ["ron"] = row.Ron,
                    ["rseff"] = row.RsEff,
                    ["rdeff"] = row.RdEff,
                    ["w_eff"] = row.Weff
                };

                var rowValues = new List<string> { Format(row.Control), Format(row.Vds), Format(row.Id) };
                foreach (var metric in optionalMetricOrder)
                {
                    if (!Wants(metric)) continue;
                    metrics.TryGetValue(metric, out var val);
                    rowValues.Add(Format(val));
                }
                outLines.Add(string.Join(',', rowValues));
            }

            File.WriteAllLines(outFile, outLines);
            message = $"Exported derived metrics → {outFile}";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Export failed: {ex.Message}";
            return false;
        }
    }

    private static string? FindFirstColumn(IReadOnlyDictionary<string, int> map, params string[] names)
    {
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (map.ContainsKey(name)) return name;
        }
        return null;
    }

    private static bool TryExportFromOppointFiles(string jobDir, out string message)
    {
        var csvPath = Path.Combine(jobDir, "results.csv");
        message = string.Empty;
        try
        {
            var oppFiles = Directory.EnumerateFiles(jobDir, "oppoint.*", SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
            if (oppFiles.Length == 0) return false;
            var elemFiles = Directory.EnumerateFiles(jobDir, "elem.*", SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();

            var rows = new List<string>();
            rows.Add(string.Join(',', new[] { "vgs", "vd", "id", "gm", "gmbs", "gds", "vth", "vdsat", "cgs", "cgd", "cgg", "gmoverid", "ueff", "ron", "rseff", "rdeff", "w_eff" }));

            string? detectedInst = null;
            for (int n = 0; n < oppFiles.Length; n++)
            {
                if (!TryParseOppointAscii(oppFiles[n], detectedInst, out var op, out var matchedInst)) continue;
                if (!string.IsNullOrWhiteSpace(matchedInst)) detectedInst = matchedInst;

                double control = GetOrNaN(op, "vgs", "vsg");
                double vds = GetOrNaN(op, "vds", "vd");
                double id = GetOrNaN(op, "ids", "id");
                double gm = GetOrNaN(op, "gm");
                double gmbs = GetOrNaN(op, "gmbs");
                double gds = GetOrNaN(op, "gds");
                double vth = GetOrNaN(op, "vth");
                double vdsat = GetOrNaN(op, "vdsat");
                double cgs = GetOrNaN(op, "cgs");
                double cgd = GetOrNaN(op, "cgd");
                double cgg = GetOrNaN(op, "cgg");
                double gmOverId = GetOrNaN(op, "gmoverid", "gm_over_id", "gm/id");
                double ueff = GetOrNaN(op, "ueff");
                double ron = GetOrNaN(op, "ron");
                double rseff = GetOrNaN(op, "rseff");
                double rdeff = GetOrNaN(op, "rdeff");
                double weff = GetOrNaN(op, "w_eff");

                rows.Add(string.Join(',', new[] { control, vds, id, gm, gmbs, gds, vth, vdsat, cgs, cgd, cgg, gmOverId, ueff, ron, rseff, rdeff, weff }.Select(Format)));
            }

            if (rows.Count <= 1)
            {
                message = "oppoint files parsed but no numeric rows assembled.";
                return false;
            }
            File.WriteAllLines(csvPath, rows);
            message = $"Built results.csv from per-step oppoint files ({rows.Count - 1} samples).";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Failed to build results from oppoint files: {ex.Message}";
            return false;
        }

        static double GetOrNaN(Dictionary<string, double> dict, params string[] keys)
        {
            foreach (var k in keys) if (dict.TryGetValue(k, out var v)) return v; return double.NaN;
        }
        static string Format(double value) => double.IsNaN(value) ? string.Empty : value.ToString("G", CultureInfo.InvariantCulture);
    }

    private static bool TryBuildResultsCsvFromNutascii(string jobDir, out string message)
    {
        try
        {
            var rawCandidates = Directory.EnumerateFiles(jobDir, "*.raw", SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateFiles(jobDir, "*.nutascii", SearchOption.TopDirectoryOnly))
                .ToArray();
            if (rawCandidates.Length == 0)
            {
                message = "No Spectre raw or nutascii outputs found.";
                return false;
            }

            string chosen = rawCandidates.OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc).First();
            var lines = File.ReadAllLines(chosen);
            if (lines.Length == 0) { message = "Empty raw/nutascii file."; return false; }

            int varCount = 0;
            int pointCount = 0;
            int headerEnd = -1;
            int rowWidth = 0;
            var names = new List<string>();
            var nums = new List<double>();

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (headerEnd < 0)
                {
                    if (line.StartsWith("No. Variables", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        if (parts.Length == 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedVars))
                        {
                            varCount = parsedVars;
                        }
                    }
                    if (line.StartsWith("No. Points", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        if (parts.Length == 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPts))
                        {
                            pointCount = parsedPts;
                        }
                    }
                    if (line.StartsWith("Variables:", StringComparison.OrdinalIgnoreCase))
                    {
                        var tokensInline = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (tokensInline.Length >= 3)
                        {
                            names.Add(tokensInline[2]);
                        }
                        i++;
                        for (; i < lines.Length; i++)
                        {
                            var l = lines[i];
                            if (string.IsNullOrWhiteSpace(l)) continue;
                            if (l.StartsWith("Values:", StringComparison.OrdinalIgnoreCase)) break;
                            var parts = l.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 3)
                            {
                                var name = parts[1];
                                if (string.IsNullOrWhiteSpace(name) && parts.Length >= 3) name = parts[2];
                                names.Add(name);
                            }
                        }
                        headerEnd = i;
                        continue;
                    }
                }

                if (headerEnd >= 0)
                {
                    if (line.StartsWith("Values:", StringComparison.OrdinalIgnoreCase))
                    {
                        rowWidth = varCount > 0 ? varCount : names.Count;
                        continue;
                    }
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var p in parts)
                    {
                        if (double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) nums.Add(v);
                    }
                }
            }

            if (varCount > 0 && names.Count < varCount)
            {
                if (!names.Contains("dc", StringComparer.OrdinalIgnoreCase))
                {
                    names.Insert(0, "dc");
                }
                while (names.Count < varCount)
                {
                    names.Insert(0, $"var{varCount - names.Count}");
                }
            }

            int findNode(string vname, string plain)
            {
                for (int j = 0; j < names.Count; j++)
                {
                    var nm = names[j];
                    if (nm.Equals(vname, StringComparison.OrdinalIgnoreCase) || nm.Equals(plain, StringComparison.OrdinalIgnoreCase))
                        return j;
                }
                return -1;
            }

            var ig = findNode("vgs", "g");
            if (ig < 0) ig = findNode("vsg", "vsg");
            var isrc = findNode("vs", "s");
            var idn = findNode("vd", "d");
            int iVdr = -1;
            for (int k = 0; k < names.Count; k++)
            {
                if (names[k].IndexOf("vdr", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    iVdr = k;
                    break;
                }
            }

            if (ig < 0 || isrc < 0 || idn < 0)
            {
                message = "Required variables v(g), v(s), v(d) not found in raw output.";
                return false;
            }

            if (rowWidth <= 0)
            {
                rowWidth = names.Count;
            }

            if (rowWidth <= 0)
            {
                message = "No variable definitions found in Spectre output.";
                return false;
            }

            var stride = rowWidth;
            var skipIndex = false;

            if (pointCount > 0 && nums.Count >= pointCount * (rowWidth + 1))
            {
                stride = rowWidth + 1;
                skipIndex = true;
            }
            else if (nums.Count % (rowWidth + 1) == 0)
            {
                stride = rowWidth + 1;
                skipIndex = true;
            }

            var points = stride > 0 ? nums.Count / stride : 0;
            if (pointCount > 0) points = Math.Min(points, pointCount);
            if (points <= 0)
            {
                message = "Not enough numeric samples parsed from Spectre raw output.";
                return false;
            }

            double ValueAt(int baseIdx, int col)
            {
                var idx = baseIdx + col;
                return idx >= 0 && idx < nums.Count ? nums[idx] : double.NaN;
            }

            var dataOffset = skipIndex ? 1 : 0;
            var sb = new List<string> { "vgs,vd,id" };
            for (int p = 0; p < points; p++)
            {
                int baseIdx = p * stride + dataOffset;
                if (baseIdx + rowWidth > nums.Count) break;

                double vg = ValueAt(baseIdx, ig);
                double vs = ValueAt(baseIdx, isrc);
                double vd = ValueAt(baseIdx, idn);
                double cur = iVdr >= 0 ? ValueAt(baseIdx, iVdr) : double.NaN;
                var id = double.IsNaN(cur) ? double.NaN : -cur;
                var vgs = vg - vs;
                sb.Add(string.Join(',', vgs.ToString(CultureInfo.InvariantCulture), vd.ToString(CultureInfo.InvariantCulture), id.ToString(CultureInfo.InvariantCulture)));
            }
            var outCsv = Path.Combine(jobDir, "results.csv");
            File.WriteAllLines(outCsv, sb);
            message = $"Recovered results.csv from Spectre raw: {Path.GetFileName(chosen)}";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Failed to recover results from Spectre raw: {ex.Message}";
            return false;
        }
    }

    private static bool TryParseOppointAscii(string path, string? expectedInst, out Dictionary<string, double> values, out string matchedInst)
    {
        values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        matchedInst = string.Empty;
        try
        {
            var lines = File.ReadAllLines(path);
            string? currentInst = null;
            bool inInstance = false;
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (inInstance) break;
                    continue;
                }

                if (line.StartsWith("Instance:", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("Element name =", StringComparison.OrdinalIgnoreCase))
                {
                    var inst = line.Contains('=') ? line.Split('=', 2)[1].Trim() : line.Split(':', 2)[1].Trim();
                    if (!string.IsNullOrWhiteSpace(expectedInst) && !string.Equals(inst, expectedInst, StringComparison.OrdinalIgnoreCase))
                    {
                        if (inInstance) break;
                        continue;
                    }
                    currentInst = inst;
                    matchedInst = currentInst;
                    values.Clear();
                    inInstance = true;
                    continue;
                }

                if (!inInstance) continue;

                var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2) continue;
                var key = parts[0].Trim(':', ' ');
                var valText = parts[1].Trim();
                if (TryParseWithUnit(valText, out var parsed))
                {
                    values[key] = parsed;
                }
                else if (double.TryParse(valText, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                {
                    values[key] = v;
                }
            }
            return values.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static void ReadOppointValues(string[] lines, ref int i, Dictionary<string, double> values)
    {
        for (; i < lines.Length; i++)
        {
            var l = lines[i];
            if (string.IsNullOrWhiteSpace(l)) continue;
            var parts = l.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return;
            var name = parts[0].Trim(':');
            if (!double.TryParse(parts[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
            {
                if (TryParseWithUnit(parts[^1], out val))
                {
                    values[name] = val;
                    continue;
                }
                return;
            }
            values[name] = val;
        }
    }

    private static bool TryParseWithUnit(string text, out double value)
    {
        value = double.NaN;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        // split numeric prefix and optional unit suffix
        int idx = text.Length - 1;
        while (idx >= 0 && (char.IsLetter(text[idx]) || text[idx] == 'Ω' || text[idx] == 'µ')) idx--;
        if (idx < 0) return false;
        var num = text.Substring(0, idx + 1);
        var unit = text[(idx + 1)..];
        if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var baseVal)) return false;
        value = baseVal * UnitMultiplier(unit);
        return true;
    }

    private static double UnitMultiplier(string unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return 1.0;
        unit = unit.Trim();
        if (unit.Length == 1)
        {
            return PrefixMultiplier(unit[0]);
        }
        // look at first char as SI prefix and ignore trailing letters (e.g., Ohm symbol)
        return PrefixMultiplier(unit[0]);
    }

    private static double PrefixMultiplier(char p)
        => p switch
        {
            'T' => 1e12,
            'G' => 1e9,
            'M' => 1e6,
            'k' => 1e3,
            'm' => 1e-3,
            'u' or 'µ' => 1e-6,
            'n' => 1e-9,
            'p' => 1e-12,
            'f' => 1e-15,
            'a' => 1e-18,
            _ => 1.0
        };

    private sealed class ExportRow
    {
        public ExportRow((double Control, double Vds, double Id, double Gm, double Gmbs, double Gds, double Vth, double Vdsat, double Cgs, double Cgd, double Cgg, double GmOverIdRaw, double Ueff, double Ron, double RsEff, double RdEff, double Weff) t)
        {
            Control = t.Control; Vds = t.Vds; Id = t.Id; Gm = t.Gm; Gmbs = t.Gmbs; Gds = t.Gds; Vth = t.Vth; Vdsat = t.Vdsat; Cgs = t.Cgs; Cgd = t.Cgd; Cgg = t.Cgg; GmOverIdRaw = t.GmOverIdRaw; Ueff = t.Ueff; Ron = t.Ron; RsEff = t.RsEff; RdEff = t.RdEff; Weff = t.Weff;
        }
        public double Control, Vds, Id, Gm, Gmbs, Gds, Vth, Vdsat, Cgs, Cgd, Cgg, GmOverIdRaw, Ueff, Ron, RsEff, RdEff, Weff;
    }
}
