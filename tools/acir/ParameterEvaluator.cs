using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Cascode.ACIR;

/// <summary>
/// Evaluates parameter expressions in ACIR, supporting symbolic references
/// and arithmetic operations with SI unit prefixes.
/// </summary>
/// <remarks>
/// Expression grammar (per spec):
/// <code>
/// paramExpr = paramValue ((* | / | + | -) paramValue)*
/// paramValue = NUMBER SIUNIT? | IDENT
/// </code>
/// Evaluation is left-to-right with no operator precedence.
/// </remarks>
public static class ParameterEvaluator
{
    private static readonly Regex TokenPattern = new(
        @"([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)?)|([+\-*/])|([0-9]*\.?[0-9]+(?:[eE][+\-]?[0-9]+)?[fpnumkMGT]?)",
        RegexOptions.Compiled
    );

    private static readonly Regex IdentifierPattern = new(
        @"^[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)?$",
        RegexOptions.Compiled
    );

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
    /// Evaluates a parameter expression given bound parameter values.
    /// </summary>
    /// <param name="expression">Expression to evaluate (e.g., "W_input*2", "1u").</param>
    /// <param name="bindings">Map of parameter names to their values.</param>
    /// <returns>Evaluated numeric value as a string with appropriate SI prefix.</returns>
    /// <exception cref="ArgumentException">Thrown if expression is invalid or references undefined parameters.</exception>
    public static string Evaluate(string expression, IReadOnlyDictionary<string, string> bindings)
    {
        bindings ??= new Dictionary<string, string>();
        return Evaluate(expression, name => ResolveParameter(name, bindings));
    }

    /// <summary>
    /// Evaluates a parameter expression using a custom identifier resolver.
    /// </summary>
    /// <param name="expression">Expression to evaluate.</param>
    /// <param name="resolveIdentifier">Resolver for identifiers (including dotted names).</param>
    /// <param name="allowUnresolvedIdentifiers">If true, a single unresolved identifier is returned as-is.</param>
    public static string Evaluate(
        string expression,
        Func<string, string?> resolveIdentifier,
        bool allowUnresolvedIdentifiers = false
    )
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(resolveIdentifier);

        var tokens = Tokenize(expression);
        if (tokens.Count == 0)
        {
            throw new ArgumentException($"Empty or invalid expression: '{expression}'");
        }

        // Single value - no operators
        if (tokens.Count == 1)
        {
            var token = tokens[0];
            if (IsIdentifier(token))
            {
                var resolved = resolveIdentifier(token);
                if (resolved is null)
                {
                    if (allowUnresolvedIdentifiers)
                    {
                        return token;
                    }
                    throw new ArgumentException($"Undefined parameter reference: {token}");
                }
                return resolved;
            }
            return token;
        }

        // Evaluate left-to-right
        var result = ResolveValue(tokens[0], resolveIdentifier);
        int i = 1;
        while (i < tokens.Count)
        {
            if (i + 1 >= tokens.Count)
            {
                throw new ArgumentException(
                    $"Incomplete expression: operator without operand in '{expression}'"
                );
            }

            var op = tokens[i];
            var right = ResolveValue(tokens[i + 1], resolveIdentifier);

            result = op switch
            {
                "+" => result + right,
                "-" => result - right,
                "*" => result * right,
                "/" => right != 0 ? result / right : throw new DivideByZeroException(),
                _ => throw new ArgumentException($"Unknown operator: {op}"),
            };

            i += 2;
        }

        return FormatNumeric(result);
    }

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

    /// <summary>
    /// Tokenizes an expression into values and operators.
    /// </summary>
    private static List<string> Tokenize(string expression)
    {
        var tokens = new List<string>();
        var matches = TokenPattern.Matches(expression);

        foreach (Match match in matches)
        {
            tokens.Add(match.Value);
        }

        return tokens;
    }

    /// <summary>
    /// Resolves a token to its numeric value.
    /// </summary>
    private static double ResolveValue(string token, Func<string, string?> resolveIdentifier)
    {
        if (IsIdentifier(token))
        {
            var resolved = resolveIdentifier(token);
            if (resolved is null)
            {
                throw new ArgumentException($"Undefined parameter reference: {token}");
            }
            return ParseNumeric(resolved);
        }
        return ParseNumeric(token);
    }

    /// <summary>
    /// Resolves a parameter reference, supporting recursive resolution for chained references.
    /// </summary>
    private static string ResolveParameter(
        string paramName,
        IReadOnlyDictionary<string, string> bindings,
        HashSet<string>? visited = null
    )
    {
        visited ??= new HashSet<string>(StringComparer.Ordinal);

        if (!bindings.TryGetValue(paramName, out var value))
        {
            throw new ArgumentException($"Undefined parameter reference: {paramName}");
        }

        // Check for circular reference
        if (!visited.Add(paramName))
        {
            throw new ArgumentException($"Circular parameter reference detected: {paramName}");
        }

        // If the value is another parameter reference, resolve recursively.
        if (IsIdentifier(value))
        {
            return ResolveParameter(value, bindings, visited);
        }

        return value;
    }

    private static bool IsIdentifier(string token)
    {
        return IdentifierPattern.IsMatch(token);
    }
}
