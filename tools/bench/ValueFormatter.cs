using System;

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
        // Format value with appropriate unit prefix
        if (Math.Abs(value) >= 1e9)
        {
            return $"{value / 1e9:G3}G {unit}";
        }
        if (Math.Abs(value) >= 1e6)
        {
            return $"{value / 1e6:G3}M {unit}";
        }
        if (Math.Abs(value) >= 1e3)
        {
            return $"{value / 1e3:G3}k {unit}";
        }
        if (Math.Abs(value) >= 1.0)
        {
            return $"{value:G3} {unit}";
        }
        if (Math.Abs(value) >= 1e-3)
        {
            return $"{value * 1e3:G3}m {unit}";
        }
        if (Math.Abs(value) >= 1e-6)
        {
            return $"{value * 1e6:G3}u {unit}";
        }
        if (Math.Abs(value) >= 1e-9)
        {
            return $"{value * 1e9:G3}n {unit}";
        }
        return $"{value:G3} {unit}";
    }
}
