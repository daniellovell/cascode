namespace Cascode.Render.Svg;

using Cascode.Language;
using Cascode.Render.Layout;
using Cascode.Render.Placement;

/// <summary>
/// Computes device placement and bounding boxes used for SVG emission and label collision detection.
/// </summary>
internal static class DevicePlacementHelper
{
    /// <summary>
    /// Axis-aligned device bounds at the computed schematic origin, plus the chosen device orientation.
    /// </summary>
    internal sealed record DevicePlacementInfo(
        double X,
        double Y,
        double Width,
        double Height,
        DeviceOrientation Orientation
    );

    /// <summary>
    /// Tries to compute the device origin and bounding box, matching the rendering placement logic.
    /// </summary>
    internal static bool TryGetDevicePlacement(
        CoarseGridResult placement,
        string deviceId,
        DeviceDeclaration device,
        out DevicePlacementInfo info
    )
    {
        info = null!;

        if (!placement.DevicePlacements.TryGetValue(deviceId, out var cell))
        {
            return false;
        }

        var deviceType = DeviceTypeHelper.Normalize(device.DeviceType);
        var orientation = cell.MirrorX ? DeviceOrientation.GateRight : DeviceOrientation.GateLeft;

        double x;
        double y;

        if (DeviceTypeHelper.IsPassive(deviceType))
        {
            var isHorizontalPassive = placement.HorizontalPassiveIds.Contains(deviceId);
            var isLeftOfAxis = cell.Column < placement.SymmetryAxis;

            if (isHorizontalPassive)
            {
                var p = DeviceGeometry.GetHorizontalPassivePlacement(
                    cell.Row,
                    cell.Column,
                    placement.ColumnCount,
                    isLeftOfAxis
                );
                x = p.X;
                y = p.Y;
                orientation = DeviceOrientation.Horizontal;
            }
            else
            {
                var p = DeviceGeometry.GetPassivePlacement(cell.Row, cell.Column);
                x = p.X;
                y = p.Y;
                orientation = DeviceOrientation.Vertical;
            }
        }
        else
        {
            var p = DeviceGeometry.GetMosfetPlacement(cell.Row, cell.Column, cell.MirrorX);
            x = p.X;
            y = p.Y;
        }

        var (w, h) = GetDeviceDimensions(deviceType);
        if (
            orientation
            is DeviceOrientation.Vertical
                or DeviceOrientation.GateUp
                or DeviceOrientation.GateDown
        )
        {
            (w, h) = (h, w);
        }

        info = new DevicePlacementInfo(x, y, w, h, orientation);
        return true;
    }

    /// <summary>
    /// Returns the un-rotated symbol dimensions for a device type.
    /// </summary>
    internal static (double Width, double Height) GetDeviceDimensions(string deviceType)
    {
        var type = DeviceTypeHelper.Normalize(deviceType);
        if (DeviceTypeHelper.IsMosfet(type))
        {
            return (DeviceGeometry.MosfetWidth, DeviceGeometry.MosfetHeight);
        }
        return (DeviceGeometry.PassiveWidth, DeviceGeometry.PassiveHeight);
    }
}
