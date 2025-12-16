using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Cascode.ACIR;
using Cascode.Bench;

namespace Cascode.Cli.Services;

internal static class BenchResultParser
{
    public sealed record TracePoint(int Index, Dictionary<string, double> AxisValues, List<MeasurementResult> Measurements);

    public static List<TracePoint> ParsePoints(string stdout, HashSet<string> sweepNames)
    {
        var points = new List<TracePoint>();
        var lines = stdout.Split('\n');

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("CASCODE_POINT", StringComparison.Ordinal))
            {
                continue;
            }

            var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var index = 0;
            var axes = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var measurements = new List<MeasurementResult>();

            foreach (var token in tokens.Skip(1))
            {
                var equals = token.IndexOf('=');
                if (equals <= 0 || equals == token.Length - 1)
                {
                    continue;
                }

                var key = token.Substring(0, equals);
                var valueStr = token.Substring(equals + 1);
                if (key.Equals("point_index", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(valueStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedIndex))
                {
                    index = parsedIndex;
                    continue;
                }

                if (!double.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedValue))
                {
                    continue;
                }

                var axisName = TryAxisName(key, sweepNames);
                if (axisName != null)
                {
                    axes[axisName] = parsedValue;
                    continue;
                }

                if (TryMeasurement(key, parsedValue, out var measurement))
                {
                    measurements.Add(measurement);
                }
            }

            points.Add(new TracePoint(index, axes, measurements));
        }

        return points.OrderBy(p => p.Index).ToList();
    }

    public static BenchResult ParseResults(string stdout, Circuit circuit, string benchName)
    {
        var results = new BenchResult { Circuit = circuit.Name, Bench = benchName };
        var nodeByMetric = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (circuit.Constraints?.Measure != null)
        {
            foreach (var group in circuit.Constraints.Measure
                         .Where(m => m.Bench.Equals(benchName, StringComparison.OrdinalIgnoreCase))
                         .GroupBy(m => m.Metric, StringComparer.OrdinalIgnoreCase))
            {
                var nodes = group.Select(x => x.Node).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                nodeByMetric[group.Key] = nodes.Count == 1 ? nodes[0] : null;
            }
        }

        foreach (var line in stdout.Split('\n'))
        {
            if (!TryParseResultLine(line, out var metric, out var value, out var unit))
            {
                continue;
            }

            var key = MakeMeasurementKey(results.Measurements, metric, nodeByMetric.TryGetValue(metric, out var node) ? node : null);
            results.Measurements[key] = new MeasurementResult
            {
                Metric = metric,
                Value = value,
                Unit = unit,
                Node = nodeByMetric.TryGetValue(metric, out var n) ? n : null
            };
        }

        return results;
    }

    public static void MergeMeasurements(Dictionary<string, MeasurementResult> target, IEnumerable<MeasurementResult> source)
    {
        foreach (var measurement in source)
        {
            var key = measurement.Node == null ? measurement.Metric : $"{measurement.Metric}@{measurement.Node}";
            target[key] = measurement;
        }
    }

    public static BenchResult CreateCombinedResults(string circuitName, IReadOnlyList<string> benchesToRun, Dictionary<string, MeasurementResult> measurements)
    {
        return new BenchResult
        {
            Circuit = circuitName,
            Bench = benchesToRun.Count == 1 ? benchesToRun[0] : "all",
            Measurements = measurements.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static string? TryAxisName(string key, HashSet<string> sweepNames)
    {
        if (!key.EndsWith("_V", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var name = key[..^2];
        return sweepNames.Contains(name) ? name : null;
    }

    private static bool TryMeasurement(string key, double value, out MeasurementResult measurement)
    {
        measurement = new MeasurementResult();
        var underscore = key.LastIndexOf('_');
        if (underscore <= 0 || underscore == key.Length - 1)
        {
            return false;
        }

        var metric = key.Substring(0, underscore);
        var unit = key.Substring(underscore + 1);
        measurement = new MeasurementResult { Metric = metric, Value = value, Unit = unit };
        return true;
    }

    private static bool TryParseResultLine(string line, out string metric, out double value, out string unit)
    {
        metric = string.Empty;
        unit = string.Empty;
        value = 0;

        var trimmed = line.Trim();
        if (!trimmed.StartsWith("RESULT:", StringComparison.Ordinal))
        {
            return false;
        }

        var match = Regex.Match(trimmed,
            @"^RESULT:\s*(?<metric>[^=]+?)\s*=\s*(?<value>[-+]?(\d+(\.\d*)?|\.\d+)([eE][-+]?\d+)?)\s*(?<unit>\w+)?",
            RegexOptions.CultureInvariant);

        if (!match.Success)
        {
            return false;
        }

        metric = match.Groups["metric"].Value.Trim();
        unit = match.Groups["unit"].Value.Trim();
        return double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string MakeMeasurementKey(Dictionary<string, MeasurementResult> existing, string metric, string? node)
    {
        var baseKey = node == null ? metric : $"{metric}@{node}";
        baseKey = baseKey.Replace(' ', '_');
        if (!existing.ContainsKey(baseKey))
        {
            return baseKey;
        }

        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{baseKey}#{i}";
            if (!existing.ContainsKey(candidate))
            {
                return candidate;
            }
        }

        return Guid.NewGuid().ToString("N");
    }
}

