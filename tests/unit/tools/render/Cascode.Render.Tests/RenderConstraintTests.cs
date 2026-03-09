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

    [Fact]
    public void Place_UniqueSameFlavorDrainSourceChain_PrefersSameMirrorX()
    {
        var circuit = TestCircuits.SameFlavorDrainSourceChainWithCompetingGateSides();
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var constraints = new PlacementConstraintSet
        {
            DevicePlacements =
            [
                new DevicePlacementConstraint("M_TOP", 2, 4, RenderConstraintStrength.Hard),
                new DevicePlacementConstraint("M_BOT", 2, 9, RenderConstraintStrength.Hard),
            ],
            AllowConstraintRelaxation = false,
        };

        var placement = CoarseGridPlacer.Place(topology, graph, constraints);

        Assert.True(placement.DevicePlacements.TryGetValue("M_TOP", out var top));
        Assert.True(placement.DevicePlacements.TryGetValue("M_BOT", out var bottom));
        Assert.Equal(top.Column, bottom.Column);
        Assert.Equal(top.MirrorX, bottom.MirrorX);
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
