namespace Cascode.Render.Analysis;

using Cascode.Language;

/// <summary>
/// Represents a connection from a device terminal to a net.
/// </summary>
public readonly record struct TerminalRef(string DeviceId, string Terminal);

/// <summary>
/// Connectivity graph built from an Cascode circuit for layout analysis.
/// </summary>
public sealed class CircuitGraph
{
    private readonly Dictionary<string, List<TerminalRef>> _netConnections;
    private readonly Dictionary<string, DeviceDeclaration> _devices;
    private readonly IReadOnlyList<InlineInstanceGroup> _inlineInstanceGroups;
    private readonly IReadOnlyList<InstanceBlockInfo> _instanceBlocks;
    private readonly HashSet<string> _supplies;
    private readonly HashSet<string> _grounds;
    private readonly HashSet<string> _inputPorts;
    private readonly HashSet<string> _outputPorts;
    private readonly HashSet<string> _biasPorts;
    private readonly HashSet<string> _internalNets;

    private CircuitGraph(
        Dictionary<string, List<TerminalRef>> netConnections,
        Dictionary<string, DeviceDeclaration> devices,
        IReadOnlyList<InlineInstanceGroup> inlineInstanceGroups,
        IReadOnlyList<InstanceBlockInfo> instanceBlocks,
        HashSet<string> supplies,
        HashSet<string> grounds,
        HashSet<string> inputPorts,
        HashSet<string> outputPorts,
        HashSet<string> biasPorts,
        HashSet<string> internalNets
    )
    {
        _netConnections = netConnections;
        _devices = devices;
        _inlineInstanceGroups = inlineInstanceGroups;
        _instanceBlocks = instanceBlocks;
        _supplies = supplies;
        _grounds = grounds;
        _inputPorts = inputPorts;
        _outputPorts = outputPorts;
        _biasPorts = biasPorts;
        _internalNets = internalNets;
    }

    /// <summary>
    /// Net name to list of (deviceId, terminal) connections.
    /// </summary>
    public IReadOnlyDictionary<string, List<TerminalRef>> NetConnections => _netConnections;

    /// <summary>
    /// Device ID to device declaration.
    /// </summary>
    public IReadOnlyDictionary<string, DeviceDeclaration> Devices => _devices;

    public IReadOnlyList<InlineInstanceGroup> InlineInstanceGroups => _inlineInstanceGroups;

    public IReadOnlyList<InstanceBlockInfo> InstanceBlocks => _instanceBlocks;

    /// <summary>
    /// Supply net names (e.g., VDD).
    /// </summary>
    public IReadOnlySet<string> Supplies => _supplies;

    /// <summary>
    /// Ground net names (e.g., GND).
    /// </summary>
    public IReadOnlySet<string> Grounds => _grounds;

    /// <summary>
    /// Input port net names.
    /// </summary>
    public IReadOnlySet<string> InputPorts => _inputPorts;

    /// <summary>
    /// Output port net names.
    /// </summary>
    public IReadOnlySet<string> OutputPorts => _outputPorts;

    /// <summary>
    /// Bias port net names.
    /// </summary>
    public IReadOnlySet<string> BiasPorts => _biasPorts;

    /// <summary>
    /// Internal net names declared in the fill block.
    /// </summary>
    public IReadOnlySet<string> InternalNets => _internalNets;

    /// <summary>
    /// Builds a circuit graph from an Cascode circuit.
    /// </summary>
    public static CircuitGraph Build(Circuit circuit)
    {
        var devices =
            circuit.Fill?.Devices.ToDictionary(d => d.Id, StringComparer.Ordinal)
            ?? new Dictionary<string, DeviceDeclaration>(StringComparer.Ordinal);
        var internalNets =
            circuit.Fill?.Nets.Select(n => n.Id).ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(StringComparer.Ordinal);

        return BuildCore(
            circuit,
            devices,
            internalNets,
            inlineInstanceGroups: Array.Empty<InlineInstanceGroup>(),
            instanceBlocks: Array.Empty<InstanceBlockInfo>()
        );
    }

    public static CircuitGraph Build(FlattenedCircuit flattenedCircuit)
    {
        ArgumentNullException.ThrowIfNull(flattenedCircuit);
        return BuildCore(
            flattenedCircuit.RootCircuit,
            flattenedCircuit.Devices,
            flattenedCircuit.InternalNets,
            flattenedCircuit.InlineInstanceGroups,
            flattenedCircuit.InstanceBlocks
        );
    }

    private static CircuitGraph BuildCore(
        Circuit circuit,
        IReadOnlyDictionary<string, DeviceDeclaration> devices,
        IReadOnlySet<string> internalNets,
        IReadOnlyList<InlineInstanceGroup> inlineInstanceGroups,
        IReadOnlyList<InstanceBlockInfo> instanceBlocks
    )
    {
        var netConnections = new Dictionary<string, List<TerminalRef>>();
        var supplies = new HashSet<string>(circuit.Supplies);
        var grounds = new HashSet<string>(circuit.Grounds);
        var inputPorts = new HashSet<string>();
        var outputPorts = new HashSet<string>();
        var biasPorts = new HashSet<string>();

        foreach (var port in circuit.Ports)
        {
            var domain = port.Type.ToLowerInvariant();
            if (domain == "bias")
            {
                biasPorts.Add(port.Name);
                continue;
            }

            switch (port.Direction)
            {
                case PortDirection.Output:
                    outputPorts.Add(port.Name);
                    break;
                case PortDirection.Input:
                case PortDirection.Io:
                    inputPorts.Add(port.Name);
                    break;
            }
        }

        foreach (var net in internalNets)
        {
            EnsureNet(netConnections, net);
        }

        var deviceMap = devices.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        foreach (var (deviceId, device) in deviceMap)
        {
            foreach (var (terminal, netName) in device.Bindings)
            {
                EnsureNet(netConnections, netName);
                netConnections[netName].Add(new TerminalRef(deviceId, terminal));
            }
        }

        foreach (var supply in supplies)
        {
            EnsureNet(netConnections, supply);
        }
        foreach (var ground in grounds)
        {
            EnsureNet(netConnections, ground);
        }
        foreach (var port in circuit.Ports)
        {
            EnsureNet(netConnections, port.Name);
        }

        return new CircuitGraph(
            netConnections,
            deviceMap,
            inlineInstanceGroups,
            instanceBlocks,
            supplies,
            grounds,
            inputPorts,
            outputPorts,
            biasPorts,
            new HashSet<string>(internalNets, StringComparer.Ordinal)
        );
    }

    /// <summary>
    /// Returns true if the net is a supply or ground.
    /// </summary>
    public bool IsSupplyOrGround(string netName) =>
        _supplies.Contains(netName) || _grounds.Contains(netName);

    /// <summary>
    /// Returns true if the net is a port (input, output, or bias).
    /// </summary>
    public bool IsPort(string netName) =>
        _inputPorts.Contains(netName)
        || _outputPorts.Contains(netName)
        || _biasPorts.Contains(netName);

    /// <summary>
    /// Gets all devices connected to a given net.
    /// </summary>
    public IEnumerable<DeviceDeclaration> GetDevicesOnNet(string netName)
    {
        if (!_netConnections.TryGetValue(netName, out var connections))
        {
            yield break;
        }

        foreach (var conn in connections)
        {
            if (_devices.TryGetValue(conn.DeviceId, out var device))
            {
                yield return device;
            }
        }
    }

    /// <summary>
    /// Gets the net name connected to a specific device terminal.
    /// </summary>
    public string? GetNetForTerminal(string deviceId, string terminal)
    {
        if (!_devices.TryGetValue(deviceId, out var device))
        {
            return null;
        }

        return device.Bindings.TryGetValue(terminal, out var netName) ? netName : null;
    }

    /// <summary>
    /// Returns all device IDs connected to the same net as the given device terminal.
    /// </summary>
    public IEnumerable<string> GetConnectedDevices(string deviceId, string terminal)
    {
        var netName = GetNetForTerminal(deviceId, terminal);
        if (netName == null || !_netConnections.TryGetValue(netName, out var connections))
        {
            yield break;
        }

        foreach (var conn in connections)
        {
            if (conn.DeviceId != deviceId)
            {
                yield return conn.DeviceId;
            }
        }
    }

    /// <summary>
    /// Checks if two nets are directly connected (same net or shorted).
    /// </summary>
    public bool AreNetsConnected(string net1, string net2) => net1 == net2;

    private static void EnsureNet(
        Dictionary<string, List<TerminalRef>> netConnections,
        string netName
    )
    {
        if (!netConnections.ContainsKey(netName))
        {
            netConnections[netName] = new List<TerminalRef>();
        }
    }
}
