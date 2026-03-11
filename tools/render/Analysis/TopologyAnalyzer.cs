namespace Cascode.Render.Analysis;

using Cascode.Language;

/// <summary>
/// Orientation of a passive element in the circuit topology.
/// </summary>
public enum PassiveOrientation
{
    /// <summary>
    /// Part of a vertical current path (VDD to GND).
    /// Examples: load resistor, degeneration resistor, bias resistor to rail.
    /// </summary>
    Vertical,

    /// <summary>
    /// Horizontal connection between nodes at similar vertical positions.
    /// Examples: CMFB resistors, feedback resistors, sensing resistors.
    /// </summary>
    Horizontal,
}

/// <summary>
/// Symmetry type for device groups that should be mirrored horizontally.
/// </summary>
public enum SymmetryType
{
    DiffPair,
    CurrentMirror,
    LoadPair,
    SymmetricPassive,
}

/// <summary>
/// A group of devices that should be placed symmetrically about an axis.
/// </summary>
public sealed record SymmetricGroup(
    IReadOnlyList<string> DeviceIds,
    string PivotNet,
    SymmetryType Type
);

/// <summary>
/// A stage in a multi-stage amplifier, representing a connected component of vertical chains.
/// </summary>
public sealed record Stage(int Index, IReadOnlyList<string> DeviceIds);

/// <summary>
/// Complete topology analysis result.
/// </summary>
public sealed class TopologyResult
{
    public required IReadOnlyDictionary<string, int> DeviceRows { get; init; }
    public required int RowCount { get; init; }
    public required IReadOnlyList<SymmetricGroup> SymmetricGroups { get; init; }
    public required IReadOnlyList<Stage> Stages { get; init; }
    public required IReadOnlySet<string> FloatingPassives { get; init; }

    /// <summary>
    /// Classification of passive elements as vertical (in current path) or horizontal (feedback/sensing).
    /// </summary>
    public required IReadOnlyDictionary<
        string,
        PassiveOrientation
    > PassiveOrientations { get; init; }
}

/// <summary>
/// Analyzes circuit topology to determine device placement in a coarse grid.
/// Replaces the role-based classification with topology-driven analysis.
/// </summary>
public static class TopologyAnalyzer
{
    /// <summary>
    /// Analyzes the circuit graph to determine row assignments, symmetric groups, and stages.
    /// </summary>
    public static TopologyResult Analyze(CircuitGraph graph)
    {
        var passiveOrientations = ClassifyPassives(graph);
        var chainGraph = BuildVerticalChainGraph(graph, passiveOrientations);
        var (deviceRows, rowCount) = AssignRows(chainGraph, graph);
        var symmetricGroups = DetectSymmetricGroups(graph);
        var stages = DetectStages(graph, deviceRows);
        var floatingPassives = DetectFloatingPassives(graph, deviceRows);

        // Assign horizontal passives to rows based on connected devices
        AssignHorizontalPassiveRows(deviceRows, passiveOrientations, graph);

        return new TopologyResult
        {
            DeviceRows = deviceRows,
            RowCount = rowCount,
            SymmetricGroups = symmetricGroups,
            Stages = stages,
            FloatingPassives = floatingPassives,
            PassiveOrientations = passiveOrientations,
        };
    }

    /// <summary>
    /// Represents a directed edge in the vertical chain graph.
    /// Direction is from VDD toward GND.
    /// </summary>
    private sealed record ChainEdge(string FromDevice, string ToDevice, string ViaNet);

    /// <summary>
    /// Classifies passive elements (resistors, capacitors, inductors) as vertical or horizontal.
    /// - Vertical: Part of VDD-to-GND current path (one terminal on rail)
    /// - Horizontal: Feedback/sensing connection between internal nodes
    /// </summary>
    private static Dictionary<string, PassiveOrientation> ClassifyPassives(CircuitGraph graph)
    {
        var orientations = new Dictionary<string, PassiveOrientation>();

        foreach (var (deviceId, device) in graph.Devices)
        {
            var deviceType = device.DeviceType.ToLowerInvariant();
            if (deviceType is not ("resistor" or "capacitor" or "inductor"))
            {
                continue;
            }

            var pNet = graph.GetNetForTerminal(deviceId, "P");
            var nNet = graph.GetNetForTerminal(deviceId, "N");

            var pIsRail = pNet != null && graph.IsSupplyOrGround(pNet);
            var nIsRail = nNet != null && graph.IsSupplyOrGround(nNet);

            if (pIsRail || nIsRail)
            {
                // Connected to a rail = vertical (load resistor, degeneration, etc.)
                orientations[deviceId] = PassiveOrientation.Vertical;
            }
            else
            {
                // Both terminals on internal nodes = horizontal (CMFB, feedback, etc.)
                orientations[deviceId] = PassiveOrientation.Horizontal;
            }
        }

        return orientations;
    }

    /// <summary>
    /// Assigns rows to horizontal passives based on the rows of devices they connect to.
    /// </summary>
    private static void AssignHorizontalPassiveRows(
        Dictionary<string, int> deviceRows,
        Dictionary<string, PassiveOrientation> passiveOrientations,
        CircuitGraph graph
    )
    {
        foreach (var (deviceId, orientation) in passiveOrientations)
        {
            if (orientation != PassiveOrientation.Horizontal)
            {
                continue;
            }

            if (deviceRows.ContainsKey(deviceId))
            {
                continue; // Already assigned
            }

            // Find rows of devices connected to the same nets as this passive
            var connectedRows = new List<int>();

            var pNet = graph.GetNetForTerminal(deviceId, "P");
            var nNet = graph.GetNetForTerminal(deviceId, "N");

            foreach (var net in new[] { pNet, nNet })
            {
                if (net == null || !graph.NetConnections.TryGetValue(net, out var connections))
                {
                    continue;
                }

                foreach (var conn in connections)
                {
                    if (
                        conn.DeviceId != deviceId
                        && deviceRows.TryGetValue(conn.DeviceId, out var row)
                    )
                    {
                        connectedRows.Add(row);
                    }
                }
            }

            if (connectedRows.Count > 0)
            {
                // Place at the row of connected devices (use min to place near the top connection)
                deviceRows[deviceId] = connectedRows.Min();
            }
        }
    }

    /// <summary>
    /// Builds a directed graph of vertical connections between devices.
    /// Edge direction: from device closer to VDD toward device closer to GND.
    /// - PMOS: drain is "down" terminal (toward GND)
    /// - NMOS: source is "down" terminal (toward GND)
    /// - Vertical resistors: terminal toward rail determines direction
    /// </summary>
    private static List<ChainEdge> BuildVerticalChainGraph(
        CircuitGraph graph,
        Dictionary<string, PassiveOrientation> passiveOrientations
    )
    {
        var edges = new List<ChainEdge>();

        foreach (var (netName, connections) in graph.NetConnections)
        {
            if (graph.IsSupplyOrGround(netName))
            {
                continue;
            }

            var upwardTerminals = new HashSet<string>(StringComparer.Ordinal);
            var downwardTerminals = new HashSet<string>(StringComparer.Ordinal);

            foreach (var conn in connections)
            {
                var device = graph.Devices.GetValueOrDefault(conn.DeviceId);
                if (device == null)
                {
                    continue;
                }

                var deviceType = device.DeviceType.ToLowerInvariant();
                var terminal = conn.Terminal.ToUpperInvariant();

                if (deviceType is "pmos" or "pfet")
                {
                    if (terminal == "D")
                    {
                        downwardTerminals.Add(conn.DeviceId);
                    }
                }
                else if (deviceType is "nmos" or "nfet")
                {
                    if (terminal == "D")
                    {
                        upwardTerminals.Add(conn.DeviceId);
                    }
                    else if (terminal == "S")
                    {
                        downwardTerminals.Add(conn.DeviceId);
                    }
                }
                else if (deviceType is "resistor" or "capacitor" or "inductor")
                {
                    // Only include vertical passives in the chain graph
                    if (
                        !passiveOrientations.TryGetValue(conn.DeviceId, out var orientation)
                        || orientation != PassiveOrientation.Vertical
                    )
                    {
                        continue; // Horizontal passive - handled separately
                    }

                    var otherTerminal = terminal == "P" ? "N" : "P";
                    var otherNet = graph.GetNetForTerminal(conn.DeviceId, otherTerminal);

                    // Determine direction based on which terminal connects to rail
                    if (otherNet != null && graph.Supplies.Contains(otherNet))
                    {
                        // Other terminal on VDD, this terminal points down
                        downwardTerminals.Add(conn.DeviceId);
                    }
                    else if (otherNet != null && graph.Grounds.Contains(otherNet))
                    {
                        // Other terminal on GND, this terminal points up
                        upwardTerminals.Add(conn.DeviceId);
                    }
                }
                else if (deviceType == "instance")
                {
                    var hasSupply = device.Bindings.Values.Any(graph.Supplies.Contains);
                    var hasGround = device.Bindings.Values.Any(graph.Grounds.Contains);

                    if (hasSupply)
                    {
                        downwardTerminals.Add(conn.DeviceId);
                    }
                    if (hasGround)
                    {
                        upwardTerminals.Add(conn.DeviceId);
                    }
                }
            }

            // Edges go from devices ABOVE (whose downward terminal connects here)
            // to devices BELOW (whose upward terminal connects here)
            foreach (var aboveDevice in downwardTerminals)
            {
                foreach (var belowDevice in upwardTerminals)
                {
                    if (aboveDevice != belowDevice)
                    {
                        edges.Add(new ChainEdge(aboveDevice, belowDevice, netName));
                    }
                }
            }
        }

        return edges;
    }

    /// <summary>
    /// Assigns row indices to devices using BFS from VDD.
    /// Row 0 is nearest to VDD, higher rows are nearer to GND.
    /// </summary>
    private static (Dictionary<string, int> DeviceRows, int RowCount) AssignRows(
        List<ChainEdge> edges,
        CircuitGraph graph
    )
    {
        var deviceRows = new Dictionary<string, int>();

        var forwardAdj = new Dictionary<string, List<string>>();
        var reverseAdj = new Dictionary<string, List<string>>();

        foreach (var deviceId in graph.Devices.Keys)
        {
            forwardAdj[deviceId] = new List<string>();
            reverseAdj[deviceId] = new List<string>();
        }

        foreach (var edge in edges)
        {
            if (forwardAdj.ContainsKey(edge.FromDevice))
            {
                forwardAdj[edge.FromDevice].Add(edge.ToDevice);
            }
            if (reverseAdj.ContainsKey(edge.ToDevice))
            {
                reverseAdj[edge.ToDevice].Add(edge.FromDevice);
            }
        }

        var vddDevices = FindDevicesConnectedToRail(graph, isSupply: true);
        var gndDevices = FindDevicesConnectedToRail(graph, isSupply: false);

        var queue = new Queue<string>();
        foreach (var device in vddDevices)
        {
            deviceRows[device] = 0;
            queue.Enqueue(device);
        }

        // Limit iterations to prevent infinite loops from cycles in the graph
        var maxIterations = graph.Devices.Count * graph.Devices.Count;
        var iterations = 0;

        while (queue.Count > 0 && iterations < maxIterations)
        {
            iterations++;
            var current = queue.Dequeue();
            var currentRow = deviceRows[current];

            foreach (var neighbor in forwardAdj.GetValueOrDefault(current, []))
            {
                var newRow = currentRow + 1;

                if (!deviceRows.TryGetValue(neighbor, out var existingRow) || newRow > existingRow)
                {
                    deviceRows[neighbor] = newRow;
                    queue.Enqueue(neighbor);
                }
            }
        }

        foreach (var device in gndDevices)
        {
            if (!deviceRows.ContainsKey(device))
            {
                deviceRows[device] = deviceRows.Count > 0 ? deviceRows.Values.Max() + 1 : 1;
            }
        }

        foreach (var device in gndDevices)
        {
            queue.Enqueue(device);
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var currentRow = deviceRows.GetValueOrDefault(current, 0);

            foreach (var neighbor in reverseAdj.GetValueOrDefault(current, []))
            {
                if (!deviceRows.ContainsKey(neighbor))
                {
                    deviceRows[neighbor] = Math.Max(0, currentRow - 1);
                    queue.Enqueue(neighbor);
                }
            }
        }

        foreach (var deviceId in graph.Devices.Keys)
        {
            if (!deviceRows.ContainsKey(deviceId))
            {
                deviceRows[deviceId] = deviceRows.Count > 0 ? deviceRows.Values.Max() / 2 : 0;
            }
        }

        var rowCount = deviceRows.Count > 0 ? deviceRows.Values.Max() + 1 : 1;

        return (deviceRows, rowCount);
    }

    /// <summary>
    /// Finds devices that connect to VDD (supply) or GND (ground) rails.
    /// </summary>
    private static HashSet<string> FindDevicesConnectedToRail(CircuitGraph graph, bool isSupply)
    {
        var railNets = isSupply ? graph.Supplies : graph.Grounds;
        var devices = new HashSet<string>();

        foreach (var (deviceId, device) in graph.Devices)
        {
            var deviceType = device.DeviceType.ToLowerInvariant();

            if (deviceType is "nmos" or "nfet" or "pmos" or "pfet")
            {
                var sourceNet = graph.GetNetForTerminal(deviceId, "S");
                var drainNet = graph.GetNetForTerminal(deviceId, "D");

                if (
                    (sourceNet != null && railNets.Contains(sourceNet))
                    || (drainNet != null && railNets.Contains(drainNet))
                )
                {
                    devices.Add(deviceId);
                }
            }
            else if (deviceType is "resistor" or "capacitor" or "inductor")
            {
                var pNet = graph.GetNetForTerminal(deviceId, "P");
                var nNet = graph.GetNetForTerminal(deviceId, "N");

                if (
                    (pNet != null && railNets.Contains(pNet))
                    || (nNet != null && railNets.Contains(nNet))
                )
                {
                    devices.Add(deviceId);
                }
            }
            else if (deviceType == "instance")
            {
                if (device.Bindings.Values.Any(railNets.Contains))
                {
                    devices.Add(deviceId);
                }
            }
        }

        return devices;
    }

    /// <summary>
    /// Detects symmetric groups: diff pairs (shared source), current mirrors (shared gate),
    /// and load pairs (same row, same device type).
    /// </summary>
    private static List<SymmetricGroup> DetectSymmetricGroups(CircuitGraph graph)
    {
        var groups = new List<SymmetricGroup>();

        groups.AddRange(DetectDiffPairs(graph));
        groups.AddRange(DetectCurrentMirrors(graph));
        groups.AddRange(DetectLoadPairs(graph));

        return groups;
    }

    /// <summary>
    /// Detects differential pairs: same device type, shared source net, gates connected to input ports.
    /// </summary>
    private static IEnumerable<SymmetricGroup> DetectDiffPairs(CircuitGraph graph)
    {
        var bySourceNet = new Dictionary<string, List<string>>();

        foreach (var (deviceId, device) in graph.Devices)
        {
            var deviceType = device.DeviceType.ToLowerInvariant();
            if (deviceType is not ("nmos" or "nfet" or "pmos" or "pfet"))
            {
                continue;
            }

            var gateNet = graph.GetNetForTerminal(deviceId, "G");
            if (gateNet == null || !graph.InputPorts.Contains(gateNet))
            {
                continue;
            }

            var sourceNet = graph.GetNetForTerminal(deviceId, "S");
            if (sourceNet == null || graph.IsSupplyOrGround(sourceNet))
            {
                continue;
            }

            if (!bySourceNet.TryGetValue(sourceNet, out var list))
            {
                list = new List<string>();
                bySourceNet[sourceNet] = list;
            }
            list.Add(deviceId);
        }

        foreach (var (pivotNet, deviceIds) in bySourceNet)
        {
            if (deviceIds.Count >= 2)
            {
                yield return new SymmetricGroup(
                    deviceIds.ToList(),
                    pivotNet,
                    SymmetryType.DiffPair
                );
            }
        }
    }

    /// <summary>
    /// Detects current mirrors: diode-connected device sharing both gate and source nets with
    /// other same-type devices.
    /// </summary>
    private static IEnumerable<SymmetricGroup> DetectCurrentMirrors(CircuitGraph graph)
    {
        var diodeConnected = new Dictionary<string, string>();

        foreach (var (deviceId, device) in graph.Devices)
        {
            var deviceType = device.DeviceType.ToLowerInvariant();
            if (deviceType is not ("nmos" or "nfet" or "pmos" or "pfet"))
            {
                continue;
            }

            var gateNet = graph.GetNetForTerminal(deviceId, "G");
            var drainNet = graph.GetNetForTerminal(deviceId, "D");

            if (gateNet != null && gateNet == drainNet)
            {
                diodeConnected[deviceId] = gateNet;
            }
        }

        var processedGateNets = new HashSet<string>();

        foreach (var (diodeDevice, gateNet) in diodeConnected)
        {
            if (processedGateNets.Contains(gateNet))
            {
                continue;
            }
            processedGateNets.Add(gateNet);

            var diodeDeviceDecl = graph.Devices[diodeDevice];
            var diodeSourceNet = graph.GetNetForTerminal(diodeDevice, "S");
            if (string.IsNullOrWhiteSpace(diodeSourceNet))
            {
                continue;
            }

            var mirrorDevices = new List<string> { diodeDevice };

            foreach (var (deviceId, device) in graph.Devices)
            {
                if (deviceId == diodeDevice)
                {
                    continue;
                }
                if (
                    device.DeviceType.ToLowerInvariant()
                    != diodeDeviceDecl.DeviceType.ToLowerInvariant()
                )
                {
                    continue;
                }

                var deviceGateNet = graph.GetNetForTerminal(deviceId, "G");
                var deviceSourceNet = graph.GetNetForTerminal(deviceId, "S");
                if (
                    deviceGateNet == gateNet
                    && string.Equals(deviceSourceNet, diodeSourceNet, StringComparison.Ordinal)
                )
                {
                    mirrorDevices.Add(deviceId);
                }
            }

            if (mirrorDevices.Count >= 2)
            {
                yield return new SymmetricGroup(mirrorDevices, gateNet, SymmetryType.CurrentMirror);
            }
        }
    }

    /// <summary>
    /// Detects load pairs: PMOS devices with source connected to VDD that share a gate net.
    /// </summary>
    private static IEnumerable<SymmetricGroup> DetectLoadPairs(CircuitGraph graph)
    {
        var loadsByGateNet = new Dictionary<string, List<string>>();

        foreach (var (deviceId, device) in graph.Devices)
        {
            var deviceType = device.DeviceType.ToLowerInvariant();
            if (deviceType is not ("pmos" or "pfet"))
            {
                continue;
            }

            var sourceNet = graph.GetNetForTerminal(deviceId, "S");
            if (sourceNet == null || !graph.Supplies.Contains(sourceNet))
            {
                continue;
            }

            var gateNet = graph.GetNetForTerminal(deviceId, "G");
            var drainNet = graph.GetNetForTerminal(deviceId, "D");
            if (gateNet == null || gateNet == drainNet)
            {
                continue;
            }

            if (!loadsByGateNet.TryGetValue(gateNet, out var list))
            {
                list = new List<string>();
                loadsByGateNet[gateNet] = list;
            }
            list.Add(deviceId);
        }

        foreach (var (pivotNet, deviceIds) in loadsByGateNet)
        {
            if (deviceIds.Count >= 2)
            {
                yield return new SymmetricGroup(
                    deviceIds.ToList(),
                    pivotNet,
                    SymmetryType.LoadPair
                );
            }
        }
    }

    /// <summary>
    /// Detects symmetric pairs of horizontal passives.
    /// These are passives that share a common net on their N terminal (e.g., vcm_node)
    /// and have P terminals connected to symmetric outputs.
    /// </summary>
    public static IReadOnlyList<(
        string Left,
        string Right,
        string PivotNet
    )> DetectSymmetricPassivePairs(CircuitGraph graph, TopologyResult topology)
    {
        var pairs = new List<(string Left, string Right, string PivotNet)>();

        // Find all horizontal passives
        var horizontalPassives = topology
            .PassiveOrientations.Where(kv => kv.Value == PassiveOrientation.Horizontal)
            .Select(kv => kv.Key)
            .ToList();

        if (horizontalPassives.Count < 2)
        {
            return pairs;
        }

        // Group by shared N terminal net
        var byNNet = new Dictionary<string, List<string>>();
        foreach (var deviceId in horizontalPassives)
        {
            var nNet = graph.GetNetForTerminal(deviceId, "N");
            if (nNet == null || graph.IsSupplyOrGround(nNet))
            {
                continue;
            }

            if (!byNNet.TryGetValue(nNet, out var list))
            {
                list = new List<string>();
                byNNet[nNet] = list;
            }
            list.Add(deviceId);
        }

        // For groups of 2, check if P terminals connect to symmetric outputs
        foreach (var (pivotNet, devices) in byNNet)
        {
            if (devices.Count != 2)
            {
                continue;
            }

            var d1 = devices[0];
            var d2 = devices[1];

            var p1Net = graph.GetNetForTerminal(d1, "P");
            var p2Net = graph.GetNetForTerminal(d2, "P");

            // Check if P terminals connect to symmetric outputs (_P/_N suffixes or output ports)
            var areSymmetricOutputs = AreSymmetricOutputNets(p1Net, p2Net, graph);
            if (areSymmetricOutputs)
            {
                // Determine which is left and which is right based on net naming
                var (left, right) = DetermineLeftRight(d1, d2, p1Net, p2Net);
                pairs.Add((left, right, pivotNet));
            }
        }

        return pairs;
    }

    /// <summary>
    /// Checks if two nets represent symmetric outputs (e.g., OUT_P and OUT_N).
    /// </summary>
    private static bool AreSymmetricOutputNets(string? net1, string? net2, CircuitGraph graph)
    {
        if (net1 == null || net2 == null)
        {
            return false;
        }

        // Both should be outputs or internal nets
        var net1IsOutput = graph.OutputPorts.Contains(net1);
        var net2IsOutput = graph.OutputPorts.Contains(net2);

        if (!net1IsOutput || !net2IsOutput)
        {
            return false;
        }

        // Check for symmetric naming patterns
        return IsSymmetricNaming(net1, net2);
    }

    /// <summary>
    /// Checks if two net names have symmetric naming (e.g., OUT_P/OUT_N, OUTP/OUTN).
    /// </summary>
    private static bool IsSymmetricNaming(string name1, string name2)
    {
        // Common patterns: _P/_N, P/N suffix, _PLUS/_MINUS
        var suffixPairs = new[]
        {
            ("_P", "_N"),
            ("_p", "_n"),
            ("P", "N"),
            ("p", "n"),
            ("_PLUS", "_MINUS"),
            ("_plus", "_minus"),
            ("+", "-"),
        };

        foreach (var (suffixA, suffixB) in suffixPairs)
        {
            if (
                (
                    name1.EndsWith(suffixA, StringComparison.Ordinal)
                    && name2.EndsWith(suffixB, StringComparison.Ordinal)
                )
                || (
                    name1.EndsWith(suffixB, StringComparison.Ordinal)
                    && name2.EndsWith(suffixA, StringComparison.Ordinal)
                )
            )
            {
                // Verify the base names match
                var base1 = name1.EndsWith(suffixA, StringComparison.Ordinal)
                    ? name1[..^suffixA.Length]
                    : name1[..^suffixB.Length];
                var base2 = name2.EndsWith(suffixA, StringComparison.Ordinal)
                    ? name2[..^suffixA.Length]
                    : name2[..^suffixB.Length];

                if (base1.Equals(base2, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Determines which device should be placed on the left vs right based on P terminal net naming.
    /// </summary>
    private static (string Left, string Right) DetermineLeftRight(
        string d1,
        string d2,
        string? p1Net,
        string? p2Net
    )
    {
        // _P suffix goes on left, _N suffix goes on right
        if (
            p1Net != null
            && (
                p1Net.EndsWith("_P", StringComparison.OrdinalIgnoreCase)
                || p1Net.EndsWith("P", StringComparison.OrdinalIgnoreCase)
                || p1Net.EndsWith("+", StringComparison.Ordinal)
            )
        )
        {
            return (d1, d2);
        }

        if (
            p2Net != null
            && (
                p2Net.EndsWith("_P", StringComparison.OrdinalIgnoreCase)
                || p2Net.EndsWith("P", StringComparison.OrdinalIgnoreCase)
                || p2Net.EndsWith("+", StringComparison.Ordinal)
            )
        )
        {
            return (d2, d1);
        }

        // Default: alphabetical order
        return string.Compare(d1, d2, StringComparison.Ordinal) < 0 ? (d1, d2) : (d2, d1);
    }

    /// <summary>
    /// Detects stages by finding connected components via signal flow.
    /// For now, treats the entire circuit as a single stage (multi-stage detection deferred).
    /// </summary>
    private static List<Stage> DetectStages(CircuitGraph graph, Dictionary<string, int> deviceRows)
    {
        var allDevices = deviceRows.Keys.ToList();
        if (allDevices.Count == 0)
        {
            return new List<Stage>();
        }

        return new List<Stage> { new Stage(0, allDevices) };
    }

    /// <summary>
    /// Detects floating passives that are not part of vertical chains.
    /// These include CMFB resistors, compensation capacitors, etc.
    /// </summary>
    private static HashSet<string> DetectFloatingPassives(
        CircuitGraph graph,
        Dictionary<string, int> deviceRows
    )
    {
        var floating = new HashSet<string>();

        foreach (var (deviceId, device) in graph.Devices)
        {
            if (!deviceRows.ContainsKey(deviceId))
            {
                floating.Add(deviceId);
                continue;
            }

            var deviceType = device.DeviceType.ToLowerInvariant();
            if (deviceType is "capacitor")
            {
                var pNet = graph.GetNetForTerminal(deviceId, "P");
                var nNet = graph.GetNetForTerminal(deviceId, "N");

                var pIsOutput = pNet != null && graph.OutputPorts.Contains(pNet);
                var nIsOutput = nNet != null && graph.OutputPorts.Contains(nNet);
                var pIsInternal = pNet != null && graph.InternalNets.Contains(pNet);
                var nIsInternal = nNet != null && graph.InternalNets.Contains(nNet);

                if ((pIsOutput && nIsInternal) || (nIsOutput && pIsInternal))
                {
                    floating.Add(deviceId);
                }
            }
            else if (deviceType is "resistor")
            {
                var pNet = graph.GetNetForTerminal(deviceId, "P");
                var nNet = graph.GetNetForTerminal(deviceId, "N");

                var pIsOutput = pNet != null && graph.OutputPorts.Contains(pNet);
                var nIsOutput = nNet != null && graph.OutputPorts.Contains(nNet);
                var pIsInternal = pNet != null && graph.InternalNets.Contains(pNet);
                var nIsInternal = nNet != null && graph.InternalNets.Contains(nNet);

                if ((pIsOutput && nIsInternal) || (nIsOutput && pIsInternal))
                {
                    floating.Add(deviceId);
                }
            }
        }

        return floating;
    }
}
