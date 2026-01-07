using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.ACIR;

public sealed partial class AttachResolver
{
    private void EnsureEndpointNode(
        InstanceDeclaration instance,
        string terminalPath,
        string endpointId,
        ResolutionContext context
    )
    {
        if (context.UnionFind.Contains(endpointId))
        {
            return;
        }

        var domain = ResolveInstanceTerminalDomain(instance, terminalPath) ?? DefaultDomain;
        AddEndpointNode(context, endpointId, domain);
    }

    private string? ResolveInstanceTerminalDomain(InstanceDeclaration instance, string terminalPath)
    {
        if (!_circuitsByName.TryGetValue(instance.Type, out var targetCircuit))
        {
            return null;
        }

        return TryResolveTerminalDomain(targetCircuit, terminalPath);
    }

    private string? TryResolveTerminalDomain(Circuit circuit, string terminalPath)
    {
        var parts = terminalPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        var baseName = parts[0];
        if (circuit.Supplies.Contains(baseName))
        {
            return parts.Length == 1 ? PowerDomain : null;
        }

        if (circuit.Grounds.Contains(baseName))
        {
            return parts.Length == 1 ? GroundDomain : null;
        }

        var port = circuit.Ports.FirstOrDefault(p =>
            p.Name.Equals(baseName, StringComparison.Ordinal)
        );
        if (port is null)
        {
            return null;
        }

        if (parts.Length == 1)
        {
            return port.Type;
        }

        return ResolveBundleFieldDomain(port.Type, parts, 1);
    }

    private string? ResolveBundleFieldDomain(string typeName, string[] pathSegments, int index)
    {
        if (index >= pathSegments.Length)
        {
            return typeName;
        }

        if (!_bundleTypesByName.TryGetValue(typeName, out var bundle))
        {
            return null;
        }

        var fieldName = pathSegments[index];
        if (!bundle.Fields.TryGetValue(fieldName, out var fieldType))
        {
            return null;
        }

        return ResolveBundleFieldDomain(fieldType, pathSegments, index + 1);
    }

    private IEnumerable<(string TerminalPath, string Domain)> ExpandPortTerminalPaths(
        PortDeclaration port
    )
    {
        return ExpandBundleTerminalPaths(port.Name, port.Type);
    }

    private IEnumerable<(string TerminalPath, string Domain)> ExpandBundleTerminalPaths(
        string basePath,
        string typeName
    )
    {
        if (!_bundleTypesByName.TryGetValue(typeName, out var bundle))
        {
            yield return (basePath, typeName);
            yield break;
        }

        foreach (var field in bundle.Fields.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            var path = $"{basePath}.{field.Key}";
            foreach (var entry in ExpandBundleTerminalPaths(path, field.Value))
            {
                yield return entry;
            }
        }
    }
}
