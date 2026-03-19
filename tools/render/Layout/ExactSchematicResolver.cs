namespace Cascode.Render.Layout;

using Cascode.Language;
using Cascode.Render.Analysis;
using Cascode.Render.Placement;
using Cascode.Render.Routing;
using Cascode.Render.Svg;

public sealed class ExactSchematicResult
{
    public required CoarseGridResult Placement { get; init; }
    public required RoutingResult Routing { get; init; }
    public required IReadOnlyList<RenderDiagnostic> Diagnostics { get; init; }
}

public readonly record struct RenderUnitPoint(int X, int Y);

public sealed record ResolvedRenderSegment(RenderUnitPoint From, RenderUnitPoint To);

public sealed class ExactPlacementContext
{
    public required CoarseGridResult Placement { get; init; }
    public required IReadOnlyList<TerminalPosition> TerminalPositions { get; init; }
    public required IReadOnlyList<Obstacle> Obstacles { get; init; }
    public required IReadOnlyList<RenderDiagnostic> Diagnostics { get; init; }
    public required int CanvasWidth { get; init; }
    public required int CanvasHeight { get; init; }
}

public static class ExactSchematicResolver
{
    private sealed record DevicePlacement(
        string DeviceId,
        string DeviceType,
        RenderUnitPoint Position,
        RenderOrientation Orientation,
        IReadOnlyDictionary<string, GridPoint> TerminalPixels
    );

    private sealed record PortPlacement(string PortName, RenderUnitPoint Position);

    public static ExactSchematicResult Resolve(
        Circuit circuit,
        CircuitGraph graph,
        RenderBlock render
    )
    {
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(render);

        var placementContext = ResolvePlacementContext(circuit, graph, render);
        var renderByName = render.Entities.ToDictionary(
            entity => entity.Name,
            StringComparer.Ordinal
        );
        var devicePlacements = ResolveDevicePlacements(graph, renderByName);
        var portPlacements = ResolvePortPlacements(circuit, renderByName, devicePlacements);
        var anchors = BuildAnchorMap(
            devicePlacements,
            portPlacements,
            placementContext.CanvasWidth,
            placementContext.CanvasHeight
        );
        var segmentsByNet = ResolveNetSegments(graph, renderByName, anchors);
        ValidateManualConnectivity(graph, placementContext.TerminalPositions, segmentsByNet);

        var allSegments = segmentsByNet.Values.SelectMany(segments => segments).ToList();
        var junctions = BuildJunctions(segmentsByNet);
        var canvasWidth = Math.Max(
            placementContext.CanvasWidth,
            allSegments.Count == 0
                ? placementContext.CanvasWidth
                : allSegments.Max(segment => Math.Max(segment.From.X, segment.To.X))
                    + DeviceGeometry.CellWidth
        );
        var canvasHeight = Math.Max(
            placementContext.CanvasHeight,
            allSegments.Count == 0
                ? placementContext.CanvasHeight
                : allSegments.Max(segment => Math.Max(segment.From.Y, segment.To.Y))
                    + DeviceGeometry.CellHeight
        );

        return new ExactSchematicResult
        {
            Placement = placementContext.Placement,
            Routing = new RoutingResult
            {
                Segments = allSegments,
                Junctions = junctions,
                SegmentsByNet = segmentsByNet.ToDictionary(
                    entry => entry.Key,
                    entry => (IReadOnlyList<WireSegment>)entry.Value,
                    StringComparer.Ordinal
                ),
                CanvasWidth = canvasWidth,
                CanvasHeight = canvasHeight,
                TerminalPositions = placementContext.TerminalPositions,
            },
            Diagnostics = placementContext.Diagnostics,
        };
    }

    public static ExactPlacementContext ResolvePlacementContext(
        Circuit circuit,
        CircuitGraph graph,
        RenderBlock render
    )
    {
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(render);

        var renderByName = render.Entities.ToDictionary(
            entity => entity.Name,
            StringComparer.Ordinal
        );
        var devicePlacements = ResolveDevicePlacements(graph, renderByName);
        var portPlacements = ResolvePortPlacements(circuit, renderByName, devicePlacements);
        var terminalPositions = BuildTerminalPositions(graph, devicePlacements, portPlacements);
        var diagnostics = DetectOverlapDiagnostics(devicePlacements.Values, portPlacements.Values);
        var (canvasWidth, canvasHeight) = ComputeCanvasSize(
            devicePlacements.Values,
            portPlacements.Values,
            Array.Empty<WireSegment>()
        );

        return new ExactPlacementContext
        {
            Placement = BuildPlacement(devicePlacements.Values, graph, canvasWidth, canvasHeight),
            TerminalPositions = terminalPositions,
            Obstacles = BuildObstacles(devicePlacements.Values),
            Diagnostics = diagnostics,
            CanvasWidth = canvasWidth,
            CanvasHeight = canvasHeight,
        };
    }

    public static IReadOnlyList<ResolvedRenderSegment> ResolveSegments(
        IReadOnlyList<RenderSegment> segments,
        IReadOnlyDictionary<string, RenderUnitPoint> anchors,
        string entityName
    )
    {
        var resolved = new List<ResolvedRenderSegment>(segments.Count);
        RenderUnitPoint? previous = null;

        foreach (var segment in segments)
        {
            var from = EvaluatePoint(segment.From, anchors, previous, entityName);
            previous = from;
            var to = EvaluatePoint(segment.To, anchors, previous, entityName);
            previous = to;

            if (from == to)
            {
                throw new InvalidOperationException(
                    $"Manual render segment for '{entityName}' resolves to zero length at ({from.X}, {from.Y})."
                );
            }

            resolved.Add(new ResolvedRenderSegment(from, to));
        }

        return resolved;
    }

    private static Dictionary<string, DevicePlacement> ResolveDevicePlacements(
        CircuitGraph graph,
        IReadOnlyDictionary<string, RenderEntity> renderByName
    )
    {
        var unresolved = new Dictionary<string, RenderEntity>(StringComparer.Ordinal);
        foreach (var (deviceId, _) in graph.Devices)
        {
            if (!renderByName.TryGetValue(deviceId, out var entry) || entry.Place is null)
            {
                throw new InvalidOperationException(
                    $"Manual render requires an explicit place for device '{deviceId}'."
                );
            }

            unresolved[deviceId] = entry;
        }

        var resolved = new Dictionary<string, DevicePlacement>(StringComparer.Ordinal);
        var anchors = new Dictionary<string, RenderUnitPoint>(StringComparer.Ordinal)
        {
            ["canvas origin"] = new RenderUnitPoint(0, 0),
        };

        while (unresolved.Count > 0)
        {
            var progressed = false;
            foreach (var deviceId in unresolved.Keys.ToArray())
            {
                var entry = unresolved[deviceId];
                if (
                    !TryEvaluatePoint(entry.Place!.Point, anchors, previous: null, out var position)
                )
                {
                    continue;
                }

                var orientation =
                    entry.Orientation ?? new RenderOrientation { Rotate = 0, MirrorX = false };
                var terminalPixels = ComputeDeviceTerminalPixels(
                    graph.Devices[deviceId],
                    position,
                    orientation
                );
                resolved[deviceId] = new DevicePlacement(
                    deviceId,
                    graph.Devices[deviceId].DeviceType,
                    position,
                    orientation,
                    terminalPixels
                );

                anchors[deviceId] = position;
                foreach (var (terminal, point) in terminalPixels)
                {
                    anchors[$"{deviceId}.{terminal}"] = new RenderUnitPoint(point.X, point.Y);
                }

                unresolved.Remove(deviceId);
                progressed = true;
            }

            if (!progressed)
            {
                var unresolvedDevices = string.Join(
                    ", ",
                    unresolved.Keys.OrderBy(name => name, StringComparer.Ordinal)
                );
                throw new InvalidOperationException(
                    $"Manual render could not resolve explicit device placement anchors for: {unresolvedDevices}."
                );
            }
        }

        return resolved;
    }

    private static Dictionary<string, PortPlacement> ResolvePortPlacements(
        Circuit circuit,
        IReadOnlyDictionary<string, RenderEntity> renderByName,
        IReadOnlyDictionary<string, DevicePlacement> devicePlacements
    )
    {
        var anchors = devicePlacements
            .SelectMany(placement =>
                placement.Value.TerminalPixels.Select(terminal => new KeyValuePair<
                    string,
                    RenderUnitPoint
                >(
                    $"{placement.Key}.{terminal.Key}",
                    new RenderUnitPoint(terminal.Value.X, terminal.Value.Y)
                ))
            )
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        foreach (var placement in devicePlacements)
        {
            anchors[placement.Key] = placement.Value.Position;
        }
        anchors["canvas origin"] = new RenderUnitPoint(0, 0);

        var unresolved = new Dictionary<string, RenderEntity>(StringComparer.Ordinal);
        foreach (var port in CircuitPortExpander.Expand(circuit))
        {
            if (!renderByName.TryGetValue(port.Name, out var entry) || entry.Place is null)
            {
                throw new InvalidOperationException(
                    $"Manual render requires an explicit place for port '{port.Name}'."
                );
            }

            if (entry.Side is null)
            {
                throw new InvalidOperationException(
                    $"Manual render requires an explicit side for port '{port.Name}'."
                );
            }
            unresolved[port.Name] = entry;
        }

        var ports = new Dictionary<string, PortPlacement>(StringComparer.Ordinal);
        while (unresolved.Count > 0)
        {
            var progressed = false;
            foreach (var portName in unresolved.Keys.ToArray())
            {
                var entry = unresolved[portName];
                if (
                    !TryEvaluatePoint(entry.Place!.Point, anchors, previous: null, out var position)
                )
                {
                    continue;
                }

                ports[portName] = new PortPlacement(portName, position);
                anchors[portName] = position;
                unresolved.Remove(portName);
                progressed = true;
            }

            if (!progressed)
            {
                var unresolvedPorts = string.Join(
                    ", ",
                    unresolved.Keys.OrderBy(name => name, StringComparer.Ordinal)
                );
                throw new InvalidOperationException(
                    $"Manual render could not resolve explicit port placement anchors for: {unresolvedPorts}."
                );
            }
        }

        return ports;
    }

    private static Dictionary<string, RenderUnitPoint> BuildAnchorMap(
        IReadOnlyDictionary<string, DevicePlacement> devicePlacements,
        IReadOnlyDictionary<string, PortPlacement> portPlacements,
        int canvasWidth,
        int canvasHeight
    )
    {
        var anchors = new Dictionary<string, RenderUnitPoint>(StringComparer.Ordinal)
        {
            ["canvas origin"] = new RenderUnitPoint(0, 0),
            ["canvas center"] = new RenderUnitPoint(canvasWidth / 2, canvasHeight / 2),
        };

        foreach (var placement in devicePlacements)
        {
            anchors[placement.Key] = placement.Value.Position;
            foreach (var (terminal, point) in placement.Value.TerminalPixels)
            {
                anchors[$"{placement.Key}.{terminal}"] = new RenderUnitPoint(point.X, point.Y);
            }
        }

        foreach (var placement in portPlacements)
        {
            anchors[placement.Key] = placement.Value.Position;
        }

        return anchors;
    }

    private static Dictionary<string, List<WireSegment>> ResolveNetSegments(
        CircuitGraph graph,
        IReadOnlyDictionary<string, RenderEntity> renderByName,
        IReadOnlyDictionary<string, RenderUnitPoint> anchors
    )
    {
        var result = new Dictionary<string, List<WireSegment>>(StringComparer.Ordinal);
        var netNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in graph.NetConnections.Where(e => e.Value.Count > 0))
        {
            netNames.Add(entry.Key);
        }

        foreach (
            var portNet in graph.InputPorts.Concat(graph.OutputPorts).Concat(graph.BiasPorts)
        )
        {
            netNames.Add(portNet);
        }

        foreach (var netName in netNames.OrderBy(name => name, StringComparer.Ordinal))
        {
            if (!renderByName.TryGetValue(netName, out var entry) || entry.Segments.Count == 0)
            {
                var requiresSegments =
                    graph.NetConnections.TryGetValue(netName, out var conns) && conns.Count > 0;
                if (!requiresSegments)
                {
                    requiresSegments =
                        graph.InputPorts.Contains(netName)
                        || graph.OutputPorts.Contains(netName)
                        || graph.BiasPorts.Contains(netName);
                }

                if (requiresSegments)
                {
                    throw new InvalidOperationException(
                        $"Manual render requires at least one seg for net '{netName}'."
                    );
                }

                continue;
            }

            var resolvedSegments = ResolveSegments(entry.Segments, anchors, netName);
            result[netName] = resolvedSegments
                .Select(segment => new WireSegment(
                    new GridPoint(segment.From.X, segment.From.Y),
                    new GridPoint(segment.To.X, segment.To.Y),
                    netName
                ))
                .ToList();
        }

        return result;
    }

    private static List<TerminalPosition> BuildTerminalPositions(
        CircuitGraph graph,
        IReadOnlyDictionary<string, DevicePlacement> devicePlacements,
        IReadOnlyDictionary<string, PortPlacement> portPlacements
    )
    {
        var terminals = new List<TerminalPosition>();
        foreach (var placement in devicePlacements.Values)
        {
            terminals.AddRange(
                placement.TerminalPixels.Select(terminal => new TerminalPosition(
                    placement.DeviceId,
                    terminal.Key,
                    terminal.Value.X,
                    terminal.Value.Y
                ))
            );
        }

        foreach (var portName in graph.InputPorts.Concat(graph.OutputPorts).Concat(graph.BiasPorts))
        {
            if (!portPlacements.TryGetValue(portName, out var placement))
            {
                continue;
            }

            terminals.Add(
                new TerminalPosition(
                    $"PORT_{portName}",
                    "P",
                    placement.Position.X,
                    placement.Position.Y
                )
            );
        }

        return terminals;
    }

    private static IReadOnlyList<RenderDiagnostic> DetectOverlapDiagnostics(
        IEnumerable<DevicePlacement> devices,
        IEnumerable<PortPlacement> ports
    )
    {
        _ = devices;
        var diagnostics = new List<RenderDiagnostic>();
        var portList = ports.ToList();
        foreach (var port in portList)
        {
            var collisions = portList.Count(other =>
                other.PortName != port.PortName && other.Position == port.Position
            );
            if (collisions > 0)
            {
                diagnostics.Add(
                    new RenderDiagnostic
                    {
                        Severity = RenderDiagnosticSeverity.Warning,
                        Code = "CASRENDER-MANUAL-PORT-OVERLAP",
                        Message =
                            $"Port '{port.PortName}' overlaps another explicit port placement.",
                        EntityRefs = new RenderDiagnosticEntityRefs { PortName = port.PortName },
                        Geometry = new RenderDiagnosticGeometry
                        {
                            Point = new RenderDiagnosticPoint(port.Position.X, port.Position.Y),
                        },
                    }
                );
            }
        }

        return diagnostics;
    }

    private static IReadOnlyList<Obstacle> BuildObstacles(IEnumerable<DevicePlacement> devices)
    {
        const int margin = 2;
        var obstacles = new List<Obstacle>();
        foreach (var device in devices)
        {
            var (minX, minY, maxX, maxY) = ComputeDeviceBounds(device);
            obstacles.Add(
                new Obstacle(
                    MinX: (int)Math.Floor(minX) + margin,
                    MinY: (int)Math.Floor(minY) + margin,
                    MaxX: (int)Math.Ceiling(maxX) - margin,
                    MaxY: (int)Math.Ceiling(maxY) - margin
                )
            );
        }

        return obstacles;
    }

    private static (double MinX, double MinY, double MaxX, double MaxY) ComputeDeviceBounds(
        DevicePlacement device
    )
    {
        var parsed =
            SymbolLibrary.GetParsedSymbol(device.DeviceType)
            ?? throw new InvalidOperationException(
                $"Manual render does not support device type '{device.DeviceType}'."
            );
        var x0 = parsed.ViewBox[0];
        var y0 = parsed.ViewBox[1];
        var x1 = parsed.ViewBox[0] + parsed.ViewBox[2];
        var y1 = parsed.ViewBox[1] + parsed.ViewBox[3];
        var corners = new[]
        {
            TransformSymbolPoint(x0, y0, parsed, device.Position, device.Orientation),
            TransformSymbolPoint(x1, y0, parsed, device.Position, device.Orientation),
            TransformSymbolPoint(x0, y1, parsed, device.Position, device.Orientation),
            TransformSymbolPoint(x1, y1, parsed, device.Position, device.Orientation),
        };

        return (
            MinX: corners.Min(point => point.X),
            MinY: corners.Min(point => point.Y),
            MaxX: corners.Max(point => point.X),
            MaxY: corners.Max(point => point.Y)
        );
    }

    private static (int X, int Y) TransformSymbolPoint(
        double x,
        double y,
        ParsedSymbol parsed,
        RenderUnitPoint position,
        RenderOrientation orientation
    )
    {
        var centerX =
            parsed.Terminals.Count > 0
                ? parsed.Terminals.Values.Average(terminal => terminal.X)
                : parsed.ViewBox[0] + parsed.ViewBox[2] / 2.0;
        var centerY =
            parsed.Terminals.Count > 0
                ? parsed.Terminals.Values.Average(terminal => terminal.Y)
                : parsed.ViewBox[1] + parsed.ViewBox[3] / 2.0;
        var dx = x - centerX;
        var dy = y - centerY;
        if (orientation.MirrorX)
        {
            dx = -dx;
        }

        var angle = Mod360(orientation.Rotate) * Math.PI / 180.0;
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        var rotatedX = dx * cos - dy * sin;
        var rotatedY = dx * sin + dy * cos;
        return (
            X: (int)Math.Round(position.X + rotatedX, MidpointRounding.AwayFromZero),
            Y: (int)Math.Round(position.Y + rotatedY, MidpointRounding.AwayFromZero)
        );
    }

    private static List<GridPoint> BuildJunctions(
        IReadOnlyDictionary<string, List<WireSegment>> segmentsByNet
    )
    {
        var junctions = new HashSet<GridPoint>();
        foreach (var segments in segmentsByNet.Values)
        {
            var endpoints = segments
                .SelectMany(segment => new[] { segment.From, segment.To })
                .ToList();
            foreach (var endpoint in endpoints)
            {
                var onInterior = segments.Any(segment =>
                    endpoint != segment.From
                    && endpoint != segment.To
                    && IsPointOnSegmentInterior(endpoint, segment)
                );
                var duplicateEndpoints = endpoints.Count(point => point == endpoint) > 2;
                if (onInterior || duplicateEndpoints)
                {
                    junctions.Add(endpoint);
                }
            }
        }

        return junctions.OrderBy(point => point.X).ThenBy(point => point.Y).ToList();
    }

    private static void ValidateManualConnectivity(
        CircuitGraph graph,
        IReadOnlyList<TerminalPosition> terminalPositions,
        IReadOnlyDictionary<string, List<WireSegment>> segmentsByNet
    )
    {
        var terminalsByNet = terminalPositions
            .GroupBy(
                terminal =>
                    terminal.DeviceId.StartsWith("PORT_", StringComparison.Ordinal)
                        ? terminal.DeviceId[5..]
                        : graph.GetNetForTerminal(terminal.DeviceId, terminal.Terminal)
                            ?? string.Empty,
                StringComparer.Ordinal
            )
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        foreach (var (netName, segments) in segmentsByNet)
        {
            if (!terminalsByNet.TryGetValue(netName, out var terminals))
            {
                continue;
            }

            var nodes = segments
                .SelectMany(segment => new[] { segment.From, segment.To })
                .Concat(terminals.Select(terminal => new GridPoint(terminal.X, terminal.Y)))
                .Distinct()
                .ToDictionary(point => point, _ => new HashSet<GridPoint>());

            foreach (var segment in segments)
            {
                nodes[segment.From].Add(segment.To);
                nodes[segment.To].Add(segment.From);
            }

            foreach (var point in nodes.Keys.ToArray())
            {
                foreach (var segment in segments)
                {
                    if (IsPointOnSegmentInterior(point, segment))
                    {
                        nodes[point].Add(segment.From);
                        nodes[point].Add(segment.To);
                        nodes[segment.From].Add(point);
                        nodes[segment.To].Add(point);
                    }
                }
            }

            var terminalPoints = new List<GridPoint>(terminals.Count);
            foreach (var terminal in terminals)
            {
                var point = new GridPoint(terminal.X, terminal.Y);
                if (!segments.Any(segment => IsPointOnSegmentInclusive(point, segment)))
                {
                    throw new InvalidOperationException(
                        $"Manual render net '{netName}' does not connect terminal '{terminal.DeviceId}.{terminal.Terminal}'."
                    );
                }

                terminalPoints.Add(point);
            }

            if (terminalPoints.Count == 0)
            {
                continue;
            }

            var visited = new HashSet<GridPoint>();
            var queue = new Queue<GridPoint>();
            queue.Enqueue(terminalPoints[0]);
            visited.Add(terminalPoints[0]);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var neighbor in nodes[current])
                {
                    if (visited.Add(neighbor))
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }

            foreach (var point in terminalPoints)
            {
                if (!visited.Contains(point))
                {
                    throw new InvalidOperationException(
                        $"Manual render net '{netName}' contains disconnected terminal geometry."
                    );
                }
            }

            foreach (var node in nodes.Keys)
            {
                if (!visited.Contains(node))
                {
                    throw new InvalidOperationException(
                        $"Manual render net '{netName}' contains dangling explicit segments."
                    );
                }
            }
        }
    }

    private static CoarseGridResult BuildPlacement(
        IEnumerable<DevicePlacement> devices,
        CircuitGraph graph,
        int canvasWidth,
        int canvasHeight
    )
    {
        var placements = devices.ToDictionary(
            device => device.DeviceId,
            device =>
            {
                var xPixels = device.Position.X;
                var yPixels = device.Position.Y;
                var row = Math.Max(
                    0,
                    (int)
                        Math.Round(
                            (yPixels - DeviceGeometry.RailMargin)
                                / (double)DeviceGeometry.CellHeight
                        )
                );
                var column = Math.Max(
                    0,
                    (int)Math.Round(xPixels / (double)DeviceGeometry.CellWidth)
                );
                return new GridCell(row, column, device.Orientation.MirrorX);
            },
            StringComparer.Ordinal
        );

        var horizontalPassives = devices
            .Where(device =>
                graph.Devices.TryGetValue(device.DeviceId, out var declaration)
                && declaration.DeviceType.ToLowerInvariant()
                    is "resistor"
                        or "capacitor"
                        or "inductor"
                && Mod360(device.Orientation.Rotate) is 0 or 180
            )
            .Select(device => device.DeviceId)
            .ToHashSet(StringComparer.Ordinal);

        return new CoarseGridResult
        {
            RowCount = Math.Max(
                1,
                (int)Math.Ceiling(canvasHeight / (double)DeviceGeometry.CellHeight)
            ),
            ColumnCount = Math.Max(
                1,
                (int)Math.Ceiling(canvasWidth / (double)DeviceGeometry.CellWidth)
            ),
            DevicePlacements = placements,
            SymmetryAxis = Math.Max(
                0,
                (int)Math.Ceiling(canvasWidth / (double)DeviceGeometry.CellWidth) / 2
            ),
            HorizontalPassiveIds = horizontalPassives,
            PortYHints = new Dictionary<string, int>(StringComparer.Ordinal),
        };
    }

    private static (int Width, int Height) ComputeCanvasSize(
        IEnumerable<DevicePlacement> devices,
        IEnumerable<PortPlacement> ports,
        IEnumerable<WireSegment> segments
    )
    {
        var points = new List<GridPoint>();
        points.AddRange(
            devices.Select(device => new GridPoint(device.Position.X, device.Position.Y))
        );
        points.AddRange(
            ports.Select(port => new GridPoint(port.Position.X, port.Position.Y))
        );
        points.AddRange(segments.SelectMany(segment => new[] { segment.From, segment.To }));

        if (points.Count == 0)
        {
            return (
                DeviceGeometry.CellWidth,
                DeviceGeometry.CellHeight + 2 * DeviceGeometry.RailMargin
            );
        }

        var maxX = points.Max(point => point.X);
        var maxY = points.Max(point => point.Y);
        return (
            Math.Max(DeviceGeometry.CellWidth, maxX + DeviceGeometry.CellWidth),
            Math.Max(
                DeviceGeometry.CellHeight + 2 * DeviceGeometry.RailMargin,
                maxY + DeviceGeometry.CellHeight
            )
        );
    }

    private static IReadOnlyDictionary<string, GridPoint> ComputeDeviceTerminalPixels(
        DeviceDeclaration device,
        RenderUnitPoint position,
        RenderOrientation orientation
    )
    {
        var parsed =
            SymbolLibrary.GetParsedSymbol(device.DeviceType)
            ?? throw new InvalidOperationException(
                $"Manual render does not support device type '{device.DeviceType}'."
            );
        if (parsed.Terminals.Count == 0)
        {
            throw new InvalidOperationException(
                $"Manual render requires explicit terminal geometry for device type '{device.DeviceType}'."
            );
        }

        var centroidX = parsed.Terminals.Values.Average(terminal => terminal.X);
        var centroidY = parsed.Terminals.Values.Average(terminal => terminal.Y);
        var angle = Mod360(orientation.Rotate) * Math.PI / 180.0;
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);

        return parsed.Terminals.ToDictionary(
            terminal => terminal.Key,
            terminal =>
            {
                var dx = terminal.Value.X - centroidX;
                var dy = terminal.Value.Y - centroidY;
                if (orientation.MirrorX)
                {
                    dx = -dx;
                }

                var rotatedX = dx * cos - dy * sin;
                var rotatedY = dx * sin + dy * cos;
                var snappedRuX = (int)
                    Math.Round(position.X + rotatedX, MidpointRounding.AwayFromZero);
                var snappedRuY = (int)
                    Math.Round(position.Y + rotatedY, MidpointRounding.AwayFromZero);
                return new GridPoint(snappedRuX, snappedRuY);
            },
            StringComparer.Ordinal
        );
    }

    private static RenderUnitPoint EvaluatePoint(
        RenderPointExpression point,
        IReadOnlyDictionary<string, RenderUnitPoint> anchors,
        RenderUnitPoint? previous,
        string entityName
    )
    {
        if (TryEvaluatePoint(point, anchors, previous, out var resolved))
        {
            return resolved;
        }

        throw new InvalidOperationException(
            $"Manual render could not resolve a point expression for '{entityName}'."
        );
    }

    private static bool TryEvaluatePoint(
        RenderPointExpression point,
        IReadOnlyDictionary<string, RenderUnitPoint> anchors,
        RenderUnitPoint? previous,
        out RenderUnitPoint resolved
    )
    {
        switch (point)
        {
            case RenderAbsPoint abs:
                resolved = new RenderUnitPoint(abs.X, abs.Y);
                return true;

            case RenderRefPoint @ref when anchors.TryGetValue(@ref.Anchor, out var anchor):
                resolved = new RenderUnitPoint(anchor.X + @ref.Dx, anchor.Y + @ref.Dy);
                return true;

            case RenderRelPoint rel when previous is not null:
                resolved = new RenderUnitPoint(
                    previous.Value.X + rel.Dx,
                    previous.Value.Y + rel.Dy
                );
                return true;

            default:
                resolved = default;
                return false;
        }
    }

    private static bool IsPointOnSegmentInclusive(GridPoint point, WireSegment segment)
    {
        return IsPointOnLine(point, segment.From, segment.To)
            && IsWithinSegmentBounds(point, segment.From, segment.To);
    }

    private static bool IsPointOnSegmentInterior(GridPoint point, WireSegment segment)
    {
        return point != segment.From
            && point != segment.To
            && IsPointOnSegmentInclusive(point, segment);
    }

    private static bool IsPointOnLine(GridPoint point, GridPoint from, GridPoint to)
    {
        var cross = (point.Y - from.Y) * (to.X - from.X) - (point.X - from.X) * (to.Y - from.Y);
        return cross == 0;
    }

    private static bool IsWithinSegmentBounds(GridPoint point, GridPoint from, GridPoint to)
    {
        var minX = Math.Min(from.X, to.X);
        var maxX = Math.Max(from.X, to.X);
        var minY = Math.Min(from.Y, to.Y);
        var maxY = Math.Max(from.Y, to.Y);
        return point.X >= minX && point.X <= maxX && point.Y >= minY && point.Y <= maxY;
    }

    private static int Mod360(int value)
    {
        var normalized = value % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

}
