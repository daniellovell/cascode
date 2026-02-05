using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Cascode.Language.BenchRuntime;

public sealed record NgspiceVectorDataset(
    double[] X,
    IReadOnlyDictionary<string, double[]> ValuesByName
);

public static class NgspiceWrdataVectorParser
{
    public static NgspiceVectorDataset Parse(string path, IReadOnlyList<string> vectorNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(vectorNames);

        var lines = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        if (lines.Length == 0)
        {
            return new NgspiceVectorDataset(
                Array.Empty<double>(),
                new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase)
            );
        }

        var vectorCount = vectorNames.Count;
        if (vectorCount == 0)
        {
            return new NgspiceVectorDataset(
                Array.Empty<double>(),
                new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase)
            );
        }

        var x = new double[lines.Length];
        var valuesByName = vectorNames.ToDictionary(
            n => n,
            _ => new double[lines.Length],
            StringComparer.OrdinalIgnoreCase
        );

        for (var row = 0; row < lines.Length; row++)
        {
            var parts = lines[row].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var parsed = TryParseRow(parts, vectorCount);
            if (parsed is null)
            {
                throw new InvalidOperationException(
                    $"Unexpected wrdata column count in '{path}' at line {row + 1}: got {parts.Length} for {vectorCount} vector(s)."
                );
            }

            x[row] = parsed.Value.X;
            for (var i = 0; i < vectorCount; i++)
            {
                valuesByName[vectorNames[i]][row] = parsed.Value.Values[i];
            }
        }

        return new NgspiceVectorDataset(x, valuesByName);
    }

    private static (double X, double[] Values)? TryParseRow(string[] parts, int vectorCount)
    {
        // Common forms seen in ngspice wrdata:
        // - <v0> <v1> <v2> ... (vectorCount columns)                   (no explicit x)
        // - <x> <v0> <v1> ... (vectorCount+1 columns)
        // - <x0> <v0> <x1> <v1> ... (2*vectorCount columns)
        if (parts.Length == vectorCount)
        {
            return (X: 0.0, parts.Select(ParseDouble).ToArray());
        }

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
