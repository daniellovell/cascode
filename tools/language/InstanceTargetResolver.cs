using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language;

internal sealed class InstanceTargetDefinition
{
    public required IReadOnlyList<PortDeclaration> Ports { get; init; }
    public required IReadOnlyList<CircuitParameter> Parameters { get; init; }
    public required IReadOnlyList<SizeDeclaration> Sizes { get; init; }
    public bool Inline { get; init; }
}

internal static class InstanceTargetResolver
{
    public static bool TryResolveConcreteTarget(
        string typeName,
        IReadOnlyDictionary<string, Circuit> circuitsByName,
        IReadOnlyDictionary<string, PartDefinition> partsByName,
        out InstanceTargetDefinition target
    )
    {
        if (circuitsByName.TryGetValue(typeName, out var circuit))
        {
            target = new InstanceTargetDefinition
            {
                Ports = circuit.Ports,
                Parameters = circuit.Parameters,
                Sizes = circuit.Sizes,
                Inline = circuit.Inline,
            };
            return true;
        }

        if (partsByName.TryGetValue(typeName, out var part))
        {
            target = new InstanceTargetDefinition
            {
                Ports = part.Ports,
                Parameters = part.Parameters,
                Sizes = Array.Empty<SizeDeclaration>(),
            };
            return true;
        }

        target = null!;
        return false;
    }

    public static IReadOnlyDictionary<string, string>? ResolvePortTypes(
        string instanceType,
        IReadOnlyDictionary<string, Circuit> circuitsByName,
        IReadOnlyDictionary<string, PartDefinition> partsByName,
        IReadOnlyDictionary<string, TraitDefinition> traitsByName
    )
    {
        if (TryResolveConcreteTarget(instanceType, circuitsByName, partsByName, out var target))
        {
            return target.Ports.ToDictionary(p => p.Name, p => p.Type, StringComparer.Ordinal);
        }

        if (traitsByName.TryGetValue(instanceType, out var trait))
        {
            return trait.Ports.ToDictionary(p => p.Name, p => p.Type, StringComparer.Ordinal);
        }

        return null;
    }
}
