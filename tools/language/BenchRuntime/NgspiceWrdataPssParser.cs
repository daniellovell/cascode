using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Cascode.Language.BenchRuntime;

public static class NgspiceWrdataPssParser
{
    public static PssDataset Parse(string path, IReadOnlyList<string> nodeNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(nodeNames);

        var lines = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        if (lines.Length == 0)
        {
            return new PssDataset(Array.Empty<double>(), new Dictionary<string, double[]>());
        }

        var times = new double[lines.Length];
        var valuesByNode = nodeNames.ToDictionary(
            n => n,
            _ => new double[lines.Length],
            StringComparer.OrdinalIgnoreCase
        );

        for (var row = 0; row < lines.Length; row++)
        {
            var parts = lines[row].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var parsed = TryParseRow(parts, nodeNames.Count);
            if (parsed is null)
            {
                throw new InvalidOperationException(
                    $"Unexpected wrdata column count in '{path}' at line {row + 1}: got {parts.Length} for {nodeNames.Count} vector(s)."
                );
            }

            times[row] = parsed.Value.X;
            for (var i = 0; i < nodeNames.Count; i++)
            {
                valuesByNode[nodeNames[i]][row] = parsed.Value.Values[i];
            }
        }

        return new PssDataset(times, valuesByNode);
    }

    private static (double X, double[] Values)? TryParseRow(string[] parts, int vectorCount)
    {
        // Common forms seen in ngspice wrdata:
        // - <x> <v0> <v1> ... (vectorCount+1 columns)
        // - <x0> <v0> <x1> <v1> ... (2*vectorCount columns)
        if (parts.Length == vectorCount + 1)
        {
            return (ParseDouble(parts[0]), parts.Skip(1).Select(ParseDouble).ToArray());
        }

        if (parts.Length == 2 * vectorCount)
        {
            var values = new double[vectorCount];
            for (var i = 0; i < vectorCount; i++)
            {
                values[i] = ParseDouble(parts[2 * i + 1]);
            }
            return (ParseDouble(parts[0]), values);
        }

        // Single-vector fallback: <x> <v>
        if (vectorCount == 1 && parts.Length == 2)
        {
            return (ParseDouble(parts[0]), new[] { ParseDouble(parts[1]) });
        }

        return null;
    }

    private static double ParseDouble(string raw)
    {
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidOperationException($"Invalid float '{raw}'.");
        }

        return value;
    }
}
