using System;
using System.Collections.Generic;

namespace Cascode.Language.BenchRuntime.Netlist;

public readonly record struct BenchNetId(int Value)
{
    public override string ToString() => Value.ToString();
}

public sealed record BenchNet(BenchNetId Id, string SpiceName, bool IsSpice0);

public sealed record BenchComponent(
    string Id,
    string Type,
    IReadOnlyDictionary<string, BenchNetId> Pins
);

public sealed record BenchNetAttributes(
    bool IsSpice0,
    bool HasIndependentVoltageSource,
    bool HasLoadElement,
    bool HasGroundTieElement
);

public sealed class BenchNetlist
{
    public BenchNetlist(
        IReadOnlyList<BenchNet> nets,
        IReadOnlyList<BenchComponent> components,
        IReadOnlyDictionary<BenchNode, BenchNetId> netIdByNode,
        IReadOnlyDictionary<BenchNetId, BenchNetAttributes> attributesByNetId
    )
    {
        ArgumentNullException.ThrowIfNull(nets);
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(netIdByNode);
        ArgumentNullException.ThrowIfNull(attributesByNetId);

        Nets = nets;
        Components = components;
        NetIdByNode = netIdByNode;
        AttributesByNetId = attributesByNetId;
    }

    public IReadOnlyList<BenchNet> Nets { get; }
    public IReadOnlyList<BenchComponent> Components { get; }
    public IReadOnlyDictionary<BenchNode, BenchNetId> NetIdByNode { get; }
    public IReadOnlyDictionary<BenchNetId, BenchNetAttributes> AttributesByNetId { get; }

    public BenchNet GetNet(BenchNetId id)
    {
        return Nets[id.Value];
    }

    public string GetSpiceNet(BenchNode node)
    {
        return GetNet(NetIdByNode[node]).SpiceName;
    }
}
