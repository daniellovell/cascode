namespace Cascode.Render.Routing;

/// <summary>
/// Post-processing methods for MazeRouter: segment merging, parallel path elimination, and stub removal.
/// </summary>
public static partial class MazeRouter
{
    /// <summary>
    /// Eliminates redundant parallel horizontal paths that converge at the same endpoint.
    /// When multiple horizontal segments share the same X range at different Y coordinates,
    /// keep only one horizontal path and add vertical connectors to maintain connectivity.
    /// </summary>
    private static List<WireSegment> EliminateRedundantParallelPaths(
        List<WireSegment> segments,
        string netName,
        IReadOnlySet<GridPoint> terminalPoints,
        IReadOnlyList<Obstacle> hardObstacles
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
            var removedSegs = new List<WireSegment>();

            foreach (var seg in segs)
            {
                if (seg == kept)
                {
                    continue;
                }

                if (
                    WouldRemovingSegmentDisconnectTerminal(
                        seg,
                        range,
                        kept,
                        verticalSegments,
                        terminalPoints,
                        hardObstacles
                    )
                )
                {
                    continue;
                }

                toRemove.Add(seg);
                removedSegs.Add(seg);
            }

            var connectors = GenerateVerticalConnectorsForParallelGroup(
                removedSegs,
                range,
                kept,
                verticalSegments,
                netName,
                hardObstacles
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
        IReadOnlyList<WireSegment> removedSegs,
        (int minX, int maxX) range,
        WireSegment kept,
        List<WireSegment> verticalSegments,
        string netName,
        IReadOnlyList<Obstacle> hardObstacles
    )
    {
        var connectors = new List<WireSegment>();

        foreach (var seg in removedSegs)
        {
            var segY = seg.From.Y;
            var keptY = kept.From.Y;

            var hasLeftVertical = HasVerticalCoverage(range.minX, segY, keptY, verticalSegments);

            var hasRightVertical = HasVerticalCoverage(range.maxX, segY, keptY, verticalSegments);

            // Add connector at LEFT endpoint if no left vertical coverage
            if (
                !hasLeftVertical
                && !ObstacleMap.SegmentIntersectsAny(
                    range.minX,
                    Math.Min(segY, keptY),
                    range.minX,
                    Math.Max(segY, keptY),
                    hardObstacles
                )
            )
            {
                connectors.Add(
                    new WireSegment(
                        new GridPoint(range.minX, Math.Min(segY, keptY)),
                        new GridPoint(range.minX, Math.Max(segY, keptY)),
                        netName
                    )
                );
            }

            // Add connector at RIGHT endpoint if no right vertical coverage
            if (
                !hasRightVertical
                && !ObstacleMap.SegmentIntersectsAny(
                    range.maxX,
                    Math.Min(segY, keptY),
                    range.maxX,
                    Math.Max(segY, keptY),
                    hardObstacles
                )
            )
            {
                connectors.Add(
                    new WireSegment(
                        new GridPoint(range.maxX, Math.Min(segY, keptY)),
                        new GridPoint(range.maxX, Math.Max(segY, keptY)),
                        netName
                    )
                );
            }
        }

        return connectors;
    }

    private static bool WouldRemovingSegmentDisconnectTerminal(
        WireSegment segment,
        (int minX, int maxX) range,
        WireSegment kept,
        IReadOnlyList<WireSegment> verticalSegments,
        IReadOnlySet<GridPoint> terminalPoints,
        IReadOnlyList<Obstacle> hardObstacles
    )
    {
        var segY = segment.From.Y;
        var keptY = kept.From.Y;

        return EndpointWouldBeDisconnected(
                new GridPoint(range.minX, segY),
                range.minX,
                segY,
                keptY,
                verticalSegments,
                terminalPoints,
                hardObstacles
            )
            || EndpointWouldBeDisconnected(
                new GridPoint(range.maxX, segY),
                range.maxX,
                segY,
                keptY,
                verticalSegments,
                terminalPoints,
                hardObstacles
            );
    }

    private static bool EndpointWouldBeDisconnected(
        GridPoint endpoint,
        int x,
        int segY,
        int keptY,
        IReadOnlyList<WireSegment> verticalSegments,
        IReadOnlySet<GridPoint> terminalPoints,
        IReadOnlyList<Obstacle> hardObstacles
    )
    {
        if (!terminalPoints.Contains(endpoint))
        {
            return false;
        }

        if (HasVerticalCoverage(x, segY, keptY, verticalSegments))
        {
            return false;
        }

        return ObstacleMap.SegmentIntersectsAny(
            x,
            Math.Min(segY, keptY),
            x,
            Math.Max(segY, keptY),
            hardObstacles
        );
    }

    private static bool HasVerticalCoverage(
        int x,
        int firstY,
        int secondY,
        IReadOnlyList<WireSegment> verticalSegments
    )
    {
        return verticalSegments.Any(v =>
            v.From.X == x
            && Math.Min(v.From.Y, v.To.Y) <= Math.Min(firstY, secondY)
            && Math.Max(v.From.Y, v.To.Y) >= Math.Max(firstY, secondY)
        );
    }

    /// <summary>
    /// Removes wire segments that have become orphaned stubs (dead ends not connected
    /// to the rest of the network). A stub is a segment where one endpoint only
    /// connects to that single segment and is not a terminal. Also removes fully
    /// isolated segments where both endpoints have degree 1 and neither is a terminal.
    /// </summary>
    internal static List<WireSegment> RemoveOrphanedStubs(
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
                // Fully isolated segment: both endpoints are dead ends and neither is a terminal
                else if (
                    fromCount == 1
                    && toCount == 1
                    && !terminalPoints.Contains(seg.From)
                    && !terminalPoints.Contains(seg.To)
                )
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
}
