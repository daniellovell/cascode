namespace Cascode.Render.Routing;

/// <summary>
/// Junction detection methods for MazeRouter.
/// </summary>
public static partial class MazeRouter
{
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
}
