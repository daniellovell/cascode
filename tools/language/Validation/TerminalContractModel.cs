using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language.Validation;

internal sealed record TerminalLeaf(string Path, string RelativePath, string LeafType);

internal sealed record TerminalContract(
    string Name,
    string Type,
    PortDirection? Direction,
    IReadOnlyList<TerminalLeaf> Leaves
);

internal static class TerminalContractModel
{
    public static IReadOnlyDictionary<string, TerminalContract> ForCircuit(Circuit circuit)
    {
        ArgumentNullException.ThrowIfNull(circuit);

        var terminals = BuildFromPorts(circuit.Ports);
        foreach (var supply in circuit.Supplies)
        {
            terminals[supply] = BuildRail(supply, "supply");
        }

        foreach (var ground in circuit.Grounds)
        {
            terminals[ground] = BuildRail(ground, "ground");
        }

        return terminals;
    }

    public static IReadOnlyDictionary<string, TerminalContract> ForInterface(
        TraitDefinition interfaceDef
    )
    {
        ArgumentNullException.ThrowIfNull(interfaceDef);
        return BuildFromPorts(interfaceDef.Ports);
    }

    public static string GetRootName(string pinRef)
    {
        var dot = pinRef.IndexOf('.', StringComparison.Ordinal);
        return dot < 0 ? pinRef : pinRef[..dot];
    }

    public static IReadOnlyList<TerminalLeaf> SelectLeaves(
        IReadOnlyList<TerminalLeaf> leaves,
        string pinRef
    )
    {
        if (!pinRef.Contains('.', StringComparison.Ordinal))
        {
            return leaves;
        }

        return leaves
            .Where(l =>
                l.Path.Equals(pinRef, StringComparison.OrdinalIgnoreCase)
                || l.Path.StartsWith(pinRef + ".", StringComparison.OrdinalIgnoreCase)
            )
            .ToList();
    }

    private static Dictionary<string, TerminalContract> BuildFromPorts(
        IEnumerable<PortDeclaration> ports
    )
    {
        return ports
            .GroupBy(port => GetRootName(port.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var leaves = group
                        .OrderBy(port => port.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(port => new TerminalLeaf(
                            port.Name,
                            GetRelativePath(group.Key, port.Name),
                            port.Type
                        ))
                        .ToList();
                    var firstPort = group.First();
                    var rootType =
                        leaves.Count == 1 && !leaves[0].Path.Contains('.')
                            ? leaves[0].LeafType
                            : group.Key;
                    return new TerminalContract(group.Key, rootType, firstPort.Direction, leaves);
                },
                StringComparer.OrdinalIgnoreCase
            );
    }

    private static TerminalContract BuildRail(string name, string type)
    {
        return new TerminalContract(
            name,
            type,
            null,
            new List<TerminalLeaf> { new(name, string.Empty, type) }
        );
    }

    private static string GetRelativePath(string rootName, string path)
    {
        if (path.Equals(rootName, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return path[(rootName.Length + 1)..];
    }
}
