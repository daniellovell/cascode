namespace Cascode.Render.Routing;

using Cascode.Render.Analysis;
using Cascode.Render.Layout;
using Cascode.Render.Placement;

/// <summary>
/// Simple maze-based router for schematic wire routing.
/// Routes nets iteratively using Manhattan paths with obstacle avoidance.
/// </summary>
public static partial class MazeRouter
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
    public static RoutingResult Route(
        CoarseGridResult placement,
        CircuitGraph graph,
        RouteConstraintSet? constraints = null
    ) => RouteWithOccupied(placement, graph, constraints).Result;

    /// <summary>
    /// Routes all nets and returns the occupied segments map (for testing).
    /// Used to verify that the occupied map only contains segments in the final result.
    /// </summary>
    /// <param name="placement">The coarse grid placement result.</param>
    /// <param name="graph">The circuit graph.</param>
    /// <returns>A tuple containing the routing result and the final occupied segments map.</returns>
    internal static (RoutingResult Result, OccupiedSegments Occupied) RouteWithOccupied(
        CoarseGridResult placement,
        CircuitGraph graph,
        RouteConstraintSet? constraints = null
    )
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

            List<WireSegment> segs;
            if (
                constraints is not null
                && constraints.NetRoutes.TryGetValue(netName, out var routeConstraint)
                && routeConstraint.Waypoints.Count > 0
            )
            {
                segs = RouteNetWithWaypoints(
                    netName,
                    terms,
                    routeConstraint.Waypoints,
                    obstacles,
                    occupied,
                    forbiddenPoints
                );
            }
            else
            {
                segs = RouteNet(netName, terms, obstacles, occupied, forbiddenPoints);
            }

            AddSegments(segs, netName, occupied, allSegments, segmentsByNet);
        }

        var junctions = FindJunctions(allSegments, terminals);

        var result = new RoutingResult
        {
            Segments = allSegments,
            Junctions = junctions,
            SegmentsByNet = segmentsByNet,
            CanvasWidth = canvasWidth,
            CanvasHeight = canvasHeight,
            TerminalPositions = terminals,
        };

        return (result, occupied);
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
        var rawSegments = new List<WireSegment>();

        if (terminals.Count < 2)
        {
            return rawSegments;
        }

        // Build MST of terminals
        var mstEdges = ComputeMST(terminals);

        // Create an overlay to track raw segments during intra-net routing.
        // This prevents ghost segments from polluting the shared occupied map
        // when they get pruned later by merge/prune operations.
        var overlay = new OverlayOccupiedSegments(occupied);

        // Route each edge
        foreach (var (fromIdx, toIdx) in mstEdges)
        {
            var from = new GridPoint(terminals[fromIdx].X, terminals[fromIdx].Y);
            var to = new GridPoint(terminals[toIdx].X, terminals[toIdx].Y);

            var path = PathFinder.FindPath(from, to, netName, obstacles, overlay, forbiddenPoints);
            rawSegments.AddRange(path);

            // Add path segments to overlay so subsequent edges avoid them
            foreach (var seg in path)
            {
                overlay.Add(seg);
            }
        }

        // Post-process to merge overlapping collinear segments
        var mergedSegments = MergeCollinearSegments(rawSegments, netName);

        // Build set of terminal points for this net
        var terminalPoints = terminals.Select(t => new GridPoint(t.X, t.Y)).ToHashSet();

        // Eliminate redundant parallel horizontal paths
        var cleanedSegments = EliminateRedundantParallelPaths(
            mergedSegments,
            netName,
            terminalPoints
        );

        return cleanedSegments;
    }

    private static List<WireSegment> RouteNetWithWaypoints(
        string netName,
        List<TerminalPosition> terminals,
        IReadOnlyList<GridPoint> waypoints,
        IReadOnlyList<Obstacle> obstacles,
        OccupiedSegments occupied,
        IReadOnlySet<GridPoint> forbiddenPoints
    )
    {
        if (waypoints.Count == 0 || terminals.Count < 2)
        {
            return RouteNet(netName, terminals, obstacles, occupied, forbiddenPoints);
        }

        var rawSegments = new List<WireSegment>();
        var overlay = new OverlayOccupiedSegments(occupied);

        var startTerminal = SelectClosestTerminal(terminals, waypoints[0], null);
        var endTerminal = SelectClosestTerminal(terminals, waypoints[^1], startTerminal);

        var routePoints = new List<GridPoint> { startTerminal };
        routePoints.AddRange(waypoints);
        routePoints.Add(endTerminal);

        for (var i = 0; i + 1 < routePoints.Count; i++)
        {
            var segmentPath = PathFinder.FindPath(
                routePoints[i],
                routePoints[i + 1],
                netName,
                obstacles,
                overlay,
                forbiddenPoints
            );
            rawSegments.AddRange(segmentPath);
            foreach (var seg in segmentPath)
            {
                overlay.Add(seg);
            }
        }

        // Attach any remaining terminals to the nearest routed waypoint.
        var anchored = new HashSet<GridPoint> { startTerminal, endTerminal };
        foreach (
            var terminal in terminals
                .Select(t => new GridPoint(t.X, t.Y))
                .Where(t => !anchored.Contains(t))
        )
        {
            var attachPoint = routePoints
                .OrderBy(point => ManhattanDistance(point, terminal))
                .FirstOrDefault();
            var segmentPath = PathFinder.FindPath(
                terminal,
                attachPoint,
                netName,
                obstacles,
                overlay,
                forbiddenPoints
            );
            rawSegments.AddRange(segmentPath);
            foreach (var seg in segmentPath)
            {
                overlay.Add(seg);
            }
        }

        var merged = MergeCollinearSegments(rawSegments, netName);
        var terminalPoints = terminals.Select(t => new GridPoint(t.X, t.Y)).ToHashSet();
        return EliminateRedundantParallelPaths(merged, netName, terminalPoints);
    }

    private static GridPoint SelectClosestTerminal(
        IReadOnlyList<TerminalPosition> terminals,
        GridPoint target,
        GridPoint? excluded
    )
    {
        var candidates = terminals.Select(t => new GridPoint(t.X, t.Y));
        if (excluded is not null)
        {
            candidates = candidates.Where(point => point != excluded.Value);
        }

        return candidates.OrderBy(point => ManhattanDistance(point, target)).First();
    }

    private static int ManhattanDistance(GridPoint a, GridPoint b)
    {
        return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    }

    /// <summary>
    /// Computes minimum spanning tree using Prim's algorithm with biased distance.
    /// Terminals sharing the same X or Y coordinate get a cost discount to encourage
    /// direct connections along device axes rather than routing through ports.
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

                    var dist = BiasedDistance(terminals[i], terminals[j]);
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

    /// <summary>
    /// Computes biased distance between terminals for MST construction.
    /// Gate-to-gate connections get a 50% discount to encourage tying gates together
    /// before routing to other terminals (like resistor internal nodes).
    /// Terminals sharing the same X coordinate (vertical alignment) also get a 50% discount
    /// to encourage routing along device stacks.
    /// </summary>
    private static int BiasedDistance(TerminalPosition a, TerminalPosition b)
    {
        var manhattan = Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

        // Gate-to-gate connections get a strong preference.
        // This ensures gates (e.g., PMOS load gates) connect directly to each other
        // before routing down to other nodes (like resistor taps).
        if (a.Terminal == "G" && b.Terminal == "G")
        {
            return manhattan / 2;
        }

        // Vertical connections (same X) get a preference.
        // This ensures devices in the same vertical stack are connected directly
        // but doesn't shortcut horizontal connections that should go through center devices.
        if (a.X == b.X)
        {
            return manhattan / 2;
        }

        return manhattan;
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
}
