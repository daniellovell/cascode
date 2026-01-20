using Cascode.Render.Layout;
using Cascode.Render.Routing;
using Cascode.Render.Svg;

namespace Cascode.Render.Tests;

public class LabelPlacerTests
{
    [Fact]
    public void PlaceLabels_BottomDevice_DoesNotOverlapGroundRail()
    {
        var result = new LabelPlacerTestContextBuilder()
            .WithCircuit(TestCircuits.BottomDevice())
            .WithGrid(rows: 2, cols: 3, axis: 1)
            .WithDevice("M_TAIL", row: 1, col: 1)
            .Build();

        var tailPlacement = result.Labels.Single(p => p.DeviceId == "M_TAIL");
        var groundRailY = result.CanvasHeight - DeviceGeometry.RailMargin / 2.0;

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

    [Theory]
    [InlineData(1, 1, 3, 1, LabelDirection.S, "middle")] // center device
    [InlineData(1, 0, 5, 2, LabelDirection.SW, "end")] // left of axis
    [InlineData(1, 4, 5, 2, LabelDirection.SE, "start")] // right of axis
    public void PlaceLabels_DirectionPreference_BasedOnPosition(
        int row,
        int col,
        int cols,
        int axis,
        LabelDirection expectedDirection,
        string expectedAnchor
    )
    {
        var result = new LabelPlacerTestContextBuilder()
            .WithCircuit(TestCircuits.SimpleCircuit())
            .WithGrid(rows: 3, cols: cols, axis: axis)
            .WithDevice("M1", row, col)
            .Build();

        var m1 = result.Labels.Single(p => p.DeviceId == "M1");
        Assert.Equal(expectedDirection, m1.Direction);
        Assert.Equal(expectedAnchor, m1.TextAnchor);
    }

    [Fact]
    public void PlaceLabels_TopDevice_DoesNotOverlapSupplyRail()
    {
        var result = new LabelPlacerTestContextBuilder()
            .WithCircuit(TestCircuits.TopDevice())
            .WithGrid(rows: 2, cols: 3, axis: 1)
            .WithDevice("M_LOAD", row: 0, col: 1)
            .Build();

        var loadPlacement = result.Labels.Single(p => p.DeviceId == "M_LOAD");
        var supplyRailY = DeviceGeometry.RailMargin / 2.0;

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
        var result = new LabelPlacerTestContextBuilder()
            .WithCircuit(TestCircuits.TwoDevices())
            .WithGrid(rows: 3, cols: 3, axis: 1)
            .WithDevice("M1", row: 1, col: 0)
            .WithDevice("M2", row: 1, col: 2, mirror: true)
            .Build();

        Assert.Equal(2, result.Labels.Count);

        var m1 = result.Labels.Single(p => p.DeviceId == "M1");
        var m2 = result.Labels.Single(p => p.DeviceId == "M2");

        var m1Bounds = TextBoundsTests.ComputeLabelBounds(m1, "M1", "W=2u L=100n");
        var m2Bounds = TextBoundsTests.ComputeLabelBounds(m2, "M2", "W=2u L=100n");

        Assert.False(
            m1Bounds.Overlaps(m2Bounds),
            $"Labels overlap: M1 bounds ({m1Bounds.X:F1},{m1Bounds.Y:F1},{m1Bounds.Width:F1},{m1Bounds.Height:F1}) "
                + $"M2 bounds ({m2Bounds.X:F1},{m2Bounds.Y:F1},{m2Bounds.Width:F1},{m2Bounds.Height:F1})"
        );
    }

    [Fact]
    public void PlaceLabels_SymmetricHorizontalPassives_DoNotOverlap()
    {
        var result = new LabelPlacerTestContextBuilder()
            .WithCircuit(TestCircuits.CmfbResistors())
            .WithGrid(rows: 3, cols: 5, axis: 2)
            .WithDevice("R_CMFB_P", row: 1, col: 1)
            .WithDevice("R_CMFB_N", row: 1, col: 3, mirror: true)
            .WithHorizontalPassive("R_CMFB_P")
            .WithHorizontalPassive("R_CMFB_N")
            .Build();

        Assert.Equal(2, result.Labels.Count);

        var rP = result.Labels.Single(p => p.DeviceId == "R_CMFB_P");
        var rN = result.Labels.Single(p => p.DeviceId == "R_CMFB_N");

        var rPBounds = TextBoundsTests.ComputeLabelBounds(rP, "R_CMFB_P", "R=500k");
        var rNBounds = TextBoundsTests.ComputeLabelBounds(rN, "R_CMFB_N", "R=500k");

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
        var result = new LabelPlacerTestContextBuilder()
            .WithCircuit(TestCircuits.CmfbResistors())
            .WithGrid(rows: 3, cols: 4, axis: 1)
            .WithDevice("R_CMFB_P", row: 1, col: 1)
            .WithDevice("R_CMFB_N", row: 1, col: 2, mirror: true)
            .WithHorizontalPassive("R_CMFB_P")
            .WithHorizontalPassive("R_CMFB_N")
            .Build();

        Assert.Equal(2, result.Labels.Count);

        var rP = result.Labels.Single(p => p.DeviceId == "R_CMFB_P");
        var rN = result.Labels.Single(p => p.DeviceId == "R_CMFB_N");

        var rPBounds = TextBoundsTests.ComputeLabelBounds(rP, "R_CMFB_P", "R=500k");
        var rNBounds = TextBoundsTests.ComputeLabelBounds(rN, "R_CMFB_N", "R=500k");

        Assert.False(
            rPBounds.Overlaps(rNBounds),
            $"Close CMFB labels overlap: R_CMFB_P at ({rPBounds.X:F1},{rPBounds.Y:F1}) size ({rPBounds.Width:F1},{rPBounds.Height:F1}) "
                + $"direction={rP.Direction}, R_CMFB_N at ({rNBounds.X:F1},{rNBounds.Y:F1}) size ({rNBounds.Width:F1},{rNBounds.Height:F1}) "
                + $"direction={rN.Direction}"
        );
    }

    [Fact]
    public void PlaceLabels_WithMultipleBiasPorts_DoesNotOverlapPorts()
    {
        var result = new LabelPlacerTestContextBuilder()
            .WithCircuit(TestCircuits.FullyDiffOtaWithTwoBiasPorts())
            .WithGrid(rows: 3, cols: 5, axis: 2)
            .WithDevice("M_INP", row: 1, col: 0)
            .WithDevice("M_INN", row: 1, col: 4, mirror: true)
            .WithDevice("M_TAIL", row: 2, col: 2)
            .WithTerminals(
                new TerminalPosition("PORT_VBIAS1", "P", 0, 90),
                new TerminalPosition("PORT_VBIAS2", "P", 0, 140)
            )
            .Build();

        var mInpPlacement = result.Labels.Single(p => p.DeviceId == "M_INP");
        var labelY = mInpPlacement.DeviceLabelY;

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

    [Fact]
    public void PlaceLabels_WithMinimalStyle_UsesSmallerFontForBounds()
    {
        var defaultResult = new LabelPlacerTestContextBuilder()
            .WithCircuit(TestCircuits.SimpleCircuit())
            .WithGrid(rows: 3, cols: 3, axis: 1)
            .WithDevice("M1", row: 1, col: 1)
            .WithStyle(StyleSheet.Default)
            .Build();

        var minimalResult = new LabelPlacerTestContextBuilder()
            .WithCircuit(TestCircuits.SimpleCircuit())
            .WithGrid(rows: 3, cols: 3, axis: 1)
            .WithDevice("M1", row: 1, col: 1)
            .WithStyle(StyleSheet.Minimal)
            .Build();

        var defaultM1 = defaultResult.Labels.Single(p => p.DeviceId == "M1");
        var minimalM1 = minimalResult.Labels.Single(p => p.DeviceId == "M1");

        Assert.Equal(LabelDirection.S, defaultM1.Direction);
        Assert.Equal(LabelDirection.S, minimalM1.Direction);

        var defaultSpacing = defaultM1.ParamLabelY - defaultM1.DeviceLabelY;
        var minimalSpacing = minimalM1.ParamLabelY - minimalM1.DeviceLabelY;

        Assert.True(
            minimalSpacing < defaultSpacing,
            $"Minimal style spacing ({minimalSpacing:F2}) should be less than default spacing ({defaultSpacing:F2})"
        );
    }
}
