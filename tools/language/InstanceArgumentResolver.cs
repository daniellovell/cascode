using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language;

internal sealed class ResolvedInstanceArguments
{
    public Dictionary<string, string> Parameters { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, SizePack> Sizes { get; } = new(StringComparer.Ordinal);
}

internal static class InstanceArgumentResolver
{
    public static ResolvedInstanceArguments Resolve(
        Circuit circuit,
        IReadOnlyDictionary<string, string> parentParameterBindings,
        IReadOnlyDictionary<string, SizePack> parentSizeBindings,
        InstanceDeclaration? instance
    )
    {
        var resolved = new ResolvedInstanceArguments();

        foreach (var param in circuit.Parameters)
        {
            var expr = ToExpression(param.Default);
            if (!string.IsNullOrWhiteSpace(expr))
            {
                resolved.Parameters[param.Name] = expr;
            }
        }

        foreach (var size in circuit.Sizes)
        {
            if (size.Default is not null)
            {
                resolved.Sizes[size.Name] = size.Default;
            }
        }

        if (circuit.Fill?.Sizes is { Count: > 0 })
        {
            foreach (var size in circuit.Fill.Sizes)
            {
                if (size.Default is not null)
                {
                    resolved.Sizes[size.Name] = size.Default;
                }
            }
        }

        if (instance is null)
        {
            return resolved;
        }

        var declaredParameterNames = circuit
            .Parameters.Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);
        var declaredSizeNames = circuit.Sizes.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var (name, paramValue) in instance.Params)
        {
            if (
                declaredParameterNames.Contains(name)
                && TryResolveForwardedParameter(
                    parentParameterBindings,
                    paramValue,
                    out var forwardedParameter
                )
            )
            {
                resolved.Parameters[name] = forwardedParameter;
                continue;
            }

            if (
                declaredSizeNames.Contains(name)
                && TryResolveForwardedSize(parentSizeBindings, paramValue, out var forwardedSize)
            )
            {
                resolved.Sizes[name] = forwardedSize;
                continue;
            }

            var expr = ToExpression(paramValue);
            if (!string.IsNullOrWhiteSpace(expr))
            {
                resolved.Parameters[name] = expr;
            }
        }

        foreach (var (name, pack) in instance.Sizes)
        {
            resolved.Sizes[name] = pack;
        }

        return resolved;
    }

    public static bool HasParameterAssignment(InstanceDeclaration instance, string parameterName)
    {
        return instance.Params.ContainsKey(parameterName);
    }

    public static bool HasSizeAssignment(
        InstanceDeclaration instance,
        string sizeName,
        IReadOnlySet<string> availableSizeNames
    )
    {
        return instance.Sizes.ContainsKey(sizeName)
            || TryGetForwardedReference(instance, sizeName, availableSizeNames, out _);
    }

    private static bool TryResolveForwardedParameter(
        IReadOnlyDictionary<string, string> parentParameterBindings,
        ParamValue paramValue,
        out string expression
    )
    {
        expression = string.Empty;
        if (!ParamValueParser.TryGetIdentifierReference(paramValue, out var referenceName))
        {
            return false;
        }

        if (!parentParameterBindings.TryGetValue(referenceName, out var resolvedExpression))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(resolvedExpression))
        {
            return false;
        }

        expression = resolvedExpression;
        return true;
    }

    private static bool TryResolveForwardedSize(
        IReadOnlyDictionary<string, SizePack> parentSizeBindings,
        ParamValue paramValue,
        out SizePack pack
    )
    {
        pack = new SizePack();
        if (!ParamValueParser.TryGetIdentifierReference(paramValue, out var referenceName))
        {
            return false;
        }

        if (!parentSizeBindings.TryGetValue(referenceName, out var resolvedPack))
        {
            return false;
        }

        pack = CloneSizePack(resolvedPack);
        return true;
    }

    private static bool TryGetForwardedReference(
        InstanceDeclaration instance,
        string argumentName,
        IReadOnlySet<string> availableSizeNames,
        out string referenceName
    )
    {
        referenceName = string.Empty;
        if (!instance.Params.TryGetValue(argumentName, out var value))
        {
            return false;
        }

        if (!ParamValueParser.TryGetIdentifierReference(value, out var candidate))
        {
            return false;
        }

        // Size alias forwarding only accepts bare identifiers (e.g. Core=Pack),
        // not member expressions like Pack.W.
        if (candidate.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        if (!availableSizeNames.Contains(candidate))
        {
            return false;
        }

        referenceName = candidate;
        return true;
    }

    private static SizePack CloneSizePack(SizePack pack)
    {
        return new SizePack { Entries = new Dictionary<string, string>(pack.Entries) };
    }

    private static string? ToExpression(ParamValue? value)
    {
        if (value is null)
        {
            return null;
        }

        return value.Numeric ?? value.Symbolic ?? value.Literal;
    }
}
