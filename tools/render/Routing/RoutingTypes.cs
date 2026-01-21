namespace Cascode.Render.Routing;

/// <summary>
/// A point on the routing grid.
/// </summary>
public readonly record struct GridPoint(int X, int Y);

/// <summary>
/// A wire segment connecting two points.
/// </summary>
public sealed record WireSegment(GridPoint From, GridPoint To, string NetName);

/// <summary>
/// Terminal position in grid coordinates.
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

    /// <summary>
    /// All terminal positions computed during routing (devices and ports).
    /// Used by renderer to place ports at exact positions matching wire endpoints.
    /// </summary>
    public required IReadOnlyList<TerminalPosition> TerminalPositions { get; init; }
}
