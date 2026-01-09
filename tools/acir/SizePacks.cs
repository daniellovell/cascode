using System;
using System.Collections.Generic;

namespace Cascode.ACIR;

/// <summary>
/// Declares a named size pack on a circuit with an optional default.
/// </summary>
public sealed class SizeDeclaration
{
    /// <summary>Size pack name.</summary>
    public required string Name { get; init; }

    /// <summary>Optional default value. If null, the size pack is required at instantiation.</summary>
    public SizePack? Default { get; init; }
}

/// <summary>
/// A size pack is a named map of key/value sizing expressions (e.g. W/L/M for MOS).
/// </summary>
public sealed class SizePack
{
    /// <summary>Entries in this size pack.</summary>
    public Dictionary<string, string> Entries { get; init; } = new();
}

/// <summary>
/// Utility methods for parsing size pack literals.
/// </summary>
public static class SizePacks
{
    /// <summary>
    /// Parses a size literal string into a SizePack.
    /// </summary>
    /// <param name="literal">Content inside parentheses, e.g., "W=2u, L=180n, M=1"</param>
    /// <param name="pack">Parsed size pack if successful</param>
    /// <param name="error">Error message if parsing fails</param>
    /// <returns>True if parsing succeeded</returns>
    public static bool TryParseSizeLiteral(string literal, out SizePack pack, out string error)
    {
        pack = new SizePack();
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(literal))
        {
            error = "Empty size literal";
            return false;
        }

        var entries = literal.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        foreach (var entry in entries)
        {
            var eqIndex = entry.IndexOf('=', StringComparison.Ordinal);
            if (eqIndex <= 0)
            {
                error = $"Invalid size entry '{entry}' - expected 'key=value'";
                return false;
            }

            var key = entry[..eqIndex].Trim();
            var value = entry[(eqIndex + 1)..].Trim();

            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                error = $"Invalid size entry '{entry}' - key or value is empty";
                return false;
            }

            if (pack.Entries.ContainsKey(key))
            {
                error = $"Duplicate size key '{key}'";
                return false;
            }

            pack.Entries[key] = value;
        }

        return true;
    }
}
