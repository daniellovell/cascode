using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Cascode.Language.BenchRuntime;

public sealed record AcDataset(
    double[] FrequenciesHz,
    IReadOnlyDictionary<string, Complex[]> NodeVoltages
);

public static class NgspiceWrdataAcParser
{
    public static AcDataset Parse(string path, IReadOnlyList<string> nodeNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(nodeNames);

        var lines = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();

        var frequencies = new double[lines.Length];
        var valuesByNode = nodeNames.ToDictionary(
            n => n,
            _ => new Complex[lines.Length],
            StringComparer.OrdinalIgnoreCase
        );

        // ngspice wrdata for AC writes one triplet per requested vector:
        //   <freq> <real> <imag>  <freq> <real> <imag>  ...
        // i.e. frequency is repeated for each vector triplet.
        var expectedCols = 3 * nodeNames.Count;

        for (var row = 0; row < lines.Length; row++)
        {
            var parts = lines[row].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != expectedCols)
            {
                throw new InvalidOperationException(
                    $"Unexpected wrdata column count in '{path}' at line {row + 1}: expected {expectedCols}, got {parts.Length}."
                );
            }

            // Each requested vector is emitted as a triplet: <freq> <real> <imag>.
            // The first triplet's <freq> column is the x-axis.
            frequencies[row] = ParseDouble(parts[0]);

            for (var i = 0; i < nodeNames.Count; i++)
            {
                var baseIndex = 3 * i;
                var real = ParseDouble(parts[baseIndex + 1]);
                var imag = ParseDouble(parts[baseIndex + 2]);
                valuesByNode[nodeNames[i]][row] = new Complex(real, imag);
            }
        }

        return new AcDataset(frequencies, valuesByNode);
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
