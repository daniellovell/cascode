namespace Cascode.Render.Svg;

using System.Globalization;
using System.Text;
using Cascode.ACIR;
using Cascode.Render.Analysis;
using Cascode.Render.Layout;
using Cascode.Render.Placement;
using Cascode.Render.Routing;

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
    private const double MosfetWidth = 17;
    private const double MosfetHeight = 26;
    private const double PassiveWidth = 26;
    private const double PassiveHeight = 9;
    private const double Margin = 40;
    private const double PortSymbolWidth = 13.5;
    private const double PortSymbolHeight = 5.0;

    private const int CellWidth = 60;
    private const int CellHeight = 50;
    private const int RailMarginConst = 15;

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
        var width = options.ExplicitWidth ?? routing.CanvasWidth + (int)(2 * Margin);
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

        sb.AppendLine($@"<g transform=""translate({Margin}, {Margin})"">");

        RenderRails(sb, routing, graph);
        RenderWires(sb, routing);
        RenderJunctions(sb, routing, style);
        RenderDevices(sb, placement, graph, options);
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
                $@"<line class=""rail"" data-net=""{EscapeXml(supply)}"" x1=""0"" y1=""{RailMarginConst / 2}"" x2=""{routing.CanvasWidth}"" y2=""{RailMarginConst / 2}"" />"
            );
            sb.AppendLine(
                $@"<text class=""port-label"" x=""0"" y=""{RailMarginConst / 2 - 5}"">{EscapeXml(supply)}</text>"
            );
        }

        if (graph.Grounds.Count > 0)
        {
            var ground = graph.Grounds.First();
            var gndY = routing.CanvasHeight - RailMarginConst / 2;
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
        RenderOptions options
    )
    {
        sb.AppendLine(@"<g id=""devices"">");

        foreach (var (deviceId, cell) in placement.DevicePlacements)
        {
            if (!graph.Devices.TryGetValue(deviceId, out var device))
            {
                continue;
            }

            var deviceType = device.DeviceType.ToLowerInvariant();
            var orientation = cell.MirrorX
                ? DeviceOrientation.GateRight
                : DeviceOrientation.GateLeft;

            double x,
                y;
            // Compute the vertical axis X (where drain/source/passive terminals connect)
            // This must align with the routing grid (pitch 10)
            var rawAxisX = cell.Column * CellWidth + CellWidth / 2 + (int)(MosfetWidth / 2.0);
            var verticalAxisX = SnapToRoutingGrid(rawAxisX);

            if (deviceType is "resistor" or "capacitor")
            {
                // After rotation, passive terminal axis is at x + PassiveHeight/2
                x = verticalAxisX - PassiveHeight / 2.0;
                y = cell.Row * CellHeight + RailMarginConst + CellHeight / 2 - PassiveWidth / 2;
                orientation = DeviceOrientation.Vertical;
            }
            else
            {
                // For non-mirrored MOSFET, drain/source is at x + 16.5
                // For mirrored MOSFET, drain/source is at x + 0.5 (MosfetWidth - 16.5)
                var drainOffset = cell.MirrorX ? 0.5 : 16.5;
                x = verticalAxisX - drainOffset;
                y = cell.Row * CellHeight + RailMarginConst + CellHeight / 2 - MosfetHeight / 2;
            }

            sb.AppendLine(
                $@"<g id=""{EscapeXml(deviceId)}"" class=""device {deviceType}"" data-device-id=""{EscapeXml(deviceId)}"" transform=""translate({F(x)}, {F(y)})"">"
            );

            var symbolContent = GetSymbolContent(deviceType, orientation);
            if (!string.IsNullOrEmpty(symbolContent))
            {
                sb.AppendLine(symbolContent);
            }
            else
            {
                var (w, h) = GetDeviceDimensions(deviceType);
                sb.AppendLine(
                    $@"<rect width=""{F(w)}"" height=""{F(h)}"" fill=""none"" stroke=""currentColor"" />"
                );
            }

            if (options.ShowDeviceLabels)
            {
                var (labelX, labelY) = GetLabelPosition(deviceType);
                sb.AppendLine(
                    $@"<text class=""device-label"" x=""{F(labelX)}"" y=""{F(labelY)}"">{EscapeXml(deviceId)}</text>"
                );
            }

            if (options.ShowParamLabels && device.Params.Count > 0)
            {
                var paramText = FormatParams(device);
                var (paramX, paramY) = GetParamPosition(deviceType);
                sb.AppendLine(
                    $@"<text class=""param-label"" x=""{F(paramX)}"" y=""{F(paramY)}"">{EscapeXml(paramText)}</text>"
                );
            }

            sb.AppendLine("</g>");
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

        // Compute terminal positions for port Y alignment
        var terminalYByNet = ComputeTerminalYPositions(placement, graph);

        // Input/bias ports on left side - align to gate Y positions
        var leftPorts = graph.InputPorts.Concat(graph.BiasPorts).ToList();
        var leftPortYs = ComputePortYPositions(leftPorts, terminalYByNet, "G");

        foreach (var portName in leftPorts)
        {
            var x = -PortSymbolWidth;
            var y = leftPortYs.GetValueOrDefault(portName, RailMarginConst + 20.0);

            sb.AppendLine(
                $@"<g class=""port"" data-port=""{EscapeXml(portName)}"" data-net=""{EscapeXml(portName)}"" transform=""translate({F(x)}, {F(y)})"">"
            );
            sb.AppendLine(symbol);

            var labelX = -5.0;
            var labelY = PortSymbolHeight / 2 + 3;
            sb.AppendLine(
                $@"<text class=""port-label"" x=""{F(labelX)}"" y=""{F(labelY)}"" text-anchor=""end"">{EscapeXml(portName)}</text>"
            );

            sb.AppendLine("</g>");
        }

        // Output ports on right side - align to mean drain Y positions
        var rightPorts = graph.OutputPorts.ToList();
        var rightPortYs = ComputePortYPositions(rightPorts, terminalYByNet, "D");

        foreach (var portName in rightPorts)
        {
            var x = (double)routing.CanvasWidth;
            var y = rightPortYs.GetValueOrDefault(portName, RailMarginConst + 20.0);

            sb.AppendLine(
                $@"<g class=""port"" data-port=""{EscapeXml(portName)}"" data-net=""{EscapeXml(portName)}"" transform=""translate({F(x)}, {F(y)})"">"
            );
            sb.AppendLine(
                $@"<g transform=""translate({F(PortSymbolWidth)}, 0) scale(-1, 1)"">{symbol}</g>"
            );

            var labelX = PortSymbolWidth + 5;
            var labelY = PortSymbolHeight / 2 + 3;
            sb.AppendLine(
                $@"<text class=""port-label"" x=""{F(labelX)}"" y=""{F(labelY)}"" text-anchor=""start"">{EscapeXml(portName)}</text>"
            );

            sb.AppendLine("</g>");
        }

        sb.AppendLine("</g>");
    }

    /// <summary>
    /// Computes Y positions for each terminal type grouped by net.
    /// Returns dict[netName] -> list of (terminal, Y) pairs.
    /// </summary>
    private static Dictionary<string, List<(string Terminal, double Y)>> ComputeTerminalYPositions(
        CoarseGridResult placement,
        CircuitGraph graph
    )
    {
        var result = new Dictionary<string, List<(string, double)>>();

        foreach (var (deviceId, cell) in placement.DevicePlacements)
        {
            if (!graph.Devices.TryGetValue(deviceId, out var device))
            {
                continue;
            }

            var deviceType = device.DeviceType.ToLowerInvariant();
            var baseY = cell.Row * CellHeight + RailMarginConst + CellHeight / 2.0;

            if (deviceType is "nmos" or "nfet" or "pmos" or "pfet")
            {
                var gateY = baseY;
                var drainY = baseY - MosfetHeight / 3;
                var sourceY = baseY + MosfetHeight / 3;

                AddTerminalY(result, graph, deviceId, "G", gateY);
                AddTerminalY(result, graph, deviceId, "D", drainY);
                AddTerminalY(result, graph, deviceId, "S", sourceY);
            }
        }

        return result;
    }

    private static void AddTerminalY(
        Dictionary<string, List<(string, double)>> result,
        CircuitGraph graph,
        string deviceId,
        string terminal,
        double y
    )
    {
        var netName = graph.GetNetForTerminal(deviceId, terminal);
        if (netName == null)
        {
            return;
        }

        if (!result.TryGetValue(netName, out var list))
        {
            list = new List<(string, double)>();
            result[netName] = list;
        }
        list.Add((terminal, y));
    }

    /// <summary>
    /// Computes Y position for each port based on connected terminals.
    /// For input ports, uses gate Y. For output ports, uses mean drain Y.
    /// Resolves collisions by stacking with small offsets.
    /// </summary>
    private static Dictionary<string, double> ComputePortYPositions(
        List<string> portNames,
        Dictionary<string, List<(string Terminal, double Y)>> terminalYByNet,
        string preferredTerminal
    )
    {
        var portYs = new Dictionary<string, double>();
        var usedYs = new List<double>();

        foreach (var portName in portNames)
        {
            double y;

            if (terminalYByNet.TryGetValue(portName, out var terminals))
            {
                // Filter to preferred terminal type (G for input, D for output)
                var matchingTerminals = terminals
                    .Where(t => t.Terminal == preferredTerminal)
                    .ToList();

                if (matchingTerminals.Count > 0)
                {
                    // Use mean Y of matching terminals
                    y = matchingTerminals.Average(t => t.Y);
                }
                else
                {
                    // Fallback to mean of all terminals on this net
                    y = terminals.Average(t => t.Y);
                }
            }
            else
            {
                // No terminals found, use default position
                y = RailMarginConst + 20.0 + usedYs.Count * 20;
            }

            // Resolve collisions - if Y is too close to an existing port, offset it
            const double minSpacing = 15.0;
            while (usedYs.Any(existingY => Math.Abs(existingY - y) < minSpacing))
            {
                y += minSpacing;
            }

            portYs[portName] = y;
            usedYs.Add(y);
        }

        return portYs;
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
        var (w, h) = GetDeviceDimensions(deviceType);

        return orientation switch
        {
            DeviceOrientation.GateRight => $"translate({F(w)}, 0) scale(-1, 1)",
            DeviceOrientation.GateUp => $"translate(0, {F(w)}) rotate(-90)",
            DeviceOrientation.GateDown => $"translate({F(h)}, 0) rotate(90)",
            DeviceOrientation.Vertical => $"translate(0, {F(w)}) rotate(-90)",
            _ => string.Empty,
        };
    }

    private static (double Width, double Height) GetDeviceDimensions(string deviceType)
    {
        var type = deviceType.ToLowerInvariant();
        if (type is "nmos" or "pmos" or "nfet" or "pfet")
        {
            return (MosfetWidth, MosfetHeight);
        }
        return (PassiveWidth, PassiveHeight);
    }

    private static (double X, double Y) GetLabelPosition(string deviceType)
    {
        var (w, h) = GetDeviceDimensions(deviceType);
        return (w / 2, h + 12);
    }

    private static (double X, double Y) GetParamPosition(string deviceType)
    {
        var (w, h) = GetDeviceDimensions(deviceType);
        return (w / 2, h + 22);
    }

    private static string FormatParams(DeviceDeclaration device)
    {
        var parts = new List<string>();
        var type = device.DeviceType.ToLowerInvariant();

        if (type is "nmos" or "pmos" or "nfet" or "pfet")
        {
            if (device.Params.TryGetValue("W", out var w))
            {
                parts.Add($"W={w}");
            }
            if (device.Params.TryGetValue("L", out var l))
            {
                parts.Add($"L={l}");
            }
            if (device.Params.TryGetValue("M", out var m) && m != "1")
            {
                parts.Add($"M={m}");
            }
        }
        else if (type == "resistor" && device.Params.TryGetValue("R", out var r))
        {
            parts.Add($"R={r}");
        }
        else if (type == "capacitor" && device.Params.TryGetValue("C", out var c))
        {
            parts.Add($"C={c}");
        }
        else if (type == "inductor" && device.Params.TryGetValue("L", out var ind))
        {
            parts.Add($"L={ind}");
        }

        return string.Join(" ", parts);
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

    /// <summary>
    /// Snaps a value to the routing grid (pitch 10) using round-to-nearest.
    /// Must match FineGridRouter.SnapToGrid for wire/terminal alignment.
    /// </summary>
    private static int SnapToRoutingGrid(int value)
    {
        const int routingPitch = 10;
        return ((value + routingPitch / 2) / routingPitch) * routingPitch;
    }
}
