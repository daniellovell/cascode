using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Cascode.ACIR;

internal static class VariantNaming
{
    private const int MaxSubcktNameLength = 64;
    private const int HashLength = 8;

    public static string BuildCanonicalName(
        string baseName,
        IReadOnlyDictionary<string, string> paramValues,
        IReadOnlyDictionary<string, SizePack> sizeValues
    )
    {
        ArgumentNullException.ThrowIfNull(baseName);
        paramValues ??= new Dictionary<string, string>(StringComparer.Ordinal);
        sizeValues ??= new Dictionary<string, SizePack>(StringComparer.Ordinal);

        var parts = new List<string>();

        foreach (var (name, value) in paramValues.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            parts.Add(name);
            parts.Add(RenderCanonicalValue(value));
        }

        foreach (var (sizeName, pack) in sizeValues.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            foreach (var (field, value) in pack.Entries.OrderBy(f => f.Key, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                parts.Add($"{sizeName}_{field}");
                parts.Add(RenderCanonicalValue(value));
            }
        }

        if (parts.Count == 0)
        {
            return baseName;
        }

        var fullName = $"{baseName}_{string.Join("_", parts)}";
        if (fullName.Length <= MaxSubcktNameLength)
        {
            return fullName;
        }

        return HashFallback(baseName, fullName);
    }

    private static string RenderCanonicalValue(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return trimmed;
        }

        if (trimmed.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return "true";
        }

        if (trimmed.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return "false";
        }

        if (trimmed.Equals("nmos", StringComparison.OrdinalIgnoreCase))
        {
            return "NMOS";
        }

        if (trimmed.Equals("pmos", StringComparison.OrdinalIgnoreCase))
        {
            return "PMOS";
        }

        return trimmed;
    }

    private static string HashFallback(string baseName, string fullName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fullName));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        var suffix = hex[..HashLength];

        var maxPrefixLength = MaxSubcktNameLength - HashLength - 1;
        var prefix = baseName;
        if (maxPrefixLength > 0 && prefix.Length > maxPrefixLength)
        {
            prefix = prefix[..maxPrefixLength];
        }

        return maxPrefixLength > 0 ? $"{prefix}_{suffix}" : suffix;
    }
}
