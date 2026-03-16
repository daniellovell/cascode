using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language;

public static class CircuitPortExpander
{
    public static IReadOnlyList<PortDeclaration> Expand(Circuit circuit)
    {
        ArgumentNullException.ThrowIfNull(circuit);

        var bindingNets =
            circuit
                .Fill?.Devices.SelectMany(device => device.Bindings.Values)
                .Distinct(StringComparer.Ordinal)
                .ToArray()
            ?? Array.Empty<string>();

        var expanded = new List<PortDeclaration>(circuit.Ports.Count);
        foreach (var port in circuit.Ports)
        {
            var leafPorts = bindingNets
                .Where(netName => netName.StartsWith($"{port.Name}.", StringComparison.Ordinal))
                .OrderBy(netName => netName, StringComparer.Ordinal)
                .ToArray();

            if (leafPorts.Length == 0)
            {
                expanded.Add(port);
                continue;
            }

            expanded.AddRange(
                leafPorts.Select(leafName => new PortDeclaration
                {
                    Name = leafName,
                    Direction = port.Direction,
                    Type = port.Type,
                })
            );
        }

        return expanded;
    }
}
