namespace Cascode.Render.Tests;

using Cascode.Language;
using Cascode.Render.Analysis;
using Cascode.Render.Layout;
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
    public void Place_PointToPointDrainSourceChain_StacksDevicesVertically()
    {
        var circuit = TestCircuits.StackedNmosPair();
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);

        var placement = CoarseGridPlacer.Place(topology, graph);

        var top = placement.DevicePlacements["M_TOP"];
        var bottom = placement.DevicePlacements["M_BOTTOM"];
        Assert.Equal(top.Column, bottom.Column);
        Assert.True(top.Row < bottom.Row);
    }

    [Fact]
    public void Place_TailDeviceBelowDiffPair_CanStayCenteredBetweenPair()
    {
        var circuit = TestCircuits.FullyDiffOtaWithTwoBiasPorts();
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);

        var placement = CoarseGridPlacer.Place(topology, graph);

        var left = placement.DevicePlacements["M_INP"];
        var right = placement.DevicePlacements["M_INN"];
        var tail = placement.DevicePlacements["M_TAIL"];
        var minColumn = Math.Min(left.Column, right.Column);
        var maxColumn = Math.Max(left.Column, right.Column);

        Assert.True(tail.Column > minColumn);
        Assert.True(tail.Column < maxColumn);
        Assert.True(tail.Row > left.Row);
        Assert.True(tail.Row > right.Row);
    }

    [Fact]
    public void Place_MixedPolarityDrainPairs_AlignVertically()
    {
        var circuit = LoadCircuitFromRepo("tests/golden/cas/ota/OTA5TFullyDiff.el.cai");
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);

        var placement = CoarseGridPlacer.Place(topology, graph);

        Assert.Equal(
            placement.DevicePlacements["M_LOAD_P"].Column,
            placement.DevicePlacements["dp.M_N"].Column
        );
        Assert.Equal(
            placement.DevicePlacements["M_LOAD_N"].Column,
            placement.DevicePlacements["dp.M_P"].Column
        );
    }

    [Theory]
    [InlineData("tests/golden/cas/stress/LNA_CSCascodeInductivelyDegenerated_Sky130.cas")]
    [InlineData("tests/golden/cas/stress/LNA_CSCascodeInductivelyDegenerated_TwoStage_Sky130.cas")]
    public void Place_LnaGatePath_StaysLeftToRightWithoutLgUturn(string relativePath)
    {
        var circuit = LoadCircuitFromRepo(relativePath);
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);

        var placement = CoarseGridPlacer.Place(topology, graph);

        var lm = placement.DevicePlacements["LM"];
        var lg = placement.DevicePlacements["LG"];
        var m1 = placement.DevicePlacements["M1"];

        Assert.False(m1.MirrorX, DescribePlacement(placement));
        Assert.True(
            lg.Column <= m1.Column,
            $"Expected LG to stay on or left of M1 to avoid an input U-turn.{Environment.NewLine}{DescribePlacement(placement)}"
        );
        Assert.Equal(lm.Row, lg.Row);
        Assert.True(
            lm.Column < lg.Column,
            $"Expected LM to stay upstream of LG so the matching path can flow left-to-right.{Environment.NewLine}{DescribePlacement(placement)}"
        );
        Assert.True(
            m1.Column - lm.Column <= 2,
            $"Expected LM/LG/M1 to stay compact from left to right.{Environment.NewLine}{DescribePlacement(placement)}"
        );
    }

    [Theory]
    [InlineData(
        "tests/golden/cas/stress/LNA_CSCascodeInductivelyDegenerated_Sky130.cas",
        "RCAS_TOP",
        "RCAS_BOT",
        "CCAS"
    )]
    [InlineData(
        "tests/golden/cas/stress/LNA_CSCascodeInductivelyDegenerated_TwoStage_Sky130.cas",
        "RCAS1_TOP",
        "RCAS1_BOT",
        "CCAS1"
    )]
    public void Place_BiasPassiveClusters_StayCompact(
        string relativePath,
        string topPassiveId,
        string bottomPassiveId,
        string decouplerId
    )
    {
        var circuit = LoadCircuitFromRepo(relativePath);
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);

        var placement = CoarseGridPlacer.Place(topology, graph);

        var columns = new[]
        {
            placement.DevicePlacements[topPassiveId].Column,
            placement.DevicePlacements[bottomPassiveId].Column,
            placement.DevicePlacements[decouplerId].Column,
        };

        Assert.True(
            columns.Max() - columns.Min() <= 1,
            $"Expected bias cluster '{topPassiveId}/{bottomPassiveId}/{decouplerId}' to stay within one column span.{Environment.NewLine}{DescribePlacement(placement)}"
        );
    }

    [Theory]
    [InlineData(
        "tests/golden/cas/stress/LNA_CSCascodeInductivelyDegenerated_Sky130.cas",
        "RCAS_TOP",
        "RCAS_BOT"
    )]
    [InlineData(
        "tests/golden/cas/stress/LNA_CSCascodeInductivelyDegenerated_TwoStage_Sky130.cas",
        "RCAS1_TOP",
        "RCAS1_BOT"
    )]
    [InlineData(
        "tests/golden/cas/stress/LNA_CSCascodeInductivelyDegenerated_TwoStage_Sky130.cas",
        "RGB2_TOP",
        "RGB2_BOT"
    )]
    [InlineData("tests/golden/cas/stress/SST12LN01_Sky130.cas", "RCASTOP", "RCASBOT")]
    [InlineData("tests/golden/cas/stress/CapFeedbackFD_Sky130.cas", "R_VCM_TOP", "R_VCM_BOT")]
    public void Place_BiasDividerRailLegs_StayNearEachOther(
        string relativePath,
        string topPassiveId,
        string bottomPassiveId
    )
    {
        var circuit = LoadCircuitFromRepo(relativePath);
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);

        var placement = CoarseGridPlacer.Place(topology, graph);
        var topColumn = placement.DevicePlacements[topPassiveId].Column;
        var bottomColumn = placement.DevicePlacements[bottomPassiveId].Column;

        Assert.True(
            Math.Abs(topColumn - bottomColumn) <= 1,
            $"Expected bias divider legs '{topPassiveId}' and '{bottomPassiveId}' to stay within one column of each other.{Environment.NewLine}{DescribePlacement(placement)}"
        );
    }

    [Fact]
    public void Place_BiasFilterChains_StayCompact()
    {
        var circuit = BiasTestCircuits.BiasFilterChain();
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);

        var placement = CoarseGridPlacer.Place(topology, graph);
        var columns = new[]
        {
            placement.DevicePlacements["R_TOP"].Column,
            placement.DevicePlacements["R_BOT"].Column,
            placement.DevicePlacements["R_FILTER"].Column,
            placement.DevicePlacements["C_FILTER"].Column,
        };

        Assert.True(
            columns.Max() - columns.Min() <= 1,
            $"Expected the divider and RC filter on the same bias chain to stay within one column span.{Environment.NewLine}{DescribePlacement(placement)}"
        );
    }

    [Theory]
    [InlineData(
        "tests/golden/cas/stress/LNA_CSCascodeInductivelyDegenerated_Sky130.cas",
        "M2",
        2,
        "RCAS_TOP",
        "RCAS_BOT",
        "CCAS"
    )]
    [InlineData(
        "tests/golden/cas/stress/LNA_CSCascodeInductivelyDegenerated_TwoStage_Sky130.cas",
        "M2",
        2,
        "RCAS1_TOP",
        "RCAS1_BOT",
        "CCAS1"
    )]
    [InlineData(
        "tests/golden/cas/stress/LNA_CSCascodeInductivelyDegenerated_TwoStage_Sky130.cas",
        "M3",
        2,
        "RGB2_TOP",
        "RGB2_BOT",
        "CGB2",
        "RG2"
    )]
    public void Place_BiasNetworks_StayNearTheGateTheyBias(
        string relativePath,
        string gateDeviceId,
        int maximumColumnDistance,
        params string[] biasDeviceIds
    )
    {
        var circuit = LoadCircuitFromRepo(relativePath);
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);

        var placement = CoarseGridPlacer.Place(topology, graph);
        var gateColumn = placement.DevicePlacements[gateDeviceId].Column;
        var nearestBiasColumnDistance = biasDeviceIds
            .Select(deviceId => Math.Abs(placement.DevicePlacements[deviceId].Column - gateColumn))
            .Min();

        Assert.True(
            nearestBiasColumnDistance <= maximumColumnDistance,
            $"Expected bias network '{string.Join("/", biasDeviceIds)}' to stay within {maximumColumnDistance} columns of gate device '{gateDeviceId}'.{Environment.NewLine}{DescribePlacement(placement)}"
        );
    }

    [Fact]
    public void Place_TwoStageLna_SecondStageGatePassives_StayAsOneLocalNeighborhood()
    {
        var circuit = LoadCircuitFromRepo(
            "tests/golden/cas/stress/LNA_CSCascodeInductivelyDegenerated_TwoStage_Sky130.cas"
        );
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);

        var placement = CoarseGridPlacer.Place(topology, graph);
        var m3Column = placement.DevicePlacements["M3"].Column;
        var rg2Column = placement.DevicePlacements["RG2"].Column;
        var cintColumn = placement.DevicePlacements["CINT"].Column;
        var biasColumns = new[]
        {
            placement.DevicePlacements["RGB2_TOP"].Column,
            placement.DevicePlacements["RGB2_BOT"].Column,
            placement.DevicePlacements["CGB2"].Column,
            rg2Column,
        };

        Assert.True(
            m3Column - rg2Column <= 1,
            $"Expected RG2 to stay within one column of M3 so the second-stage gate bias path does not detour across the schematic.{Environment.NewLine}{DescribePlacement(placement)}"
        );
        Assert.True(
            m3Column - cintColumn <= 3,
            $"Expected CINT to stay within three columns of M3 so the interstage gate path no longer loops back around the first stage.{Environment.NewLine}{DescribePlacement(placement)}"
        );
        Assert.True(
            biasColumns.Max() - biasColumns.Min() <= 2,
            $"Expected the RGB2/CGB2/RG2 bias neighborhood to stay within two columns instead of splitting the second-stage gate path.{Environment.NewLine}{DescribePlacement(placement)}"
        );
    }

    [Fact]
    public void Place_TwoStageLna_GateBiasShunts_StayOnTheConsumerSideOfTheBiasEntry()
    {
        var circuit = LoadCircuitFromRepo(
            "tests/golden/cas/stress/LNA_CSCascodeInductivelyDegenerated_TwoStage_Sky130.cas"
        );
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);

        var placement = CoarseGridPlacer.Place(topology, graph);
        var rg2Row = placement.DevicePlacements["RG2"].Row;
        var rgb2TopRow = placement.DevicePlacements["RGB2_TOP"].Row;
        var rgb2BotRow = placement.DevicePlacements["RGB2_BOT"].Row;
        var cgb2Row = placement.DevicePlacements["CGB2"].Row;

        Assert.True(
            rgb2TopRow <= rg2Row,
            $"Expected the VDD leg RGB2_TOP to stay above or level with RG2 so the bias path descends into the second-stage gate neighborhood.{Environment.NewLine}{DescribePlacement(placement)}"
        );
        Assert.True(
            rgb2BotRow >= rg2Row,
            $"Expected the GND leg RGB2_BOT to stay at or below RG2 so the vgb2 branch does not climb back up after RGB2_TOP.{Environment.NewLine}{DescribePlacement(placement)}"
        );
        Assert.True(
            cgb2Row >= rg2Row,
            $"Expected the GND shunt CGB2 to stay at or below RG2 so the vgb2 branch does not detour back up through the second-stage bias network.{Environment.NewLine}{DescribePlacement(placement)}"
        );
    }

    [Fact]
    public void Place_GateBiasPassive_ExposesItsGateTerminalTowardTheGatedMos()
    {
        var circuit = LoadCircuitFromRepo(
            "tests/golden/cas/stress/LNA_CSCascodeInductivelyDegenerated_TwoStage_Sky130.cas"
        );
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);

        var placement = CoarseGridPlacer.Place(topology, graph);
        var gateTerminalX = GetTerminalX(placement, graph, "RG2", "N");
        var biasTerminalX = GetTerminalX(placement, graph, "RG2", "P");
        var m3GateX = GetTerminalX(placement, graph, "M3", "G");

        Assert.True(
            Math.Abs(gateTerminalX - m3GateX) < Math.Abs(biasTerminalX - m3GateX),
            $"Expected RG2 to expose its ng2 terminal toward M3.G so the second-stage gate path does not loop around the resistor.{Environment.NewLine}{DescribePlacement(placement)}"
        );
    }

    [Theory]
    [InlineData("tests/golden/cas/stress/LNA_CSCascodeInductivelyDegenerated_Sky130.cas")]
    [InlineData("tests/golden/cas/stress/LNA_CSCascodeInductivelyDegenerated_TwoStage_Sky130.cas")]
    [InlineData("tests/golden/cas/stress/SST12LN01_Sky130.cas")]
    public void Place_VddConnectedMosLoads_StayDirectlyAboveTheirMos(string relativePath)
    {
        var circuit = LoadCircuitFromRepo(relativePath);
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);

        var placement = CoarseGridPlacer.Place(topology, graph);
        var loadPairs = FindVddConnectedMosLoadPairs(graph);
        Assert.NotEmpty(loadPairs);

        foreach (var (loadDeviceId, mosDeviceId, signalNet) in loadPairs)
        {
            var load = placement.DevicePlacements[loadDeviceId];
            var mos = placement.DevicePlacements[mosDeviceId];
            Assert.True(
                load.Column == mos.Column,
                $"Expected VDD-connected load '{loadDeviceId}' on net '{signalNet}' to share a column with MOS '{mosDeviceId}'.{Environment.NewLine}{DescribePlacement(placement)}"
            );
            Assert.True(
                load.Row < mos.Row,
                $"Expected VDD-connected load '{loadDeviceId}' on net '{signalNet}' to stay above MOS '{mosDeviceId}'.{Environment.NewLine}{DescribePlacement(placement)}"
            );
        }
    }

    [Theory]
    [InlineData("tests/golden/cas/stress/LNA_CSCascodeInductivelyDegenerated_Sky130.cas")]
    [InlineData("tests/golden/cas/stress/LNA_CSCascodeInductivelyDegenerated_TwoStage_Sky130.cas")]
    public void Place_OutputCouplingPassives_KeepSignalTerminalOnMosSide_AndOutputTerminalOnPortSide(
        string relativePath
    )
    {
        var circuit = LoadCircuitFromRepo(relativePath);
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);

        var placement = CoarseGridPlacer.Place(topology, graph);
        var couplingPairs = FindOutputCouplingPassivePairs(graph);
        Assert.NotEmpty(couplingPairs);

        foreach (
            var (
                passiveDeviceId,
                mosDeviceId,
                mosTerminal,
                passiveSignalTerminal,
                signalNet
            ) in couplingPairs
        )
        {
            var outputTerminal = passiveSignalTerminal == "P" ? "N" : "P";
            var passiveSignalX = GetTerminalX(
                placement,
                graph,
                passiveDeviceId,
                passiveSignalTerminal
            );
            var passiveOutputX = GetTerminalX(placement, graph, passiveDeviceId, outputTerminal);
            var mosX = GetTerminalX(placement, graph, mosDeviceId, mosTerminal);
            Assert.True(
                passiveSignalX >= mosX,
                $"Expected output coupling passive '{passiveDeviceId}' on net '{signalNet}' to keep its signal terminal on or to the right of {mosDeviceId}.{mosTerminal}, but got X={passiveSignalX} vs X={mosX}.{Environment.NewLine}{DescribePlacement(placement)}"
            );
            Assert.True(
                passiveOutputX >= passiveSignalX,
                $"Expected output coupling passive '{passiveDeviceId}' on net '{signalNet}' to expose its output terminal on or to the right of its signal terminal, but got output X={passiveOutputX} vs signal X={passiveSignalX}.{Environment.NewLine}{DescribePlacement(placement)}"
            );
        }
    }

    private static Circuit LoadCircuitFromRepo(string relativePath)
    {
        var repoRoot = Directory.GetCurrentDirectory();
        while (repoRoot != null && !File.Exists(Path.Combine(repoRoot, "Cascode.sln")))
        {
            repoRoot = Directory.GetParent(repoRoot)?.FullName;
        }

        Assert.NotNull(repoRoot);
        var fullPath = Path.Combine(repoRoot!, relativePath);
        using var reader = File.OpenText(fullPath);
        var readResult = CascodeReader.TryRead(reader, fullPath);
        Assert.True(readResult.Success, "Failed to parse Cascode file");
        return readResult.Document!.Circuits.First(c => c.Level == CascodeLevel.EL);
    }

    private static string DescribePlacement(CoarseGridResult placement)
    {
        return string.Join(
            Environment.NewLine,
            placement
                .DevicePlacements.OrderBy(kv => kv.Value.Row)
                .ThenBy(kv => kv.Value.Column)
                .Select(kv =>
                    $"{kv.Key}: row={kv.Value.Row}, col={kv.Value.Column}, mirrorX={kv.Value.MirrorX}"
                )
        );
    }

    private static IReadOnlyList<(
        string LoadDeviceId,
        string MosDeviceId,
        string SignalNet
    )> FindVddConnectedMosLoadPairs(CircuitGraph graph)
    {
        var pairs = new List<(string LoadDeviceId, string MosDeviceId, string SignalNet)>();
        foreach (var (deviceId, device) in graph.Devices)
        {
            if (
                device.DeviceType.ToLowerInvariant()
                is not ("resistor" or "capacitor" or "inductor")
            )
            {
                continue;
            }

            var vddTerminal = device.Bindings.FirstOrDefault(binding =>
                graph.Supplies.Contains(binding.Value)
            );
            if (string.IsNullOrEmpty(vddTerminal.Key))
            {
                continue;
            }

            var signalNet = device
                .Bindings.Where(binding =>
                    !string.Equals(binding.Key, vddTerminal.Key, StringComparison.Ordinal)
                )
                .Select(binding => binding.Value)
                .SingleOrDefault();
            if (
                signalNet == null
                || graph.IsSupplyOrGround(signalNet)
                || !graph.NetConnections.TryGetValue(signalNet, out var connections)
            )
            {
                continue;
            }

            var mosConnections = connections
                .Where(connection =>
                    graph.Devices.TryGetValue(connection.DeviceId, out var connectedDevice)
                    && connectedDevice.DeviceType.ToLowerInvariant()
                        is "nmos"
                            or "nfet"
                            or "pmos"
                            or "pfet"
                    && connection.Terminal is "D" or "S"
                )
                .Select(connection => connection.DeviceId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (mosConnections.Length != 1)
            {
                continue;
            }

            pairs.Add((deviceId, mosConnections[0], signalNet));
        }

        return pairs;
    }

    private static IReadOnlyList<(
        string PassiveDeviceId,
        string MosDeviceId,
        string MosTerminal,
        string PassiveSignalTerminal,
        string SignalNet
    )> FindOutputCouplingPassivePairs(CircuitGraph graph)
    {
        var pairs =
            new List<(
                string PassiveDeviceId,
                string MosDeviceId,
                string MosTerminal,
                string PassiveSignalTerminal,
                string SignalNet
            )>();
        foreach (var (deviceId, device) in graph.Devices)
        {
            if (
                device.DeviceType.ToLowerInvariant()
                is not ("resistor" or "capacitor" or "inductor")
            )
            {
                continue;
            }

            var outputBinding = device.Bindings.FirstOrDefault(binding =>
                graph.OutputPorts.Contains(binding.Value)
            );
            if (string.IsNullOrEmpty(outputBinding.Key))
            {
                continue;
            }

            var signalNet = device
                .Bindings.Where(binding =>
                    !string.Equals(binding.Key, outputBinding.Key, StringComparison.Ordinal)
                )
                .Select(binding => binding.Value)
                .Distinct(StringComparer.Ordinal)
                .SingleOrDefault();
            if (
                signalNet == null
                || graph.IsSupplyOrGround(signalNet)
                || !graph.NetConnections.TryGetValue(signalNet, out var connections)
            )
            {
                continue;
            }

            var mosConnections = connections
                .Where(connection =>
                    graph.Devices.TryGetValue(connection.DeviceId, out var connectedDevice)
                    && connectedDevice.DeviceType.ToLowerInvariant()
                        is "nmos"
                            or "nfet"
                            or "pmos"
                            or "pfet"
                    && connection.Terminal is "D" or "S"
                )
                .ToArray();
            var mosDeviceIds = mosConnections
                .Select(connection => connection.DeviceId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (mosDeviceIds.Length != 1)
            {
                continue;
            }

            var mosConnection = mosConnections.First(connection =>
                connection.DeviceId == mosDeviceIds[0]
            );
            var passiveSignalTerminal = outputBinding.Key.Equals(
                "P",
                StringComparison.OrdinalIgnoreCase
            )
                ? "N"
                : "P";
            pairs.Add(
                (
                    deviceId,
                    mosConnection.DeviceId,
                    mosConnection.Terminal,
                    passiveSignalTerminal,
                    signalNet
                )
            );
        }

        return pairs;
    }

    private static int GetTerminalX(
        CoarseGridResult placement,
        CircuitGraph graph,
        string deviceId,
        string terminal
    )
    {
        var cell = placement.DevicePlacements[deviceId];
        var deviceType = graph.Devices[deviceId].DeviceType.ToLowerInvariant();
        if (deviceType is "nmos" or "nfet" or "pmos" or "pfet")
        {
            var mos = DeviceGeometry.GetMosfetPlacement(cell.Row, cell.Column, cell.MirrorX);
            return terminal switch
            {
                "G" => mos.GateX,
                "D" or "S" => mos.AxisX,
                _ => throw new InvalidOperationException($"Unsupported MOS terminal '{terminal}'."),
            };
        }

        if (deviceType is "resistor" or "capacitor" or "inductor")
        {
            if (placement.HorizontalPassiveIds.Contains(deviceId))
            {
                var horizontalPassive = DeviceGeometry.GetHorizontalPassivePlacement(
                    cell.Row,
                    cell.Column,
                    placement.ColumnCount,
                    pOnLeft: !cell.MirrorX
                );
                return terminal switch
                {
                    "P" => horizontalPassive.PX,
                    "N" => horizontalPassive.NX,
                    _ => throw new InvalidOperationException(
                        $"Unsupported passive terminal '{terminal}'."
                    ),
                };
            }

            var verticalPassive = DeviceGeometry.GetPassivePlacement(cell.Row, cell.Column);
            return terminal switch
            {
                "P" or "N" => verticalPassive.PX,
                _ => throw new InvalidOperationException(
                    $"Unsupported passive terminal '{terminal}'."
                ),
            };
        }

        throw new InvalidOperationException($"Unsupported device type '{deviceType}'.");
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
