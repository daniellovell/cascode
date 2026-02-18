namespace Cascode.Render.Analysis;

using Cascode.Language;

public sealed record InlineInstanceGroup(
    string InstanceId,
    string CircuitType,
    IReadOnlyList<string> DeviceIds
);

/// <summary>
/// Metadata for a non-inline instance rendered as an opaque block.
/// </summary>
public sealed record InstanceBlockInfo(
    string InstanceId,
    string CircuitType,
    IReadOnlyList<string> SignalPortNames
);

public sealed record FlattenedCircuit(
    Circuit RootCircuit,
    IReadOnlyDictionary<string, DeviceDeclaration> Devices,
    IReadOnlySet<string> InternalNets,
    IReadOnlyList<InlineInstanceGroup> InlineInstanceGroups,
    IReadOnlyList<InstanceBlockInfo> InstanceBlocks
);

public static class CircuitFlattener
{
    public static FlattenedCircuit Flatten(
        Circuit circuit,
        CascodeDocument document,
        CircuitResolutionResult? resolution = null
    )
    {
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(document);

        var circuitsByName = document.Circuits.ToDictionary(c => c.Name, StringComparer.Ordinal);
        var devices = new Dictionary<string, DeviceDeclaration>(StringComparer.Ordinal);
        var internalNets = new HashSet<string>(StringComparer.Ordinal);
        var groups = new List<InlineInstanceGroup>();
        var instanceBlocks = new List<InstanceBlockInfo>();

        if (circuit.Fill is null)
        {
            return new FlattenedCircuit(circuit, devices, internalNets, groups, instanceBlocks);
        }

        foreach (var net in circuit.Fill.Nets)
        {
            internalNets.Add(net.Id);
        }

        foreach (var device in circuit.Fill.Devices)
        {
            devices[device.Id] = device;
        }

        foreach (var instance in circuit.Fill.Instances)
        {
            if (!circuitsByName.TryGetValue(instance.Type, out var targetCircuit))
            {
                continue;
            }

            if (!targetCircuit.Inline)
            {
                CollectInstanceBlock(
                    instance,
                    targetCircuit,
                    circuit,
                    resolution,
                    devices,
                    instanceBlocks
                );
                continue;
            }

            var expandedDeviceIds = ExpandInlineCircuit(
                instance,
                targetCircuit,
                hierarchyPath: new List<string>(),
                parentNetSubstitutions: new Dictionary<string, string>(StringComparer.Ordinal),
                parentCircuitResolution: resolution,
                circuitsByName,
                devices,
                internalNets,
                groups
            );

            if (expandedDeviceIds.Count > 0)
            {
                groups.Add(
                    new InlineInstanceGroup(
                        string.Join('.', new[] { instance.Id }),
                        instance.Type,
                        expandedDeviceIds
                    )
                );
            }
        }

        return new FlattenedCircuit(circuit, devices, internalNets, groups, instanceBlocks);
    }

    private static void CollectInstanceBlock(
        InstanceDeclaration instance,
        Circuit targetCircuit,
        Circuit parentCircuit,
        CircuitResolutionResult? resolution,
        Dictionary<string, DeviceDeclaration> devices,
        List<InstanceBlockInfo> instanceBlocks
    )
    {
        var substitutions = BuildNetSubstitutions(instance, targetCircuit, resolution);

        var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (portName, netName) in substitutions)
        {
            bindings[portName] = netName;
        }

        devices[instance.Id] = new DeviceDeclaration
        {
            Id = instance.Id,
            DeviceType = "instance",
            Bindings = bindings,
        };

        var signalPorts = targetCircuit
            .Ports.Select(p => p.Name)
            .Where(portName =>
                bindings.TryGetValue(portName, out var mappedNet)
                && !string.IsNullOrWhiteSpace(mappedNet)
                && !parentCircuit.Supplies.Contains(mappedNet)
                && !parentCircuit.Grounds.Contains(mappedNet)
            )
            .ToList();

        instanceBlocks.Add(new InstanceBlockInfo(instance.Id, instance.Type, signalPorts));
    }

    private static List<string> ExpandInlineCircuit(
        InstanceDeclaration instance,
        Circuit inlineCircuit,
        List<string> hierarchyPath,
        Dictionary<string, string> parentNetSubstitutions,
        CircuitResolutionResult? parentCircuitResolution,
        IReadOnlyDictionary<string, Circuit> circuitsByName,
        Dictionary<string, DeviceDeclaration> devices,
        HashSet<string> internalNets,
        List<InlineInstanceGroup> groups
    )
    {
        var currentPath = new List<string>(hierarchyPath) { instance.Id };
        var prefix = string.Join('.', currentPath);

        var localSubstitutions = BuildNetSubstitutions(
            instance,
            inlineCircuit,
            parentCircuitResolution
        );
        var netSubstitutions = ComposeNetSubstitutions(parentNetSubstitutions, localSubstitutions);

        var inlineInternalNets = new HashSet<string>(StringComparer.Ordinal);
        if (inlineCircuit.Fill?.Nets is not null)
        {
            foreach (var net in inlineCircuit.Fill.Nets)
            {
                inlineInternalNets.Add(net.Id);
                internalNets.Add($"{prefix}.{net.Id}");
            }
        }

        AddPrefixedInternalNetSubstitutions(prefix, inlineInternalNets, netSubstitutions);

        var deviceIdsInThisInstance = new List<string>();

        if (inlineCircuit.Fill?.Devices is not null)
        {
            foreach (
                var device in inlineCircuit.Fill.Devices.OrderBy(d => d.Id, StringComparer.Ordinal)
            )
            {
                var flattenedId = $"{prefix}.{device.Id}";
                devices[flattenedId] = FlattenDevice(
                    device,
                    flattenedId,
                    currentPath,
                    netSubstitutions,
                    inlineInternalNets
                );
                deviceIdsInThisInstance.Add(flattenedId);
            }
        }

        if (inlineCircuit.Fill?.Instances is not null)
        {
            foreach (
                var nested in inlineCircuit.Fill.Instances.OrderBy(
                    i => i.Id,
                    StringComparer.Ordinal
                )
            )
            {
                if (!circuitsByName.TryGetValue(nested.Type, out var nestedCircuit))
                {
                    continue;
                }

                if (!nestedCircuit.Inline)
                {
                    continue;
                }

                var nestedDeviceIds = ExpandInlineCircuit(
                    nested,
                    nestedCircuit,
                    currentPath,
                    netSubstitutions,
                    parentCircuitResolution: null,
                    circuitsByName,
                    devices,
                    internalNets,
                    groups
                );

                if (nestedDeviceIds.Count > 0)
                {
                    var nestedPrefix = $"{prefix}.{nested.Id}";
                    groups.Add(new InlineInstanceGroup(nestedPrefix, nested.Type, nestedDeviceIds));
                    deviceIdsInThisInstance.AddRange(nestedDeviceIds);
                }
            }
        }

        return deviceIdsInThisInstance;
    }

    private static DeviceDeclaration FlattenDevice(
        DeviceDeclaration device,
        string flattenedId,
        IReadOnlyList<string> hierarchyPath,
        Dictionary<string, string> substitutions,
        HashSet<string> internalNets
    )
    {
        var flattenedBindings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (terminal, netName) in device.Bindings)
        {
            flattenedBindings[terminal] = SubstituteNet(
                netName,
                hierarchyPath,
                substitutions,
                internalNets
            );
        }

        return new DeviceDeclaration
        {
            Id = flattenedId,
            DeviceType = device.DeviceType,
            Primitive = device.Primitive,
            SizeName = device.SizeName,
            Size = device.Size,
            Bindings = flattenedBindings,
        };
    }

    private static void AddPrefixedInternalNetSubstitutions(
        string prefix,
        HashSet<string> inlineInternalNets,
        Dictionary<string, string> netSubstitutions
    )
    {
        foreach (var netId in inlineInternalNets)
        {
            netSubstitutions[netId] = $"{prefix}.{netId}";
        }
    }

    private static Dictionary<string, string> BuildNetSubstitutions(
        InstanceDeclaration instance,
        Circuit inlineCircuit,
        CircuitResolutionResult? resolution
    )
    {
        var substitutions = new Dictionary<string, string>(StringComparer.Ordinal);
        ResolveBindings(
            inlineCircuit.Ports.Select(p => p.Name),
            instance,
            resolution,
            substitutions
        );
        ResolveBindings(inlineCircuit.Supplies, instance, resolution, substitutions);
        ResolveBindings(inlineCircuit.Grounds, instance, resolution, substitutions);
        return substitutions;
    }

    private static void ResolveBindings(
        IEnumerable<string> names,
        InstanceDeclaration instance,
        CircuitResolutionResult? resolution,
        Dictionary<string, string> substitutions
    )
    {
        foreach (var name in names)
        {
            if (instance.Bindings.TryGetValue(name, out var boundNet))
            {
                substitutions[name] =
                    resolution?.NetToRepresentative.GetValueOrDefault(boundNet, boundNet)
                    ?? boundNet;
                continue;
            }

            if (
                resolution?.TerminalToNet.TryGetValue($"{instance.Id}.{name}", out var resolvedNet)
                == true
            )
            {
                substitutions[name] = resolvedNet;
            }
        }
    }

    private static Dictionary<string, string> ComposeNetSubstitutions(
        Dictionary<string, string> parentSubstitutions,
        Dictionary<string, string> localSubstitutions
    )
    {
        var composed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, boundNet) in localSubstitutions)
        {
            composed[name] = parentSubstitutions.TryGetValue(boundNet, out var parentBoundNet)
                ? parentBoundNet
                : boundNet;
        }
        return composed;
    }

    private static string SubstituteNet(
        string netName,
        IReadOnlyList<string> hierarchyPath,
        Dictionary<string, string> substitutions,
        HashSet<string> internalNets
    )
    {
        if (substitutions.TryGetValue(netName, out var boundNet))
        {
            return boundNet;
        }

        if (internalNets.Contains(netName))
        {
            var prefix = string.Join('.', hierarchyPath);
            return $"{prefix}.{netName}";
        }

        return netName;
    }
}
