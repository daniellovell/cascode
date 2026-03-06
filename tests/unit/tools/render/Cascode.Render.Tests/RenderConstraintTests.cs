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
        Assert.Contains(segments, segment => IsPointOnSegment(waypoint, segment));
    }

    [Fact]
    public void Place_HardConstraintOnVerticalPassive_DoesNotConflictWithCenterConstraint()
    {
        // RC lowpass: R1 (horizontal passive, IN→OUT), C1 (vertical passive, OUT→GND).
        // C1 is a rail-connected vertical passive — not in a symmetric group and not
        // horizontal — so AddCenterDeviceConstraints forces it to col == symmetryAxis.
        // A hard placement at a different column must override the center constraint.
        var circuit = TestCircuits.RcLowpass();
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);

        // Render units (2, 4) → cell (row=0, col=0) via MapRenderUnitsToCell.
        // symmetryAxis = 1 for the default 3-column grid, so col=0 ≠ symmetryAxis.
        var constraints = new PlacementConstraintSet
        {
            DevicePlacements =
            [
                new DevicePlacementConstraint(
                    DeviceId: "C1",
                    XRu: 2,
                    YRu: 4,
                    Strength: RenderConstraintStrength.Hard
                ),
            ],
            AllowConstraintRelaxation = false,
        };

        // This must not throw RenderConstraintUnsatException.
        var placement = CoarseGridPlacer.Place(topology, graph, constraints);

        Assert.True(placement.DevicePlacements.TryGetValue("C1", out var c1));
        Assert.Equal(0, c1.Column);
    }

    [Fact]
    public void Analyze_InductorUsesSamePassiveOrientationRulesAsResistorAndCapacitor()
    {
        var circuit = new Circuit
        {
            Name = "lc_orientation",
            Level = CascodeLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "IN",
                    Type = "signal",
                },
                new()
                {
                    Direction = PortDirection.Output,
                    Name = "OUT",
                    Type = "signal",
                },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        Id = "L1",
                        DeviceType = "inductor",
                        Primitive = "InductorIdeal",
                        Bindings = new Dictionary<string, string> { ["P"] = "IN", ["N"] = "OUT" },
                    },
                    new()
                    {
                        Id = "C1",
                        DeviceType = "capacitor",
                        Primitive = "CapacitorIdeal",
                        Bindings = new Dictionary<string, string> { ["P"] = "OUT", ["N"] = "GND" },
                    },
                },
            },
        };

        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);

        Assert.True(topology.PassiveOrientations.TryGetValue("L1", out var l1Orientation));
        Assert.Equal(PassiveOrientation.Horizontal, l1Orientation);
        Assert.True(topology.PassiveOrientations.TryGetValue("C1", out var c1Orientation));
        Assert.Equal(PassiveOrientation.Vertical, c1Orientation);
    }

    [Fact]
    public void Place_NoInterveningTreatsMosAsThreeByThreeFootprint()
    {
        var circuit = new Circuit
        {
            Name = "no_intervening_mos_footprint",
            Level = CascodeLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>(),
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        Id = "RCAS_TOP",
                        DeviceType = "resistor",
                        Primitive = "ResistorIdeal",
                        Bindings = new Dictionary<string, string> { ["P"] = "VDD", ["N"] = "vcas" },
                    },
                    new()
                    {
                        Id = "RCAS_BOT",
                        DeviceType = "resistor",
                        Primitive = "ResistorIdeal",
                        Bindings = new Dictionary<string, string> { ["P"] = "vcas", ["N"] = "GND" },
                    },
                    new()
                    {
                        Id = "M2",
                        DeviceType = "nmos",
                        Primitive = "NMOS_Level1",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "nd",
                            ["G"] = "vbias",
                            ["S"] = "GND",
                        },
                    },
                },
            },
        };

        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var constraints = new PlacementConstraintSet
        {
            DevicePlacements =
            [
                new DevicePlacementConstraint("RCAS_TOP", 7, 4, RenderConstraintStrength.Hard),
                new DevicePlacementConstraint("RCAS_BOT", 7, 24, RenderConstraintStrength.Hard),
                new DevicePlacementConstraint("M2", 12, 14, RenderConstraintStrength.Hard),
            ],
            AllowConstraintRelaxation = false,
        };

        Assert.Throws<RenderConstraintUnsatException>(() =>
            CoarseGridPlacer.Place(topology, graph, constraints)
        );
    }

    [Fact]
    public void Place_PostPlacementCompaction_RemovesEmptyRowsAndColumns()
    {
        var circuit = TestCircuits.TwoDevices();
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);

        // (2,4) -> cell (0,0), (11,14) -> cell (2,2)
        // Hard constraints intentionally create an empty row and column between devices.
        var constraints = new PlacementConstraintSet
        {
            DevicePlacements =
            [
                new DevicePlacementConstraint("M1", 2, 4, RenderConstraintStrength.Hard),
                new DevicePlacementConstraint("M2", 11, 14, RenderConstraintStrength.Hard),
            ],
            AllowConstraintRelaxation = false,
        };

        var placement = CoarseGridPlacer.Place(topology, graph, constraints);

        Assert.Equal(2, placement.RowCount);
        Assert.Equal(2, placement.ColumnCount);
        Assert.True(placement.DevicePlacements.TryGetValue("M1", out var m1));
        Assert.True(placement.DevicePlacements.TryGetValue("M2", out var m2));
        Assert.Equal((0, 0), (m1.Row, m1.Column));
        Assert.Equal((1, 1), (m2.Row, m2.Column));
    }

    [Fact]
    public void Place_RailConnectedPassives_FaceConnectedRail()
    {
        var circuit = new Circuit
        {
            Name = "rail_connected_passives_vertical",
            Level = CascodeLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>(),
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        Id = "R_LOAD",
                        DeviceType = "resistor",
                        Primitive = "ResistorIdeal",
                        Bindings = new Dictionary<string, string> { ["P"] = "VDD", ["N"] = "nint" },
                    },
                    new()
                    {
                        Id = "C_SHUNT",
                        DeviceType = "capacitor",
                        Primitive = "CapacitorIdeal",
                        Bindings = new Dictionary<string, string> { ["P"] = "nint", ["N"] = "GND" },
                    },
                },
                Nets = new List<NetDeclaration>
                {
                    new() { Id = "nint", Domain = "signal" },
                },
            },
        };

        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var placement = CoarseGridPlacer.Place(topology, graph);
        var terminalsByNet = MazeRouter.GetTerminalsByNet(placement, graph);

        var allTerminals = terminalsByNet.SelectMany(kv => kv.Value).ToList();
        var rP = allTerminals.Single(t => t.DeviceId == "R_LOAD" && t.Terminal == "P");
        var rN = allTerminals.Single(t => t.DeviceId == "R_LOAD" && t.Terminal == "N");
        var cP = allTerminals.Single(t => t.DeviceId == "C_SHUNT" && t.Terminal == "P");
        var cN = allTerminals.Single(t => t.DeviceId == "C_SHUNT" && t.Terminal == "N");

        Assert.Equal(rP.X, rN.X);
        Assert.NotEqual(rP.Y, rN.Y);
        Assert.True(rP.Y < rN.Y, "Expected VDD-connected terminal R_LOAD.P to face north.");
        Assert.Equal(cP.X, cN.X);
        Assert.NotEqual(cP.Y, cN.Y);
        Assert.True(cN.Y > cP.Y, "Expected GND-connected terminal C_SHUNT.N to face south.");
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
