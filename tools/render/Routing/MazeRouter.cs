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
    /// <summary>
    /// Compute routing for all nets on the given placement and circuit graph, producing wire segments, junctions, and per-net routes.
    /// </summary>
    /// <param name="placement">Coarse placement result that defines canvas dimensions and component positions used for routing.</param>
    /// <param name="graph">Circuit graph describing nets and terminals to be routed.</param>
    /// <param name="constraints">Optional routing constraints (for example per-net waypoints) that modify routing behaviour.</param>
    /// <returns>A RoutingResult containing wire segments, detected junctions, per-net segment lists, canvas size, and terminal positions.</returns>
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
    /// <summary>
    /// Routes all nets for the given placement and circuit, producing routing geometry and the final occupancy map.
    /// </summary>
    /// <param name="placement">Coarse-grid placement of devices used to compute canvas size and obstacles.</param>
    /// <param name="graph">Circuit connectivity including supplies and grounds to be routed.</param>
    /// <param name="constraints">Optional per-net routing constraints (e.g., waypoints) to influence routing; null to use default behavior.</param>
    /// <returns>
    /// A tuple where `Result` is a RoutingResult containing routed segments, junctions, per-net segment lists, canvas dimensions, and terminal positions,
    /// and `Occupied` is the OccupiedSegments map reflecting all occupied grid segments after routing.
    /// </returns>
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
            var terms = terminalsByNet.GetValueOrDefault(supply, new List<TerminalPosition>());
            var segs = RouteRail(supply, terms, canvasWidth, DeviceGeometry.RailMargin / 2);
            AddSegments(segs, supply, occupied, allSegments, segmentsByNet);
        }

        foreach (var ground in graph.Grounds)
        {
            var terms = terminalsByNet.GetValueOrDefault(ground, new List<TerminalPosition>());
            var railY = canvasHeight - DeviceGeometry.RailMargin / 2;
            var segs = RouteRail(ground, terms, canvasWidth, railY);
            AddSegments(segs, ground, occupied, allSegments, segmentsByNet);
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
        }

        // Always emit a full-width rail so declared power nets are visible and connectable.
        segments.Add(
            new WireSegment(new GridPoint(0, railY), new GridPoint(canvasWidth, railY), netName)
        );

        return segments;
    }

    /// <summary>
    /// Routes a signal net by building MST and routing each edge.
    /// <summary>
    /// Connects the given terminals for a signal net with obstacle-aware Manhattan paths and returns the cleaned set of wire segments for that net.
    /// </summary>
    /// <param name="netName">The name of the net being routed.</param>
    /// <param name="terminals">List of terminal positions (grid coordinates) belonging to the net.</param>
    /// <param name="obstacles">Obstacles that pathfinding must avoid.</param>
    /// <param name="occupied">Tracker of already occupied segments used to prevent collisions with existing routes.</param>
    /// <param name="forbiddenPoints">Grid points that must not be traversed (for example, terminals from other nets).</param>
    /// <returns>A list of cleaned WireSegment objects representing the routed wires for the net; empty if fewer than two terminals.</returns>
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

        // Create an overlay to track raw segments during intra-net routing.
        // This prevents ghost segments from polluting the shared occupied map
        // when they get pruned later by merge/prune operations.
        var overlay = new OverlayOccupiedSegments(occupied);
        var remainingTerminals = terminals.ToList();

        RouteBoundarySegmentsFirst(
            netName,
            terminals,
            remainingTerminals,
            obstacles,
            overlay,
            forbiddenPoints,
            rawSegments
        );

        // Build MST of the remaining terminals
        var mstEdges = ComputeMST(remainingTerminals);

        // Route each edge
        foreach (var (fromIdx, toIdx) in mstEdges)
        {
            var from = new GridPoint(remainingTerminals[fromIdx].X, remainingTerminals[fromIdx].Y);
            var to = new GridPoint(remainingTerminals[toIdx].X, remainingTerminals[toIdx].Y);

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

    private static void RouteBoundarySegmentsFirst(
        string netName,
        IReadOnlyList<TerminalPosition> terminals,
        List<TerminalPosition> remainingTerminals,
        IReadOnlyList<Obstacle> obstacles,
        OverlayOccupiedSegments overlay,
        IReadOnlySet<GridPoint> forbiddenPoints,
        List<WireSegment> rawSegments
    )
    {
        var removablePorts = new List<TerminalPosition>();
        foreach (
            var portTerminal in terminals.Where(t =>
                t.DeviceId.StartsWith("PORT_", StringComparison.Ordinal)
            )
        )
        {
            var anchor = terminals
                .Where(t =>
                    t != portTerminal
                    && !t.DeviceId.StartsWith("PORT_", StringComparison.Ordinal)
                    && t.Y == portTerminal.Y
                )
                .OrderBy(t => Math.Abs(t.X - portTerminal.X))
                .FirstOrDefault();
            if (anchor is null)
            {
                continue;
            }

            var path = PathFinder.FindPath(
                new GridPoint(portTerminal.X, portTerminal.Y),
                new GridPoint(anchor.X, anchor.Y),
                netName,
                obstacles,
                overlay,
                forbiddenPoints
            );
            if (path.Count == 0 || path.Any(segment => segment.From.Y != segment.To.Y))
            {
                continue;
            }

            rawSegments.AddRange(path);
            foreach (var segment in path)
            {
                overlay.Add(segment);
            }

            removablePorts.Add(portTerminal);
        }

        foreach (var portTerminal in removablePorts)
        {
            remainingTerminals.Remove(portTerminal);
        }
    }

    /// <summary>
    /// Routes a net using the provided waypoints as intermediate anchors and produces a cleaned set of wire segments.
    /// </summary>
    /// <param name="netName">The name of the net being routed.</param>
    /// <param name="terminals">List of terminal positions belonging to the net.</param>
    /// <param name="waypoints">Ordered grid points that the route should pass through; when empty or if fewer than two terminals are present, the method falls back to standard MST-based routing.</param>
    /// <param name="obstacles">Static obstacles to avoid during pathfinding.</param>
    /// <param name="occupied">Current occupied-segment tracker used to avoid collisions with already routed wires.</param>
    /// <param name="forbiddenPoints">Grid points that the route must not traverse (forbidden locations from other nets).</param>
    /// <returns>A list of WireSegment objects representing the routed net after merging collinear segments and removing redundant parallel paths.</returns>
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

        var startTerminalIndex = SelectClosestTerminalIndex(terminals, waypoints[0], null);
        var endTerminalIndex = SelectClosestTerminalIndex(
            terminals,
            waypoints[^1],
            startTerminalIndex
        );
        var startTerminal = ToGridPoint(terminals[startTerminalIndex]);
        var endTerminal = ToGridPoint(terminals[endTerminalIndex]);

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
        var anchoredIndices = new HashSet<int> { startTerminalIndex, endTerminalIndex };
        for (var terminalIndex = 0; terminalIndex < terminals.Count; terminalIndex++)
        {
            if (anchoredIndices.Contains(terminalIndex))
            {
                continue;
            }

            var terminal = ToGridPoint(terminals[terminalIndex]);
            var attachPoint =
                routePoints.Count > 0
                    ? routePoints.OrderBy(point => ManhattanDistance(point, terminal)).First()
                    : terminal;
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

    /// <summary>
    /// Selects the index of the terminal closest to a target grid point using Manhattan distance.
    /// </summary>
    /// <param name="terminals">List of terminal positions to search.</param>
    /// <param name="target">Target grid point to measure distance to.</param>
    /// <param name="excludedIndex">Optional terminal index to exclude from consideration.</param>
    /// <returns>
    /// The index of the closest terminal. If no suitable index is found but <paramref name="terminals"/> is non-empty, returns 0; returns -1 if <paramref name="terminals"/> is empty.
    /// </returns>
    private static int SelectClosestTerminalIndex(
        IReadOnlyList<TerminalPosition> terminals,
        GridPoint target,
        int? excludedIndex
    )
    {
        var bestIndex = -1;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < terminals.Count; i++)
        {
            if (excludedIndex.HasValue && i == excludedIndex.Value)
            {
                continue;
            }

            var point = ToGridPoint(terminals[i]);
            var distance = ManhattanDistance(point, target);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        if (bestIndex >= 0)
        {
            return bestIndex;
        }

        return terminals.Count > 0 ? 0 : -1;
    }

    /// <summary>
    /// Convert a TerminalPosition to a GridPoint.
    /// </summary>
    /// <returns>A GridPoint with the terminal's X and Y coordinates.</returns>
    private static GridPoint ToGridPoint(TerminalPosition terminal)
    {
        return new GridPoint(terminal.X, terminal.Y);
    }

    /// <summary>
    /// Computes the Manhattan distance between two grid points.
    /// </summary>
    /// <param name="a">The first grid point.</param>
    /// <param name="b">The second grid point.</param>
    /// <returns>The Manhattan distance (sum of absolute differences in X and Y) between the two points.</returns>
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

        // Prefer horizontal lanes when one endpoint is an external port.
        // This helps feedthrough paths stay straight at the circuit boundary.
        var aIsPort = a.DeviceId.StartsWith("PORT_", StringComparison.Ordinal);
        var bIsPort = b.DeviceId.StartsWith("PORT_", StringComparison.Ordinal);
        if (a.Y == b.Y && (aIsPort || bIsPort))
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
