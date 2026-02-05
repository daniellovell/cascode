using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Cascode.Cli.Services;

internal static class CharExportService
{
    private sealed record RawRow(int SourceLine, double Vgs, double Vds, double Id);

    internal static bool ExportDerived(
        string jobDir,
        HashSet<string>? metricFilter,
        out string outFile,
        out string message
    )
    {
        outFile = Path.Combine(jobDir, "derived.csv");
        try
        {
            var csvPath = Path.Combine(jobDir, "results.csv");
            if (!File.Exists(csvPath))
            {
                message = $"Results file not found: {csvPath}";
                return false;
            }

            var lines = File.ReadAllLines(csvPath);
            if (lines.Length == 0)
            {
                message = "Empty results file.";
                return false;
            }

            var header = lines[0].Split(',', StringSplitOptions.TrimEntries);
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < header.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(header[i]))
                {
                    map[header[i]] = i;
                }
            }

            static bool TryParseInvariant(string? text, out double value)
            {
                value = double.NaN;
                if (string.IsNullOrWhiteSpace(text))
                {
                    return false;
                }

                return double.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value
                );
            }

            static double Get(IReadOnlyList<string> parts, int idx)
            {
                if (idx < 0 || idx >= parts.Count)
                {
                    return double.NaN;
                }

                return TryParseInvariant(parts[idx], out var v) ? v : double.NaN;
            }

            bool Wants(string name) =>
                metricFilter is null || metricFilter.Count == 0 || metricFilter.Contains(name);

            var iControl = FindFirstColumn(map, "vgs", "vsg");
            var iVds = FindFirstColumn(map, "vds", "vd");
            var iId = FindFirstColumn(map, "id", "ids");
            var iGm = FindFirstColumn(map, "gm");
            var iGds = FindFirstColumn(map, "gds");
            var iVth = FindFirstColumn(map, "vth");
            var iVdsat = FindFirstColumn(map, "vdsat");
            var iCgs = FindFirstColumn(map, "cgs");
            var iCgd = FindFirstColumn(map, "cgd");
            var iCgg = FindFirstColumn(map, "cgg");
            var iGmOverId = FindFirstColumn(map, "gmoverid", "gm_over_id");

            if (iControl is null || iVds is null || iId is null)
            {
                message = "results.csv is missing required columns (need vgs/vsg, vds/vd, id/ids).";
                return false;
            }

            var outLines = new List<string>();
            var headerOut = new List<string> { "vgs", "vds", "id" };

            var optional = new[]
            {
                "gm",
                "gds",
                "gm_over_id",
                "ro",
                "gm_ro",
                "vstar",
                "ft",
                "vth",
                "vdsat",
                "cgs",
                "cgd",
                "cgg",
            };
            foreach (var col in optional)
            {
                if (Wants(col))
                {
                    headerOut.Add(col);
                }
            }

            outLines.Add(string.Join(',', headerOut));

            const double TwoPi = 2.0 * Math.PI;

            // If the simulation did not provide gm directly, approximate it from the Id vs Vgs sweep.
            // This supports PDKs whose transistor wrappers are subckts (no stable internal device to query).
            Dictionary<int, double>? computedGmBySourceLine = null;
            if (iGm is null && Wants("gm"))
            {
                var raw = new List<RawRow>(Math.Max(0, lines.Length - 1));
                for (var row = 1; row < lines.Length; row++)
                {
                    if (string.IsNullOrWhiteSpace(lines[row]))
                    {
                        continue;
                    }

                    var parts = lines[row].Split(',', StringSplitOptions.TrimEntries).ToList();
                    var vgs = Get(parts, iControl.Value);
                    var vds = Get(parts, iVds.Value);
                    var id = Get(parts, iId.Value);
                    if (double.IsNaN(vgs) || double.IsNaN(id))
                    {
                        continue;
                    }
                    raw.Add(new RawRow(row, vgs, vds, id));
                }

                if (raw.Count >= 2)
                {
                    var ordered = raw.OrderBy(r => r.Vgs).ToList();
                    computedGmBySourceLine = new Dictionary<int, double>();
                    for (var i = 0; i < ordered.Count; i++)
                    {
                        static double Deriv(double x0, double y0, double x1, double y1)
                        {
                            var dx = x1 - x0;
                            if (Math.Abs(dx) < 1e-30)
                            {
                                return double.NaN;
                            }
                            return (y1 - y0) / dx;
                        }

                        double gm;
                        if (i == 0)
                        {
                            gm = Deriv(
                                ordered[0].Vgs,
                                ordered[0].Id,
                                ordered[1].Vgs,
                                ordered[1].Id
                            );
                        }
                        else if (i == ordered.Count - 1)
                        {
                            gm = Deriv(
                                ordered[^2].Vgs,
                                ordered[^2].Id,
                                ordered[^1].Vgs,
                                ordered[^1].Id
                            );
                        }
                        else
                        {
                            gm = Deriv(
                                ordered[i - 1].Vgs,
                                ordered[i - 1].Id,
                                ordered[i + 1].Vgs,
                                ordered[i + 1].Id
                            );
                        }

                        computedGmBySourceLine[ordered[i].SourceLine] = gm;
                    }
                }
            }

            for (var row = 1; row < lines.Length; row++)
            {
                if (string.IsNullOrWhiteSpace(lines[row]))
                {
                    continue;
                }

                var parts = lines[row].Split(',', StringSplitOptions.TrimEntries).ToList();
                var vgs = Get(parts, iControl.Value);
                var vds = Get(parts, iVds.Value);
                var id = Get(parts, iId.Value);

                var gm = iGm is not null
                    ? Get(parts, iGm.Value)
                    : (
                        computedGmBySourceLine is not null
                        && computedGmBySourceLine.TryGetValue(row, out var gmDerived)
                            ? gmDerived
                            : double.NaN
                    );
                var gds = iGds is null ? double.NaN : Get(parts, iGds.Value);
                var vth = iVth is null ? double.NaN : Get(parts, iVth.Value);
                var vdsat = iVdsat is null ? double.NaN : Get(parts, iVdsat.Value);
                var cgs = iCgs is null ? double.NaN : Get(parts, iCgs.Value);
                var cgd = iCgd is null ? double.NaN : Get(parts, iCgd.Value);
                var cgg = iCgg is null ? double.NaN : Get(parts, iCgg.Value);

                var gmOverId = iGmOverId is null
                    ? (
                        double.IsNaN(gm) || double.IsNaN(id) || Math.Abs(id) < 1e-30
                            ? double.NaN
                            : gm / id
                    )
                    : Get(parts, iGmOverId.Value);

                var ro = double.IsNaN(gds) || Math.Abs(gds) < 1e-30 ? double.NaN : 1.0 / gds;
                var gmRo = double.IsNaN(gm) || double.IsNaN(ro) ? double.NaN : gm * ro;
                var vstar =
                    double.IsNaN(gmOverId) || Math.Abs(gmOverId) < 1e-30
                        ? double.NaN
                        : 2.0 / gmOverId;
                var ft =
                    double.IsNaN(gm) || double.IsNaN(cgg) || cgg <= 0
                        ? double.NaN
                        : gm / (TwoPi * cgg);

                static string F(double v) =>
                    double.IsNaN(v)
                        ? string.Empty
                        : v.ToString("G17", CultureInfo.InvariantCulture);

                var values = new List<string> { F(vgs), F(vds), F(id) };
                foreach (var col in optional)
                {
                    if (!Wants(col))
                    {
                        continue;
                    }

                    values.Add(
                        col switch
                        {
                            "gm" => F(gm),
                            "gds" => F(gds),
                            "gm_over_id" => F(gmOverId),
                            "ro" => F(ro),
                            "gm_ro" => F(gmRo),
                            "vstar" => F(vstar),
                            "ft" => F(ft),
                            "vth" => F(vth),
                            "vdsat" => F(vdsat),
                            "cgs" => F(cgs),
                            "cgd" => F(cgd),
                            "cgg" => F(cgg),
                            _ => string.Empty,
                        }
                    );
                }

                outLines.Add(string.Join(',', values));
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

    private static int? FindFirstColumn(IReadOnlyDictionary<string, int> map, params string[] names)
    {
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }
            if (map.TryGetValue(name, out var idx))
            {
                return idx;
            }
        }
        return null;
    }
}
