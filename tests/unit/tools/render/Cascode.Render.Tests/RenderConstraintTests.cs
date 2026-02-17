namespace Cascode.Render.Tests;

using Cascode.Language;
using Cascode.Render.Analysis;
using Cascode.Render.Placement;
using Cascode.Render.Routing;

public sealed class RenderConstraintTests
{
    [Fact]
    public void Place_HardConstraint_PinsDeviceToTargetCell()
    {
        var circuit = TestCircuits.TwoDevices();
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);

        var constraints = new PlacementConstraintSet
        {
            DevicePlacements =
            [
                new DevicePlacementConstraint(
                    DeviceId: "M1",
                    XRu: 2,
                    YRu: 4,
                    Strength: RenderConstraintStrength.Hard
                ),
            ],
        };

        var placement = CoarseGridPlacer.Place(topology, graph, constraints);

        Assert.True(placement.DevicePlacements.TryGetValue("M1", out var m1));
        Assert.Equal(0, m1.Row);
        Assert.Equal(0, m1.Column);
    }

    [Fact]
    public void Place_ConflictingHardConstraints_ThrowsUnsat()
    {
        var circuit = TestCircuits.TwoDevices();
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);

        var constraints = new PlacementConstraintSet
        {
            DevicePlacements =
            [
                new DevicePlacementConstraint("M1", 2, 4, RenderConstraintStrength.Hard),
                new DevicePlacementConstraint("M2", 2, 4, RenderConstraintStrength.Hard),
            ],
            AllowConstraintRelaxation = false,
        };

        var ex = Assert.Throws<RenderConstraintUnsatException>(() =>
            CoarseGridPlacer.Place(topology, graph, constraints)
        );
        Assert.Contains("M1", ex.Entities);
        Assert.Contains("M2", ex.Entities);
    }

    [Fact]
    public void Route_WithWaypointConstraint_PassesThroughWaypoint()
    {
        var circuit = TestCircuits.SimpleCircuit();
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var placement = CoarseGridPlacer.Place(topology, graph);

        var waypoint = new GridPoint(40, 80);
        var routeConstraints = new RouteConstraintSet
        {
            NetRoutes = new Dictionary<string, NetRouteConstraint>(StringComparer.Ordinal)
            {
                ["IN"] = new NetRouteConstraint(
                    NetName: "IN",
                    Waypoints: [waypoint],
                    Strength: RenderConstraintStrength.Hard,
                    Mode: RenderRouteMode.Ortho
                ),
            },
        };

        var routing = MazeRouter.Route(placement, graph, routeConstraints);
        Assert.True(routing.SegmentsByNet.TryGetValue("IN", out var segments));
        Assert.True(segments.Count > 0);
        Assert.Contains(segments, segment => IsPointOnSegment(waypoint, segment));
    }

    private static bool IsPointOnSegment(GridPoint point, WireSegment segment)
    {
        if (segment.From.X == segment.To.X)
        {
            if (point.X != segment.From.X)
            {
                return false;
            }

            var minY = Math.Min(segment.From.Y, segment.To.Y);
            var maxY = Math.Max(segment.From.Y, segment.To.Y);
            return point.Y >= minY && point.Y <= maxY;
        }

        if (segment.From.Y == segment.To.Y)
        {
            if (point.Y != segment.From.Y)
            {
                return false;
            }

            var minX = Math.Min(segment.From.X, segment.To.X);
            var maxX = Math.Max(segment.From.X, segment.To.X);
            return point.X >= minX && point.X <= maxX;
        }

        return false;
    }
}
