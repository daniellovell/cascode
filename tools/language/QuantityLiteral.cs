using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Cascode.Language;

/// <summary>
/// Parses Cascode quantity literals into their numeric and unit components.
/// Keeps language-layer quantity handling consistent across parsing and compliance checks.
/// </summary>
internal static partial class QuantityLiteral
{
    /// <summary>
    /// Splits a quantity literal such as <c>20MHz</c> or <c>9nV/rtHz</c> into value and unit parts.
    /// </summary>
    /// <param name="raw">Raw quantity literal text.</param>
    /// <returns>The numeric prefix and unit suffix.</returns>
    public static (string Value, string Unit) SplitValueAndUnit(string raw)
    {
        var match = QuantityPattern().Match(raw);
        if (match.Success)
        {
            return (match.Groups[1].Value, match.Groups[2].Value);
        }

        return (raw, string.Empty);
    }

    /// <summary>
    /// Converts a split quantity literal into its numeric magnitude.
    /// </summary>
    /// <param name="value">
    /// Numeric literal text, including an SI prefix suffix supported by the Cascode lexer.
    /// </param>
    /// <returns>The numeric magnitude expressed in base units.</returns>
    /// <exception cref="FormatException">
    /// Thrown when the numeric portion uses an unsupported SI suffix or cannot be parsed.
    /// </exception>
    public static double ParseMagnitude(string value)
    {
        value = value.Trim();

        var multiplier = 1.0;
        var numericPart = value;
        if (value.Length > 0 && char.IsLetter(value[^1]))
        {
            var suffix = value[^1];
            numericPart = value[..^1];
            multiplier = suffix switch
            {
                'k' => 1e3,
                'M' => 1e6,
                'm' => 1e-3,
                'G' => 1e9,
                'T' => 1e12,
                'u' => 1e-6,
                'n' => 1e-9,
                'p' => 1e-12,
                'f' => 1e-15,
                _ => throw new FormatException(
                    $"Unrecognized unit suffix '{suffix}' in value: {value}"
                ),
            };
        }

        if (
            !double.TryParse(
                numericPart,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed
            )
        )
        {
            throw new FormatException($"Invalid numeric value: {value}");
        }

        return parsed * multiplier;
    }

    [GeneratedRegex(
        @"^(-?(?:[0-9]*\.?[0-9]+(?:[eE][+\-]?[0-9]+)?)(?:[fpnumkMGT]?))([A-Za-z]+(?:/rtHz)?)$"
    )]
    private static partial Regex QuantityPattern();
}
