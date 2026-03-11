namespace Cascode.Render.Tests;

using Cascode.Language;
using Cascode.Render.Analysis;
using Cascode.Render.Layout;
using Cascode.Render.Placement;
using Cascode.Render.Routing;
using Cascode.Render.Svg;

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
        // C1 is a rail-connected vertical passive, but it is neither horizontal nor part of a
        // symmetric group, so no placement rule should force it onto the symmetry axis.
        // A hard placement away from that axis must still remain satisfiable.
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
        var circuit = TestCircuits.TwoIndependentDevices();
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);

        // (2,4) -> cell (0,0), (11,14) -> cell (2,2)
        // Hard constraints intentionally create an empty row and column between devices.
        var constraints = new PlacementConstraintSet
        {
            DevicePlacements =
            [
                new DevicePlacementConstraint("M_IN", 2, 4, RenderConstraintStrength.Hard),
                new DevicePlacementConstraint("R_LOAD", 11, 14, RenderConstraintStrength.Hard),
            ],
            AllowConstraintRelaxation = false,
        };

        var placement = CoarseGridPlacer.Place(topology, graph, constraints);

        Assert.Equal(2, placement.RowCount);
        Assert.Equal(2, placement.ColumnCount);
        Assert.True(placement.DevicePlacements.TryGetValue("M_IN", out var m1));
        Assert.True(placement.DevicePlacements.TryGetValue("R_LOAD", out var m2));
        Assert.Equal((0, 0), (m1.Row, m1.Column));
        Assert.Equal((1, 1), (m2.Row, m2.Column));
    }

    [Fact]
    public void Place_CurrentMirrorHardConstraint_RejectsDifferentRows()
    {
        var circuit = TestCircuits.CurrentMirrorPair();
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var constraints = new PlacementConstraintSet
        {
            DevicePlacements =
            [
                new DevicePlacementConstraint("M_REF", 2, 4, RenderConstraintStrength.Hard),
                new DevicePlacementConstraint("M_OUT", 7, 14, RenderConstraintStrength.Hard),
            ],
            AllowConstraintRelaxation = false,
        };

        var ex = Assert.Throws<RenderConstraintUnsatException>(() =>
            CoarseGridPlacer.Place(topology, graph, constraints)
        );
        Assert.Contains("M_REF", ex.Entities);
        Assert.Contains("M_OUT", ex.Entities);
    }

    [Fact]
    public void Place_CurrentMirrorDetection_RequiresSharedSourceNet()
    {
        var circuit = TestCircuits.SharedGateDifferentSourcePair();
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var constraints = new PlacementConstraintSet
        {
            DevicePlacements =
            [
                new DevicePlacementConstraint("M_REF", 2, 4, RenderConstraintStrength.Hard),
                new DevicePlacementConstraint("M_OUT", 7, 14, RenderConstraintStrength.Hard),
            ],
            AllowConstraintRelaxation = false,
        };

        Assert.DoesNotContain(
            topology.SymmetricGroups,
            group => group.Type == SymmetryType.CurrentMirror
        );
        var placement = CoarseGridPlacer.Place(topology, graph, constraints);
        Assert.True(placement.DevicePlacements.TryGetValue("M_REF", out var mRef));
        Assert.True(placement.DevicePlacements.TryGetValue("M_OUT", out var mOut));
        Assert.NotEqual(mRef.Row, mOut.Row);
    }

    [Fact]
    public void Place_DrainSourceConnectedPair_PrefersVerticalAlignment()
    {
        var circuit = TestCircuits.DrainSourceConnectedPair();
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);

        var placement = CoarseGridPlacer.Place(topology, graph);

        Assert.True(placement.DevicePlacements.TryGetValue("M_TOP", out var mTop));
        Assert.True(placement.DevicePlacements.TryGetValue("M_BOT", out var mBot));
        Assert.Equal(mTop.Column, mBot.Column);
    }

    [Fact]
    public void Place_SharingDrainNet_PrefersVerticalAlignment()
    {
        var circuit = TestCircuits.DrainDrainConnectedPair();
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);

        var placement = CoarseGridPlacer.Place(topology, graph);

        Assert.True(placement.DevicePlacements.TryGetValue("M_LEFT", out var mLeft));
        Assert.True(placement.DevicePlacements.TryGetValue("M_RIGHT", out var mRight));
        Assert.Equal(mLeft.Column, mRight.Column);
    }

    [Fact]
    public void Place_SharingSourceNet_PrefersVerticalAlignment()
    {
        var circuit = TestCircuits.SourceSourceConnectedPair();
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);

        var placement = CoarseGridPlacer.Place(topology, graph);

        Assert.True(placement.DevicePlacements.TryGetValue("M_LEFT", out var mLeft));
        Assert.True(placement.DevicePlacements.TryGetValue("M_RIGHT", out var mRight));
        Assert.Equal(mLeft.Column, mRight.Column);
    }

    [Fact]
    public void Place_SharingGateSignal_PrefersHorizontalAlignmentWithoutDrainSourceConnection()
    {
        var circuit = TestCircuits.SharedGatePair();
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);

        var placement = CoarseGridPlacer.Place(topology, graph);

        Assert.True(placement.DevicePlacements.TryGetValue("M_LEFT", out var mLeft));
        Assert.True(placement.DevicePlacements.TryGetValue("M_RIGHT", out var mRight));
        Assert.Equal(mLeft.Row, mRight.Row);
    }

    [Fact]
    public void Place_DrainSourceConnection_TakesPriorityOverSharedGateSignal()
    {
        var circuit = TestCircuits.DrainSourceConnectionOverridesSharedGatePair();
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);

        var placement = CoarseGridPlacer.Place(topology, graph);

        Assert.True(placement.DevicePlacements.TryGetValue("M_TOP", out var mTop));
        Assert.True(placement.DevicePlacements.TryGetValue("M_BOT", out var mBot));
        Assert.Equal(mTop.Column, mBot.Column);
    }

    [Fact]
    public void Place_DiffPairHardConstraint_RejectsDifferentRows()
    {
        var circuit = TestCircuits.TwoDevices();
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var constraints = new PlacementConstraintSet
        {
            DevicePlacements =
            [
                new DevicePlacementConstraint("M1", 2, 4, RenderConstraintStrength.Hard),
                new DevicePlacementConstraint("M2", 7, 9, RenderConstraintStrength.Hard),
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
    public void Place_DiffPairHardConstraint_MakesGatesFaceOppositeDirections()
    {
        var circuit = TestCircuits.TwoDevices();
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var constraints = new PlacementConstraintSet
        {
            DevicePlacements =
            [
                new DevicePlacementConstraint("M1", 2, 4, RenderConstraintStrength.Hard),
                new DevicePlacementConstraint("M2", 7, 4, RenderConstraintStrength.Hard),
            ],
            AllowConstraintRelaxation = false,
        };

        var placement = CoarseGridPlacer.Place(topology, graph, constraints);

        Assert.True(placement.DevicePlacements.TryGetValue("M1", out var m1));
        Assert.True(placement.DevicePlacements.TryGetValue("M2", out var m2));
        Assert.Equal(m1.Row, m2.Row);

        var left = m1.Column <= m2.Column ? m1 : m2;
        var right = left == m1 ? m2 : m1;
        Assert.False(left.MirrorX, "Expected left diff-pair gate to face west.");
        Assert.True(right.MirrorX, "Expected right diff-pair gate to face east.");
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

    [Fact]
    public void Place_StressLnaTwoStage_CintTerminalDoesNotLieOnM1M2Connection()
    {
        var fullPath = Path.Combine(
            GetRepoRoot(),
            "tests/golden/cas/stress/LNA_CSCascodeInductivelyDegenerated_TwoStage_Sky130.cas"
        );
        using var reader = File.OpenText(fullPath);
        var readResult = CascodeReader.TryRead(reader, fullPath);
        Assert.True(readResult.Success, "Failed to parse Cascode file");
        var doc = readResult.Document!;
        var elCircuit = doc.Circuits.First(c => c.Level == CascodeLevel.EL);

        var graph = CircuitGraph.Build(elCircuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var placement = CoarseGridPlacer.Place(topology, graph);
        var terminalsByNet = MazeRouter.GetTerminalsByNet(placement, graph);
        var allTerminals = terminalsByNet.SelectMany(kv => kv.Value).ToList();
        var m1Drain = allTerminals.Single(t => t.DeviceId == "M1" && t.Terminal == "D");
        var m2Source = allTerminals.Single(t => t.DeviceId == "M2" && t.Terminal == "S");
        var cintP = allTerminals.Single(t => t.DeviceId == "CINT" && t.Terminal == "P");
        var cintN = allTerminals.Single(t => t.DeviceId == "CINT" && t.Terminal == "N");

        Assert.False(IsPointOnOrthogonalSegment(cintP, m1Drain, m2Source));
        Assert.False(IsPointOnOrthogonalSegment(cintN, m1Drain, m2Source));
    }

    [Fact]
    public void Place_StressLnaTwoStage_KeepsLgHorizontalOnNmatchFanout()
    {
        var fullPath = Path.Combine(
            GetRepoRoot(),
            "tests/golden/cas/stress/LNA_CSCascodeInductivelyDegenerated_TwoStage_Sky130.cas"
        );
        using var reader = File.OpenText(fullPath);
        var readResult = CascodeReader.TryRead(reader, fullPath);
        Assert.True(readResult.Success, "Failed to parse Cascode file");
        var doc = readResult.Document!;
        var elCircuit = doc.Circuits.First(c => c.Level == CascodeLevel.EL);

        var graph = CircuitGraph.Build(elCircuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var placement = CoarseGridPlacer.Place(topology, graph);
        Assert.True(graph.Devices.TryGetValue("LG", out var lgDevice));
        Assert.True(
            DevicePlacementHelper.TryGetDevicePlacement(placement, "LG", lgDevice, out var lgInfo)
        );
        Assert.Equal(DeviceOrientation.Horizontal, lgInfo.Orientation);

        Assert.True(graph.Devices.TryGetValue("CM", out var cmDevice));
        Assert.True(
            DevicePlacementHelper.TryGetDevicePlacement(placement, "CM", cmDevice, out var cmInfo)
        );
        Assert.NotEqual(lgInfo.X, cmInfo.X);
    }

    [Fact]
    public void Place_OffNetTerminalOnDrainSourceBackbone_ThrowsUnsat()
    {
        var circuit = new Circuit
        {
            Name = "off_net_terminal_on_backbone",
            Level = CascodeLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "VG_TOP",
                    Type = "bias",
                },
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "VG_BOT",
                    Type = "bias",
                },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        Id = "M_TOP",
                        DeviceType = "nmos",
                        Primitive = "NMOS_Level1",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "OUT",
                            ["G"] = "VG_TOP",
                            ["S"] = "cas",
                        },
                    },
                    new()
                    {
                        Id = "M_BOT",
                        DeviceType = "nmos",
                        Primitive = "NMOS_Level1",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "cas",
                            ["G"] = "VG_BOT",
                            ["S"] = "GND",
                        },
                    },
                    new()
                    {
                        Id = "C_BLOCK",
                        DeviceType = "capacitor",
                        Primitive = "CapacitorIdeal",
                        Bindings = new Dictionary<string, string> { ["P"] = "tap", ["N"] = "GND" },
                    },
                },
                Nets = new List<NetDeclaration>
                {
                    new() { Id = "cas", Domain = "signal" },
                    new() { Id = "tap", Domain = "signal" },
                    new() { Id = "OUT", Domain = "signal" },
                },
            },
        };

        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var constraints = new PlacementConstraintSet
        {
            DevicePlacements =
            [
                new DevicePlacementConstraint("M_TOP", 2, 4, RenderConstraintStrength.Hard),
                new DevicePlacementConstraint("C_BLOCK", 2, 14, RenderConstraintStrength.Hard),
                new DevicePlacementConstraint("M_BOT", 2, 24, RenderConstraintStrength.Hard),
            ],
            AllowConstraintRelaxation = false,
        };

        Assert.Throws<RenderConstraintUnsatException>(() =>
            CoarseGridPlacer.Place(topology, graph, constraints)
        );
    }

    [Theory]
    [InlineData("nmos")]
    [InlineData("pmos")]
    public void Place_SameFlavorDrainSourceChain_PrioritizesGateFacingSignalSource(
        string deviceFlavor
    )
    {
        var circuit =
            deviceFlavor == "pmos"
                ? TestCircuits.SameFlavorPmosDrainSourceChainWithCompetingGateSides()
                : TestCircuits.SameFlavorDrainSourceChainWithCompetingGateSides();
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var constraints = new PlacementConstraintSet
        {
            DevicePlacements =
            [
                new DevicePlacementConstraint("M_TOP", 7, 4, RenderConstraintStrength.Hard),
                new DevicePlacementConstraint("M_BOT", 7, 9, RenderConstraintStrength.Hard),
                new DevicePlacementConstraint("R_LEFT", 2, 9, RenderConstraintStrength.Hard),
                new DevicePlacementConstraint("R_RIGHT", 12, 4, RenderConstraintStrength.Hard),
            ],
            AllowConstraintRelaxation = false,
        };

        var placement = CoarseGridPlacer.Place(topology, graph, constraints);

        Assert.True(placement.DevicePlacements.TryGetValue("M_TOP", out var top));
        Assert.True(placement.DevicePlacements.TryGetValue("M_BOT", out var bottom));
        Assert.Equal(top.Column, bottom.Column);
        Assert.True(
            top.MirrorX,
            $"Expected M_TOP gate to face the right-side source in the {deviceFlavor} chain."
        );
        Assert.False(
            bottom.MirrorX,
            $"Expected M_BOT gate to face the left-side source in the {deviceFlavor} chain."
        );
    }

    [Fact]
    public void Place_DrainSourceNetWithThirdPropagation_DoesNotForceSameMirrorX()
    {
        var circuit = TestCircuits.DrainSourceNetWithThirdPropagation();
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var constraints = new PlacementConstraintSet
        {
            DevicePlacements =
            [
                new DevicePlacementConstraint("M_TOP", 2, 4, RenderConstraintStrength.Hard),
                new DevicePlacementConstraint("M_BOT", 2, 9, RenderConstraintStrength.Hard),
                new DevicePlacementConstraint("M_AUX", 7, 9, RenderConstraintStrength.Hard),
            ],
            AllowConstraintRelaxation = false,
        };

        var placement = CoarseGridPlacer.Place(topology, graph, constraints);

        Assert.True(placement.DevicePlacements.TryGetValue("M_TOP", out var top));
        Assert.True(placement.DevicePlacements.TryGetValue("M_BOT", out var bottom));
        Assert.NotEqual(top.MirrorX, bottom.MirrorX);
    }

    [Fact]
    public void Place_SharedSignalCmosLShape_CentersVerticalDeviceBetweenHorizontalPair()
    {
        var circuit = TestCircuits.FullyDiffOtaWithTwoBiasPorts();
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);

        var placement = CoarseGridPlacer.Place(topology, graph);

        Assert.True(placement.DevicePlacements.TryGetValue("M_INP", out var mInp));
        Assert.True(placement.DevicePlacements.TryGetValue("M_INN", out var mInn));
        Assert.True(placement.DevicePlacements.TryGetValue("M_TAIL", out var mTail));
        Assert.Equal(mInp.Row, mInn.Row);
        Assert.NotEqual(mInp.Column, mInn.Column);
        Assert.NotEqual(mInp.Row, mTail.Row);
        Assert.Equal(mInp.Column + mInn.Column, mTail.Column * 2);
    }

    [Theory]
    [InlineData("tests/golden/cas/ota/OTA5TSingleEnded.el.cai")]
    [InlineData("tests/golden/cas/ota/OTA5TFullyDiff.el.cai")]
    [InlineData("tests/golden/cas/lna/LNA_CSCascodeInductivelyDegenerated_Sky130.el.cai")]
    public void Place_SharedSignalCmosPairs_RemainLocallyClustered(string cascodePath)
    {
        var fullPath = Path.Combine(GetRepoRoot(), cascodePath);
        using var reader = File.OpenText(fullPath);
        var readResult = CascodeReader.TryRead(reader, fullPath);
        Assert.True(readResult.Success, "Failed to parse Cascode file");
        var doc = readResult.Document!;
        var elCircuit = doc.Circuits.First(c => c.Level == CascodeLevel.EL);

        var graph = CircuitGraph.Build(elCircuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var placement = CoarseGridPlacer.Place(topology, graph);

        var alignedPairs = 0;
        foreach (var (deviceA, deviceB, netName) in GetSharedSignalCmosPairs(graph))
        {
            Assert.True(
                placement.DevicePlacements.TryGetValue(deviceA, out var cellA),
                $"Missing placement for device '{deviceA}'."
            );
            Assert.True(
                placement.DevicePlacements.TryGetValue(deviceB, out var cellB),
                $"Missing placement for device '{deviceB}'."
            );

            var rowDiff = Math.Abs(cellA.Row - cellB.Row);
            var colDiff = Math.Abs(cellA.Column - cellB.Column);
            var manhattanDistance = rowDiff + colDiff;

            Assert.True(
                rowDiff <= 1 || colDiff <= 1,
                $"Expected CMOS devices '{deviceA}' and '{deviceB}' sharing net '{netName}' to stay within one row or column band."
            );
            if (
                IsCenteredPassiveLoadPair(
                    graph,
                    topology,
                    placement.DevicePlacements,
                    deviceA,
                    deviceB,
                    netName
                )
            )
            {
                Assert.True(
                    manhattanDistance <= 8,
                    $"Expected load pair '{deviceA}'/'{deviceB}' with centered passive sensing on '{netName}' to remain within one inserted passive span, got Manhattan distance {manhattanDistance}."
                );
                alignedPairs++;
                continue;
            }

            if (IsSymmetricPair(topology, deviceA, deviceB))
            {
                Assert.True(
                    manhattanDistance <= 8,
                    $"Expected symmetric CMOS devices '{deviceA}' and '{deviceB}' sharing net '{netName}' to remain within one mirrored branch span, got Manhattan distance {manhattanDistance}."
                );
                alignedPairs++;
                continue;
            }

            Assert.True(
                manhattanDistance <= 5,
                $"Expected CMOS devices '{deviceA}' and '{deviceB}' sharing net '{netName}' to remain locally clustered, got Manhattan distance {manhattanDistance}."
            );
            alignedPairs++;
        }

        Assert.True(alignedPairs > 0, "Expected at least one shared-signal CMOS pair.");
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

    private static bool IsPointOnOrthogonalSegment(
        TerminalPosition point,
        TerminalPosition endpointA,
        TerminalPosition endpointB
    )
    {
        if (endpointA.X == endpointB.X)
        {
            if (point.X != endpointA.X)
            {
                return false;
            }

            var minY = Math.Min(endpointA.Y, endpointB.Y);
            var maxY = Math.Max(endpointA.Y, endpointB.Y);
            return point.Y > minY && point.Y < maxY;
        }

        if (endpointA.Y == endpointB.Y)
        {
            if (point.Y != endpointA.Y)
            {
                return false;
            }

            var minX = Math.Min(endpointA.X, endpointB.X);
            var maxX = Math.Max(endpointA.X, endpointB.X);
            return point.X > minX && point.X < maxX;
        }

        return false;
    }

    private static IEnumerable<(
        string DeviceA,
        string DeviceB,
        string NetName
    )> GetSharedSignalCmosPairs(CircuitGraph graph)
    {
        var yielded = new HashSet<(string DeviceA, string DeviceB)>();
        foreach (var (netName, refs) in graph.NetConnections)
        {
            if (graph.Supplies.Contains(netName) || graph.Grounds.Contains(netName))
            {
                continue;
            }

            var cmosIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var terminalRef in refs)
            {
                if (IsBodyOrShieldTerminal(terminalRef.Terminal))
                {
                    continue;
                }

                if (!graph.Devices.TryGetValue(terminalRef.DeviceId, out var device))
                {
                    continue;
                }

                var type = device.DeviceType.ToLowerInvariant();
                if (type is "nmos" or "nfet" or "pmos" or "pfet")
                {
                    cmosIds.Add(terminalRef.DeviceId);
                }
            }

            var sorted = cmosIds.OrderBy(id => id, StringComparer.Ordinal).ToList();
            for (var i = 0; i < sorted.Count; i++)
            {
                for (var j = i + 1; j < sorted.Count; j++)
                {
                    var key = (sorted[i], sorted[j]);
                    if (yielded.Add(key))
                    {
                        yield return (sorted[i], sorted[j], netName);
                    }
                }
            }
        }
    }

    private static bool IsCenteredPassiveLoadPair(
        CircuitGraph graph,
        TopologyResult topology,
        IReadOnlyDictionary<string, GridCell> placements,
        string deviceA,
        string deviceB,
        string pivotNet
    )
    {
        var loadPair = topology.SymmetricGroups.FirstOrDefault(group =>
            group.Type == SymmetryType.LoadPair
            && string.Equals(group.PivotNet, pivotNet, StringComparison.Ordinal)
            && group.DeviceIds.Contains(deviceA, StringComparer.Ordinal)
            && group.DeviceIds.Contains(deviceB, StringComparer.Ordinal)
        );
        if (loadPair is null)
        {
            return false;
        }

        var loadByOuterNet = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [graph.GetNetForTerminal(deviceA, "D") ?? string.Empty] = deviceA,
            [graph.GetNetForTerminal(deviceB, "D") ?? string.Empty] = deviceB,
        };
        if (loadByOuterNet.ContainsKey(string.Empty) || loadByOuterNet.Count != 2)
        {
            return false;
        }

        foreach (
            var passivePair in TopologyAnalyzer
                .DetectSymmetricPassivePairs(graph, topology)
                .Where(pair => string.Equals(pair.PivotNet, pivotNet, StringComparison.Ordinal))
        )
        {
            var passiveIds = new[] { passivePair.Left, passivePair.Right }
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (passiveIds.Count != 2)
            {
                continue;
            }

            var passiveAOuterNet = graph.GetNetForTerminal(passiveIds[0], "P");
            var passiveBOuterNet = graph.GetNetForTerminal(passiveIds[1], "P");
            if (
                passiveAOuterNet is null
                || passiveBOuterNet is null
                || !loadByOuterNet.ContainsKey(passiveAOuterNet)
                || !loadByOuterNet.ContainsKey(passiveBOuterNet)
            )
            {
                continue;
            }

            var loadAPlacement = placements[deviceA];
            var loadBPlacement = placements[deviceB];
            var passiveAPlacement = placements[passiveIds[0]];
            var passiveBPlacement = placements[passiveIds[1]];

            var leftLoad =
                loadAPlacement.Column <= loadBPlacement.Column ? loadAPlacement : loadBPlacement;
            var rightLoad =
                loadAPlacement.Column <= loadBPlacement.Column ? loadBPlacement : loadAPlacement;
            var leftPassive =
                passiveAPlacement.Column <= passiveBPlacement.Column
                    ? passiveAPlacement
                    : passiveBPlacement;
            var rightPassive =
                passiveAPlacement.Column <= passiveBPlacement.Column
                    ? passiveBPlacement
                    : passiveAPlacement;

            return leftLoad.Row == leftPassive.Row
                && rightLoad.Row == rightPassive.Row
                && leftLoad.Column < leftPassive.Column
                && rightPassive.Column < rightLoad.Column
                && leftLoad.Column + rightLoad.Column == leftPassive.Column + rightPassive.Column;
        }

        return false;
    }

    private static bool IsSymmetricPair(TopologyResult topology, string deviceA, string deviceB)
    {
        return topology.SymmetricGroups.Any(group =>
        {
            var ids = group.DeviceIds.Distinct(StringComparer.Ordinal).ToList();
            return ids.Contains(deviceA, StringComparer.Ordinal)
                && ids.Contains(deviceB, StringComparer.Ordinal);
        });
    }

    private static bool IsBodyOrShieldTerminal(string terminal)
    {
        var t = terminal.Trim().ToUpperInvariant();
        return t is "B" or "BULK" or "BODY" or "SH" or "SHIELD";
    }

    private static string GetRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir != null && !File.Exists(Path.Combine(dir, "Cascode.sln")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        return dir ?? throw new InvalidOperationException("Could not find repo root");
    }
}
