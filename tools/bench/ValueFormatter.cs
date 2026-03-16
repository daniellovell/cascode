using System;
using System.Globalization;

namespace Cascode.Bench;

/// <summary>
/// Utility for formatting numeric values with unit prefixes for display.
/// </summary>
public static class ValueFormatter
{
    private const int SignificantDigits = 4;
    private static readonly (double Divisor, string Prefix)[] PrefixScales =
    [
        (1e-15, "f"),
        (1e-12, "p"),
        (1e-9, "n"),
        (1e-6, "u"),
        (1e-3, "m"),
        (1.0, ""),
        (1e3, "k"),
        (1e6, "M"),
        (1e9, "G"),
        (1e12, "T"),
    ];

    /// <summary>
    /// Formats a numeric value with an appropriate unit prefix (G, M, k, m, u, n, etc.).
    /// </summary>
    /// <param name="value">The numeric value to format.</param>
    /// <param name="unit">The unit string to append (e.g., "Hz", "dB", "V").</param>
    /// <returns>A formatted string with the value scaled to an appropriate prefix and the unit appended.</returns>
    public static string FormatValue(double value, string unit)
    {
        if (!double.IsFinite(value))
        {
            return FormatWithUnit(value.ToString(CultureInfo.InvariantCulture), unit);
        }

        if (value == 0)
        {
            return FormatWithUnit("0", unit);
        }

        var abs = Math.Abs(value);
        var prefixIndex = SelectPrefixIndex(abs);
        var divisor = PrefixScales[prefixIndex].Divisor;
        var scaledValue = value / divisor;
        var roundedScaledValue = RoundToSignificantDigits(scaledValue, SignificantDigits);

        while (Math.Abs(roundedScaledValue) >= 1000 && prefixIndex < PrefixScales.Length - 1)
        {
            prefixIndex++;
            divisor = PrefixScales[prefixIndex].Divisor;
            scaledValue = value / divisor;
            roundedScaledValue = RoundToSignificantDigits(scaledValue, SignificantDigits);
        }

        var prefix = PrefixScales[prefixIndex].Prefix;
        var scaled = roundedScaledValue.ToString("G4", CultureInfo.InvariantCulture);
        var prefixedUnit = $"{prefix}{unit}".Trim();
        return prefixedUnit.Length == 0 ? scaled : $"{scaled} {prefixedUnit}";
    }

    private static int SelectPrefixIndex(double abs)
    {
        for (var i = PrefixScales.Length - 1; i >= 0; i--)
        {
            if (abs >= PrefixScales[i].Divisor)
            {
                return i;
            }
        }

        return 0;
    }

    private static double RoundToSignificantDigits(double value, int digits)
    {
        if (value == 0)
        {
            return 0;
        }

        var abs = Math.Abs(value);
        var exponent = Math.Floor(Math.Log10(abs));
        var scale = Math.Pow(10, digits - 1 - exponent);
        return Math.Round(value * scale, MidpointRounding.AwayFromZero) / scale;
    }

    private static string FormatWithUnit(string numericText, string unit)
    {
        var trimmedUnit = unit.Trim();
        return trimmedUnit.Length == 0 ? numericText : $"{numericText} {trimmedUnit}";
    }
}
