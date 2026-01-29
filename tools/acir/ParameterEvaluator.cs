using System;
using System.Collections.Generic;
using System.Globalization;

namespace Cascode.ACIR;

/// <summary>
/// Provides SI-prefixed numeric parsing and formatting helpers.
/// </summary>
public static class ParameterEvaluator
{
    private static readonly Dictionary<char, double> SIPrefixes = new()
    {
        ['f'] = 1e-15,
        ['p'] = 1e-12,
        ['n'] = 1e-9,
        ['u'] = 1e-6,
        ['m'] = 1e-3,
        ['k'] = 1e3,
        ['M'] = 1e6,
        ['G'] = 1e9,
        ['T'] = 1e12,
    };

    /// <summary>
    /// Parses an SI-prefixed numeric value to double.
    /// </summary>
    /// <param name="value">Numeric string with optional SI prefix (e.g., "2u", "100n", "1.5k").</param>
    /// <returns>Parsed double value.</returns>
    /// <exception cref="ArgumentException">Thrown if value cannot be parsed.</exception>
    public static double ParseNumeric(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Empty numeric value");
        }

        value = value.Trim();

        // Check for SI prefix at end
        var lastChar = value[^1];
        if (SIPrefixes.TryGetValue(lastChar, out var multiplier))
        {
            var numPart = value[..^1];
            if (
                double.TryParse(
                    numPart,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var num
                )
            )
            {
                return num * multiplier;
            }
            throw new ArgumentException($"Cannot parse numeric portion: '{numPart}'");
        }

        // No SI prefix - parse as-is
        if (
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
        )
        {
            return result;
        }

        throw new ArgumentException($"Cannot parse numeric value: '{value}'");
    }

    /// <summary>
    /// Formats a double value back to a string with appropriate SI prefix.
    /// </summary>
    /// <param name="value">Numeric value to format.</param>
    /// <returns>Formatted string with SI prefix for readability.</returns>
    public static string FormatNumeric(double value)
    {
        if (value == 0)
        {
            return "0";
        }

        var absValue = Math.Abs(value);

        // Select appropriate SI prefix
        if (absValue >= 1e12)
        {
            return FormatWithPrefix(value, 1e12, "T");
        }
        if (absValue >= 1e9)
        {
            return FormatWithPrefix(value, 1e9, "G");
        }
        if (absValue >= 1e6)
        {
            return FormatWithPrefix(value, 1e6, "M");
        }
        if (absValue >= 1e3)
        {
            return FormatWithPrefix(value, 1e3, "k");
        }
        if (absValue >= 1)
        {
            return value.ToString("G6", CultureInfo.InvariantCulture);
        }
        if (absValue >= 1e-3)
        {
            return FormatWithPrefix(value, 1e-3, "m");
        }
        if (absValue >= 1e-6)
        {
            return FormatWithPrefix(value, 1e-6, "u");
        }
        if (absValue >= 1e-9)
        {
            return FormatWithPrefix(value, 1e-9, "n");
        }
        if (absValue >= 1e-12)
        {
            return FormatWithPrefix(value, 1e-12, "p");
        }
        if (absValue >= 1e-15)
        {
            return FormatWithPrefix(value, 1e-15, "f");
        }

        // Very small or unusual values - use scientific notation
        return value.ToString("G6", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Formats a value with the specified SI prefix.
    /// </summary>
    private static string FormatWithPrefix(double value, double divisor, string prefix)
    {
        var scaled = value / divisor;
        var formatted = scaled.ToString("G6", CultureInfo.InvariantCulture);
        return formatted + prefix;
    }
}
