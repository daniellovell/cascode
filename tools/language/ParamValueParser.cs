using System.Text.RegularExpressions;

namespace Cascode.Language;

/// <summary>
/// Parses string values into ParamValue instances with correct type classification.
/// </summary>
public static partial class ParamValueParser
{
    /// <summary>
    /// Parses a parameter value string into a ParamValue with the appropriate field set.
    /// </summary>
    /// <param name="value">The value string to parse.</param>
    /// <returns>A ParamValue with the appropriate field set (Symbolic, Numeric, or Literal).</returns>
    public static ParamValue Parse(string value)
    {
        if (NumericValuePattern().IsMatch(value))
            return new ParamValue { Numeric = value };

        if (value == "??" || IdentifierPattern().IsMatch(value))
            return new ParamValue { Symbolic = value };

        return new ParamValue { Literal = value };
    }

    /// <summary>
    /// Regex pattern for numeric values with optional SI suffixes.
    /// Matches: optional leading minus, digits, optional decimal, optional SI suffix, optional unit letters.
    /// Examples: "10k", "-1.5M", "180n", "2u", "1.8" - all numeric.
    /// Non-matches: "res1stor", "nmos2" - these are literals.
    /// </summary>
    [GeneratedRegex(@"^-?\d+\.?\d*[fpnumkMGT]?[A-Za-z]*$")]
    private static partial Regex NumericValuePattern();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)?$")]
    private static partial Regex IdentifierPattern();
}
