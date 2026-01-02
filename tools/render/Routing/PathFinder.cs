namespace Cascode.Render.Routing;

using Cascode.Render.Layout;

/// <summary>
/// Finds Manhattan paths between two points, avoiding obstacles and overlaps.
/// </summary>
public static class PathFinder
{
    private static readonly int Pitch = DeviceGeometry.RoutingPitch;

    /// <summary>
    /// Finds a path from 'from' to 'to', avoiding obstacles, overlapping segments, and forbidden points.
    /// Returns list of wire segments forming the path.
    /// </summary>
    public static List<WireSegment> FindPath(
        GridPoint from,
        GridPoint to,
        string netName,
        IReadOnlyList<Obstacle> obstacles,
        OccupiedSegments occupied,
        IReadOnlySet<GridPoint>? forbiddenPoints = null
    )
    {
        forbiddenPoints ??= new HashSet<GridPoint>();

        // Same point - no path needed
        if (from.X == to.X && from.Y == to.Y)
        {
            return new List<WireSegment>();
        }

        // Try direct L-paths first (most common case)
        var path = TryLPath(
            from,
            to,
            netName,
            obstacles,
            occupied,
            forbiddenPoints,
            horizontalFirst: true
        );
        if (path != null)
        {
            return path;
        }

        path = TryLPath(
            from,
            to,
            netName,
            obstacles,
            occupied,
            forbiddenPoints,
            horizontalFirst: false
        );
        if (path != null)
        {
            return path;
        }

        // Try jogging by small offsets to avoid conflicts
        path = TryJogPath(from, to, netName, obstacles, occupied, forbiddenPoints);
        if (path != null)
        {
            return path;
        }

        // Fallback: try both L-path orientations, prefer one without forbidden point violations
        var fallback1 = CreateLPath(from, to, netName, horizontalFirst: true);
        if (!PathViolatesForbiddenPoints(fallback1, forbiddenPoints))
        {
            return fallback1;
        }

        var fallback2 = CreateLPath(from, to, netName, horizontalFirst: false);
        if (!PathViolatesForbiddenPoints(fallback2, forbiddenPoints))
        {
            return fallback2;
        }

        // Last resort: return a path even if it violates (better to have a visible wire)
        return fallback1;
    }

    /// <summary>
    /// Tries to create an L-shaped path (two segments with one corner).
    /// </summary>
    private static List<WireSegment>? TryLPath(
        GridPoint from,
        GridPoint to,
        string netName,
        IReadOnlyList<Obstacle> obstacles,
        OccupiedSegments occupied,
        IReadOnlySet<GridPoint> forbiddenPoints,
        bool horizontalFirst
    )
    {
        // Straight line case
        if (from.X == to.X || from.Y == to.Y)
        {
            if (
                IsSegmentValid(
                    from.X,
                    from.Y,
                    to.X,
                    to.Y,
                    netName,
                    obstacles,
                    occupied,
                    forbiddenPoints
                )
            )
            {
                return new List<WireSegment> { new WireSegment(from, to, netName) };
            }
            return null;
        }

        // L-shaped path
        var corner = horizontalFirst ? new GridPoint(to.X, from.Y) : new GridPoint(from.X, to.Y);

        // Corner must not be a forbidden point (other net's terminal)
        if (forbiddenPoints.Contains(corner))
        {
            return null;
        }

        var seg1Valid = IsSegmentValid(
            from.X,
            from.Y,
            corner.X,
            corner.Y,
            netName,
            obstacles,
            occupied,
            forbiddenPoints
        );
        var seg2Valid = IsSegmentValid(
            corner.X,
            corner.Y,
            to.X,
            to.Y,
            netName,
            obstacles,
            occupied,
            forbiddenPoints
        );

        if (seg1Valid && seg2Valid)
        {
            return new List<WireSegment>
            {
                new WireSegment(from, corner, netName),
                new WireSegment(corner, to, netName),
            };
        }

        return null;
    }

    /// <summary>
    /// Tries paths with small jogs to avoid obstacles/overlaps.
    /// </summary>
    private static List<WireSegment>? TryJogPath(
        GridPoint from,
        GridPoint to,
        string netName,
        IReadOnlyList<Obstacle> obstacles,
        OccupiedSegments occupied,
        IReadOnlySet<GridPoint> forbiddenPoints
    )
    {
        var offsets = new[] { Pitch, -Pitch, 2 * Pitch, -2 * Pitch };

        // Try horizontal jog (route via intermediate Y)
        foreach (var dy in offsets)
        {
            var midY = from.Y + dy;
            var path = TryThreeSegmentPath(
                from,
                to,
                netName,
                obstacles,
                occupied,
                forbiddenPoints,
                jogHorizontal: false,
                jogCoord: midY
            );
            if (path != null)
            {
                return path;
            }
        }

        // Try vertical jog (route via intermediate X from source)
        foreach (var dx in offsets)
        {
            var midX = from.X + dx;
            var path = TryThreeSegmentPath(
                from,
                to,
                netName,
                obstacles,
                occupied,
                forbiddenPoints,
                jogHorizontal: true,
                jogCoord: midX
            );
            if (path != null)
            {
                return path;
            }
        }

        // Try jogs from destination side (helps when conflict is near destination)
        foreach (var dy in offsets)
        {
            var midY = to.Y + dy;
            var path = TryThreeSegmentPath(
                from,
                to,
                netName,
                obstacles,
                occupied,
                forbiddenPoints,
                jogHorizontal: false,
                jogCoord: midY
            );
            if (path != null)
            {
                return path;
            }
        }

        foreach (var dx in offsets)
        {
            var midX = to.X + dx;
            var path = TryThreeSegmentPath(
                from,
                to,
                netName,
                obstacles,
                occupied,
                forbiddenPoints,
                jogHorizontal: true,
                jogCoord: midX
            );
            if (path != null)
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// Tries a three-segment path with a jog.
    /// </summary>
    private static List<WireSegment>? TryThreeSegmentPath(
        GridPoint from,
        GridPoint to,
        string netName,
        IReadOnlyList<Obstacle> obstacles,
        OccupiedSegments occupied,
        IReadOnlySet<GridPoint> forbiddenPoints,
        bool jogHorizontal,
        int jogCoord
    )
    {
        GridPoint mid1,
            mid2;

        if (jogHorizontal)
        {
            // Jog via X = jogCoord
            mid1 = new GridPoint(jogCoord, from.Y);
            mid2 = new GridPoint(jogCoord, to.Y);
        }
        else
        {
            // Jog via Y = jogCoord
            mid1 = new GridPoint(from.X, jogCoord);
            mid2 = new GridPoint(to.X, jogCoord);
        }

        // Intermediate points must not be forbidden points
        if (forbiddenPoints.Contains(mid1) || forbiddenPoints.Contains(mid2))
        {
            return null;
        }

        var seg1Valid = IsSegmentValid(
            from.X,
            from.Y,
            mid1.X,
            mid1.Y,
            netName,
            obstacles,
            occupied,
            forbiddenPoints
        );
        var seg2Valid = IsSegmentValid(
            mid1.X,
            mid1.Y,
            mid2.X,
            mid2.Y,
            netName,
            obstacles,
            occupied,
            forbiddenPoints
        );
        var seg3Valid = IsSegmentValid(
            mid2.X,
            mid2.Y,
            to.X,
            to.Y,
            netName,
            obstacles,
            occupied,
            forbiddenPoints
        );

        if (seg1Valid && seg2Valid && seg3Valid)
        {
            var segments = new List<WireSegment>();

            if (!mid1.Equals(from))
            {
                segments.Add(new WireSegment(from, mid1, netName));
            }
            if (!mid2.Equals(mid1))
            {
                segments.Add(new WireSegment(mid1, mid2, netName));
            }
            if (!to.Equals(mid2))
            {
                segments.Add(new WireSegment(mid2, to, netName));
            }

            return segments.Count > 0 ? segments : null;
        }

        return null;
    }

    /// <summary>
    /// Creates an L-path without validation (fallback).
    /// </summary>
    private static List<WireSegment> CreateLPath(
        GridPoint from,
        GridPoint to,
        string netName,
        bool horizontalFirst
    )
    {
        if (from.X == to.X || from.Y == to.Y)
        {
            return new List<WireSegment> { new WireSegment(from, to, netName) };
        }

        var corner = horizontalFirst ? new GridPoint(to.X, from.Y) : new GridPoint(from.X, to.Y);

        return new List<WireSegment>
        {
            new WireSegment(from, corner, netName),
            new WireSegment(corner, to, netName),
        };
    }

    /// <summary>
    /// Checks if a segment is valid (no obstacle intersection, no overlap, no forbidden points).
    /// </summary>
    private static bool IsSegmentValid(
        int x1,
        int y1,
        int x2,
        int y2,
        string netName,
        IReadOnlyList<Obstacle> obstacles,
        OccupiedSegments occupied,
        IReadOnlySet<GridPoint> forbiddenPoints
    )
    {
        if (ObstacleMap.SegmentIntersectsAny(x1, y1, x2, y2, obstacles))
        {
            return false;
        }

        if (occupied.WouldOverlap(x1, y1, x2, y2, netName))
        {
            return false;
        }

        // Check if segment passes through any forbidden point (other net terminals)
        if (SegmentPassesThroughForbiddenPoint(x1, y1, x2, y2, forbiddenPoints))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Checks if a segment passes through any forbidden point (excluding endpoints).
    /// </summary>
    private static bool SegmentPassesThroughForbiddenPoint(
        int x1,
        int y1,
        int x2,
        int y2,
        IReadOnlySet<GridPoint> forbiddenPoints
    )
    {
        foreach (var fp in forbiddenPoints)
        {
            // Skip if forbidden point is at segment endpoint (that's OK)
            if ((fp.X == x1 && fp.Y == y1) || (fp.X == x2 && fp.Y == y2))
            {
                continue;
            }

            // Check if point is on horizontal segment
            if (y1 == y2 && fp.Y == y1)
            {
                var minX = Math.Min(x1, x2);
                var maxX = Math.Max(x1, x2);
                if (fp.X > minX && fp.X < maxX)
                {
                    return true;
                }
            }

            // Check if point is on vertical segment
            if (x1 == x2 && fp.X == x1)
            {
                var minY = Math.Min(y1, y2);
                var maxY = Math.Max(y1, y2);
                if (fp.Y > minY && fp.Y < maxY)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if any segment in the path passes through a forbidden point.
    /// </summary>
    private static bool PathViolatesForbiddenPoints(
        List<WireSegment> path,
        IReadOnlySet<GridPoint> forbiddenPoints
    )
    {
        foreach (var seg in path)
        {
            if (
                SegmentPassesThroughForbiddenPoint(
                    seg.From.X,
                    seg.From.Y,
                    seg.To.X,
                    seg.To.Y,
                    forbiddenPoints
                )
            )
            {
                return true;
            }
        }

        // Also check intermediate corner points (not path endpoints)
        for (var i = 0; i < path.Count - 1; i++)
        {
            var corner = path[i].To;
            if (forbiddenPoints.Contains(corner))
            {
                return true;
            }
        }

        return false;
    }
}
