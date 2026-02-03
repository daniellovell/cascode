namespace Cascode.Render.Svg;

using Cascode.Render.Analysis;
using Cascode.Render.Layout;
using Cascode.Render.Placement;
using Cascode.Render.Routing;

/// <summary>
/// Direction for label placement relative to a device.
/// </summary>
public enum LabelDirection
{
    N,
    NE,
    E,
    SE,
    S,
    SW,
    W,
    NW,
}

/// <summary>
/// Axis-aligned bounding box for collision detection.
/// </summary>
public readonly record struct TextBounds(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public bool Overlaps(TextBounds other)
    {
        return X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;
    }
}

/// <summary>
/// Result of label placement for a single device.
/// </summary>
public sealed record LabelPlacement(
    string DeviceId,
    double DeviceLabelX,
    double DeviceLabelY,
    double ParamLabelX,
    double ParamLabelY,
    LabelDirection Direction,
    string TextAnchor
);

/// <summary>
/// Places device and parameter labels to minimize collisions with other elements.
/// </summary>
public sealed class LabelPlacer
{
    private StyleSheet _style = StyleSheet.Default;
    private double DeviceLabelFontSize => _style.FontSize;
    private double ParamLabelFontSize => _style.FontSize - 2;
    private const double CharWidthRatio = 0.7;
    private const double LineHeightRatio = 1.2;
    private const double LabelGap = 2.0;
    private const double LabelPadding = 4.0;

    private const int RailOverlapWeight = 10000;
    private const int LabelOverlapWeight = 5000;
    private const int PortOverlapWeight = 3000;
    private const int DeviceOverlapWeight = 2000;
    private const int WireOverlapWeight = 100;

    private const double RailCollisionOffset = 5;
    private const double RailCollisionHeight = 10;

    private static readonly LabelDirection[] RightSidePreference =
    [
        LabelDirection.SE,
        LabelDirection.E,
        LabelDirection.S,
        LabelDirection.NE,
        LabelDirection.SW,
        LabelDirection.N,
        LabelDirection.W,
        LabelDirection.NW,
    ];

    private static readonly LabelDirection[] LeftSidePreference =
    [
        LabelDirection.SW,
        LabelDirection.W,
        LabelDirection.S,
        LabelDirection.NW,
        LabelDirection.SE,
        LabelDirection.N,
        LabelDirection.E,
        LabelDirection.NE,
    ];

    private static readonly LabelDirection[] CenterPreference =
    [
        LabelDirection.S,
        LabelDirection.SE,
        LabelDirection.SW,
        LabelDirection.E,
        LabelDirection.W,
        LabelDirection.NE,
        LabelDirection.NW,
        LabelDirection.N,
    ];

    /// <summary>
    /// Computes optimal label positions for all devices.
    /// </summary>
    public IReadOnlyList<LabelPlacement> PlaceLabels(
        CoarseGridResult placement,
        RoutingResult routing,
        CircuitGraph graph,
        StyleSheet style
    )
    {
        _style = style;
        var obstacles = BuildObstacles(placement, routing, graph);
        var placements = new List<LabelPlacement>();
        var placedLabelBounds = new List<TextBounds>();

        foreach (var (deviceId, cell) in placement.DevicePlacements)
        {
            if (!graph.Devices.TryGetValue(deviceId, out var device))
            {
                continue;
            }

            var deviceType = device.DeviceType.ToLowerInvariant();
            var (deviceX, deviceY) = GetDevicePosition(deviceId, cell, deviceType, placement);
            var (deviceWidth, deviceHeight) = GetDeviceDimensions(deviceType);

            var deviceLabelText = deviceId;
            var paramText = FormatParams(device);
            var hasParams = !string.IsNullOrEmpty(paramText);

            var bestPlacement = FindBestDirection(
                deviceId,
                deviceX,
                deviceY,
                deviceWidth,
                deviceHeight,
                deviceLabelText,
                paramText,
                hasParams,
                obstacles,
                placedLabelBounds,
                cell.Column,
                placement.SymmetryAxis,
                placement.ColumnCount
            );

            placements.Add(bestPlacement);

            var combinedBounds = ComputeCombinedLabelBounds(
                bestPlacement,
                deviceLabelText,
                paramText,
                hasParams
            );
            placedLabelBounds.Add(combinedBounds);
        }

        return placements;
    }

    private LabelPlacement FindBestDirection(
        string deviceId,
        double deviceX,
        double deviceY,
        double deviceWidth,
        double deviceHeight,
        string deviceLabelText,
        string paramText,
        bool hasParams,
        ObstacleSet obstacles,
        List<TextBounds> placedLabels,
        int column,
        int symmetryAxis,
        int columnCount
    )
    {
        var bestScore = int.MaxValue;
        LabelPlacement? bestPlacement = null;

        var directionPreference = GetDirectionPreference(column, symmetryAxis, columnCount);

        for (var i = 0; i < directionPreference.Length; i++)
        {
            var direction = directionPreference[i];
            var candidate = ComputePlacement(
                deviceId,
                deviceX,
                deviceY,
                deviceWidth,
                deviceHeight,
                direction
            );

            var labelBounds = ComputeCombinedLabelBounds(
                candidate,
                deviceLabelText,
                paramText,
                hasParams
            );

            var score = ComputeScore(labelBounds, obstacles, placedLabels, i);

            if (score < bestScore)
            {
                bestScore = score;
                bestPlacement = candidate;
            }
        }

        return bestPlacement
            ?? ComputePlacement(
                deviceId,
                deviceX,
                deviceY,
                deviceWidth,
                deviceHeight,
                LabelDirection.SE
            );
    }

    private static LabelDirection[] GetDirectionPreference(
        int column,
        int symmetryAxis,
        int columnCount
    )
    {
        if (column < symmetryAxis)
        {
            return LeftSidePreference;
        }

        if (column > symmetryAxis)
        {
            return RightSidePreference;
        }

        return CenterPreference;
    }

    private LabelPlacement ComputePlacement(
        string deviceId,
        double deviceX,
        double deviceY,
        double deviceWidth,
        double deviceHeight,
        LabelDirection direction
    )
    {
        var centerX = deviceX + deviceWidth / 2;
        var centerY = deviceY + deviceHeight / 2;
        var labelHeight = DeviceLabelFontSize * LineHeightRatio;
        var paramHeight = ParamLabelFontSize * LineHeightRatio;

        double labelX,
            labelY,
            paramLabelY;
        string textAnchor;

        switch (direction)
        {
            case LabelDirection.N:
                labelX = centerX;
                labelY = deviceY - LabelPadding - paramHeight - LabelGap;
                paramLabelY = deviceY - LabelPadding;
                textAnchor = "middle";
                break;

            case LabelDirection.NE:
                labelX = deviceX + deviceWidth + LabelPadding;
                labelY = deviceY - paramHeight - LabelGap;
                paramLabelY = deviceY;
                textAnchor = "start";
                break;

            case LabelDirection.E:
                labelX = deviceX + deviceWidth + LabelPadding;
                labelY = centerY - (labelHeight + LabelGap + paramHeight) / 2 + labelHeight;
                paramLabelY = labelY + LabelGap + paramHeight;
                textAnchor = "start";
                break;

            case LabelDirection.SE:
                labelX = deviceX + deviceWidth + LabelPadding;
                labelY = deviceY + deviceHeight + labelHeight;
                paramLabelY = labelY + LabelGap + paramHeight;
                textAnchor = "start";
                break;

            case LabelDirection.S:
                labelX = centerX;
                labelY = deviceY + deviceHeight + LabelPadding + labelHeight;
                paramLabelY = labelY + LabelGap + paramHeight;
                textAnchor = "middle";
                break;

            case LabelDirection.SW:
                labelX = deviceX - LabelPadding;
                labelY = deviceY + deviceHeight + labelHeight;
                paramLabelY = labelY + LabelGap + paramHeight;
                textAnchor = "end";
                break;

            case LabelDirection.W:
                labelX = deviceX - LabelPadding;
                labelY = centerY - (labelHeight + LabelGap + paramHeight) / 2 + labelHeight;
                paramLabelY = labelY + LabelGap + paramHeight;
                textAnchor = "end";
                break;

            case LabelDirection.NW:
                labelX = deviceX - LabelPadding;
                labelY = deviceY - paramHeight - LabelGap;
                paramLabelY = deviceY;
                textAnchor = "end";
                break;

            default:
                labelX = centerX;
                labelY = deviceY + deviceHeight + LabelPadding + labelHeight;
                paramLabelY = labelY + LabelGap + paramHeight;
                textAnchor = "middle";
                break;
        }

        return new LabelPlacement(
            deviceId,
            labelX,
            labelY,
            labelX,
            paramLabelY,
            direction,
            textAnchor
        );
    }

    private TextBounds ComputeCombinedLabelBounds(
        LabelPlacement placement,
        string deviceLabelText,
        string paramText,
        bool hasParams
    )
    {
        var deviceLabelWidth = deviceLabelText.Length * DeviceLabelFontSize * CharWidthRatio;
        var deviceLabelHeight = DeviceLabelFontSize * LineHeightRatio;
        var paramLabelWidth = hasParams
            ? paramText.Length * ParamLabelFontSize * CharWidthRatio
            : 0;
        var paramLabelHeight = hasParams ? ParamLabelFontSize * LineHeightRatio : 0;

        var totalWidth = Math.Max(deviceLabelWidth, paramLabelWidth);
        var totalHeight = deviceLabelHeight + (hasParams ? LabelGap + paramLabelHeight : 0);

        double boundsX;
        switch (placement.TextAnchor)
        {
            case "middle":
                boundsX = placement.DeviceLabelX - totalWidth / 2;
                break;
            case "end":
                boundsX = placement.DeviceLabelX - totalWidth;
                break;
            default:
                boundsX = placement.DeviceLabelX;
                break;
        }

        var boundsY = placement.DeviceLabelY - deviceLabelHeight;

        return new TextBounds(boundsX, boundsY, totalWidth, totalHeight);
    }

    private int ComputeScore(
        TextBounds labelBounds,
        ObstacleSet obstacles,
        List<TextBounds> placedLabels,
        int directionIndex
    )
    {
        var score = 0;

        foreach (var rail in obstacles.Rails)
        {
            if (labelBounds.Overlaps(rail))
            {
                score += RailOverlapWeight;
            }
        }

        foreach (var device in obstacles.Devices)
        {
            if (labelBounds.Overlaps(device))
            {
                score += DeviceOverlapWeight;
            }
        }

        foreach (var wire in obstacles.Wires)
        {
            if (labelBounds.Overlaps(wire))
            {
                score += WireOverlapWeight;
            }
        }

        foreach (var placed in placedLabels)
        {
            if (labelBounds.Overlaps(placed))
            {
                score += LabelOverlapWeight;
            }
        }

        foreach (var port in obstacles.Ports)
        {
            if (labelBounds.Overlaps(port))
            {
                score += PortOverlapWeight;
            }
        }

        score += directionIndex;

        return score;
    }

    private ObstacleSet BuildObstacles(
        CoarseGridResult placement,
        RoutingResult routing,
        CircuitGraph graph
    )
    {
        var rails = new List<TextBounds>();
        var devices = new List<TextBounds>();
        var wires = new List<TextBounds>();
        var ports = new List<TextBounds>();

        if (graph.Supplies.Count > 0)
        {
            var railY = DeviceGeometry.RailMargin / 2.0;
            rails.Add(
                new TextBounds(
                    0,
                    railY - RailCollisionOffset,
                    routing.CanvasWidth,
                    RailCollisionHeight
                )
            );
        }

        if (graph.Grounds.Count > 0)
        {
            var railY = routing.CanvasHeight - DeviceGeometry.RailMargin / 2.0;
            rails.Add(
                new TextBounds(
                    0,
                    railY - RailCollisionOffset,
                    routing.CanvasWidth,
                    RailCollisionHeight
                )
            );
        }

        foreach (var (deviceId, cell) in placement.DevicePlacements)
        {
            if (!graph.Devices.TryGetValue(deviceId, out var device))
            {
                continue;
            }

            var deviceType = device.DeviceType.ToLowerInvariant();
            var (x, y) = GetDevicePosition(deviceId, cell, deviceType, placement);
            var (w, h) = GetDeviceDimensions(deviceType);
            devices.Add(new TextBounds(x, y, w, h));
        }

        foreach (var segment in routing.Segments)
        {
            var minX = Math.Min(segment.From.X, segment.To.X);
            var maxX = Math.Max(segment.From.X, segment.To.X);
            var minY = Math.Min(segment.From.Y, segment.To.Y);
            var maxY = Math.Max(segment.From.Y, segment.To.Y);

            var wireWidth = Math.Max(maxX - minX, 2);
            var wireHeight = Math.Max(maxY - minY, 2);

            wires.Add(new TextBounds(minX - 1, minY - 1, wireWidth + 2, wireHeight + 2));
        }

        AddPortObstacles(ports, routing, graph);

        return new ObstacleSet(rails, devices, wires, ports);
    }

    private void AddPortObstacles(List<TextBounds> ports, RoutingResult routing, CircuitGraph graph)
    {
        var portPositions = routing
            .TerminalPositions.Where(t => t.DeviceId.StartsWith("PORT_", StringComparison.Ordinal))
            .ToDictionary(t => t.DeviceId.Substring(5), t => (X: t.X, Y: t.Y));

        var leftPorts = graph.InputPorts.Concat(graph.BiasPorts).ToList();
        foreach (var portName in leftPorts)
        {
            var pos = portPositions.TryGetValue(portName, out var p)
                ? p
                : (X: 0, Y: DeviceGeometry.RailMargin + 20);

            var portX = -DeviceGeometry.PortPinX;
            var portY = pos.Y - DeviceGeometry.PortPinY;

            ports.Add(
                new TextBounds(portX, portY, DeviceGeometry.PortWidth, DeviceGeometry.PortHeight)
            );

            var labelWidth = portName.Length * DeviceLabelFontSize * CharWidthRatio;
            var labelX = portX - 5 - labelWidth;
            var labelY = portY;
            ports.Add(new TextBounds(labelX, labelY, labelWidth + 10, DeviceGeometry.PortHeight));
        }

        var rightPorts = graph.OutputPorts.ToList();
        foreach (var portName in rightPorts)
        {
            var pos = portPositions.TryGetValue(portName, out var p)
                ? p
                : (X: routing.CanvasWidth, Y: DeviceGeometry.RailMargin + 20);

            var portX = (double)routing.CanvasWidth;
            var portY = pos.Y - DeviceGeometry.PortPinY;

            ports.Add(
                new TextBounds(portX, portY, DeviceGeometry.PortWidth, DeviceGeometry.PortHeight)
            );

            var labelWidth = portName.Length * DeviceLabelFontSize * CharWidthRatio;
            var labelX = portX + DeviceGeometry.PortWidth + 5;
            var labelY = portY;
            ports.Add(new TextBounds(labelX, labelY, labelWidth, DeviceGeometry.PortHeight));
        }
    }

    private static (double X, double Y) GetDevicePosition(
        string deviceId,
        GridCell cell,
        string deviceType,
        CoarseGridResult placement
    )
    {
        if (deviceType is "resistor" or "capacitor")
        {
            var isHorizontalPassive = placement.HorizontalPassiveIds.Contains(deviceId);
            var isLeftOfAxis = cell.Column < placement.SymmetryAxis;

            if (isHorizontalPassive)
            {
                var placementInfo = DeviceGeometry.GetHorizontalPassivePlacement(
                    cell.Row,
                    cell.Column,
                    placement.ColumnCount,
                    isLeftOfAxis
                );
                return (placementInfo.X, placementInfo.Y);
            }

            var passivePlacement = DeviceGeometry.GetPassivePlacement(cell.Row, cell.Column);
            return (passivePlacement.X, passivePlacement.Y);
        }

        var mosfetPlacement = DeviceGeometry.GetMosfetPlacement(
            cell.Row,
            cell.Column,
            cell.MirrorX
        );
        return (mosfetPlacement.X, mosfetPlacement.Y);
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

    private static string FormatParams(Language.DeviceDeclaration device)
    {
        var parts = new List<string>();
        var type = device.DeviceType.ToLowerInvariant();

        if (type is "nmos" or "pmos" or "nfet" or "pfet")
        {
            if (device.Size is not null)
            {
                if (device.Size.Entries.TryGetValue("W", out var w))
                {
                    parts.Add($"W={w}");
                }
                if (device.Size.Entries.TryGetValue("L", out var l))
                {
                    parts.Add($"L={l}");
                }
                if (device.Size.Entries.TryGetValue("M", out var m) && m != "1")
                {
                    parts.Add($"M={m}");
                }
            }
            else if (!string.IsNullOrWhiteSpace(device.SizeName))
            {
                parts.Add($"size={device.SizeName}");
            }
        }
        else if (type == "resistor")
        {
            if (device.Size?.Entries.TryGetValue("R", out var r) == true)
            {
                parts.Add($"R={r}");
            }
            else if (!string.IsNullOrWhiteSpace(device.SizeName))
            {
                parts.Add($"size={device.SizeName}");
            }
        }
        else if (type == "capacitor")
        {
            if (device.Size?.Entries.TryGetValue("C", out var c) == true)
            {
                parts.Add($"C={c}");
            }
            else if (!string.IsNullOrWhiteSpace(device.SizeName))
            {
                parts.Add($"size={device.SizeName}");
            }
        }
        else if (type == "inductor")
        {
            if (device.Size?.Entries.TryGetValue("L", out var ind) == true)
            {
                parts.Add($"L={ind}");
            }
            else if (!string.IsNullOrWhiteSpace(device.SizeName))
            {
                parts.Add($"size={device.SizeName}");
            }
        }

        return string.Join(" ", parts);
    }

    private sealed record ObstacleSet(
        List<TextBounds> Rails,
        List<TextBounds> Devices,
        List<TextBounds> Wires,
        List<TextBounds> Ports
    );
}
