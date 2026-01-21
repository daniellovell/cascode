using Cascode.Render.Svg;

namespace Cascode.Render.Tests;

public class TextBoundsTests
{
    [Fact]
    public void Overlaps_DetectsCollisionCorrectly()
    {
        var bounds1 = new TextBounds(0, 0, 10, 10);
        var bounds2 = new TextBounds(5, 5, 10, 10);
        var bounds3 = new TextBounds(20, 20, 10, 10);

        Assert.True(bounds1.Overlaps(bounds2), "Overlapping bounds should be detected");
        Assert.True(bounds2.Overlaps(bounds1), "Overlap detection should be symmetric");
        Assert.False(bounds1.Overlaps(bounds3), "Non-overlapping bounds should not be detected");
        Assert.False(bounds3.Overlaps(bounds1), "Non-overlap detection should be symmetric");
    }

    [Fact]
    public void Overlaps_ActualCmfbPositions_DetectsOverlap()
    {
        // Actual positions from OTA5TFullyDiff render output:
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
    public void Overlaps_EdgeTouch_DoesNotOverlap()
    {
        var bounds1 = new TextBounds(0, 0, 10, 10);
        var bounds2 = new TextBounds(10, 0, 10, 10);

        Assert.False(bounds1.Overlaps(bounds2), "Edge-touching bounds should not overlap");
    }

    internal static TextBounds ComputeLabelBounds(
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
}
