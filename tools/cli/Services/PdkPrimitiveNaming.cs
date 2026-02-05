using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cascode.Cli.Services;

internal static partial class PdkPrimitiveNaming
{
    private const string DefaultPrimitiveName = "Primitive";

    public static string PrimitiveNameFromModelName(string modelName)
    {
        var name = BinModelSuffixPattern().Replace(modelName ?? string.Empty, string.Empty);

        var lastSep = name.LastIndexOf("__", StringComparison.Ordinal);
        if (lastSep >= 0 && lastSep + 2 < name.Length)
        {
            name = name[(lastSep + 2)..];
        }

        name = name.Replace('.', '_');
        name = SanitizeIdentifier(name);
        return string.IsNullOrWhiteSpace(name) ? DefaultPrimitiveName : name;
    }

    public static string PrimitiveFamilyNameFromModelName(string modelName)
    {
        return PrimitiveFamilyNameFromPrimitiveName(PrimitiveNameFromModelName(modelName));
    }

    public static string PrimitiveFamilyNameFromPrimitiveName(string primitiveName)
    {
        if (string.IsNullOrWhiteSpace(primitiveName))
        {
            return DefaultPrimitiveName;
        }

        var trimmed = primitiveName.Trim();
        var collapsed = FixedVariantSuffixPattern().Replace(trimmed, string.Empty);
        return string.IsNullOrWhiteSpace(collapsed) ? trimmed : collapsed;
    }

    public static bool IsFamilyRepresentativeModel(string modelName)
    {
        var primitive = PrimitiveNameFromModelName(modelName);
        var family = PrimitiveFamilyNameFromPrimitiveName(primitive);
        return primitive.Equals(family, StringComparison.OrdinalIgnoreCase);
    }

    public static int PreferModelTypeRank(string? modelType)
    {
        return string.Equals(modelType, "model", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    }

    private static string SanitizeIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var chars = name.Trim()
            .Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_')
            .ToArray();
        var sanitized = new string(chars);
        if (sanitized.Length > 0 && !char.IsLetter(sanitized[0]) && sanitized[0] != '_')
        {
            sanitized = "_" + sanitized;
        }

        return sanitized;
    }

    [GeneratedRegex(@"_(?:aF|bM)\d+(?:W\d+p\d+)?(?:L\d+p\d+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex FixedVariantSuffixPattern();

    [GeneratedRegex(@"(?:__|_)model(?:_base)?(?:\.\d+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex BinModelSuffixPattern();
}
