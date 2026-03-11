namespace Cascode.Render.Svg;

using System.Text;
using Cascode.Language;
using Cascode.Render.Analysis;
using Cascode.Render.Placement;
using static Cascode.Render.Svg.SvgFormat;

/// <summary>
/// Renders dashed boxes around inline instance groups for debugging and inspection.
/// </summary>
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

            if (
                !DevicePlacementHelper.TryGetDevicePlacement(
                    placement,
                    deviceId,
                    device,
                    out var info
                )
            )
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

        const double padding = 4;
        const double instanceClearance = 10;
        var groupSet = new HashSet<string>(deviceIds, StringComparer.Ordinal);
        var boundsMinX = minX.Value - padding;
        var boundsMinY = minY.Value - padding;
        var boundsMaxX = maxX.Value + padding;
        var boundsMaxY = maxY.Value + padding;

        foreach (var (deviceId, _) in placement.DevicePlacements)
        {
            if (
                groupSet.Contains(deviceId)
                || !graph.Devices.TryGetValue(deviceId, out var device)
                || !string.Equals(device.DeviceType, "instance", StringComparison.OrdinalIgnoreCase)
                || !DevicePlacementHelper.TryGetDevicePlacement(
                    placement,
                    deviceId,
                    device,
                    out var instancePlacement
                )
            )
            {
                continue;
            }

            var instanceMinX = instancePlacement.X;
            var instanceMaxX = instancePlacement.X + instancePlacement.Width;
            var instanceMinY = instancePlacement.Y;
            var instanceMaxY = instancePlacement.Y + instancePlacement.Height;
            var overlapsY = boundsMinY < instanceMaxY && boundsMaxY > instanceMinY;
            if (!overlapsY)
            {
                continue;
            }

            if (boundsMaxX <= instanceMinX)
            {
                boundsMaxX = Math.Min(boundsMaxX, instanceMinX - instanceClearance);
                continue;
            }

            if (boundsMinX >= instanceMaxX)
            {
                boundsMinX = Math.Max(boundsMinX, instanceMaxX + instanceClearance);
                continue;
            }

            var boundsCenterX = (boundsMinX + boundsMaxX) / 2.0;
            var instanceCenterX = (instanceMinX + instanceMaxX) / 2.0;
            if (boundsCenterX <= instanceCenterX)
            {
                boundsMaxX = Math.Min(boundsMaxX, instanceMinX - instanceClearance);
            }
            else
            {
                boundsMinX = Math.Max(boundsMinX, instanceMaxX + instanceClearance);
            }
        }

        if (boundsMaxX <= boundsMinX)
        {
            var midX = (boundsMinX + boundsMaxX) / 2.0;
            boundsMinX = midX - 0.5;
            boundsMaxX = midX + 0.5;
        }

        return (
            X: boundsMinX,
            Y: boundsMinY,
            Width: boundsMaxX - boundsMinX,
            Height: boundsMaxY - boundsMinY
        );
    }
}
