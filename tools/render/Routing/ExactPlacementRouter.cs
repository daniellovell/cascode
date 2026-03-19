namespace Cascode.Render.Routing;

using Cascode.Render.Analysis;
using Cascode.Render.Layout;

/// <summary>
/// Routes nets against exact manual placements without invoking the coarse placer.
/// </summary>
public static class ExactPlacementRouter
{
    public static RoutingResult Route(
        CircuitGraph graph,
        ExactPlacementContext placement,
        RouteConstraintSet? constraints = null
    )
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(placement);

        return MazeRouter
            .RouteWithResolvedTerminals(
                graph,
                placement.TerminalPositions,
                placement.Obstacles,
                placement.CanvasWidth,
                placement.CanvasHeight,
                constraints
            )
            .Result;
    }
}
