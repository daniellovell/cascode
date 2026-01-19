using Cascode.ACIR;
using Cascode.Render.Analysis;
using Cascode.Render.Layout;
using Cascode.Render.Placement;
using Cascode.Render.Routing;
using Cascode.Render.Svg;

namespace Cascode.Render.Tests;

public class LabelPlacerTests
{
    [Fact]
    public void PlaceLabels_BottomDevice_DoesNotOverlapGroundRail()
    {
        // Arrange: Device at bottom row near ground rail
        var circuit = CreateSimpleCircuitWithBottomDevice();
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);

        var placement = new CoarseGridResult
        {
            RowCount = 2,
            ColumnCount = 3,
            SymmetryAxis = 1,
            HorizontalPassiveIds = new HashSet<string>(),
            DevicePlacements = new Dictionary<string, GridCell>
            {
                ["M_TAIL"] = new GridCell(1, 1, false),
            },
        };

        var canvasHeight =
            DeviceGeometry.RailMargin
            + placement.RowCount * DeviceGeometry.CellHeight
            + DeviceGeometry.RailMargin;
        var canvasWidth = placement.ColumnCount * DeviceGeometry.CellWidth;

        var routing = new RoutingResult
        {
            Segments = new List<WireSegment>(),
            Junctions = new List<GridPoint>(),
            SegmentsByNet = new Dictionary<string, IReadOnlyList<WireSegment>>(),
            CanvasWidth = canvasWidth,
            CanvasHeight = canvasHeight,
            TerminalPositions = new List<TerminalPosition>(),
        };

        var placer = new LabelPlacer();

        // Act
        var placements = placer.PlaceLabels(placement, routing, graph, StyleSheet.Default);

        // Assert
        var tailPlacement = placements.Single(p => p.DeviceId == "M_TAIL");

        // Ground rail is at canvasHeight - RailMargin/2
        var groundRailY = canvasHeight - DeviceGeometry.RailMargin / 2.0;

        // Label should not overlap the ground rail area (rail +/- 5px for the line thickness)
        Assert.True(
            tailPlacement.ParamLabelY < groundRailY - 5
                || tailPlacement.Direction == LabelDirection.N
                || tailPlacement.Direction == LabelDirection.NE
                || tailPlacement.Direction == LabelDirection.NW
                || tailPlacement.Direction == LabelDirection.E
                || tailPlacement.Direction == LabelDirection.W,
            $"Label at Y={tailPlacement.ParamLabelY} overlaps ground rail at Y={groundRailY}. Direction={tailPlacement.Direction}"
        );
    }

    [Fact]
    public void PlaceLabels_CenterDevice_PrefersSouthDirection()
    {
        // Arrange: Single device at center column with no obstacles
        var circuit = CreateSimpleCircuit();
        var graph = CircuitGraph.Build(circuit);

        var placement = new CoarseGridResult
        {
            RowCount = 3,
            ColumnCount = 3,
            SymmetryAxis = 1,
            HorizontalPassiveIds = new HashSet<string>(),
            DevicePlacements = new Dictionary<string, GridCell>
            {
                ["M1"] = new GridCell(1, 1, false),
            },
        };

        var canvasHeight =
            DeviceGeometry.RailMargin
            + placement.RowCount * DeviceGeometry.CellHeight
            + DeviceGeometry.RailMargin;
        var canvasWidth = placement.ColumnCount * DeviceGeometry.CellWidth;

        var routing = new RoutingResult
        {
            Segments = new List<WireSegment>(),
            Junctions = new List<GridPoint>(),
            SegmentsByNet = new Dictionary<string, IReadOnlyList<WireSegment>>(),
            CanvasWidth = canvasWidth,
            CanvasHeight = canvasHeight,
            TerminalPositions = new List<TerminalPosition>(),
        };

        var placer = new LabelPlacer();

        // Act
        var placements = placer.PlaceLabels(placement, routing, graph, StyleSheet.Default);

        // Assert: Center devices prefer S direction
        var m1Placement = placements.Single(p => p.DeviceId == "M1");
        Assert.Equal(LabelDirection.S, m1Placement.Direction);
        Assert.Equal("middle", m1Placement.TextAnchor);
    }

    [Fact]
    public void PlaceLabels_LeftSideDevice_PrefersSouthwestDirection()
    {
        // Arrange: Device on left side of symmetry axis
        var circuit = CreateSimpleCircuit();
        var graph = CircuitGraph.Build(circuit);

        var placement = new CoarseGridResult
        {
            RowCount = 3,
            ColumnCount = 5,
            SymmetryAxis = 2,
            HorizontalPassiveIds = new HashSet<string>(),
            DevicePlacements = new Dictionary<string, GridCell>
            {
                ["M1"] = new GridCell(1, 0, false),
            },
        };

        var canvasHeight =
            DeviceGeometry.RailMargin
            + placement.RowCount * DeviceGeometry.CellHeight
            + DeviceGeometry.RailMargin;
        var canvasWidth = placement.ColumnCount * DeviceGeometry.CellWidth;

        var routing = new RoutingResult
        {
            Segments = new List<WireSegment>(),
            Junctions = new List<GridPoint>(),
            SegmentsByNet = new Dictionary<string, IReadOnlyList<WireSegment>>(),
            CanvasWidth = canvasWidth,
            CanvasHeight = canvasHeight,
            TerminalPositions = new List<TerminalPosition>(),
        };

        var placer = new LabelPlacer();

        // Act
        var placements = placer.PlaceLabels(placement, routing, graph, StyleSheet.Default);

        // Assert: Left-side devices prefer SW direction
        var m1Placement = placements.Single(p => p.DeviceId == "M1");
        Assert.Equal(LabelDirection.SW, m1Placement.Direction);
        Assert.Equal("end", m1Placement.TextAnchor);
    }

    [Fact]
    public void PlaceLabels_RightSideDevice_PrefersSoutheastDirection()
    {
        // Arrange: Device on right side of symmetry axis
        var circuit = CreateSimpleCircuit();
        var graph = CircuitGraph.Build(circuit);

        var placement = new CoarseGridResult
        {
            RowCount = 3,
            ColumnCount = 5,
            SymmetryAxis = 2,
            HorizontalPassiveIds = new HashSet<string>(),
            DevicePlacements = new Dictionary<string, GridCell>
            {
                ["M1"] = new GridCell(1, 4, false),
            },
        };

        var canvasHeight =
            DeviceGeometry.RailMargin
            + placement.RowCount * DeviceGeometry.CellHeight
            + DeviceGeometry.RailMargin;
        var canvasWidth = placement.ColumnCount * DeviceGeometry.CellWidth;

        var routing = new RoutingResult
        {
            Segments = new List<WireSegment>(),
            Junctions = new List<GridPoint>(),
            SegmentsByNet = new Dictionary<string, IReadOnlyList<WireSegment>>(),
            CanvasWidth = canvasWidth,
            CanvasHeight = canvasHeight,
            TerminalPositions = new List<TerminalPosition>(),
        };

        var placer = new LabelPlacer();

        // Act
        var placements = placer.PlaceLabels(placement, routing, graph, StyleSheet.Default);

        // Assert: Right-side devices prefer SE direction
        var m1Placement = placements.Single(p => p.DeviceId == "M1");
        Assert.Equal(LabelDirection.SE, m1Placement.Direction);
        Assert.Equal("start", m1Placement.TextAnchor);
    }

    [Fact]
    public void PlaceLabels_TopDevice_DoesNotOverlapSupplyRail()
    {
        // Arrange: Device at top row near VDD rail
        var circuit = CreateCircuitWithTopDevice();
        var graph = CircuitGraph.Build(circuit);

        var placement = new CoarseGridResult
        {
            RowCount = 2,
            ColumnCount = 3,
            SymmetryAxis = 1,
            HorizontalPassiveIds = new HashSet<string>(),
            DevicePlacements = new Dictionary<string, GridCell>
            {
                ["M_LOAD"] = new GridCell(0, 1, false),
            },
        };

        var canvasHeight =
            DeviceGeometry.RailMargin
            + placement.RowCount * DeviceGeometry.CellHeight
            + DeviceGeometry.RailMargin;
        var canvasWidth = placement.ColumnCount * DeviceGeometry.CellWidth;

        var routing = new RoutingResult
        {
            Segments = new List<WireSegment>(),
            Junctions = new List<GridPoint>(),
            SegmentsByNet = new Dictionary<string, IReadOnlyList<WireSegment>>(),
            CanvasWidth = canvasWidth,
            CanvasHeight = canvasHeight,
            TerminalPositions = new List<TerminalPosition>(),
        };

        var placer = new LabelPlacer();

        // Act
        var placements = placer.PlaceLabels(placement, routing, graph, StyleSheet.Default);

        // Assert
        var loadPlacement = placements.Single(p => p.DeviceId == "M_LOAD");

        // Supply rail is at RailMargin/2
        var supplyRailY = DeviceGeometry.RailMargin / 2.0;

        // For top row devices, label should not be placed above into the VDD rail area
        Assert.True(
            loadPlacement.DeviceLabelY > supplyRailY + 5
                || loadPlacement.Direction == LabelDirection.S
                || loadPlacement.Direction == LabelDirection.SE
                || loadPlacement.Direction == LabelDirection.SW
                || loadPlacement.Direction == LabelDirection.E
                || loadPlacement.Direction == LabelDirection.W,
            $"Label at Y={loadPlacement.DeviceLabelY} overlaps supply rail at Y={supplyRailY}. Direction={loadPlacement.Direction}"
        );
    }

    [Fact]
    public void PlaceLabels_MultipleDevices_AvoidsLabelOverlap()
    {
        // Arrange: Two adjacent devices
        var circuit = CreateCircuitWithTwoDevices();
        var graph = CircuitGraph.Build(circuit);

        var placement = new CoarseGridResult
        {
            RowCount = 3,
            ColumnCount = 3,
            SymmetryAxis = 1,
            HorizontalPassiveIds = new HashSet<string>(),
            DevicePlacements = new Dictionary<string, GridCell>
            {
                ["M1"] = new GridCell(1, 0, false),
                ["M2"] = new GridCell(1, 2, true),
            },
        };

        var canvasHeight =
            DeviceGeometry.RailMargin
            + placement.RowCount * DeviceGeometry.CellHeight
            + DeviceGeometry.RailMargin;
        var canvasWidth = placement.ColumnCount * DeviceGeometry.CellWidth;

        var routing = new RoutingResult
        {
            Segments = new List<WireSegment>(),
            Junctions = new List<GridPoint>(),
            SegmentsByNet = new Dictionary<string, IReadOnlyList<WireSegment>>(),
            CanvasWidth = canvasWidth,
            CanvasHeight = canvasHeight,
            TerminalPositions = new List<TerminalPosition>(),
        };

        var placer = new LabelPlacer();

        // Act
        var placements = placer.PlaceLabels(placement, routing, graph, StyleSheet.Default);

        // Assert: Both devices should have labels placed
        Assert.Equal(2, placements.Count);

        var m1 = placements.Single(p => p.DeviceId == "M1");
        var m2 = placements.Single(p => p.DeviceId == "M2");

        // Compute actual label bounds and verify they don't overlap
        var m1Bounds = ComputeLabelBounds(m1, "M1", "W=2u L=100n");
        var m2Bounds = ComputeLabelBounds(m2, "M2", "W=2u L=100n");

        Assert.False(
            m1Bounds.Overlaps(m2Bounds),
            $"Labels overlap: M1 bounds ({m1Bounds.X:F1},{m1Bounds.Y:F1},{m1Bounds.Width:F1},{m1Bounds.Height:F1}) "
                + $"M2 bounds ({m2Bounds.X:F1},{m2Bounds.Y:F1},{m2Bounds.Width:F1},{m2Bounds.Height:F1})"
        );
    }

    [Fact]
    public void PlaceLabels_SymmetricHorizontalPassives_DoNotOverlap()
    {
        // Arrange: Two symmetric horizontal resistors (like CMFB resistors)
        // Using actual column positions from OTA5TFullyDiff: columns 1 and 3 with axis at 2
        // But the pixel positions end up close together due to horizontal passive placement
        var circuit = CreateCircuitWithCmfbResistors();
        var graph = CircuitGraph.Build(circuit);

        // Match the actual OTA5TFullyDiff layout: 5 columns, axis at 2
        // Horizontal passives at columns 1 and 3 (fill columns)
        var placement = new CoarseGridResult
        {
            RowCount = 3,
            ColumnCount = 5,
            SymmetryAxis = 2,
            HorizontalPassiveIds = new HashSet<string> { "R_CMFB_P", "R_CMFB_N" },
            DevicePlacements = new Dictionary<string, GridCell>
            {
                ["R_CMFB_P"] = new GridCell(1, 1, false),
                ["R_CMFB_N"] = new GridCell(1, 3, true),
            },
        };

        var canvasHeight =
            DeviceGeometry.RailMargin
            + placement.RowCount * DeviceGeometry.CellHeight
            + DeviceGeometry.RailMargin;
        var canvasWidth = placement.ColumnCount * DeviceGeometry.CellWidth;

        var routing = new RoutingResult
        {
            Segments = new List<WireSegment>(),
            Junctions = new List<GridPoint>(),
            SegmentsByNet = new Dictionary<string, IReadOnlyList<WireSegment>>(),
            CanvasWidth = canvasWidth,
            CanvasHeight = canvasHeight,
            TerminalPositions = new List<TerminalPosition>(),
        };

        var placer = new LabelPlacer();

        // Act
        var placements = placer.PlaceLabels(placement, routing, graph, StyleSheet.Default);

        // Assert
        Assert.Equal(2, placements.Count);

        var rP = placements.Single(p => p.DeviceId == "R_CMFB_P");
        var rN = placements.Single(p => p.DeviceId == "R_CMFB_N");

        // Compute actual label bounds and verify they don't overlap
        var rPBounds = ComputeLabelBounds(rP, "R_CMFB_P", "R=500k");
        var rNBounds = ComputeLabelBounds(rN, "R_CMFB_N", "R=500k");

        Assert.False(
            rPBounds.Overlaps(rNBounds),
            $"CMFB resistor labels overlap: R_CMFB_P bounds ({rPBounds.X:F1},{rPBounds.Y:F1},{rPBounds.Width:F1},{rPBounds.Height:F1}) "
                + $"direction={rP.Direction}, R_CMFB_N bounds ({rNBounds.X:F1},{rNBounds.Y:F1},{rNBounds.Width:F1},{rNBounds.Height:F1}) "
                + $"direction={rN.Direction}"
        );
    }

    [Fact]
    public void PlaceLabels_CloseHorizontalPassives_DoNotOverlap()
    {
        // Arrange: Two horizontal resistors in adjacent columns (closer than typical symmetric placement)
        // This simulates the actual OTA5TFullyDiff where devices end up ~90px apart
        var circuit = CreateCircuitWithCmfbResistors();
        var graph = CircuitGraph.Build(circuit);

        // Force close placement by using adjacent columns
        var placement = new CoarseGridResult
        {
            RowCount = 3,
            ColumnCount = 4,
            SymmetryAxis = 1,
            HorizontalPassiveIds = new HashSet<string> { "R_CMFB_P", "R_CMFB_N" },
            DevicePlacements = new Dictionary<string, GridCell>
            {
                ["R_CMFB_P"] = new GridCell(1, 1, false),
                ["R_CMFB_N"] = new GridCell(1, 2, true),
            },
        };

        var canvasHeight =
            DeviceGeometry.RailMargin
            + placement.RowCount * DeviceGeometry.CellHeight
            + DeviceGeometry.RailMargin;
        var canvasWidth = placement.ColumnCount * DeviceGeometry.CellWidth;

        var routing = new RoutingResult
        {
            Segments = new List<WireSegment>(),
            Junctions = new List<GridPoint>(),
            SegmentsByNet = new Dictionary<string, IReadOnlyList<WireSegment>>(),
            CanvasWidth = canvasWidth,
            CanvasHeight = canvasHeight,
            TerminalPositions = new List<TerminalPosition>(),
        };

        var placer = new LabelPlacer();

        // Act
        var placements = placer.PlaceLabels(placement, routing, graph, StyleSheet.Default);

        // Assert
        Assert.Equal(2, placements.Count);

        var rP = placements.Single(p => p.DeviceId == "R_CMFB_P");
        var rN = placements.Single(p => p.DeviceId == "R_CMFB_N");

        // Compute actual label bounds and verify they don't overlap
        var rPBounds = ComputeLabelBounds(rP, "R_CMFB_P", "R=500k");
        var rNBounds = ComputeLabelBounds(rN, "R_CMFB_N", "R=500k");

        Assert.False(
            rPBounds.Overlaps(rNBounds),
            $"Close CMFB labels overlap: R_CMFB_P at ({rPBounds.X:F1},{rPBounds.Y:F1}) size ({rPBounds.Width:F1},{rPBounds.Height:F1}) "
                + $"direction={rP.Direction}, R_CMFB_N at ({rNBounds.X:F1},{rNBounds.Y:F1}) size ({rNBounds.Width:F1},{rNBounds.Height:F1}) "
                + $"direction={rN.Direction}"
        );
    }

    private static TextBounds ComputeLabelBounds(
        LabelPlacement placement,
        string deviceLabel,
        string paramLabel
    )
    {
        const double DeviceLabelFontSize = 10.0;
        const double ParamLabelFontSize = 8.0;
        const double CharWidthRatio = 0.7;
        const double LineHeightRatio = 1.2;
        const double LabelGap = 2.0;

        var deviceLabelWidth = deviceLabel.Length * DeviceLabelFontSize * CharWidthRatio;
        var deviceLabelHeight = DeviceLabelFontSize * LineHeightRatio;
        var paramLabelWidth = paramLabel.Length * ParamLabelFontSize * CharWidthRatio;
        var paramLabelHeight = ParamLabelFontSize * LineHeightRatio;

        var totalWidth = Math.Max(deviceLabelWidth, paramLabelWidth);
        var totalHeight = deviceLabelHeight + LabelGap + paramLabelHeight;

        double boundsX = placement.TextAnchor switch
        {
            "middle" => placement.DeviceLabelX - totalWidth / 2,
            "end" => placement.DeviceLabelX - totalWidth,
            _ => placement.DeviceLabelX,
        };

        var boundsY = placement.DeviceLabelY - deviceLabelHeight;

        return new TextBounds(boundsX, boundsY, totalWidth, totalHeight);
    }

    [Fact]
    public void TextBounds_Overlaps_DetectsCollisionCorrectly()
    {
        // Arrange
        var bounds1 = new TextBounds(0, 0, 10, 10);
        var bounds2 = new TextBounds(5, 5, 10, 10);
        var bounds3 = new TextBounds(20, 20, 10, 10);

        // Assert
        Assert.True(bounds1.Overlaps(bounds2), "Overlapping bounds should be detected");
        Assert.True(bounds2.Overlaps(bounds1), "Overlap detection should be symmetric");
        Assert.False(bounds1.Overlaps(bounds3), "Non-overlapping bounds should not be detected");
        Assert.False(bounds3.Overlaps(bounds1), "Non-overlap detection should be symmetric");
    }

    [Fact]
    public void TextBounds_ActualCmfbPositions_DetectsOverlap()
    {
        // These are the ACTUAL positions from OTA5TFullyDiff render output:
        // R_CMFB_P: x=67.5, y=110.5, text-anchor="middle"
        // R_CMFB_N: x=140.5, y=106.5, text-anchor="end"

        var rPPlacement = new LabelPlacement(
            "R_CMFB_P",
            DeviceLabelX: 67.5,
            DeviceLabelY: 110.5,
            ParamLabelX: 67.5,
            ParamLabelY: 122.1,
            Direction: LabelDirection.S,
            TextAnchor: "middle"
        );

        var rNPlacement = new LabelPlacement(
            "R_CMFB_N",
            DeviceLabelX: 140.5,
            DeviceLabelY: 106.5,
            ParamLabelX: 140.5,
            ParamLabelY: 118.1,
            Direction: LabelDirection.W,
            TextAnchor: "end"
        );

        var rPBounds = ComputeLabelBounds(rPPlacement, "R_CMFB_P", "R=500k");
        var rNBounds = ComputeLabelBounds(rNPlacement, "R_CMFB_N", "R=500k");

        // These SHOULD overlap based on our manual calculation:
        // R_CMFB_P with middle anchor at x=67.5, width~54: bounds X from 40.5 to 94.5
        // R_CMFB_N with end anchor at x=140.5, width~54: bounds X from 86.5 to 140.5
        // Overlap in X: 86.5 to 94.5 (8px)

        Assert.True(
            rPBounds.Overlaps(rNBounds),
            $"These actual positions SHOULD overlap! "
                + $"R_CMFB_P bounds: X={rPBounds.X:F1} to {rPBounds.Right:F1}, Y={rPBounds.Y:F1} to {rPBounds.Bottom:F1}. "
                + $"R_CMFB_N bounds: X={rNBounds.X:F1} to {rNBounds.Right:F1}, Y={rNBounds.Y:F1} to {rNBounds.Bottom:F1}"
        );
    }

    [Fact]
    public void TextBounds_EdgeTouch_DoesNotOverlap()
    {
        // Arrange: Two boxes that touch at edges but don't overlap
        var bounds1 = new TextBounds(0, 0, 10, 10);
        var bounds2 = new TextBounds(10, 0, 10, 10);

        // Assert: Edge-touching boxes should not be considered overlapping
        Assert.False(bounds1.Overlaps(bounds2), "Edge-touching bounds should not overlap");
    }

    [Fact]
    public void PlaceLabels_WithMultipleBiasPorts_DoesNotOverlapPorts()
    {
        // Arrange: Fully differential OTA with multiple bias ports
        var circuit = CreateFullyDiffOtaWithTwoBiasPorts();
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);

        var placement = new CoarseGridResult
        {
            RowCount = 3,
            ColumnCount = 5,
            SymmetryAxis = 2,
            HorizontalPassiveIds = new HashSet<string>(),
            DevicePlacements = new Dictionary<string, GridCell>
            {
                ["M_INP"] = new GridCell(1, 0, false),
                ["M_INN"] = new GridCell(1, 4, true),
                ["M_TAIL"] = new GridCell(2, 2, false),
            },
        };

        var canvasHeight =
            DeviceGeometry.RailMargin
            + placement.RowCount * DeviceGeometry.CellHeight
            + DeviceGeometry.RailMargin;
        var canvasWidth = placement.ColumnCount * DeviceGeometry.CellWidth;

        // Create terminal positions for the bias ports at specific Y positions
        var terminalPositions = new List<TerminalPosition>
        {
            new("PORT_VBIAS1", "P", 0, 90),
            new("PORT_VBIAS2", "P", 0, 140),
        };

        var routing = new RoutingResult
        {
            Segments = new List<WireSegment>(),
            Junctions = new List<GridPoint>(),
            SegmentsByNet = new Dictionary<string, IReadOnlyList<WireSegment>>(),
            CanvasWidth = canvasWidth,
            CanvasHeight = canvasHeight,
            TerminalPositions = terminalPositions,
        };

        var placer = new LabelPlacer();

        // Act
        var placements = placer.PlaceLabels(placement, routing, graph, StyleSheet.Default);

        // Assert: M_INP label should not overlap with VBIAS2 port area
        var mInpPlacement = placements.Single(p => p.DeviceId == "M_INP");

        // VBIAS2 port is at Y=140, port height is 5, so port area is roughly Y=137.5 to Y=142.5
        // The label for M_INP should either:
        // 1. Be above the port area (label bottom < 135)
        // 2. Be below the port area (label top > 145)
        // 3. Be placed in a different direction (E, SE) that doesn't overlap

        // For left-side devices, SW/W/S are preferred, but if port is in the way,
        // it should choose a different direction
        var labelY = mInpPlacement.DeviceLabelY;

        // If the label is in SW direction (which would overlap VBIAS2),
        // the algorithm should have chosen a different direction
        var doesNotOverlapPort =
            mInpPlacement.Direction == LabelDirection.S
            || mInpPlacement.Direction == LabelDirection.SE
            || mInpPlacement.Direction == LabelDirection.E
            || mInpPlacement.Direction == LabelDirection.NE
            || mInpPlacement.Direction == LabelDirection.N
            || mInpPlacement.Direction == LabelDirection.NW
            || labelY < 130
            || labelY > 150;

        Assert.True(
            doesNotOverlapPort,
            $"M_INP label at Y={labelY} with direction {mInpPlacement.Direction} may overlap VBIAS2 port at Y=140"
        );
    }

    private static Circuit CreateSimpleCircuit()
    {
        return new Circuit
        {
            Name = "simple",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new() { Name = "IN", Type = "signal" },
                new() { Name = "OUT", Type = "signal" },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        Id = "M1",
                        DeviceType = "nmos",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "OUT",
                            ["G"] = "IN",
                            ["S"] = "GND",
                        },
                        Params = new Dictionary<string, string> { ["W"] = "1u", ["L"] = "100n" },
                    },
                },
            },
        };
    }

    private static Circuit CreateSimpleCircuitWithBottomDevice()
    {
        return new Circuit
        {
            Name = "bottom_device",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new() { Name = "BIAS", Type = "bias" },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        Id = "M_TAIL",
                        DeviceType = "nmos",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "tail_node",
                            ["G"] = "BIAS",
                            ["S"] = "GND",
                        },
                        Params = new Dictionary<string, string> { ["W"] = "10u", ["L"] = "500n" },
                    },
                },
                Nets = new List<NetDeclaration>
                {
                    new() { Id = "tail_node", Domain = "signal" },
                },
            },
        };
    }

    private static Circuit CreateCircuitWithTopDevice()
    {
        return new Circuit
        {
            Name = "top_device",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new() { Name = "OUT", Type = "signal" },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        Id = "M_LOAD",
                        DeviceType = "pmos",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "OUT",
                            ["G"] = "OUT",
                            ["S"] = "VDD",
                        },
                        Params = new Dictionary<string, string> { ["W"] = "5u", ["L"] = "200n" },
                    },
                },
            },
        };
    }

    private static Circuit CreateCircuitWithTwoDevices()
    {
        return new Circuit
        {
            Name = "two_devices",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new() { Name = "IN_P", Type = "signal" },
                new() { Name = "IN_N", Type = "signal" },
                new() { Name = "OUT", Type = "signal" },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        Id = "M1",
                        DeviceType = "nmos",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "OUT",
                            ["G"] = "IN_P",
                            ["S"] = "tail",
                        },
                        Params = new Dictionary<string, string> { ["W"] = "2u", ["L"] = "100n" },
                    },
                    new()
                    {
                        Id = "M2",
                        DeviceType = "nmos",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "OUT",
                            ["G"] = "IN_N",
                            ["S"] = "tail",
                        },
                        Params = new Dictionary<string, string> { ["W"] = "2u", ["L"] = "100n" },
                    },
                },
                Nets = new List<NetDeclaration>
                {
                    new() { Id = "tail", Domain = "signal" },
                },
            },
        };
    }

    private static Circuit CreateFullyDiffOtaWithTwoBiasPorts()
    {
        return new Circuit
        {
            Name = "fully_diff_ota",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new() { Name = "IN_P", Type = "signal" },
                new() { Name = "IN_N", Type = "signal" },
                new() { Name = "VBIAS1", Type = "bias" },
                new() { Name = "VBIAS2", Type = "bias" },
                new() { Name = "OUT_P", Type = "signal" },
                new() { Name = "OUT_N", Type = "signal" },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        Id = "M_INP",
                        DeviceType = "nmos",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "out_p_int",
                            ["G"] = "IN_P",
                            ["S"] = "tail",
                        },
                        Params = new Dictionary<string, string> { ["W"] = "2u", ["L"] = "180n" },
                    },
                    new()
                    {
                        Id = "M_INN",
                        DeviceType = "nmos",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "out_n_int",
                            ["G"] = "IN_N",
                            ["S"] = "tail",
                        },
                        Params = new Dictionary<string, string> { ["W"] = "2u", ["L"] = "180n" },
                    },
                    new()
                    {
                        Id = "M_TAIL",
                        DeviceType = "nmos",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "tail",
                            ["G"] = "VBIAS2",
                            ["S"] = "GND",
                        },
                        Params = new Dictionary<string, string> { ["W"] = "4u", ["L"] = "180n" },
                    },
                },
                Nets = new List<NetDeclaration>
                {
                    new() { Id = "tail", Domain = "signal" },
                    new() { Id = "out_p_int", Domain = "signal" },
                    new() { Id = "out_n_int", Domain = "signal" },
                },
            },
        };
    }

    private static Circuit CreateCircuitWithCmfbResistors()
    {
        return new Circuit
        {
            Name = "cmfb_resistors",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new() { Name = "OUT_P", Type = "signal" },
                new() { Name = "OUT_N", Type = "signal" },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        Id = "R_CMFB_P",
                        DeviceType = "resistor",
                        Bindings = new Dictionary<string, string>
                        {
                            ["P"] = "OUT_P",
                            ["N"] = "vcm_sense",
                        },
                        Params = new Dictionary<string, string> { ["R"] = "500k" },
                    },
                    new()
                    {
                        Id = "R_CMFB_N",
                        DeviceType = "resistor",
                        Bindings = new Dictionary<string, string>
                        {
                            ["P"] = "OUT_N",
                            ["N"] = "vcm_sense",
                        },
                        Params = new Dictionary<string, string> { ["R"] = "500k" },
                    },
                },
                Nets = new List<NetDeclaration>
                {
                    new() { Id = "vcm_sense", Domain = "signal" },
                },
            },
        };
    }
}
