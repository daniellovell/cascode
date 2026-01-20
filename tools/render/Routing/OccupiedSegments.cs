namespace Cascode.Render.Routing;

/// <summary>
/// Interface for checking occupied segments during routing.
/// </summary>
public interface IOccupiedSegments
{
    /// <summary>
    /// Adds a routed segment to the occupied set.
    /// </summary>
    void Add(WireSegment seg);

    /// <summary>
    /// Checks if a proposed segment would illegally overlap with existing segments.
    /// Returns true if the segment would run coincident with a segment from a different net.
    /// </summary>
    bool WouldOverlap(int x1, int y1, int x2, int y2, string netName);
}

/// <summary>
/// Tracks already-routed wire segments to prevent overlap.
/// Two nets may cross at a single point but never run coincident.
/// </summary>
public sealed class OccupiedSegments : IOccupiedSegments
{
    private readonly List<(int X1, int Y1, int X2, int Y2, string Net)> _segments = new();

    /// <summary>
    /// Gets the count of segments in the occupied set (for testing only).
    /// </summary>
    internal int Count => _segments.Count;

    /// <summary>
    /// Checks if a specific segment is in the occupied set (for testing only).
    /// </summary>
    /// <param name="seg">The segment to check for.</param>
    /// <returns>True if the segment is in the occupied set; otherwise, false.</returns>
    internal bool Contains(WireSegment seg)
    {
        return _segments.Any(s =>
            s.X1 == seg.From.X
            && s.Y1 == seg.From.Y
            && s.X2 == seg.To.X
            && s.Y2 == seg.To.Y
            && s.Net == seg.NetName
        );
    }

    /// <summary>
    /// Adds a routed segment to the occupied set.
    /// </summary>
    public void Add(WireSegment seg)
    {
        _segments.Add((seg.From.X, seg.From.Y, seg.To.X, seg.To.Y, seg.NetName));
    }

    /// <summary>
    /// Checks if a proposed segment would illegally overlap with existing segments.
    /// Returns true if the segment would run coincident with a segment from a different net.
    /// Single-point crossings are allowed. Same-net overlap is allowed.
    /// </summary>
    public bool WouldOverlap(int x1, int y1, int x2, int y2, string netName)
    {
        foreach (var (ox1, oy1, ox2, oy2, oNet) in _segments)
        {
            if (oNet == netName)
            {
                continue; // Same net, overlap is fine
            }

            if (SegmentsCoincide(x1, y1, x2, y2, ox1, oy1, ox2, oy2))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Checks if two segments run coincident (share more than a single point).
    /// </summary>
    internal static bool SegmentsCoincide(
        int ax1,
        int ay1,
        int ax2,
        int ay2,
        int bx1,
        int by1,
        int bx2,
        int by2
    )
    {
        var aHorizontal = ay1 == ay2;
        var bHorizontal = by1 == by2;

        // Both horizontal
        if (aHorizontal && bHorizontal)
        {
            if (ay1 != by1)
            {
                return false; // Different Y, can't coincide
            }

            var aMinX = Math.Min(ax1, ax2);
            var aMaxX = Math.Max(ax1, ax2);
            var bMinX = Math.Min(bx1, bx2);
            var bMaxX = Math.Max(bx1, bx2);

            // Overlap length > 0 means coincidence (not just touching at endpoint)
            var overlapStart = Math.Max(aMinX, bMinX);
            var overlapEnd = Math.Min(aMaxX, bMaxX);
            return overlapEnd > overlapStart;
        }

        // Both vertical
        if (!aHorizontal && !bHorizontal)
        {
            if (ax1 != bx1)
            {
                return false; // Different X, can't coincide
            }

            var aMinY = Math.Min(ay1, ay2);
            var aMaxY = Math.Max(ay1, ay2);
            var bMinY = Math.Min(by1, by2);
            var bMaxY = Math.Max(by1, by2);

            var overlapStart = Math.Max(aMinY, bMinY);
            var overlapEnd = Math.Min(aMaxY, bMaxY);
            return overlapEnd > overlapStart;
        }

        // One horizontal, one vertical: can only cross at a point, never coincide
        return false;
    }
}

/// <summary>
/// An overlay on top of a base occupied segments map.
/// Segments added to the overlay are only tracked locally and not added to the base.
/// Used during intra-net routing to avoid MST edges overlapping each other
/// without polluting the shared occupied map with segments that may be pruned later.
/// </summary>
internal sealed class OverlayOccupiedSegments : IOccupiedSegments
{
    private readonly IOccupiedSegments _base;
    private readonly List<(int X1, int Y1, int X2, int Y2, string Net)> _local = new();

    /// <summary>
    /// Creates an overlay on top of the specified base occupied segments.
    /// </summary>
    /// <param name="baseOccupied">The base occupied segments to layer on top of.</param>
    public OverlayOccupiedSegments(IOccupiedSegments baseOccupied)
    {
        _base = baseOccupied;
    }

    /// <summary>
    /// Adds a segment to the local overlay only (not to the base).
    /// </summary>
    public void Add(WireSegment seg)
    {
        _local.Add((seg.From.X, seg.From.Y, seg.To.X, seg.To.Y, seg.NetName));
    }

    /// <summary>
    /// Checks if a proposed segment would illegally overlap with existing segments
    /// in either the base or the local overlay.
    /// </summary>
    public bool WouldOverlap(int x1, int y1, int x2, int y2, string netName)
    {
        // Check base first
        if (_base.WouldOverlap(x1, y1, x2, y2, netName))
        {
            return true;
        }

        // Check local overlay
        foreach (var (ox1, oy1, ox2, oy2, oNet) in _local)
        {
            if (oNet == netName)
            {
                continue; // Same net, overlap is fine
            }

            if (OccupiedSegments.SegmentsCoincide(x1, y1, x2, y2, ox1, oy1, ox2, oy2))
            {
                return true;
            }
        }

        return false;
    }
}
