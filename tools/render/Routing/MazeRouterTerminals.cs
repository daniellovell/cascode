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
                var pTerminal = GetPassiveTerminalPosition(deviceType, "P", cell);
                var nTerminal = GetPassiveTerminalPosition(deviceType, "N", cell);
                positions.Add(new TerminalPosition(deviceId, "P", pTerminal.X, pTerminal.Y));
                positions.Add(new TerminalPosition(deviceId, "N", nTerminal.X, nTerminal.Y));
            }
        }

        // Port terminals
        var terminalYByNet = ComputeTerminalYByNet(positions, graph);

        // Left ports (inputs, bias) - use average Y
        var leftPorts = graph.InputPorts.Concat(graph.BiasPorts).ToList();
        var leftYs = ComputePortYPositions(
            leftPorts,
            terminalYByNet,
            preferMinY: false,
            placement.PortYHints
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
            placement.PortYHints
        );
        foreach (var port in graph.OutputPorts)
        {
            var y = rightYs.GetValueOrDefault(port, DeviceGeometry.RailMargin + 50);
            positions.Add(new TerminalPosition($"PORT_{port}", "P", canvasWidth, y));
        }

        return positions;
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

    private static (int X, int Y) GetPassiveTerminalPosition(
        string deviceType,
        string terminal,
        GridCell cell
    )
    {
        var baseX = DeviceGeometry.GetCellCenterX(cell.Column);
        var baseY = DeviceGeometry.GetCellCenterY(cell.Row);
        var (xOffset2, yOffset2) = CoarseGridPlacer.GetTerminalEdgeOffset2(
            deviceType,
            terminal,
            cell
        );
        if (xOffset2 != 0)
        {
            var x = DeviceGeometry.RoundToInt(
                baseX + xOffset2 * (DeviceGeometry.PassiveWidth / 2.0)
            );
            return (x, DeviceGeometry.RoundToInt(baseY));
        }

        if (yOffset2 != 0)
        {
            var x = DeviceGeometry.SnapToRoutingGrid(baseX + DeviceGeometry.MosfetWidth / 2.0);
            var y = DeviceGeometry.RoundToInt(
                baseY + yOffset2 * (DeviceGeometry.PassiveWidth / 2.0)
            );
            return (x, y);
        }

        return (DeviceGeometry.RoundToInt(baseX), DeviceGeometry.RoundToInt(baseY));
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
