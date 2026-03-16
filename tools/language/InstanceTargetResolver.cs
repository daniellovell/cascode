using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language;

internal enum InstanceTargetKind
{
    Primitive,
    Part,
    Circuit,
}

internal enum InstanceTargetResolutionError
{
    None,
    Unresolved,
    IncompatibleDeclaredType,
    Ambiguous,
}

internal sealed class InstanceTargetDefinition
{
    public required string Name { get; init; }
    public required InstanceTargetKind Kind { get; init; }
    public required IReadOnlyList<PortDeclaration> Ports { get; init; }
    public required IReadOnlyList<CircuitParameter> Parameters { get; init; }
    public required IReadOnlyList<SizeDeclaration> Sizes { get; init; }
    public PrimitiveDefinition? Primitive { get; init; }
    public PartDefinition? Part { get; init; }
    public Circuit? Circuit { get; init; }
    public bool Inline { get; init; }
}

internal static class InstanceTargetResolver
{
    private static readonly IReadOnlyList<PortDeclaration> NmosPorts =
    [
        PrimitivePort("D"),
        PrimitivePort("G"),
        PrimitivePort("S"),
        PrimitivePort("B"),
    ];

    private static readonly IReadOnlyList<PortDeclaration> TwoTerminalPorts =
    [
        PrimitivePort("P"),
        PrimitivePort("N"),
    ];

    private static readonly IReadOnlyList<PortDeclaration> DiodePorts =
    [
        PrimitivePort("A"),
        PrimitivePort("K"),
    ];

    public static string GetReferenceName(string typeName)
    {
        var lastDot = typeName.LastIndexOf('.');
        return lastDot >= 0 ? typeName[(lastDot + 1)..] : typeName;
    }

    public static bool TryResolveConcreteTarget(
        string typeName,
        string? declaredType,
        IReadOnlyDictionary<string, Circuit> circuitsByName,
        IReadOnlyDictionary<string, PartDefinition> partsByName,
        IReadOnlyDictionary<string, PrimitiveDefinition> primitivesByName,
        out InstanceTargetDefinition target,
        out InstanceTargetResolutionError error
    )
    {
        var shortName = GetReferenceName(typeName);
        var candidates = new List<InstanceTargetDefinition>();

        if (primitivesByName.TryGetValue(shortName, out var primitive))
        {
            candidates.Add(
                new InstanceTargetDefinition
                {
                    Name = primitive.Name,
                    Kind = InstanceTargetKind.Primitive,
                    Ports = GetPrimitivePorts(primitive.Kind),
                    Parameters = Array.Empty<CircuitParameter>(),
                    Sizes = Array.Empty<SizeDeclaration>(),
                    Primitive = primitive,
                }
            );
        }

        if (partsByName.TryGetValue(shortName, out var part))
        {
            candidates.Add(
                new InstanceTargetDefinition
                {
                    Name = part.Name,
                    Kind = InstanceTargetKind.Part,
                    Ports = part.Ports,
                    Parameters = part.Parameters,
                    Sizes = Array.Empty<SizeDeclaration>(),
                    Part = part,
                }
            );
        }

        if (circuitsByName.TryGetValue(shortName, out var circuit))
        {
            candidates.Add(
                new InstanceTargetDefinition
                {
                    Name = circuit.Name,
                    Kind = InstanceTargetKind.Circuit,
                    Ports = circuit.Ports,
                    Parameters = circuit.Parameters,
                    Sizes = circuit.Sizes,
                    Circuit = circuit,
                    Inline = circuit.Inline,
                }
            );
        }

        if (candidates.Count == 0)
        {
            target = null!;
            error = InstanceTargetResolutionError.Unresolved;
            return false;
        }

        var compatible = candidates
            .Where(candidate => IsDeclaredTypeCompatible(candidate, declaredType))
            .ToList();
        if (compatible.Count == 1)
        {
            target = compatible[0];
            error = InstanceTargetResolutionError.None;
            return true;
        }

        if (compatible.Count == 0)
        {
            target = null!;
            error = InstanceTargetResolutionError.IncompatibleDeclaredType;
            return false;
        }

        target = null!;
        error = InstanceTargetResolutionError.Ambiguous;
        return false;
    }

    public static IReadOnlyDictionary<string, string>? ResolvePortTypes(
        string instanceType,
        string? declaredType,
        IReadOnlyDictionary<string, Circuit> circuitsByName,
        IReadOnlyDictionary<string, PartDefinition> partsByName,
        IReadOnlyDictionary<string, PrimitiveDefinition> primitivesByName,
        IReadOnlyDictionary<string, TraitDefinition> traitsByName
    )
    {
        if (
            TryResolveConcreteTarget(
                instanceType,
                declaredType,
                circuitsByName,
                partsByName,
                primitivesByName,
                out var target,
                out _
            )
        )
        {
            return target.Ports.ToDictionary(p => p.Name, p => p.Type, StringComparer.Ordinal);
        }

        var shortName = GetReferenceName(instanceType);
        if (traitsByName.TryGetValue(shortName, out var trait))
        {
            return trait.Ports.ToDictionary(p => p.Name, p => p.Type, StringComparer.Ordinal);
        }

        return null;
    }

    private static bool IsDeclaredTypeCompatible(
        InstanceTargetDefinition target,
        string? declaredType
    )
    {
        if (string.IsNullOrWhiteSpace(declaredType))
        {
            return true;
        }

        if (declaredType.Equals("Some", StringComparison.Ordinal))
        {
            return false;
        }

        if (declaredType.Equals(target.Name, StringComparison.Ordinal))
        {
            return true;
        }

        return target.Kind switch
        {
            InstanceTargetKind.Primitive => declaredType.Equals(
                target.Primitive!.Kind,
                StringComparison.Ordinal
            ),
            InstanceTargetKind.Part => target.Part!.Implements.Contains(
                declaredType,
                StringComparer.Ordinal
            ),
            InstanceTargetKind.Circuit => target.Circuit!.Traits?.Contains(
                declaredType,
                StringComparer.Ordinal
            ) == true,
            _ => false,
        };
    }

    private static IReadOnlyList<PortDeclaration> GetPrimitivePorts(string primitiveKind) =>
        primitiveKind switch
        {
            "NMOS" => NmosPorts,
            "PMOS" => NmosPorts,
            "Resistor" => TwoTerminalPorts,
            "Capacitor" => TwoTerminalPorts,
            "Inductor" => TwoTerminalPorts,
            "Diode" => DiodePorts,
            _ => Array.Empty<PortDeclaration>(),
        };

    private static PortDeclaration PrimitivePort(string name) =>
        new()
        {
            Direction = PortDirection.Io,
            Name = name,
            Type = "analog",
        };
}
