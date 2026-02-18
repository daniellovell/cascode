namespace Cascode.Render.Svg;

using System.Text;
using Cascode.Language;
using Cascode.Render.Analysis;
using Cascode.Render.Layout;
using Cascode.Render.Placement;
using Cascode.Render.Routing;
using static Cascode.Render.Svg.SvgFormat;

/// <summary>
/// Options for SVG rendering.
/// </summary>
public sealed class RenderOptions
{
    public bool ShowNetLabels { get; init; }
    public bool ShowDeviceLabels { get; init; } = true;
    public bool ShowParamLabels { get; init; } = true;
    public string? Title { get; init; }
    public int? ExplicitWidth { get; init; }
    public int? ExplicitHeight { get; init; }
}

/// <summary>
/// Renders circuit schematics to SVG format.
/// </summary>
public sealed class SvgRenderer
{
    private const double Margin = 40;

    /// <summary>
    /// Renders the circuit to SVG using the placement and routing system.
    /// </summary>
    public string Render(
        CoarseGridResult placement,
        RoutingResult routing,
        CircuitGraph graph,
        StyleSheet style,
        RenderOptions options
    )
    {
        var leftPorts = graph.InputPorts.Concat(graph.BiasPorts).ToList();
        // Scale port margins with canvas width - wider circuits get more breathing room
        var basePortMargin = Math.Max(40, routing.CanvasWidth / 5);
        var extraLeftMargin = leftPorts.Count > 0 ? basePortMargin : 0;
        var extraRightMargin = graph.OutputPorts.Count > 0 ? (int)(basePortMargin * 0.75) : 0;
        var width =
            options.ExplicitWidth
            ?? routing.CanvasWidth + (int)(2 * Margin) + extraLeftMargin + extraRightMargin;
        var height = options.ExplicitHeight ?? routing.CanvasHeight + (int)(2 * Margin);

        var sb = new StringBuilder();

        sb.AppendLine(
            $@"<svg xmlns=""http://www.w3.org/2000/svg"" viewBox=""0 0 {width} {height}"" width=""{width}"" height=""{height}"">"
        );

        sb.AppendLine("<style>");
        sb.AppendLine(style.ToCss());
        sb.AppendLine("</style>");

        if (!string.IsNullOrEmpty(options.Title))
        {
            sb.AppendLine($@"<title>{EscapeXml(options.Title)}</title>");
        }

        if (style.BackgroundColor != "transparent")
        {
            sb.AppendLine(
                $@"<rect width=""{width}"" height=""{height}"" fill=""{style.BackgroundColor}"" />"
            );
        }

        sb.AppendLine($@"<g transform=""translate({Margin + extraLeftMargin}, {Margin})"">");

        var labelPlacer = new LabelPlacer();
        var labelPlacements = labelPlacer.PlaceLabels(placement, routing, graph, style);

        RenderRails(sb, routing, graph);
        RenderWires(sb, routing);
        RenderJunctions(sb, routing, style);
        RenderDevices(sb, placement, graph, options, labelPlacements);
        InlineBoundaryRenderer.Render(sb, placement, graph);
        RenderPortLabels(sb, placement, routing, graph);

        if (options.ShowNetLabels)
        {
            RenderNetLabels(sb, routing);
        }

        sb.AppendLine("</g>");
        sb.AppendLine("</svg>");

        return sb.ToString();
    }

    private static void RenderRails(StringBuilder sb, RoutingResult routing, CircuitGraph graph)
    {
        sb.AppendLine(@"<g id=""rails"">");

        if (graph.Supplies.Count > 0)
        {
            var supply = graph.Supplies.First();
            sb.AppendLine(
                $@"<line class=""rail"" data-net=""{EscapeXml(supply)}"" x1=""0"" y1=""{DeviceGeometry.RailMargin / 2}"" x2=""{routing.CanvasWidth}"" y2=""{DeviceGeometry.RailMargin / 2}"" />"
            );
            sb.AppendLine(
                $@"<text class=""port-label"" x=""0"" y=""{DeviceGeometry.RailMargin / 2 - 5}"">{EscapeXml(supply)}</text>"
            );
        }

        if (graph.Grounds.Count > 0)
        {
            var ground = graph.Grounds.First();
            var gndY = routing.CanvasHeight - DeviceGeometry.RailMargin / 2;
            sb.AppendLine(
                $@"<line class=""rail"" data-net=""{EscapeXml(ground)}"" x1=""0"" y1=""{gndY}"" x2=""{routing.CanvasWidth}"" y2=""{gndY}"" />"
            );
            sb.AppendLine(
                $@"<text class=""port-label"" x=""0"" y=""{gndY + 15}"">{EscapeXml(ground)}</text>"
            );
        }

        sb.AppendLine("</g>");
    }

    private static void RenderWires(StringBuilder sb, RoutingResult routing)
    {
        sb.AppendLine(@"<g id=""wires"">");

        foreach (var (netName, segments) in routing.SegmentsByNet)
        {
            sb.AppendLine($@"<g class=""net"" data-net=""{EscapeXml(netName)}"">");
            foreach (var seg in segments)
            {
                sb.AppendLine(
                    $@"<line class=""wire"" x1=""{seg.From.X}"" y1=""{seg.From.Y}"" x2=""{seg.To.X}"" y2=""{seg.To.Y}"" />"
                );
            }
            sb.AppendLine("</g>");
        }

        sb.AppendLine("</g>");
    }

    private static void RenderJunctions(StringBuilder sb, RoutingResult routing, StyleSheet style)
    {
        sb.AppendLine(@"<g id=""junctions"">");

        foreach (var junction in routing.Junctions)
        {
            sb.AppendLine(
                $@"<circle class=""junction"" cx=""{junction.X}"" cy=""{junction.Y}"" r=""{style.JunctionRadius}"" />"
            );
        }

        sb.AppendLine("</g>");
    }

    private static void RenderDevices(
        StringBuilder sb,
        CoarseGridResult placement,
        CircuitGraph graph,
        RenderOptions options,
        IReadOnlyList<LabelPlacement> labelPlacements
    )
    {
        var labelLookup = labelPlacements.ToDictionary(p => p.DeviceId);

        sb.AppendLine(@"<g id=""devices"">");

        foreach (var (deviceId, cell) in placement.DevicePlacements)
        {
            if (!graph.Devices.TryGetValue(deviceId, out var device))
            {
                continue;
            }

            var deviceType = DeviceTypeHelper.Normalize(device.DeviceType);
            if (
                !DevicePlacementHelper.TryGetDevicePlacement(
                    placement,
                    deviceId,
                    device,
                    out var placementInfo
                )
            )
            {
                continue;
            }

            if (DeviceTypeHelper.IsInstanceBlock(deviceType))
            {
                RenderInstanceBlock(sb, deviceId, placementInfo);
                continue;
            }

            sb.AppendLine(
                $@"<g id=""{EscapeXml(deviceId)}"" class=""device {deviceType}"" data-device-id=""{EscapeXml(deviceId)}"" transform=""translate({F(placementInfo.X)}, {F(placementInfo.Y)})"">"
            );

            var symbolContent = GetSymbolContent(deviceType, placementInfo.Orientation);
            if (!string.IsNullOrEmpty(symbolContent))
            {
                sb.AppendLine(symbolContent);
            }
            else
            {
                var (w, h) = DevicePlacementHelper.GetDeviceDimensions(deviceType);
                sb.AppendLine(
                    $@"<rect width=""{F(w)}"" height=""{F(h)}"" fill=""none"" stroke=""currentColor"" />"
                );
            }

            sb.AppendLine("</g>");
        }

        sb.AppendLine("</g>");

        if (options.ShowDeviceLabels || options.ShowParamLabels)
        {
            RenderDeviceLabels(sb, placement, graph, options, labelLookup);
        }
    }

    private static void RenderDeviceLabels(
        StringBuilder sb,
        CoarseGridResult placement,
        CircuitGraph graph,
        RenderOptions options,
        Dictionary<string, LabelPlacement> labelLookup
    )
    {
        sb.AppendLine(@"<g id=""device-labels"">");

        var blockInfoLookup = graph.InstanceBlocks.ToDictionary(
            b => b.InstanceId,
            StringComparer.Ordinal
        );

        foreach (var (deviceId, _) in placement.DevicePlacements)
        {
            if (!graph.Devices.TryGetValue(deviceId, out var device))
            {
                continue;
            }

            if (!labelLookup.TryGetValue(deviceId, out var labelPlacement))
            {
                continue;
            }

            if (options.ShowDeviceLabels)
            {
                sb.AppendLine(
                    $@"<text class=""device-label"" x=""{F(labelPlacement.DeviceLabelX)}"" y=""{F(labelPlacement.DeviceLabelY)}"" text-anchor=""{labelPlacement.TextAnchor}"">{EscapeXml(deviceId)}</text>"
                );
            }

            if (options.ShowParamLabels)
            {
                var paramText = blockInfoLookup.TryGetValue(deviceId, out var blockInfo)
                    ? blockInfo.CircuitType
                    : DeviceParamFormatter.FormatParams(device);
                if (!string.IsNullOrEmpty(paramText))
                {
                    sb.AppendLine(
                        $@"<text class=""param-label"" x=""{F(labelPlacement.ParamLabelX)}"" y=""{F(labelPlacement.ParamLabelY)}"" text-anchor=""{labelPlacement.TextAnchor}"">{EscapeXml(paramText)}</text>"
                    );
                }
            }
        }

        sb.AppendLine("</g>");
    }

    private static void RenderPortLabels(
        StringBuilder sb,
        CoarseGridResult placement,
        RoutingResult routing,
        CircuitGraph graph
    )
    {
        sb.AppendLine(@"<g id=""ports"">");

        var symbol = SymbolLibrary.GetSymbolForDevice("port");

        // Build lookup of port terminal positions from routing
        // Port terminals are stored as "PORT_<name>" with terminal "P"
        var portPositions = routing
            .TerminalPositions.Where(t => t.DeviceId.StartsWith("PORT_", StringComparison.Ordinal))
            .ToDictionary(t => t.DeviceId.Substring(5), t => (X: t.X, Y: t.Y));

        // Input/bias ports on left side (X = 0)
        var leftPorts = graph.InputPorts.Concat(graph.BiasPorts).ToList();

        foreach (var portName in leftPorts)
        {
            var x = -DeviceGeometry.PortPinX;
            var pos = portPositions.TryGetValue(portName, out var p)
                ? p
                : (X: 0, Y: DeviceGeometry.RailMargin + 20);
            var originY = pos.Y - DeviceGeometry.PortPinY;

            sb.AppendLine(
                $@"<g class=""port"" data-port=""{EscapeXml(portName)}"" data-net=""{EscapeXml(portName)}"" transform=""translate({F(x)}, {F(originY)})"">"
            );
            sb.AppendLine(symbol);

            var labelX = -5.0;
            var labelY = DeviceGeometry.PortHeight / 2 + 3;
            sb.AppendLine(
                $@"<text class=""port-label"" x=""{F(labelX)}"" y=""{F(labelY)}"" text-anchor=""end"">{EscapeXml(portName)}</text>"
            );

            sb.AppendLine("</g>");
        }

        // Output ports on right side (X = canvasWidth)
        var rightPorts = graph.OutputPorts.ToList();

        foreach (var portName in rightPorts)
        {
            var x = (double)routing.CanvasWidth;
            var pos = portPositions.TryGetValue(portName, out var p)
                ? p
                : (X: routing.CanvasWidth, Y: DeviceGeometry.RailMargin + 20);
            var originY = pos.Y - DeviceGeometry.PortPinY;

            sb.AppendLine(
                $@"<g class=""port"" data-port=""{EscapeXml(portName)}"" data-net=""{EscapeXml(portName)}"" transform=""translate({F(x)}, {F(originY)})"">"
            );
            sb.AppendLine(
                $@"<g transform=""translate({F(DeviceGeometry.PortPinX)}, 0) scale(-1, 1)"">{symbol}</g>"
            );

            var labelX = DeviceGeometry.PortWidth + 5;
            var labelY = DeviceGeometry.PortHeight / 2 + 3;
            sb.AppendLine(
                $@"<text class=""port-label"" x=""{F(labelX)}"" y=""{F(labelY)}"" text-anchor=""start"">{EscapeXml(portName)}</text>"
            );

            sb.AppendLine("</g>");
        }

        sb.AppendLine("</g>");
    }

    private static void RenderNetLabels(StringBuilder sb, RoutingResult routing)
    {
        sb.AppendLine(@"<g id=""net-labels"">");

        foreach (var (netName, segments) in routing.SegmentsByNet)
        {
            if (segments.Count == 0)
            {
                continue;
            }

            var firstSeg = segments[0];
            var midX = (firstSeg.From.X + firstSeg.To.X) / 2;
            var midY = (firstSeg.From.Y + firstSeg.To.Y) / 2;

            sb.AppendLine(
                $@"<text class=""param-label"" x=""{midX}"" y=""{midY - 5}"">{EscapeXml(netName)}</text>"
            );
        }

        sb.AppendLine("</g>");
    }

    private static void RenderInstanceBlock(
        StringBuilder sb,
        string deviceId,
        DevicePlacementHelper.DevicePlacementInfo placementInfo
    )
    {
        var w = placementInfo.Width;
        var h = placementInfo.Height;
        sb.AppendLine(
            $@"<g id=""{EscapeXml(deviceId)}"" class=""device instance"" data-device-id=""{EscapeXml(deviceId)}"" transform=""translate({F(placementInfo.X)}, {F(placementInfo.Y)})"">"
        );
        sb.AppendLine($@"<rect class=""block"" width=""{F(w)}"" height=""{F(h)}"" />");
        sb.AppendLine(
            $@"<text class=""block-label"" x=""{F(w / 2)}"" y=""{F(h / 2 + 3)}"" text-anchor=""middle"">{EscapeXml(deviceId)}</text>"
        );
        sb.AppendLine("</g>");
    }

    private static string GetSymbolContent(string deviceType, DeviceOrientation orientation)
    {
        var symbol = SymbolLibrary.GetSymbolForDevice(deviceType);
        if (string.IsNullOrEmpty(symbol))
        {
            return string.Empty;
        }

        var transform = GetOrientationTransform(deviceType, orientation);
        if (!string.IsNullOrEmpty(transform))
        {
            return $@"<g transform=""{transform}"">{symbol}</g>";
        }

        return symbol;
    }

    private static string GetOrientationTransform(string deviceType, DeviceOrientation orientation)
    {
        var (w, h) = DevicePlacementHelper.GetDeviceDimensions(deviceType);

        return orientation switch
        {
            DeviceOrientation.GateRight => $"translate({F(w)}, 0) scale(-1, 1)",
            DeviceOrientation.GateUp => $"translate(0, {F(w)}) rotate(-90)",
            DeviceOrientation.GateDown => $"translate({F(h)}, 0) rotate(90)",
            DeviceOrientation.Vertical => $"translate(0, {F(w)}) rotate(-90)",
            _ => string.Empty,
        };
    }
}
