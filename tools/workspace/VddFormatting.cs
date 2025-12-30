using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Cascode.Workspace;

/// <summary>
/// Helpers for parsing and formatting VDD tokens and volt values.
/// Centralizes the logic so models, devices, DB writers and the CLI renderers stay consistent.
/// </summary>
public static partial class VddFormatting
{
    private static readonly Regex TokenRegex = VoltageCompactFormatPattern();

    /// <summary>
    /// Extracts a canonical VDD token from a model voltage domain using config regex rules.
    /// Returns an empty string when the domain is null/whitespace.
    /// When a match is found, the integer part is left-padded to 2 digits to normalize tags (e.g., 1.8V → 01v8).
    /// If no match is found, returns the lowercased input for traceability.
    /// </summary>
    public static string ExtractTokenFromVoltageDomain(string? voltageDomain, PdkMatchingConfig cfg)
    {
        if (string.IsNullOrWhiteSpace(voltageDomain))
            return string.Empty;
        var lower = voltageDomain.ToLowerInvariant();
        var pattern = string.IsNullOrWhiteSpace(cfg.Normalization.VddExtractRegex)
            ? @"(?<n>\d+)(?:\.(?<f>\d+))?v"
            : cfg.Normalization.VddExtractRegex;
        var m = Regex.Match(lower, pattern, RegexOptions.CultureInvariant);
        if (!m.Success)
            return lower;
        var n = m.Groups["n"].Value;
        var f = m.Groups["f"].Success ? m.Groups["f"].Value : "0";
        return $"{n.PadLeft(2, '0')}v{f}";
    }

    /// <summary>
    /// Parses a canonical VDD token (e.g., 01v8, 00v9, 01v05) into volts.
    /// </summary>
    public static bool TryTokenToVolts(string token, out double volts)
    {
        volts = 0;
        var m = TokenRegex.Match(token ?? string.Empty);
        if (!m.Success)
            return false;
        var n = m.Groups["n"].Value;
        var f = m.Groups["f"].Value;
        // Avoid floating parsing quirks: construct from integer and fractional strings
        if (!int.TryParse(n, NumberStyles.None, CultureInfo.InvariantCulture, out var i))
            return false;
        var frac = 0.0;
        if (f.Length > 0)
        {
            if (!int.TryParse(f, NumberStyles.None, CultureInfo.InvariantCulture, out var fi))
                return false;
            frac = fi / Math.Pow(10, f.Length);
        }
        volts = i + frac;
        return true;
    }

    /// <summary>
    /// Formats a canonical token into a user-facing string (e.g., 01v8 → 1.8V, 05v0 → 5.0V, 01v05 → 1.05V).
    /// Falls back to the original token if it does not match the canonical pattern.
    /// </summary>
    public static string TokenToPretty(string token)
    {
        var m = TokenRegex.Match(token ?? string.Empty);
        if (!m.Success)
            return token ?? string.Empty;
        var n = m.Groups["n"].Value.TrimStart('0');
        if (n.Length == 0)
            n = "0";
        var f = m.Groups["f"].Value;
        // Trim trailing zeros but leave at least one digit (e.g., "0")
        var trimmed = f.TrimEnd('0');
        if (trimmed.Length == 0)
            trimmed = "0";
        return $"{n}.{trimmed}V";
    }

    /// <summary>
    /// Formats a numeric voltage into a pretty string (e.g., 1.8 -> 1.8V, 1.05 -> 1.05V, 5.0 -> 5.0V).
    /// </summary>
    public static string PrettyFromVolts(double volts)
    {
        // Avoid binary FP artifacts (e.g., 3.2999999998) by rounding and formatting with up to 3 decimals.
        var rounded = Math.Round(volts, 3, MidpointRounding.AwayFromZero);
        var s = rounded.ToString("0.###", CultureInfo.InvariantCulture);
        if (!s.Contains('.'))
            s += ".0"; // ensure at least one decimal place
        return s + "V";
    }

    [GeneratedRegex(
        "^(?<n>\\d+)v(?<f>\\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant
    )]
    private static partial Regex VoltageCompactFormatPattern();
}
