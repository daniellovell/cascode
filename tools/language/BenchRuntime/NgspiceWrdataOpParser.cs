using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Cascode.Language.BenchRuntime;

public static class NgspiceWrdataOpParser
{
    public static IReadOnlyDictionary<string, double> ParseCurrents(
        string path,
        IReadOnlyList<string> sourceNames
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(sourceNames);

        if (sourceNames.Count == 0)
        {
            return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        }

        var values = ParseVectorValuesOrThrow(path, sourceNames.Count);

        var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < sourceNames.Count; i++)
        {
            map[sourceNames[i]] = values[i];
        }

        return map;
    }

    public static IReadOnlyDictionary<string, double> ParseNodeVoltages(
        string path,
        IReadOnlyList<string> nodeKeys
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(nodeKeys);

        if (nodeKeys.Count == 0)
        {
            return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        }

        var values = ParseVectorValuesOrThrow(path, nodeKeys.Count);
        var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < nodeKeys.Count; i++)
        {
            map[nodeKeys[i]] = values[i];
        }

        return map;
    }

    private static double[] ParseVectorValuesOrThrow(string path, int vectorCount)
    {
        var lines = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        if (lines.Length == 0)
        {
            throw new InvalidOperationException($"Empty wrdata file '{path}'.");
        }

        // For op, ngspice emits a single row. Use the last row defensively.
        var parts = lines[^1].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        var values = TryParseVectorRow(parts, vectorCount);
        if (values is null)
        {
            throw new InvalidOperationException(
                $"Unexpected wrdata column count in '{path}': got {parts.Length} for {vectorCount} vector(s)."
            );
        }

        return values;
    }

    private static double[]? TryParseVectorRow(string[] parts, int vectorCount)
    {
        // Common forms seen in ngspice wrdata:
        // - <v0> <v1> <v2> ... (vectorCount columns)
        // - <x> <v0> <v1> ... (vectorCount+1 columns)
        // - <x0> <v0> <x1> <v1> ... (2*vectorCount columns)
        if (parts.Length == vectorCount)
        {
            return parts.Select(ParseDouble).ToArray();
        }

        if (parts.Length == vectorCount + 1)
        {
            return parts.Skip(1).Select(ParseDouble).ToArray();
        }

        if (parts.Length == 2 * vectorCount)
        {
            var values = new double[vectorCount];
            for (var i = 0; i < vectorCount; i++)
            {
                values[i] = ParseDouble(parts[2 * i + 1]);
            }
            return values;
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
