using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Cascode.Language.BenchRuntime;

public static class NgspiceWrdataSpParser
{
    public static BenchSParameterMatrix Parse(string path, int numPorts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (numPorts <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(numPorts),
                numPorts,
                "numPorts must be a positive integer."
            );
        }

        var lines = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        var frequencies = new double[lines.Length];
        var elements = new Dictionary<BenchPortPair, Complex[]>();

        for (var toPort = 1; toPort <= numPorts; toPort++)
        {
            for (var fromPort = 1; fromPort <= numPorts; fromPort++)
            {
                elements[new BenchPortPair(toPort, fromPort)] = new Complex[lines.Length];
            }
        }

        var expectedCols = 3 * numPorts * numPorts;
        for (var row = 0; row < lines.Length; row++)
        {
            var parts = lines[row].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != expectedCols)
            {
                throw new InvalidOperationException(
                    $"Unexpected wrdata column count in '{path}' at line {row + 1}: expected {expectedCols}, got {parts.Length}."
                );
            }

            // ngspice wrdata for complex vectors writes one triplet per vector:
            //   <freq> <real> <imag>  <freq> <real> <imag> ...
            frequencies[row] = ParseDouble(parts[0]);

            for (var toPort = 1; toPort <= numPorts; toPort++)
            {
                for (var fromPort = 1; fromPort <= numPorts; fromPort++)
                {
                    var vectorIndex = ((toPort - 1) * numPorts) + (fromPort - 1);
                    var baseIndex = 3 * vectorIndex;
                    var real = ParseDouble(parts[baseIndex + 1]);
                    var imag = ParseDouble(parts[baseIndex + 2]);
                    elements[new BenchPortPair(toPort, fromPort)][row] = new Complex(real, imag);
                }
            }
        }

        return new BenchSParameterMatrix(frequencies, elements);
    }

    public static SpNoiseDataset ParseNoiseFigure(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var lines = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        var frequencies = new double[lines.Length];
        var noiseFigure = new double[lines.Length];
        var minNoiseFigure = new double[lines.Length];
        var noiseResistance = new double[lines.Length];

        for (var row = 0; row < lines.Length; row++)
        {
            var parts = lines[row].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 9)
            {
                throw new InvalidOperationException(
                    $"Unexpected wrdata column count in '{path}' at line {row + 1}: expected 9, got {parts.Length}."
                );
            }

            frequencies[row] = ParseDouble(parts[0]);
            noiseFigure[row] = ParseDouble(parts[1]);
            minNoiseFigure[row] = ParseDouble(parts[4]);
            noiseResistance[row] = ParseDouble(parts[7]);
        }

        return new SpNoiseDataset(frequencies, noiseFigure, minNoiseFigure, noiseResistance);
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
