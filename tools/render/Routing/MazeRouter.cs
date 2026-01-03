namespace Cascode.Render.Routing;

using Cascode.Render.Analysis;
using Cascode.Render.Layout;
using Cascode.Render.Placement;

/// <summary>
/// Simple maze-based router for schematic wire routing.
/// Routes nets iteratively using Manhattan paths with obstacle avoidance.
/// </summary>
public static class MazeRouter
{
    /// <summary>
    /// Gets terminals grouped by net name (for testing).
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<TerminalPosition>> GetTerminalsByNet(
        CoarseGridResult placement,
        CircuitGraph graph
    )
    {
        var canvasWidth = placement.ColumnCount * DeviceGeometry.CellWidth;
        var canvasHeight =
            placement.RowCount * DeviceGeometry.CellHeight + 2 * DeviceGeometry.RailMargin;

        var terminals = ComputeTerminalPositions(placement, graph, canvasWidth, canvasHeight);
        var byNet = GroupTerminalsByNet(terminals, graph);
        return byNet.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<TerminalPosition>)kv.Value);
    }

    /// <summary>
    /// Routes all nets in the circuit.
    /// </summary>
    public static RoutingResult Route(CoarseGridResult placement, CircuitGraph graph)
    {
        var canvasWidth = placement.ColumnCount * DeviceGeometry.CellWidth;
        var canvasHeight =
            placement.RowCount * DeviceGeometry.CellHeight + 2 * DeviceGeometry.RailMargin;

        var terminals = ComputeTerminalPositions(placement, graph, canvasWidth, canvasHeight);
        var terminalsByNet = GroupTerminalsByNet(terminals, graph);
        var obstacles = ObstacleMap.FromPlacement(placement, graph);
        var occupied = new OccupiedSegments();

        var allSegments = new List<WireSegment>();
        var segmentsByNet = new Dictionary<string, IReadOnlyList<WireSegment>>();

        // Route power rails first
        foreach (var supply in graph.Supplies)
        {
            if (terminalsByNet.TryGetValue(supply, out var terms))
            {
                var segs = RouteRail(supply, terms, canvasWidth, DeviceGeometry.RailMargin / 2);
                AddSegments(segs, supply, occupied, allSegments, segmentsByNet);
            }
        }

        foreach (var ground in graph.Grounds)
        {
            if (terminalsByNet.TryGetValue(ground, out var terms))
            {
                var railY = canvasHeight - DeviceGeometry.RailMargin / 2;
                var segs = RouteRail(ground, terms, canvasWidth, railY);
                AddSegments(segs, ground, occupied, allSegments, segmentsByNet);
            }
        }

        // Route signal nets ordered by terminal count (simpler nets first)
        var signalNets = terminalsByNet
            .Keys.Where(n => !graph.Supplies.Contains(n) && !graph.Grounds.Contains(n))
            .OrderBy(n => terminalsByNet[n].Count)
            .ToList();

        // Collect all terminal positions for forbidden point checking
        var allTerminalPoints = new Dictionary<string, HashSet<GridPoint>>();
        foreach (var (net, terms) in terminalsByNet)
        {
            allTerminalPoints[net] = terms.Select(t => new GridPoint(t.X, t.Y)).ToHashSet();
        }

        foreach (var netName in signalNets)
        {
            var terms = terminalsByNet[netName];
            if (terms.Count < 2)
            {
                continue;
            }

            // Forbidden points are terminals from OTHER nets
            var forbiddenPoints = new HashSet<GridPoint>();
            foreach (var (otherNet, points) in allTerminalPoints)
            {
                if (otherNet != netName)
                {
                    forbiddenPoints.UnionWith(points);
                }
            }

            var segs = RouteNet(netName, terms, obstacles, occupied, forbiddenPoints);
            AddSegments(segs, netName, occupied, allSegments, segmentsByNet);
        }

        var junctions = FindJunctions(allSegments);

        return new RoutingResult
        {
            Segments = allSegments,
            Junctions = junctions,
            SegmentsByNet = segmentsByNet,
            CanvasWidth = canvasWidth,
            CanvasHeight = canvasHeight,
        };
    }

    /// <summary>
    /// Routes a power rail (VDD or GND).
    /// </summary>
    private static List<WireSegment> RouteRail(
        string netName,
        List<TerminalPosition> terminals,
        int canvasWidth,
        int railY
    )
    {
        var segments = new List<WireSegment>();

        // Collect X coordinates where terminals connect to the rail
        var xCoords = new List<int>();

        foreach (var term in terminals)
        {
            // Vertical drop from terminal to rail
            if (term.Y != railY)
            {
                segments.Add(
                    new WireSegment(
                        new GridPoint(term.X, term.Y),
                        new GridPoint(term.X, railY),
                        netName
                    )
                );
            }
            xCoords.Add(term.X);
        }

        // Add horizontal rail segment connecting all drops
        if (xCoords.Count >= 2)
        {
            var minX = xCoords.Min();
            var maxX = xCoords.Max();
            segments.Add(
                new WireSegment(new GridPoint(minX, railY), new GridPoint(maxX, railY), netName)
            );
        }

        return segments;
    }

    /// <summary>
    /// Routes a signal net by building MST and routing each edge.
    /// </summary>
    private static List<WireSegment> RouteNet(
        string netName,
        List<TerminalPosition> terminals,
        IReadOnlyList<Obstacle> obstacles,
        OccupiedSegments occupied,
        IReadOnlySet<GridPoint> forbiddenPoints
    )
    {
        var segments = new List<WireSegment>();

        if (terminals.Count < 2)
        {
            return segments;
        }

        // Build MST of terminals
        var mstEdges = ComputeMST(terminals);

        // Route each edge
        foreach (var (fromIdx, toIdx) in mstEdges)
        {
            var from = new GridPoint(terminals[fromIdx].X, terminals[fromIdx].Y);
            var to = new GridPoint(terminals[toIdx].X, terminals[toIdx].Y);

            var path = PathFinder.FindPath(from, to, netName, obstacles, occupied, forbiddenPoints);
            segments.AddRange(path);

            // Add path segments to occupied immediately so subsequent edges avoid them
            foreach (var seg in path)
            {
                occupied.Add(seg);
            }
        }

        return segments;
    }

    /// <summary>
    /// Computes minimum spanning tree using Prim's algorithm with Manhattan distance.
    /// Returns list of (fromIndex, toIndex) edges.
    /// </summary>
    private static List<(int, int)> ComputeMST(List<TerminalPosition> terminals)
    {
        var n = terminals.Count;
        var inMST = new bool[n];
        var edges = new List<(int, int)>();

        inMST[0] = true;
        var mstCount = 1;

        while (mstCount < n)
        {
            var bestDist = int.MaxValue;
            var bestFrom = -1;
            var bestTo = -1;

            for (var i = 0; i < n; i++)
            {
                if (!inMST[i])
                {
                    continue;
                }

                for (var j = 0; j < n; j++)
                {
                    if (inMST[j])
                    {
                        continue;
                    }

                    var dist = ManhattanDistance(terminals[i], terminals[j]);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestFrom = i;
                        bestTo = j;
                    }
                }
            }

            if (bestTo < 0)
            {
                break;
            }

            inMST[bestTo] = true;
            mstCount++;
            edges.Add((bestFrom, bestTo));
        }

        return edges;
    }

    private static int ManhattanDistance(TerminalPosition a, TerminalPosition b)
    {
        return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    }

    /// <summary>
    /// Adds segments to tracking structures.
    /// </summary>
    private static void AddSegments(
        List<WireSegment> segments,
        string netName,
        OccupiedSegments occupied,
        List<WireSegment> allSegments,
        Dictionary<string, IReadOnlyList<WireSegment>> segmentsByNet
    )
    {
        foreach (var seg in segments)
        {
            occupied.Add(seg);
        }
        allSegments.AddRange(segments);
        segmentsByNet[netName] = segments;
    }

    /// <summary>
    /// Finds junction points where 3+ wire segments meet.
    /// </summary>
    private static List<GridPoint> FindJunctions(List<WireSegment> segments)
    {
        var pointCounts = new Dictionary<GridPoint, int>();

        foreach (var seg in segments)
        {
            pointCounts[seg.From] = pointCounts.GetValueOrDefault(seg.From, 0) + 1;
            pointCounts[seg.To] = pointCounts.GetValueOrDefault(seg.To, 0) + 1;
        }

        // Also count mid-segment intersections
        for (var i = 0; i < segments.Count; i++)
        {
            for (var j = i + 1; j < segments.Count; j++)
            {
                var intersection = GetIntersection(segments[i], segments[j]);
                if (intersection.HasValue)
                {
                    var pt = intersection.Value;
                    pointCounts[pt] = pointCounts.GetValueOrDefault(pt, 0) + 2;
                }
            }
        }

        return pointCounts.Where(kv => kv.Value >= 3).Select(kv => kv.Key).ToList();
    }

    /// <summary>
    /// Gets intersection point of two segments if they cross.
    /// </summary>
    private static GridPoint? GetIntersection(WireSegment a, WireSegment b)
    {
        var aHorizontal = a.From.Y == a.To.Y;
        var bHorizontal = b.From.Y == b.To.Y;

        if (aHorizontal == bHorizontal)
        {
            return null; // Parallel, no single intersection
        }

        var h = aHorizontal ? a : b;
        var v = aHorizontal ? b : a;

        var x = v.From.X;
        var y = h.From.Y;

        var hMinX = Math.Min(h.From.X, h.To.X);
        var hMaxX = Math.Max(h.From.X, h.To.X);
        var vMinY = Math.Min(v.From.Y, v.To.Y);
        var vMaxY = Math.Max(v.From.Y, v.To.Y);

        if (x > hMinX && x < hMaxX && y > vMinY && y < vMaxY)
        {
            return new GridPoint(x, y);
        }

        return null;
    }

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

            if (deviceType is "nmos" or "nfet" or "pmos" or "pfet")
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
            else if (deviceType is "resistor" or "capacitor")
            {
                var p = DeviceGeometry.GetPassivePlacement(cell.Row, cell.Column);
                positions.Add(new TerminalPosition(deviceId, "P", p.PX, p.PY));
                positions.Add(new TerminalPosition(deviceId, "N", p.NX, p.NY));
            }
        }

        // Port terminals
        var terminalYByNet = ComputeTerminalYByNet(positions, graph);

        // Left ports (inputs, bias) - use average Y
        var leftPorts = graph.InputPorts.Concat(graph.BiasPorts).ToList();
        var leftYs = ComputePortYPositions(leftPorts, terminalYByNet, preferMaxY: false);
        foreach (var port in leftPorts)
        {
            var y = leftYs.GetValueOrDefault(port, DeviceGeometry.RailMargin + 50);
            positions.Add(new TerminalPosition($"PORT_{port}", "P", 0, y));
        }

        // Right ports (outputs) - use max Y to align with drain positions
        var rightYs = ComputePortYPositions(
            graph.OutputPorts.ToList(),
            terminalYByNet,
            preferMaxY: true
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

    /// <summary>
    /// Computes Y positions for ports based on connected terminals.
    /// </summary>
    private static Dictionary<string, int> ComputePortYPositions(
        List<string> portNames,
        Dictionary<string, List<int>> terminalYByNet,
        bool preferMaxY
    )
    {
        var result = new Dictionary<string, int>();
        var usedYs = new List<int>();
        const int minSpacing = 15;

        foreach (var port in portNames)
        {
            int y;

            if (terminalYByNet.TryGetValue(port, out var ys) && ys.Count > 0)
            {
                // Match SvgRenderer logic: max for outputs, average for inputs
                y = preferMaxY ? ys.Max() : (int)ys.Average();
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
