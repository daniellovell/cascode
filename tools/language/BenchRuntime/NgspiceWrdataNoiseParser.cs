using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Cascode.Language.BenchRuntime;

public sealed record NoiseDataset(double[] FrequenciesHz, double[] OutputNoiseVPerRtHz);

public static class NgspiceWrdataNoiseParser
{
    public static NoiseDataset Parse(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var lines = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        var freqs = new double[lines.Length];
        var onoise = new double[lines.Length];

        for (var row = 0; row < lines.Length; row++)
        {
            var parts = lines[row].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                throw new InvalidOperationException(
                    $"Unexpected wrdata column count in '{path}' at line {row + 1}: expected 2, got {parts.Length}."
                );
            }

            freqs[row] = ParseDouble(parts[0]);
            onoise[row] = ParseDouble(parts[1]);
        }

        return new NoiseDataset(freqs, onoise);
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
