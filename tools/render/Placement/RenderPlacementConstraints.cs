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

    /// <summary>
    /// Initializes a new RenderConstraintUnsatException with a message and the entities involved in the unsatisfiable constraint.
    /// </summary>
    /// <param name="message">The error message describing the unsatisfied constraint.</param>
    /// <param name="entities">The identifiers of entities involved in the unsatisfiable constraint.</param>
    public RenderConstraintUnsatException(string message, IReadOnlyList<string> entities)
        : base(message)
    {
        Entities = entities;
    }
}

internal static class RenderCoordinateMapper
{
    /// <summary>
    /// Map horizontal and vertical render-unit coordinates into integer cell row and column indices.
    /// </summary>
    /// <param name="xRu">Horizontal coordinate in render units.</param>
    /// <param name="yRu">Vertical coordinate in render units.</param>
    /// <returns>
    /// A tuple containing the cell row and column: <c>Row</c> is the mapped cell row index, <c>Col</c> is the mapped cell column index.
    /// </returns>
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