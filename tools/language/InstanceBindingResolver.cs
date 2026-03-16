using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Cascode.Language;

/// <summary>
/// Context produced by resolving instance argument bindings and building lookup structures
/// for expression evaluation during SPICE emission.
/// </summary>
internal sealed class BindingContext
{
    public IReadOnlyDictionary<string, string> ParamBindings { get; }
    public IReadOnlyDictionary<string, SizePack> SizeBindings { get; }
    public IReadOnlyDictionary<string, string> LookupParameters { get; }
    public IReadOnlyDictionary<string, SizePack> LookupSizes { get; }
    public ExpressionContext ExpressionContext { get; }

    internal BindingContext(
        IReadOnlyDictionary<string, string> paramBindings,
        IReadOnlyDictionary<string, SizePack> sizeBindings,
        IReadOnlyDictionary<string, string> lookupParameters,
        IReadOnlyDictionary<string, SizePack> lookupSizes,
        ExpressionContext expressionContext
    )
    {
        ParamBindings = paramBindings;
        SizeBindings = sizeBindings;
        LookupParameters = lookupParameters;
        LookupSizes = lookupSizes;
        ExpressionContext = expressionContext;
    }
}

/// <summary>
/// Context for evaluating parameter and size-pack expressions during SPICE emission.
/// Resolves identifiers against parameter bindings and size-pack field references.
/// </summary>
internal sealed class ExpressionContext
{
    private readonly IReadOnlyDictionary<string, string> _paramBindings;
    private readonly IReadOnlyDictionary<string, SizePack> _sizeBindings;
    private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _resolving = new(StringComparer.Ordinal);

    internal ExpressionContext(
        IReadOnlyDictionary<string, string> paramBindings,
        IReadOnlyDictionary<string, SizePack> sizeBindings
    )
    {
        _paramBindings = paramBindings;
        _sizeBindings = sizeBindings;
    }

    public string Evaluate(string expression)
    {
        return ExpressionEvaluator.Evaluate(expression, ResolveIdentifier);
    }

    private string ResolveIdentifier(string identifier)
    {
        if (_cache.TryGetValue(identifier, out var cached))
        {
            return cached;
        }

        if (!_resolving.Add(identifier))
        {
            throw new ArgumentException($"Circular parameter reference detected: {identifier}");
        }

        if (TryResolveSizeField(identifier, out var sizeExpr))
        {
            var resolved = Evaluate(sizeExpr);
            _cache[identifier] = resolved;
            _resolving.Remove(identifier);
            return resolved;
        }

        if (_paramBindings.TryGetValue(identifier, out var binding) && binding is not null)
        {
            var resolved = Evaluate(binding);
            _cache[identifier] = resolved;
            _resolving.Remove(identifier);
            return resolved;
        }

        _resolving.Remove(identifier);
        throw new ArgumentException($"Undefined parameter reference: {identifier}");
    }

    private bool TryResolveSizeField(string identifier, out string expression)
    {
        var dotIndex = identifier.IndexOf('.');
        if (dotIndex <= 0)
        {
            expression = string.Empty;
            return false;
        }

        var sizeName = identifier[..dotIndex];
        var field = identifier[(dotIndex + 1)..];
        if (_sizeBindings.TryGetValue(sizeName, out var pack))
        {
            if (pack.Entries.TryGetValue(field, out var expr))
            {
                expression = expr;
                return true;
            }

            if (field.Equals("M", StringComparison.OrdinalIgnoreCase))
            {
                expression = "1";
                return true;
            }
        }

        expression = string.Empty;
        return false;
    }
}

/// <summary>
/// Resolves instance argument/size bindings and builds the parameter/size lookup structures
/// used for expression evaluation during SPICE emission.
/// </summary>
internal static class InstanceBindingResolver
{
    /// <summary>
    /// Resolves bindings for a circuit instance and returns a context containing lookup
    /// structures and expression evaluation context.
    /// </summary>
    public static BindingContext Resolve(
        Circuit circuit,
        IReadOnlyDictionary<string, string> parentParameters,
        IReadOnlyDictionary<string, SizePack> parentSizes,
        InstanceDeclaration? instance
    )
    {
        var argumentBindings = InstanceArgumentResolver.Resolve(
            circuit,
            parentParameters,
            parentSizes,
            instance
        );
        var normalizedParams = NormalizeParameters(parentParameters, argumentBindings.Parameters);
        var normalizedSizes = NormalizeSizes(parentSizes, argumentBindings.Sizes);
        var lookupParams = BuildLookupParameters(parentParameters, normalizedParams);
        var lookupSizes = BuildLookupSizes(parentSizes, normalizedSizes);
        var expressionContext = new ExpressionContext(lookupParams, lookupSizes);

        return new BindingContext(
            normalizedParams,
            normalizedSizes,
            lookupParams,
            lookupSizes,
            expressionContext
        );
    }

    /// <summary>
    /// Creates an expression context from resolved parameter and size bindings.
    /// Used when emitting a pre-resolved variant.
    /// </summary>
    public static ExpressionContext CreateContext(
        IReadOnlyDictionary<string, string> paramBindings,
        IReadOnlyDictionary<string, SizePack> sizeBindings
    )
    {
        return new ExpressionContext(paramBindings, sizeBindings);
    }

    private static Dictionary<string, string> BuildLookupParameters(
        IReadOnlyDictionary<string, string> parentParameters,
        IReadOnlyDictionary<string, string> localParameters
    )
    {
        var lookup = new Dictionary<string, string>(parentParameters, StringComparer.Ordinal);
        foreach (var (name, expression) in localParameters)
        {
            lookup[name] = expression;
        }

        return lookup;
    }

    private static Dictionary<string, SizePack> BuildLookupSizes(
        IReadOnlyDictionary<string, SizePack> parentSizes,
        IReadOnlyDictionary<string, SizePack> localSizes
    )
    {
        var lookup = CloneSizeBindings(parentSizes);
        foreach (var (name, pack) in localSizes)
        {
            lookup[name] = CloneSizePack(pack);
        }

        return lookup;
    }

    private static Dictionary<string, string> NormalizeParameters(
        IReadOnlyDictionary<string, string> parentParameters,
        IReadOnlyDictionary<string, string> localParameters
    )
    {
        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, expression) in localParameters)
        {
            normalized[name] = RewriteParameterExpression(name, expression, parentParameters);
        }

        return normalized;
    }

    private static Dictionary<string, SizePack> NormalizeSizes(
        IReadOnlyDictionary<string, SizePack> parentSizes,
        IReadOnlyDictionary<string, SizePack> localSizes
    )
    {
        var normalized = new Dictionary<string, SizePack>(StringComparer.Ordinal);
        foreach (var (name, pack) in localSizes)
        {
            var inheritedPack = parentSizes.TryGetValue(name, out var parentPack)
                ? parentPack
                : null;
            var rewrittenPack = inheritedPack is null
                ? new SizePack()
                : CloneSizePack(inheritedPack);
            foreach (var (field, expression) in pack.Entries)
            {
                rewrittenPack.Entries[field] = RewriteSizeExpression(
                    name,
                    expression,
                    inheritedPack
                );
            }

            normalized[name] = rewrittenPack;
        }

        return normalized;
    }

    private static string RewriteParameterExpression(
        string parameterName,
        string expression,
        IReadOnlyDictionary<string, string> parentParameters
    )
    {
        if (
            !parentParameters.TryGetValue(parameterName, out var inheritedExpression)
            || string.IsNullOrWhiteSpace(inheritedExpression)
        )
        {
            return expression;
        }

        return ReplaceIdentifierReference(expression, parameterName, inheritedExpression);
    }

    private static string RewriteSizeExpression(
        string sizeName,
        string expression,
        SizePack? inheritedPack
    )
    {
        if (inheritedPack is null)
        {
            return expression;
        }

        var rewritten = expression;
        foreach (var (field, inheritedExpression) in inheritedPack.Entries)
        {
            if (string.IsNullOrWhiteSpace(inheritedExpression))
            {
                continue;
            }

            rewritten = ReplaceQualifiedReference(
                rewritten,
                $"{sizeName}.{field}",
                inheritedExpression
            );
        }

        if (!inheritedPack.Entries.ContainsKey("M"))
        {
            rewritten = ReplaceQualifiedReference(rewritten, $"{sizeName}.M", "1");
        }

        return rewritten;
    }

    private static string ReplaceIdentifierReference(
        string expression,
        string identifier,
        string replacement
    )
    {
        var pattern = $@"(?<![A-Za-z0-9_\.]){Regex.Escape(identifier)}(?![A-Za-z0-9_])";
        return Regex.Replace(expression, pattern, FormatReplacementExpression(replacement));
    }

    private static string ReplaceQualifiedReference(
        string expression,
        string qualifiedReference,
        string replacement
    )
    {
        var pattern = $@"(?<![A-Za-z0-9_]){Regex.Escape(qualifiedReference)}(?![A-Za-z0-9_])";
        return Regex.Replace(expression, pattern, FormatReplacementExpression(replacement));
    }

    private static string FormatReplacementExpression(string expression)
    {
        return IsAtomicExpression(expression) ? expression : $"({expression})";
    }

    private static bool IsAtomicExpression(string expression)
    {
        var trimmed = expression.Trim();
        if (trimmed.Length == 0)
        {
            return true;
        }

        if (
            trimmed.Equals("true", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("false", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("NMOS", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("PMOS", StringComparison.OrdinalIgnoreCase)
        )
        {
            return true;
        }

        return Regex.IsMatch(trimmed, @"^[A-Za-z0-9_.]+$")
            || Regex.IsMatch(trimmed, @"^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?[A-Za-z]*$");
    }

    private static Dictionary<string, SizePack> CloneSizeBindings(
        IReadOnlyDictionary<string, SizePack> sizeBindings
    )
    {
        var clone = new Dictionary<string, SizePack>(StringComparer.Ordinal);
        foreach (var (name, pack) in sizeBindings)
        {
            clone[name] = CloneSizePack(pack);
        }

        return clone;
    }

    private static SizePack CloneSizePack(SizePack pack)
    {
        return new SizePack { Entries = new Dictionary<string, string>(pack.Entries) };
    }
}
