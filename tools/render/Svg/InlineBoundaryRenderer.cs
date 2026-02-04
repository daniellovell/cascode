namespace Cascode.Render.Svg;

using System.Globalization;
using System.Text;
using Cascode.Language;
using Cascode.Render.Analysis;
using Cascode.Render.Layout;
using Cascode.Render.Placement;

internal static class InlineBoundaryRenderer
{
    public static void Render(StringBuilder sb, CoarseGridResult placement, CircuitGraph graph)
    {
        if (graph.InlineInstanceGroups.Count == 0)
        {
            return;
        }

        sb.AppendLine(@"<g id=""inline-instance-boundaries"">");

        foreach (
            var group in graph.InlineInstanceGroups.OrderBy(
                g => g.InstanceId,
                StringComparer.Ordinal
            )
        )
        {
            var bounds = GetGroupBounds(placement, graph, group.DeviceIds);
            if (bounds is null)
            {
                continue;
            }

            var label = string.IsNullOrWhiteSpace(group.CircuitType)
                ? group.InstanceId
                : $"{group.InstanceId}: {group.CircuitType}";
            sb.AppendLine(
                $@"<rect class=""inline-boundary"" x=""{F(bounds.Value.X)}"" y=""{F(bounds.Value.Y)}"" width=""{F(bounds.Value.Width)}"" height=""{F(bounds.Value.Height)}"" />"
            );
            sb.AppendLine(
                $@"<text class=""inline-boundary-label"" x=""{F(bounds.Value.X)}"" y=""{F(bounds.Value.Y - 6)}"">{EscapeXml(label)}</text>"
            );
        }

        sb.AppendLine("</g>");
    }

    private static (double X, double Y, double Width, double Height)? GetGroupBounds(
        CoarseGridResult placement,
        CircuitGraph graph,
        IReadOnlyList<string> deviceIds
    )
    {
        double? minX = null;
        double? minY = null;
        double? maxX = null;
        double? maxY = null;

        foreach (var deviceId in deviceIds)
        {
            if (!graph.Devices.TryGetValue(deviceId, out var device))
            {
                continue;
            }

            if (!TryGetDevicePlacement(placement, deviceId, device, out var info))
            {
                continue;
            }

            minX = minX.HasValue ? Math.Min(minX.Value, info.X) : info.X;
            minY = minY.HasValue ? Math.Min(minY.Value, info.Y) : info.Y;
            maxX = maxX.HasValue ? Math.Max(maxX.Value, info.X + info.Width) : info.X + info.Width;
            maxY = maxY.HasValue
                ? Math.Max(maxY.Value, info.Y + info.Height)
                : info.Y + info.Height;
        }

        if (!minX.HasValue || !minY.HasValue || !maxX.HasValue || !maxY.HasValue)
        {
            return null;
        }

        const double padding = 12;
        return (
            X: minX.Value - padding,
            Y: minY.Value - padding,
            Width: (maxX.Value - minX.Value) + padding * 2,
            Height: (maxY.Value - minY.Value) + padding * 2
        );
    }

    private sealed record DevicePlacementInfo(
        double X,
        double Y,
        double Width,
        double Height,
        DeviceOrientation Orientation
    );

    private static bool TryGetDevicePlacement(
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

        var deviceType = device.DeviceType.ToLowerInvariant();
        var orientation = cell.MirrorX ? DeviceOrientation.GateRight : DeviceOrientation.GateLeft;

        double x;
        double y;

        if (deviceType is "resistor" or "capacitor" or "inductor")
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

    private static (double Width, double Height) GetDeviceDimensions(string deviceType)
    {
        var type = deviceType.ToLowerInvariant();
        if (type is "nmos" or "pmos" or "nfet" or "pfet")
        {
            return (DeviceGeometry.MosfetWidth, DeviceGeometry.MosfetHeight);
        }
        return (DeviceGeometry.PassiveWidth, DeviceGeometry.PassiveHeight);
    }

    private static string F(double value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string EscapeXml(string text)
    {
        return text.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}
