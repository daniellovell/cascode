using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Cascode.ACIR;

public static class PrimitiveResolver
{
    private static readonly Regex SizeFieldPattern = new(
        @"\b(?<size>[A-Za-z_][A-Za-z0-9_]*)\.(?<field>[A-Za-z_][A-Za-z0-9_]*)\b",
        RegexOptions.Compiled
    );

    public static SizePack? ResolveSizePack(
        DeviceDeclaration device,
        IReadOnlyDictionary<string, SizePack>? sizeBindings
    )
    {
        if (device.Size is not null)
        {
            return device.Size;
        }

        if (
            device.SizeName is not null
            && sizeBindings is not null
            && sizeBindings.TryGetValue(device.SizeName, out var pack)
        )
        {
            return pack;
        }

        return null;
    }

    public static Dictionary<string, string> BuildParamExpressions(
        DeviceDeclaration device,
        PrimitiveDefinition primitive,
        IReadOnlyDictionary<string, SizePack>? sizeBindings = null
    )
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        var sizeParam = primitive.SizeParameter;
        var sizePack = ResolveSizePack(device, sizeBindings);
        var sizeName = device.SizeName;

        foreach (var (key, expr) in primitive.Params)
        {
            var rendered = expr;
            if (!string.IsNullOrWhiteSpace(sizeParam))
            {
                if (sizePack is not null)
                {
                    rendered = ReplaceSizeFields(rendered, sizeParam, sizePack);
                }
                else if (!string.IsNullOrWhiteSpace(sizeName))
                {
                    rendered = ReplaceSizeParamName(rendered, sizeParam, sizeName);
                }
            }
            parameters[key] = rendered;
        }

        return parameters;
    }

    public static IReadOnlyCollection<string> GetSizeFields(PrimitiveDefinition primitive)
    {
        if (string.IsNullOrWhiteSpace(primitive.SizeParameter))
        {
            return Array.Empty<string>();
        }

        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var expr in primitive.Params.Values)
        {
            foreach (Match match in SizeFieldPattern.Matches(expr))
            {
                var sizeName = match.Groups["size"].Value;
                if (!sizeName.Equals(primitive.SizeParameter, StringComparison.Ordinal))
                {
                    continue;
                }

                fields.Add(match.Groups["field"].Value);
            }
        }

        return fields;
    }

    private static string ReplaceSizeFields(string expression, string sizeParam, SizePack pack)
    {
        return SizeFieldPattern.Replace(
            expression,
            match =>
            {
                var sizeName = match.Groups["size"].Value;
                var field = match.Groups["field"].Value;
                if (!sizeName.Equals(sizeParam, StringComparison.Ordinal))
                {
                    return match.Value;
                }

                return pack.Entries.TryGetValue(field, out var value) ? value : match.Value;
            }
        );
    }

    private static string ReplaceSizeParamName(string expression, string sizeParam, string sizeName)
    {
        return SizeFieldPattern.Replace(
            expression,
            match =>
            {
                var matchSize = match.Groups["size"].Value;
                if (!matchSize.Equals(sizeParam, StringComparison.Ordinal))
                {
                    return match.Value;
                }

                return $"{sizeName}.{match.Groups["field"].Value}";
            }
        );
    }
}
