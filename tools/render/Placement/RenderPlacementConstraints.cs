namespace Cascode.Render.Placement;

using Cascode.Language;
using Cascode.Render.Layout;

public sealed record DevicePlacementConstraint(
    string DeviceId,
    int XRu,
    int YRu,
    RenderConstraintStrength Strength
);

public sealed class PlacementConstraintSet
{
    public IReadOnlyList<DevicePlacementConstraint> DevicePlacements { get; init; } =
        Array.Empty<DevicePlacementConstraint>();

    public bool AllowConstraintRelaxation { get; init; }
}

public sealed class RenderConstraintUnsatException : Exception
{
    public IReadOnlyList<string> Entities { get; }

    public RenderConstraintUnsatException(string message, IReadOnlyList<string> entities)
        : base(message)
    {
        Entities = entities;
    }
}

internal static class RenderCoordinateMapper
{
    internal static (int Row, int Col) MapRenderUnitsToCell(int xRu, int yRu)
    {
        var xPx = xRu * DeviceGeometry.RoutingPitch;
        var yPx = yRu * DeviceGeometry.RoutingPitch;

        var col = (int)
            Math.Round(
                (xPx - DeviceGeometry.CellWidth / 2.0) / DeviceGeometry.CellWidth,
                MidpointRounding.AwayFromZero
            );
        var row = (int)
            Math.Round(
                (yPx - DeviceGeometry.RailMargin - DeviceGeometry.CellHeight / 2.0)
                    / DeviceGeometry.CellHeight,
                MidpointRounding.AwayFromZero
            );

        return (row, col);
    }
}
