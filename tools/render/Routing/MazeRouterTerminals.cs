namespace Cascode.Render.Routing;

using Cascode.Render.Analysis;
using Cascode.Render.Layout;
using Cascode.Render.Placement;

/// <summary>
/// Terminal and port placement methods for MazeRouter.
/// </summary>
public static partial class MazeRouter
{
    /// <summary>
    /// Computes terminal positions for all devices and ports.
    /// </summary>
    private static List<TerminalPosition> ComputeTerminalPositions(
        CoarseGridResult placement,
        CircuitGraph graph,
        int canvasWidth,
        int canvasHeight
    )
    {
        var positions = new List<TerminalPosition>();

        // Device terminals
        foreach (var (deviceId, cell) in placement.DevicePlacements)
        {
            if (!graph.Devices.TryGetValue(deviceId, out var device))
            {
                continue;
            }

            var deviceType = device.DeviceType.ToLowerInvariant();

            if (deviceType == "instance")
            {
                var blockInfo = graph.InstanceBlocks.FirstOrDefault(b => b.InstanceId == deviceId);
                var signalPorts =
                    blockInfo?.SignalPortNames ?? (IReadOnlyList<string>)Array.Empty<string>();
                var bp = DeviceGeometry.GetInstanceBlockPlacement(
                    cell.Row,
                    cell.Column,
                    signalPorts,
                    graph.Supplies,
                    graph.Grounds,
                    device.Bindings
                );
                foreach (var (terminal, pos) in bp.Terminals)
                {
                    positions.Add(new TerminalPosition(deviceId, terminal, pos.X, pos.Y));
                }
            }
            else if (deviceType is "nmos" or "nfet" or "pmos" or "pfet")
            {
                var isPmos = deviceType is "pmos" or "pfet";
                var p = DeviceGeometry.GetMosfetPlacement(cell.Row, cell.Column, cell.MirrorX);

                positions.Add(new TerminalPosition(deviceId, "G", p.GateX, p.GateY));
                positions.Add(
                    new TerminalPosition(deviceId, "D", p.DrainX, isPmos ? p.SourceY : p.DrainY)
                );
                positions.Add(
                    new TerminalPosition(deviceId, "S", p.SourceX, isPmos ? p.DrainY : p.SourceY)
                );
            }
            else if (deviceType is "resistor" or "capacitor" or "inductor")
            {
                var isHorizontalPassive = placement.HorizontalPassiveIds.Contains(deviceId);
                var isLeftOfAxis = PlacementAxis.IsLeftOfAxis(placement, cell.Column);

                if (isHorizontalPassive)
                {
                    var p = DeviceGeometry.GetHorizontalPassivePlacement(
                        cell.Row,
                        cell.Column,
                        placement.ColumnCount,
                        isLeftOfAxis
                    );
                    positions.Add(new TerminalPosition(deviceId, "P", p.PX, p.PY));
                    positions.Add(new TerminalPosition(deviceId, "N", p.NX, p.NY));
                }
                else
                {
                    var p = DeviceGeometry.GetPassivePlacement(cell.Row, cell.Column);
                    positions.Add(new TerminalPosition(deviceId, "P", p.PX, p.PY));
                    positions.Add(new TerminalPosition(deviceId, "N", p.NX, p.NY));
                }
            }
        }

        // Port terminals
        var terminalYByNet = ComputeTerminalYByNet(positions, graph);
        var preferredPortYs = new Dictionary<string, int>(
            placement.PortYHints,
            StringComparer.Ordinal
        );
        foreach (var (port, y) in ComputeFeedthroughPortHints(placement, graph))
        {
            preferredPortYs[port] = y;
        }

        // Left ports (inputs, bias) - use average Y
        var leftPorts = graph.InputPorts.Concat(graph.BiasPorts).ToList();
        var leftYs = ComputePortYPositions(
            leftPorts,
            terminalYByNet,
            preferMinY: false,
            preferredPortYs
        );
        foreach (var port in leftPorts)
        {
            var y = leftYs.GetValueOrDefault(port, DeviceGeometry.RailMargin + 50);
            positions.Add(new TerminalPosition($"PORT_{port}", "P", 0, y));
        }

        // Right ports (outputs) - use average Y for balanced routing
        var rightYs = ComputePortYPositions(
            graph.OutputPorts.ToList(),
            terminalYByNet,
            preferMinY: false,
            preferredPortYs
        );
        foreach (var port in graph.OutputPorts)
        {
            var y = rightYs.GetValueOrDefault(port, DeviceGeometry.RailMargin + 50);
            positions.Add(new TerminalPosition($"PORT_{port}", "P", canvasWidth, y));
        }

        return positions;
    }

    private static IReadOnlyDictionary<string, int> ComputeFeedthroughPortHints(
        CoarseGridResult placement,
        CircuitGraph graph
    )
    {
        var pairs = new List<(string LeftPort, string RightPort, int BaseY)>();
        foreach (var (deviceId, device) in graph.Devices)
        {
            var deviceType = device.DeviceType.ToLowerInvariant();
            if (deviceType is not ("resistor" or "capacitor" or "inductor"))
            {
                continue;
            }

            if (
                !placement.DevicePlacements.TryGetValue(deviceId, out var cell)
                || !device.Bindings.TryGetValue("P", out var pNet)
                || !device.Bindings.TryGetValue("N", out var nNet)
            )
            {
                continue;
            }

            var pIsLeft = graph.InputPorts.Contains(pNet) || graph.BiasPorts.Contains(pNet);
            var nIsLeft = graph.InputPorts.Contains(nNet) || graph.BiasPorts.Contains(nNet);
            var pIsRight = graph.OutputPorts.Contains(pNet);
            var nIsRight = graph.OutputPorts.Contains(nNet);
            if (!(pIsLeft && nIsRight) && !(nIsLeft && pIsRight))
            {
                continue;
            }

            var y = placement.HorizontalPassiveIds.Contains(deviceId)
                ? DeviceGeometry
                    .GetHorizontalPassivePlacement(
                        cell.Row,
                        cell.Column,
                        placement.ColumnCount,
                        PlacementAxis.IsLeftOfAxis(placement, cell.Column)
                    )
                    .PY
                : DeviceGeometry.GetPassivePlacement(cell.Row, cell.Column).PY;
            pairs.Add((pIsLeft ? pNet : nNet, pIsRight ? pNet : nNet, y));
        }

        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        var usedYs = new List<int>();
        foreach (
            var (leftPort, rightPort, baseY) in pairs
                .OrderBy(pair => pair.BaseY)
                .ThenBy(pair => IsPositivePortName(pair.LeftPort) ? 0 : 1)
                .ThenBy(pair => pair.LeftPort, StringComparer.Ordinal)
        )
        {
            var y = baseY;
            while (usedYs.Any(existing => Math.Abs(existing - y) < 15))
            {
                y += 15;
            }

            result[leftPort] = y;
            result[rightPort] = y;
            usedYs.Add(y);
        }

        return result;
    }

    private static bool IsPositivePortName(string portName)
    {
        return portName.EndsWith(".P", StringComparison.OrdinalIgnoreCase)
            || portName.EndsWith("_P", StringComparison.OrdinalIgnoreCase)
            || portName.EndsWith("+", StringComparison.Ordinal);
    }

    /// <summary>
    /// Groups terminal Y positions by net for port alignment.
    /// </summary>
    private static Dictionary<string, List<int>> ComputeTerminalYByNet(
        List<TerminalPosition> positions,
        CircuitGraph graph
    )
    {
        var result = new Dictionary<string, List<int>>();

        foreach (var pos in positions)
        {
            var netName = graph.GetNetForTerminal(pos.DeviceId, pos.Terminal);
            if (netName == null)
            {
                continue;
            }

            if (!result.TryGetValue(netName, out var list))
            {
                list = new List<int>();
                result[netName] = list;
            }
            list.Add(pos.Y);
        }

        return result;
    }

    /// <summary>
    /// Computes Y positions for ports based on connected terminals.
    /// </summary>
    private static Dictionary<string, int> ComputePortYPositions(
        List<string> portNames,
        Dictionary<string, List<int>> terminalYByNet,
        bool preferMinY,
        IReadOnlyDictionary<string, int>? preferredYHints = null
    )
    {
        var result = new Dictionary<string, int>();
        var usedYs = new List<int>();
        const int minSpacing = 15;

        foreach (var port in portNames)
        {
            int y;

            if (preferredYHints != null && preferredYHints.TryGetValue(port, out var hintedY))
            {
                y = hintedY;
            }
            else if (terminalYByNet.TryGetValue(port, out var ys) && ys.Count > 0)
            {
                // Use minimum Y when preferMinY is true, average Y when false (for balanced routing)
                y = preferMinY ? ys.Min() : (int)ys.Average();
            }
            else
            {
                y = DeviceGeometry.RailMargin + 50 + usedYs.Count * 20;
            }

            // Avoid collisions
            while (usedYs.Any(used => Math.Abs(used - y) < minSpacing))
            {
                y += minSpacing;
            }

            result[port] = y;
            usedYs.Add(y);
        }

        return result;
    }

    /// <summary>
    /// Groups terminals by net name.
    /// </summary>
    private static Dictionary<string, List<TerminalPosition>> GroupTerminalsByNet(
        List<TerminalPosition> terminals,
        CircuitGraph graph
    )
    {
        var byNet = new Dictionary<string, List<TerminalPosition>>();

        foreach (var term in terminals)
        {
            string? netName;

            if (term.DeviceId.StartsWith("PORT_", StringComparison.Ordinal))
            {
                netName = term.DeviceId.Substring(5);
            }
            else
            {
                netName = graph.GetNetForTerminal(term.DeviceId, term.Terminal);
            }

            if (netName == null)
            {
                continue;
            }

            if (!byNet.TryGetValue(netName, out var list))
            {
                list = new List<TerminalPosition>();
                byNet[netName] = list;
            }
            list.Add(term);
        }

        return byNet;
    }
}
