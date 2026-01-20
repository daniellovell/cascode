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

        var junctions = FindJunctions(allSegments, terminals);

        return new RoutingResult
        {
            Segments = allSegments,
            Junctions = junctions,
            SegmentsByNet = segmentsByNet,
            CanvasWidth = canvasWidth,
            CanvasHeight = canvasHeight,
            TerminalPositions = terminals,
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
        var rawSegments = new List<WireSegment>();

        if (terminals.Count < 2)
        {
            return rawSegments;
        }

        // Build MST of terminals
        var mstEdges = ComputeMST(terminals);

        // Route each edge
        foreach (var (fromIdx, toIdx) in mstEdges)
        {
            var from = new GridPoint(terminals[fromIdx].X, terminals[fromIdx].Y);
            var to = new GridPoint(terminals[toIdx].X, terminals[toIdx].Y);

            var path = PathFinder.FindPath(from, to, netName, obstacles, occupied, forbiddenPoints);
            rawSegments.AddRange(path);

            // Add path segments to occupied immediately so subsequent edges avoid them
            foreach (var seg in path)
            {
                occupied.Add(seg);
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

    /// <summary>
    /// Eliminates redundant parallel horizontal paths that converge at the same endpoint.
    /// When multiple horizontal segments share the same X range at different Y coordinates,
    /// keep only one horizontal path and add vertical connectors to maintain connectivity.
    /// </summary>
    private static List<WireSegment> EliminateRedundantParallelPaths(
        List<WireSegment> segments,
        string netName,
        IReadOnlySet<GridPoint> terminalPoints
    )
    {
        if (segments.Count <= 1)
        {
            return segments;
        }

        var horizontalSegments = segments.Where(s => s.From.Y == s.To.Y).ToList();
        var verticalSegments = segments.Where(s => s.From.X == s.To.X).ToList();

        if (horizontalSegments.Count <= 1)
        {
            return segments;
        }

        var byXRange = GroupHorizontalSegmentsByXRange(horizontalSegments);
        var toRemove = new HashSet<WireSegment>();
        var toAdd = new List<WireSegment>();

        foreach (var (range, segs) in byXRange)
        {
            if (segs.Count <= 1)
            {
                continue;
            }

            var sortedByY = segs.OrderBy(s => s.From.Y).ToList();
            var midY = (sortedByY.First().From.Y + sortedByY.Last().From.Y) / 2;
            var kept = sortedByY.MinBy(s => Math.Abs(s.From.Y - midY))!;

            foreach (var seg in segs)
            {
                if (seg != kept)
                {
                    toRemove.Add(seg);
                }
            }

            var connectors = GenerateVerticalConnectorsForParallelGroup(
                segs,
                range,
                kept,
                verticalSegments,
                netName
            );
            toAdd.AddRange(connectors);
        }

        if (toRemove.Count == 0)
        {
            return segments;
        }

        var result = segments.Where(s => !toRemove.Contains(s)).ToList();
        result.AddRange(toAdd);
        result = MergeCollinearSegments(result, netName);
        result = RemoveOrphanedStubs(result, terminalPoints);

        return result;
    }

    /// <summary>
    /// Groups horizontal segments by their X-coordinate range (minX, maxX).
    /// </summary>
    private static Dictionary<
        (int minX, int maxX),
        List<WireSegment>
    > GroupHorizontalSegmentsByXRange(List<WireSegment> horizontalSegments)
    {
        var byXRange = new Dictionary<(int minX, int maxX), List<WireSegment>>();
        foreach (var seg in horizontalSegments)
        {
            var minX = Math.Min(seg.From.X, seg.To.X);
            var maxX = Math.Max(seg.From.X, seg.To.X);
            var key = (minX, maxX);
            if (!byXRange.TryGetValue(key, out var list))
            {
                list = new List<WireSegment>();
                byXRange[key] = list;
            }
            list.Add(seg);
        }
        return byXRange;
    }

    /// <summary>
    /// Generates vertical connector segments to maintain connectivity when removing
    /// redundant parallel horizontal segments. Connectors are added at endpoints only
    /// when existing vertical segments don't already provide connectivity.
    /// </summary>
    private static IEnumerable<WireSegment> GenerateVerticalConnectorsForParallelGroup(
        List<WireSegment> segs,
        (int minX, int maxX) range,
        WireSegment kept,
        List<WireSegment> verticalSegments,
        string netName
    )
    {
        var connectors = new List<WireSegment>();

        foreach (var seg in segs)
        {
            if (seg == kept)
            {
                continue;
            }

            var segY = seg.From.Y;
            var keptY = kept.From.Y;

            var hasLeftVertical = verticalSegments.Any(v =>
                v.From.X == range.minX
                && Math.Min(v.From.Y, v.To.Y) <= Math.Min(segY, keptY)
                && Math.Max(v.From.Y, v.To.Y) >= Math.Max(segY, keptY)
            );

            var hasRightVertical = verticalSegments.Any(v =>
                v.From.X == range.maxX
                && Math.Min(v.From.Y, v.To.Y) <= Math.Min(segY, keptY)
                && Math.Max(v.From.Y, v.To.Y) >= Math.Max(segY, keptY)
            );

            if (hasLeftVertical || hasRightVertical)
            {
                continue;
            }

            connectors.Add(
                new WireSegment(
                    new GridPoint(range.minX, Math.Min(segY, keptY)),
                    new GridPoint(range.minX, Math.Max(segY, keptY)),
                    netName
                )
            );
        }

        return connectors;
    }

    /// <summary>
    /// Removes wire segments that have become orphaned stubs (dead ends not connected
    /// to the rest of the network). A stub is a segment where one endpoint only
    /// connects to that single segment and is not a terminal.
    /// </summary>
    private static List<WireSegment> RemoveOrphanedStubs(
        List<WireSegment> segments,
        IReadOnlySet<GridPoint> terminalPoints
    )
    {
        if (segments.Count <= 1)
        {
            return segments;
        }

        var changed = true;
        var result = segments.ToList();

        // Iteratively remove stubs until none remain
        while (changed)
        {
            changed = false;

            // Count endpoint occurrences
            var endpointCounts = new Dictionary<GridPoint, int>();
            foreach (var seg in result)
            {
                endpointCounts[seg.From] = endpointCounts.GetValueOrDefault(seg.From) + 1;
                endpointCounts[seg.To] = endpointCounts.GetValueOrDefault(seg.To) + 1;
            }

            // Find segments where one endpoint is a dead end (appears only once)
            // and that dead end is NOT a terminal
            var toRemove = new List<WireSegment>();
            foreach (var seg in result)
            {
                var fromCount = endpointCounts[seg.From];
                var toCount = endpointCounts[seg.To];

                // A segment is a stub if:
                // - One endpoint appears only once (dead end)
                // - That dead-end point is NOT a terminal
                // - The other endpoint has multiple connections
                if (fromCount == 1 && toCount > 1 && !terminalPoints.Contains(seg.From))
                {
                    toRemove.Add(seg);
                    changed = true;
                }
                else if (toCount == 1 && fromCount > 1 && !terminalPoints.Contains(seg.To))
                {
                    toRemove.Add(seg);
                    changed = true;
                }
            }

            foreach (var seg in toRemove)
            {
                result.Remove(seg);
            }
        }

        return result;
    }

    /// <summary>
    /// Merges overlapping collinear segments and creates proper branch points.
    /// This eliminates duplicate coverage when multiple MST edges share a common path.
    /// </summary>
    private static List<WireSegment> MergeCollinearSegments(
        List<WireSegment> segments,
        string netName
    )
    {
        if (segments.Count <= 1)
        {
            return segments;
        }

        var result = new List<WireSegment>();

        // Group vertical segments by X coordinate
        var verticalByX = GroupVerticalSegmentsByX(segments);

        // Group horizontal segments by Y coordinate
        var horizontalByY = GroupHorizontalSegmentsByY(segments);

        MergeVerticalSegments(verticalByX, horizontalByY, netName, result);
        MergeHorizontalSegments(horizontalByY, verticalByX, netName, result);

        return result;
    }

    /// <summary>
    /// Groups vertical segments by their X coordinate.
    /// </summary>
    private static Dictionary<int, List<WireSegment>> GroupVerticalSegmentsByX(
        List<WireSegment> segments
    )
    {
        return segments
            .Where(s => s.From.X == s.To.X)
            .GroupBy(s => s.From.X)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>
    /// Groups horizontal segments by their Y coordinate.
    /// </summary>
    private static Dictionary<int, List<WireSegment>> GroupHorizontalSegmentsByY(
        List<WireSegment> segments
    )
    {
        return segments
            .Where(s => s.From.Y == s.To.Y)
            .GroupBy(s => s.From.Y)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>
    /// Merges vertical segments by collecting important Y coordinates on each vertical axis
    /// and creating merged segments that connect consecutive covered points.
    /// </summary>
    private static void MergeVerticalSegments(
        Dictionary<int, List<WireSegment>> verticalByX,
        Dictionary<int, List<WireSegment>> horizontalByY,
        string netName,
        List<WireSegment> result
    )
    {
        // Collect all important Y coordinates on each vertical axis (endpoints and intersections with horizontal)
        foreach (var (x, vertSegs) in verticalByX)
        {
            var yPoints = new SortedSet<int>();

            // Add endpoints from all vertical segments on this axis
            foreach (var seg in vertSegs)
            {
                yPoints.Add(seg.From.Y);
                yPoints.Add(seg.To.Y);
            }

            // Add intersection points with horizontal segments
            foreach (var (y, horSegs) in horizontalByY)
            {
                foreach (var hSeg in horSegs)
                {
                    var minX = Math.Min(hSeg.From.X, hSeg.To.X);
                    var maxX = Math.Max(hSeg.From.X, hSeg.To.X);

                    // Check if this vertical axis intersects any horizontal segment
                    if (x >= minX && x <= maxX)
                    {
                        // Check if any vertical segment covers this Y
                        foreach (var vSeg in vertSegs)
                        {
                            var minY = Math.Min(vSeg.From.Y, vSeg.To.Y);
                            var maxY = Math.Max(vSeg.From.Y, vSeg.To.Y);
                            if (y >= minY && y <= maxY)
                            {
                                yPoints.Add(y);
                                break;
                            }
                        }
                    }
                }
            }

            // Create merged segments by connecting consecutive covered points
            var sortedY = yPoints.ToList();
            for (var i = 0; i < sortedY.Count - 1; i++)
            {
                var y1 = sortedY[i];
                var y2 = sortedY[i + 1];

                // Check if any original segment covers this range
                var covered = vertSegs.Any(seg =>
                {
                    var minY = Math.Min(seg.From.Y, seg.To.Y);
                    var maxY = Math.Max(seg.From.Y, seg.To.Y);
                    return minY <= y1 && maxY >= y2;
                });

                if (covered)
                {
                    result.Add(
                        new WireSegment(new GridPoint(x, y1), new GridPoint(x, y2), netName)
                    );
                }
            }
        }
    }

    /// <summary>
    /// Merges horizontal segments by collecting important X coordinates on each horizontal axis
    /// and creating merged segments that connect consecutive covered points.
    /// </summary>
    private static void MergeHorizontalSegments(
        Dictionary<int, List<WireSegment>> horizontalByY,
        Dictionary<int, List<WireSegment>> verticalByX,
        string netName,
        List<WireSegment> result
    )
    {
        // Same for horizontal segments - collect important X coordinates
        foreach (var (y, horSegs) in horizontalByY)
        {
            var xPoints = new SortedSet<int>();

            // Add endpoints from all horizontal segments on this axis
            foreach (var seg in horSegs)
            {
                xPoints.Add(seg.From.X);
                xPoints.Add(seg.To.X);
            }

            // Add intersection points with vertical segments
            foreach (var (x, vertSegs) in verticalByX)
            {
                foreach (var vSeg in vertSegs)
                {
                    var minY = Math.Min(vSeg.From.Y, vSeg.To.Y);
                    var maxY = Math.Max(vSeg.From.Y, vSeg.To.Y);

                    // Check if this horizontal axis intersects any vertical segment
                    if (y >= minY && y <= maxY)
                    {
                        // Check if any horizontal segment covers this X
                        foreach (var hSeg in horSegs)
                        {
                            var minX = Math.Min(hSeg.From.X, hSeg.To.X);
                            var maxX = Math.Max(hSeg.From.X, hSeg.To.X);
                            if (x >= minX && x <= maxX)
                            {
                                xPoints.Add(x);
                                break;
                            }
                        }
                    }
                }
            }

            // Create merged segments by connecting consecutive covered points
            var sortedX = xPoints.ToList();
            for (var i = 0; i < sortedX.Count - 1; i++)
            {
                var x1 = sortedX[i];
                var x2 = sortedX[i + 1];

                // Check if any original segment covers this range
                var covered = horSegs.Any(seg =>
                {
                    var minX = Math.Min(seg.From.X, seg.To.X);
                    var maxX = Math.Max(seg.From.X, seg.To.X);
                    return minX <= x1 && maxX >= x2;
                });

                if (covered)
                {
                    result.Add(
                        new WireSegment(new GridPoint(x1, y), new GridPoint(x2, y), netName)
                    );
                }
            }
        }
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

    /// <summary>
    /// Finds junction points where 3+ connections meet (wire segments + device terminals).
    /// A device terminal at a wire endpoint counts as +1 connection.
    /// </summary>
    private static List<GridPoint> FindJunctions(
        List<WireSegment> segments,
        List<TerminalPosition> terminals
    )
    {
        // Group segments by net for proper junction detection
        var segmentsByNet = segments
            .GroupBy(s => s.NetName)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Create lookup for terminal positions by coordinate
        var terminalPoints = new HashSet<GridPoint>();
        foreach (var term in terminals)
        {
            terminalPoints.Add(new GridPoint(term.X, term.Y));
        }

        var junctions = new HashSet<GridPoint>();

        foreach (var (_, netSegments) in segmentsByNet)
        {
            var pointCounts = new Dictionary<GridPoint, int>();

            foreach (var seg in netSegments)
            {
                pointCounts[seg.From] = pointCounts.GetValueOrDefault(seg.From, 0) + 1;
                pointCounts[seg.To] = pointCounts.GetValueOrDefault(seg.To, 0) + 1;
            }

            // Also count mid-segment intersections (only within same net)
            for (var i = 0; i < netSegments.Count; i++)
            {
                for (var j = i + 1; j < netSegments.Count; j++)
                {
                    var intersection = GetIntersection(netSegments[i], netSegments[j]);
                    if (intersection.HasValue)
                    {
                        var pt = intersection.Value;
                        pointCounts[pt] = pointCounts.GetValueOrDefault(pt, 0) + 2;
                    }
                }
            }

            // For points that are device terminals, add +1 (terminal is an implicit connection)
            foreach (var (point, _) in pointCounts)
            {
                if (terminalPoints.Contains(point))
                {
                    pointCounts[point] += 1;
                }
            }

            // Add points where 3+ connections meet
            foreach (var (point, count) in pointCounts)
            {
                if (count >= 3)
                {
                    junctions.Add(point);
                }
            }
        }

        return junctions.ToList();
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
                // Check if this is a horizontal passive
                var isHorizontalPassive = placement.HorizontalPassiveIds.Contains(deviceId);
                var isLeftOfAxis = cell.Column < placement.SymmetryAxis;

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

        // Left ports (inputs, bias) - use average Y
        var leftPorts = graph.InputPorts.Concat(graph.BiasPorts).ToList();
        var leftYs = ComputePortYPositions(leftPorts, terminalYByNet, preferMinY: false);
        foreach (var port in leftPorts)
        {
            var y = leftYs.GetValueOrDefault(port, DeviceGeometry.RailMargin + 50);
            positions.Add(new TerminalPosition($"PORT_{port}", "P", 0, y));
        }

        // Right ports (outputs) - use average Y for balanced routing
        var rightYs = ComputePortYPositions(
            graph.OutputPorts.ToList(),
            terminalYByNet,
            preferMinY: false
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
        bool preferMinY
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
                // min for outputs (align with topmost drain near loads), average for inputs
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
