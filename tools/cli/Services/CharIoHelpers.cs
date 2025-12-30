using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Spectre.Console;

namespace Cascode.Cli.Services;

internal static class CharIoHelpers
{
    internal static (List<string> Headers, List<double[]> Samples) LoadDerivedCsv(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length == 0)
            return (new List<string>(), new List<double[]>());
        var headers = lines[0].Split(',', StringSplitOptions.TrimEntries).ToList();
        var samples = new List<double[]>();
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var parts = line.Split(',', StringSplitOptions.None);
            var values = new double[headers.Count];
            for (var j = 0; j < headers.Count; j++)
            {
                if (
                    j < parts.Length
                    && double.TryParse(
                        parts[j],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var val
                    )
                )
                    values[j] = val;
                else
                    values[j] = double.NaN;
            }
            samples.Add(values);
        }
        return (headers, samples);
    }

    internal static (int Index, string Name) FindColumn(
        IReadOnlyList<string> headers,
        params string[] aliases
    )
    {
        for (var i = 0; i < headers.Count; i++)
        {
            var header = headers[i];
            foreach (var alias in aliases)
            {
                if (header.Equals(alias, StringComparison.OrdinalIgnoreCase))
                    return (i, header);
            }
        }
        return (-1, aliases.FirstOrDefault() ?? string.Empty);
    }

    internal static void RenderSparkline(
        IReadOnlyList<double[]> samples,
        int columnIndex,
        string label
    )
    {
        var series = BuildSeries(samples, columnIndex);
        var finite = series.Where(double.IsFinite).ToList();
        if (finite.Count == 0)
            return;
        var min = finite.Min();
        var max = finite.Max();
        if (Math.Abs(max - min) < 1e-12)
            max = min + 1e-12;
        var glyphs = "▁▂▃▄▅▆▇█";
        var spark = new System.Text.StringBuilder();
        foreach (var value in series)
        {
            var idx = 0;
            if (double.IsFinite(value))
            {
                var normalized = (value - min) / (max - min);
                normalized = Math.Clamp(normalized, 0.0, 1.0);
                idx = (int)Math.Round(normalized * (glyphs.Length - 1));
            }
            spark.Append(glyphs[idx]);
        }
        AnsiConsole.MarkupLine(
            $"[cyan]{label}[/]: {spark} [grey](min {FormatNumber(min)} / max {FormatNumber(max)})[/]"
        );
    }

    internal static string FormatNumber(double value)
    {
        if (!double.IsFinite(value))
            return string.Empty;
        var abs = Math.Abs(value);
        if (abs >= 1e3 || (abs > 0 && abs < 1e-3))
            return value.ToString("0.###E+0", CultureInfo.InvariantCulture);
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static double[] BuildSeries(IReadOnlyList<double[]> samples, int columnIndex)
    {
        var result = new double[samples.Count];
        for (var i = 0; i < samples.Count; i++)
        {
            var value =
                (columnIndex >= 0 && columnIndex < samples[i].Length)
                    ? samples[i][columnIndex]
                    : double.NaN;
            result[i] = double.IsFinite(value) ? value : 0.0;
        }
        return result;
    }
}
