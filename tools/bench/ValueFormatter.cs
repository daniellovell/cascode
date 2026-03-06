using System;
using System.Globalization;

namespace Cascode.Bench;

/// <summary>
/// Utility for formatting numeric values with unit prefixes for display.
/// </summary>
public static class ValueFormatter
{
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
        var (divisor, prefix) = abs switch
        {
            >= 1e12 => (1e12, "T"),
            >= 1e9 => (1e9, "G"),
            >= 1e6 => (1e6, "M"),
            >= 1e3 => (1e3, "k"),
            >= 1 => (1.0, ""),
            >= 1e-3 => (1e-3, "m"),
            >= 1e-6 => (1e-6, "u"),
            >= 1e-9 => (1e-9, "n"),
            >= 1e-12 => (1e-12, "p"),
            _ => (1e-15, "f"),
        };

        var scaled = (value / divisor).ToString("G4", CultureInfo.InvariantCulture);
        var prefixedUnit = $"{prefix}{unit}".Trim();
        return prefixedUnit.Length == 0 ? scaled : $"{scaled} {prefixedUnit}";
    }

    private static string FormatWithUnit(string numericText, string unit)
    {
        var trimmedUnit = unit.Trim();
        return trimmedUnit.Length == 0 ? numericText : $"{numericText} {trimmedUnit}";
    }
}
