namespace Cascode.Render.Routing;

using Cascode.Render.Analysis;
using Cascode.Render.Placement;
using Google.OrTools.Sat;

/// <summary>
/// A point on the fine routing grid.
/// </summary>
public readonly record struct GridPoint(int X, int Y);

/// <summary>
/// A wire segment connecting two points.
/// </summary>
public sealed record WireSegment(GridPoint From, GridPoint To, string NetName);

/// <summary>
/// Terminal position in fine grid coordinates.
/// </summary>
public sealed record TerminalPosition(string DeviceId, string Terminal, int X, int Y);

/// <summary>
/// Complete routing result.
/// </summary>
public sealed class RoutingResult
{
    public required IReadOnlyList<WireSegment> Segments { get; init; }
    public required IReadOnlyList<GridPoint> Junctions { get; init; }
    public required IReadOnlyDictionary<
        string,
        IReadOnlyList<WireSegment>
    > SegmentsByNet { get; init; }
    public required int CanvasWidth { get; init; }
    public required int CanvasHeight { get; init; }
}

/// <summary>
/// Routes wires on a fine grid using SAT constraints to prevent overlaps.
/// </summary>
public static class FineGridRouter
{
    private const int CellWidth = 60;
    private const int CellHeight = 50;
    private const int RoutingPitch = 10;
    private const int RailMargin = 15;
    private const double MaxSolveTimeSeconds = 3.0;

    private const int MosfetWidth = 17;
    private const int MosfetHeight = 26;
    private const int PassiveWidth = 26;
    private const int PassiveHeight = 9;

    /// <summary>
    /// Routes all nets in the circuit.
    /// </summary>
    public static RoutingResult Route(CoarseGridResult placement, CircuitGraph graph)
    {
        var canvasWidth = placement.ColumnCount * CellWidth;
        var canvasHeight = placement.RowCount * CellHeight + 2 * RailMargin;

        var terminals = ComputeTerminalPositions(placement, graph);
        var terminalsByNet = GroupTerminalsByNet(terminals, graph);

        var allSegments = new List<WireSegment>();
        var segmentsByNet = new Dictionary<string, IReadOnlyList<WireSegment>>();

        var usedSegments = new HashSet<(int, int, int, int)>();

        foreach (var (netName, netTerminals) in terminalsByNet)
        {
            // Rail nets (VDD/GND) can route with just 1 terminal
            var isRail = graph.Supplies.Contains(netName) || graph.Grounds.Contains(netName);
            if (netTerminals.Count < 2 && !isRail)
            {
                continue;
            }
            if (netTerminals.Count == 0)
            {
                continue;
            }

            var netSegments = RouteNet(
                netName,
                netTerminals,
                usedSegments,
                canvasWidth,
                canvasHeight,
                graph
            );

            foreach (var seg in netSegments)
            {
                var key = NormalizeSegment(seg.From.X, seg.From.Y, seg.To.X, seg.To.Y);
                usedSegments.Add(key);
            }

            allSegments.AddRange(netSegments);
            segmentsByNet[netName] = netSegments;
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
    /// Computes terminal positions in fine grid coordinates.
    /// </summary>
    private static List<TerminalPosition> ComputeTerminalPositions(
        CoarseGridResult placement,
        CircuitGraph graph
    )
    {
        var positions = new List<TerminalPosition>();

        foreach (var (deviceId, cell) in placement.DevicePlacements)
        {
            var device = graph.Devices.GetValueOrDefault(deviceId);
            if (device == null)
            {
                continue;
            }

            var deviceType = device.DeviceType.ToLowerInvariant();
            var baseX = cell.Column * CellWidth + CellWidth / 2;
            var baseY = cell.Row * CellHeight + RailMargin + CellHeight / 2;

            if (deviceType is "nmos" or "nfet" or "pmos" or "pfet")
            {
                // The vertical axis (where drain/source connects) is always at baseX + MosfetWidth/2
                // snapped to the routing grid. The gate is on the opposite side.
                // Mirroring affects visual placement but terminals stay on the same column axis.
                var verticalAxisX = SnapToGrid(baseX + MosfetWidth / 2);
                var gateX = cell.MirrorX
                    ? verticalAxisX + MosfetWidth / 2
                    : verticalAxisX - MosfetWidth;
                var gateY = baseY;

                // In the symbol, the "top" terminal (y=0.5) and "bottom" terminal (y=25.5)
                // For NMOS: top=drain, bottom=source (drain up toward load, source to GND)
                // For PMOS: top=source (to VDD), bottom=drain (down toward next stage)
                int drainY,
                    sourceY;
                if (deviceType is "pmos" or "pfet")
                {
                    sourceY = baseY - MosfetHeight / 3; // Source at top (VDD)
                    drainY = baseY + MosfetHeight / 3; // Drain at bottom
                }
                else
                {
                    drainY = baseY - MosfetHeight / 3; // Drain at top
                    sourceY = baseY + MosfetHeight / 3; // Source at bottom (GND)
                }

                positions.Add(
                    new TerminalPosition(deviceId, "G", SnapToGrid(gateX), SnapToGrid(gateY))
                );
                positions.Add(
                    new TerminalPosition(deviceId, "D", verticalAxisX, SnapToGrid(drainY))
                );
                positions.Add(
                    new TerminalPosition(deviceId, "S", verticalAxisX, SnapToGrid(sourceY))
                );
            }
            else if (deviceType is "resistor" or "capacitor")
            {
                // Align passive terminals with MOSFET drain/source axis
                var verticalAxisX = SnapToGrid(baseX + MosfetWidth / 2);
                var pY = baseY - PassiveWidth / 2;
                var nY = baseY + PassiveWidth / 2;

                positions.Add(new TerminalPosition(deviceId, "P", verticalAxisX, SnapToGrid(pY)));
                positions.Add(new TerminalPosition(deviceId, "N", verticalAxisX, SnapToGrid(nY)));
            }
        }

        return positions;
    }

    /// <summary>
    /// Groups terminals by their connected net.
    /// </summary>
    private static Dictionary<string, List<TerminalPosition>> GroupTerminalsByNet(
        List<TerminalPosition> terminals,
        CircuitGraph graph
    )
    {
        var byNet = new Dictionary<string, List<TerminalPosition>>();

        foreach (var term in terminals)
        {
            var netName = graph.GetNetForTerminal(term.DeviceId, term.Terminal);
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

    /// <summary>
    /// Routes a single net using SAT solver for optimal path.
    /// Falls back to simple routing if SAT times out.
    /// </summary>
    private static List<WireSegment> RouteNet(
        string netName,
        List<TerminalPosition> terminals,
        HashSet<(int, int, int, int)> usedSegments,
        int canvasWidth,
        int canvasHeight,
        CircuitGraph graph
    )
    {
        if (terminals.Count == 0)
        {
            return new List<WireSegment>();
        }

        // Rail nets (VDD/GND) can route with just 1 terminal
        if (graph.Supplies.Contains(netName))
        {
            return RouteToRail(netName, terminals, RailMargin / 2);
        }

        if (graph.Grounds.Contains(netName))
        {
            return RouteToRail(netName, terminals, canvasHeight - RailMargin / 2);
        }

        // Non-rail nets need at least 2 terminals to route
        if (terminals.Count < 2)
        {
            return new List<WireSegment>();
        }

        // TODO: SAT routing doesn't enforce proper connectivity yet
        // Use simple L-shaped routing for now
        return SimpleRoute(netName, terminals);
    }

    /// <summary>
    /// Routes terminals to a horizontal rail (VDD or GND).
    /// </summary>
    private static List<WireSegment> RouteToRail(
        string netName,
        List<TerminalPosition> terminals,
        int railY
    )
    {
        var segments = new List<WireSegment>();

        foreach (var term in terminals)
        {
            segments.Add(
                new WireSegment(
                    new GridPoint(term.X, term.Y),
                    new GridPoint(term.X, railY),
                    netName
                )
            );
        }

        if (terminals.Count > 1)
        {
            var minX = terminals.Min(t => t.X);
            var maxX = terminals.Max(t => t.X);
            segments.Add(
                new WireSegment(new GridPoint(minX, railY), new GridPoint(maxX, railY), netName)
            );
        }

        return segments;
    }

    /// <summary>
    /// Attempts SAT-based routing for a net.
    /// </summary>
    private static List<WireSegment>? TrySatRouting(
        string netName,
        List<TerminalPosition> terminals,
        HashSet<(int, int, int, int)> usedSegments,
        int canvasWidth,
        int canvasHeight
    )
    {
        var model = new CpModel();

        var minX = Math.Max(0, terminals.Min(t => t.X) - 3 * RoutingPitch);
        var maxX = Math.Min(canvasWidth, terminals.Max(t => t.X) + 3 * RoutingPitch);
        var minY = Math.Max(0, terminals.Min(t => t.Y) - 3 * RoutingPitch);
        var maxY = Math.Min(canvasHeight, terminals.Max(t => t.Y) + 3 * RoutingPitch);

        var gridPointsX = new List<int>();
        var gridPointsY = new List<int>();

        for (var x = minX; x <= maxX; x += RoutingPitch)
        {
            gridPointsX.Add(x);
        }
        for (var y = minY; y <= maxY; y += RoutingPitch)
        {
            gridPointsY.Add(y);
        }

        foreach (var term in terminals)
        {
            if (!gridPointsX.Contains(term.X))
            {
                gridPointsX.Add(term.X);
            }
            if (!gridPointsY.Contains(term.Y))
            {
                gridPointsY.Add(term.Y);
            }
        }

        gridPointsX.Sort();
        gridPointsY.Sort();

        var segmentVars = new Dictionary<(int, int, int, int), BoolVar>();
        var segments = new List<(int x1, int y1, int x2, int y2)>();

        for (var i = 0; i < gridPointsX.Count; i++)
        {
            for (var j = 0; j < gridPointsY.Count; j++)
            {
                var x = gridPointsX[i];
                var y = gridPointsY[j];

                if (i + 1 < gridPointsX.Count)
                {
                    var x2 = gridPointsX[i + 1];
                    var key = NormalizeSegment(x, y, x2, y);
                    if (!usedSegments.Contains(key))
                    {
                        segments.Add((x, y, x2, y));
                        segmentVars[key] = model.NewBoolVar($"h_{x}_{y}_{x2}");
                    }
                }

                if (j + 1 < gridPointsY.Count)
                {
                    var y2 = gridPointsY[j + 1];
                    var key = NormalizeSegment(x, y, x, y2);
                    if (!usedSegments.Contains(key))
                    {
                        segments.Add((x, y, x, y2));
                        segmentVars[key] = model.NewBoolVar($"v_{x}_{y}_{y2}");
                    }
                }
            }
        }

        if (segmentVars.Count == 0)
        {
            return null;
        }

        var terminalPoints = terminals.Select(t => (t.X, t.Y)).ToHashSet();
        var allPoints = new HashSet<(int, int)>();

        foreach (var (x1, y1, x2, y2) in segments)
        {
            allPoints.Add((x1, y1));
            allPoints.Add((x2, y2));
        }

        foreach (var point in allPoints)
        {
            var adjacentSegments = new List<BoolVar>();

            foreach (var (x1, y1, x2, y2) in segments)
            {
                var key = NormalizeSegment(x1, y1, x2, y2);
                if (!segmentVars.TryGetValue(key, out var segVar))
                {
                    continue;
                }

                if ((x1, y1) == point || (x2, y2) == point)
                {
                    adjacentSegments.Add(segVar);
                }
            }

            if (adjacentSegments.Count == 0)
            {
                continue;
            }

            if (terminalPoints.Contains(point))
            {
                model.Add(LinearExpr.Sum(adjacentSegments) >= 1);
            }
        }

        var objective = new List<LinearExpr>();
        foreach (var (_, segVar) in segmentVars)
        {
            objective.Add(segVar);
        }

        if (objective.Count > 0)
        {
            model.Minimize(LinearExpr.Sum(objective));
        }

        var solver = new CpSolver();
        solver.StringParameters = $"max_time_in_seconds:{MaxSolveTimeSeconds}";
        var status = solver.Solve(model);

        if (status != CpSolverStatus.Optimal && status != CpSolverStatus.Feasible)
        {
            return null;
        }

        var result = new List<WireSegment>();

        foreach (var ((x1, y1, x2, y2), segVar) in segmentVars)
        {
            if (solver.BooleanValue(segVar))
            {
                result.Add(new WireSegment(new GridPoint(x1, y1), new GridPoint(x2, y2), netName));
            }
        }

        return result;
    }

    /// <summary>
    /// Routes using a horizontal trunk with vertical drops (comb-style routing).
    /// Creates cleaner schematics than star routing to a center point.
    /// </summary>
    private static List<WireSegment> SimpleRoute(string netName, List<TerminalPosition> terminals)
    {
        var segments = new List<WireSegment>();

        if (terminals.Count < 2)
        {
            return segments;
        }

        // Sort terminals by X position
        var sorted = terminals.OrderBy(t => t.X).ToList();

        // Compute trunk Y as median of terminal Ys (tends to minimize total wire length)
        var ys = terminals.Select(t => t.Y).OrderBy(y => y).ToList();
        var trunkY = SnapToGrid(ys[ys.Count / 2]);

        // Create vertical drops from each terminal to the trunk
        foreach (var term in sorted)
        {
            if (term.Y != trunkY)
            {
                segments.Add(
                    new WireSegment(
                        new GridPoint(term.X, term.Y),
                        new GridPoint(term.X, trunkY),
                        netName
                    )
                );
            }
        }

        // Create horizontal trunk connecting all drop points
        for (var i = 0; i < sorted.Count - 1; i++)
        {
            var x1 = sorted[i].X;
            var x2 = sorted[i + 1].X;
            if (x1 != x2)
            {
                segments.Add(
                    new WireSegment(new GridPoint(x1, trunkY), new GridPoint(x2, trunkY), netName)
                );
            }
        }

        return segments;
    }

    /// <summary>
    /// Finds junction points where 3 or more wire segments meet.
    /// </summary>
    private static List<GridPoint> FindJunctions(List<WireSegment> segments)
    {
        var pointCounts = new Dictionary<GridPoint, int>();

        foreach (var seg in segments)
        {
            pointCounts[seg.From] = pointCounts.GetValueOrDefault(seg.From, 0) + 1;
            pointCounts[seg.To] = pointCounts.GetValueOrDefault(seg.To, 0) + 1;
        }

        return pointCounts.Where(kv => kv.Value >= 3).Select(kv => kv.Key).ToList();
    }

    /// <summary>
    /// Snaps a coordinate to the routing grid (rounds to nearest).
    /// </summary>
    private static int SnapToGrid(int value)
    {
        return ((value + RoutingPitch / 2) / RoutingPitch) * RoutingPitch;
    }

    /// <summary>
    /// Normalizes a segment so that (from, to) is always ordered consistently.
    /// </summary>
    private static (int, int, int, int) NormalizeSegment(int x1, int y1, int x2, int y2)
    {
        if (x1 < x2 || (x1 == x2 && y1 < y2))
        {
            return (x1, y1, x2, y2);
        }
        return (x2, y2, x1, y1);
    }
}
