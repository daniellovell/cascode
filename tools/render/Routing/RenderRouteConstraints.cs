namespace Cascode.Render.Routing;

using Cascode.Language;

public sealed record NetRouteConstraint(
    string NetName,
    IReadOnlyList<GridPoint> GuidePoints,
    RenderConstraintStrength Strength,
    RenderRouteMode Mode
);

public sealed class RouteConstraintSet
{
    public IReadOnlyDictionary<string, NetRouteConstraint> NetRoutes { get; init; } =
        new Dictionary<string, NetRouteConstraint>(StringComparer.Ordinal);

    public bool AllowConstraintRelaxation { get; init; }
}
