using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Cascode.Language;
using Cascode.Render.Analysis;
using Cascode.Render.Layout;
using Cascode.Render.Placement;
using Cascode.Render.Routing;
using Cascode.Render.Svg;

namespace Cascode.Native;

internal static partial class SchematicLayoutProjection
{
    /// <summary>
    /// Builds a structural representation of the circuit containing devices, ports, nets, supplies, and grounds.
    /// </summary>
    /// <param name="circuit">The circuit providing ports, supplies, and grounds.</param>
    /// <param name="graph">The circuit graph providing devices and net connections.</param>
    /// <returns>
    /// A <see cref="StructuralInfo"/> containing:
    /// - Devices: ordered by device id; each device has a lowercased Type and Terminals sorted by name.
    /// - Ports: ordered by name; each port's Direction is converted to the Cascode string and its Type is preserved.
    /// - Nets: ordered by net name; each net's Connections are arrays of [DeviceId, Terminal].
    /// - Supplies and Grounds: ordered lists taken from the circuit.
    /// </returns>
    public static StructuralInfo BuildStructural(Circuit circuit, CircuitGraph graph)
    {
        var devices = graph
            .Devices.OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new StructuralDevice
            {
                Id = entry.Key,
                Type = entry.Value.DeviceType.ToLowerInvariant(),
                Terminals = entry
                    .Value.Bindings.Keys.OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray(),
                Primitive = entry.Value.Primitive,
                Size = entry.Value.Size?.Entries
                    ?? (IReadOnlyDictionary<string, string>)
                       new Dictionary<string, string>(StringComparer.Ordinal),
            })
            .ToArray();

        var ports = circuit
            .Ports.OrderBy(port => port.Name, StringComparer.Ordinal)
            .Select(port => new StructuralPort
            {
                Name = port.Name,
                Direction = port.Direction.ToCascodeString(),
                Type = port.Type,
            })
            .ToArray();

        var nets = graph
            .NetConnections.OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new StructuralNet
            {
                Name = entry.Key,
                Connections = entry
                    .Value.Select(conn => new[] { conn.DeviceId, conn.Terminal })
                    .ToArray(),
            })
            .ToArray();

        return new StructuralInfo
        {
            Devices = devices,
            Ports = ports,
            Nets = nets,
            Supplies = circuit.Supplies.OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            Grounds = circuit.Grounds.OrderBy(name => name, StringComparer.Ordinal).ToArray(),
        };
    }

    /// <summary>
    /// Builds layout data for rendering by combining circuit metadata, optional render entities, placement, and routing results.
    /// </summary>
    /// <param name="circuit">The source circuit definition used to resolve device and port metadata.</param>
    /// <param name="render">Optional render block whose Entities provide per-entity orientation and side hints; may be null.</param>
    /// <param name="placement">Coarse placement result describing grid cell positions for devices.</param>
    /// <param name="routing">Routing result containing wire segments, junctions, and terminal positions.</param>
    /// <returns>
    /// A <see cref="LayoutInfo"/> containing:
    /// - Devices: layout entries for each placed device with position, orientation, and bounding box;
    /// - Ports: layout entries for ports routed as terminals whose DeviceId starts with "PORT_";
    /// - Nets: nets with their segments converted to render units and junction points lying on those segments.
    /// </returns>
    public static LayoutInfo BuildLayout(
        Circuit circuit,
        RenderBlock? render,
        CoarseGridResult placement,
        RoutingResult routing
    )
    {
        var renderByName =
            render?.Entities.ToDictionary(entity => entity.Name, StringComparer.Ordinal)
            ?? new Dictionary<string, RenderEntity>(StringComparer.Ordinal);

        // Group routing terminal positions by device for centroid-based positioning
        var terminalsByDevice = routing
            .TerminalPositions.Where(t =>
                !t.DeviceId.StartsWith("PORT_", StringComparison.Ordinal)
            )
            .GroupBy(t => t.DeviceId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.ToArray(),
                StringComparer.Ordinal
            );

        var devices = placement
            .DevicePlacements.OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry =>
                BuildLayoutDevice(
                    circuit,
                    entry.Key,
                    entry.Value,
                    placement,
                    renderByName,
                    terminalsByDevice
                )
            )
            .ToArray();

        var ports = routing
            .TerminalPositions.Where(terminal =>
                terminal.DeviceId.StartsWith("PORT_", StringComparison.Ordinal)
            )
            .Select(terminal => terminal.DeviceId[5..])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => BuildLayoutPort(name, circuit, renderByName, routing))
            .ToArray();

        var nets = routing
            .SegmentsByNet.OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new LayoutNet
            {
                Name = entry.Key,
                Segments = entry
                    .Value.Select(segment => new SegmentValue
                    {
                        From = new PointValue
                        {
                            X = ToRenderUnitsExact(segment.From.X),
                            Y = ToRenderUnitsExact(segment.From.Y),
                        },
                        To = new PointValue
                        {
                            X = ToRenderUnitsExact(segment.To.X),
                            Y = ToRenderUnitsExact(segment.To.Y),
                        },
                    })
                    .ToArray(),
                Junctions = routing
                    .Junctions.Where(junction =>
                        entry.Value.Any(segment => IsPointOnSegment(junction, segment))
                    )
                    .Select(junction => new PointValue
                    {
                        X = ToRenderUnitsExact(junction.X),
                        Y = ToRenderUnitsExact(junction.Y),
                    })
                    .ToArray(),
            })
            .ToArray();

        return new LayoutInfo
        {
            Devices = devices,
            Ports = ports,
            Nets = nets,
        };
    }

    /// <summary>
    /// Builds a cache of rendering data for devices, including per-terminal render positions and computed bounding boxes.
    /// </summary>
    /// <param name="circuit">The circuit containing device definitions and fill data used to determine device types.</param>
    /// <param name="placement">Placement results used to compute device bounding boxes.</param>
    /// <param name="routing">Routing results providing terminal positions; only non-port terminals are included.</param>
    /// <returns>
    /// A RenderCacheInfo whose <c>TerminalPoints</c> maps device IDs to dictionaries of terminal name → position (in render units),
    /// and whose <c>ComputedBboxes</c> maps device IDs to their computed bounding boxes.
    /// </returns>
    public static RenderCacheInfo BuildRenderCache(
        Circuit circuit,
        CoarseGridResult placement,
        RoutingResult routing
    )
    {
        var terminals = new Dictionary<string, IReadOnlyDictionary<string, PointValue>>(
            StringComparer.Ordinal
        );
        foreach (
            var group in routing
                .TerminalPositions.Where(terminal =>
                    !terminal.DeviceId.StartsWith("PORT_", StringComparison.Ordinal)
                )
                .GroupBy(terminal => terminal.DeviceId, StringComparer.Ordinal)
        )
        {
            terminals[group.Key] = group.ToDictionary(
                terminal => terminal.Terminal,
                terminal => new PointValue
                {
                    X = ToRenderUnitsExact(terminal.X),
                    Y = ToRenderUnitsExact(terminal.Y),
                },
                StringComparer.Ordinal
            );
        }

        // Group routing terminals by device for centroid computation
        var terminalsByDevice = routing
            .TerminalPositions.Where(t =>
                !t.DeviceId.StartsWith("PORT_", StringComparison.Ordinal)
            )
            .GroupBy(t => t.DeviceId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.ToArray(),
                StringComparer.Ordinal
            );

        var bboxes = new Dictionary<string, BboxValue>(StringComparer.Ordinal);
        foreach (var (deviceId, cell) in placement.DevicePlacements)
        {
            var device = circuit.Fill?.Devices.FirstOrDefault(d => d.Id == deviceId);
            var type = device?.DeviceType.ToLowerInvariant() ?? "unknown";

            PointValue position;
            if (terminalsByDevice.TryGetValue(deviceId, out var devTerminals) && devTerminals.Length > 0)
            {
                position = new PointValue
                {
                    X = devTerminals.Average(t => ToRenderUnitsExact(t.X)),
                    Y = devTerminals.Average(t => ToRenderUnitsExact(t.Y)),
                };
            }
            else
            {
                position = new PointValue
                {
                    X = ToRenderUnitsExact(
                        (int)Math.Round(DeviceGeometry.GetCellCenterX(cell.Column), MidpointRounding.AwayFromZero)
                    ),
                    Y = ToRenderUnitsExact(
                        (int)Math.Round(DeviceGeometry.GetCellCenterY(cell.Row), MidpointRounding.AwayFromZero)
                    ),
                };
            }

            bboxes[deviceId] = BuildDeviceBbox(type, position, placement, deviceId);
        }

        return new RenderCacheInfo { TerminalPoints = terminals, ComputedBboxes = bboxes };
    }

    /// <summary>
    /// Builds a symbol catalog containing vector paths, viewBox, and terminal positions
    /// for each unique device type in the structural info plus ports.
    /// </summary>
    /// <param name="structural">The structural info containing device type references.</param>
    /// <returns>A dictionary mapping device type names to their symbol catalog entries.</returns>
    public static IReadOnlyDictionary<string, SymbolCatalogEntry> BuildSymbolCatalog(
        StructuralInfo structural
    )
    {
        var catalog = new Dictionary<string, SymbolCatalogEntry>(StringComparer.Ordinal);

        // Collect unique device types from structural devices
        var deviceTypes = structural
            .Devices.Select(d => d.Type)
            .Distinct(StringComparer.Ordinal);

        foreach (var deviceType in deviceTypes)
        {
            if (catalog.ContainsKey(deviceType))
            {
                continue;
            }

            var parsed = SymbolLibrary.GetParsedSymbol(deviceType);
            if (parsed is null)
            {
                continue;
            }

            catalog[deviceType] = ConvertParsedSymbol(parsed);
        }

        // Include port symbol if there are any ports
        if (structural.Ports.Count > 0 && !catalog.ContainsKey("port"))
        {
            var portParsed = SymbolLibrary.GetParsedSymbol("port");
            if (portParsed is not null)
            {
                catalog["port"] = ConvertParsedSymbol(portParsed);
            }
        }

        return catalog;
    }

    /// <summary>
    /// Converts a <see cref="ParsedSymbol"/> from the render library into the API response type.
    /// Paths and terminals are pre-scaled from SVG-local coordinates to device-centered render-unit
    /// coordinates so the client renderer only needs to translate by device position + apply orientation.
    /// </summary>
    private static SymbolCatalogEntry ConvertParsedSymbol(ParsedSymbol parsed)
    {
        // Scale factor: SVG units → render units (pixels / RoutingPitch)
        double sx = 1.0 / DeviceGeometry.RoutingPitch;
        double sy = 1.0 / DeviceGeometry.RoutingPitch;

        // Center at terminal centroid so that catalog offsets align exactly with
        // routing terminal positions when added to the device position (also a
        // terminal centroid). Falls back to viewBox center for symbols without terminals.
        double cx = parsed.Terminals.Count > 0
            ? parsed.Terminals.Values.Average(t => t.X)
            : parsed.ViewBox[2] / 2.0;
        double cy = parsed.Terminals.Count > 0
            ? parsed.Terminals.Values.Average(t => t.Y)
            : parsed.ViewBox[3] / 2.0;

        return new SymbolCatalogEntry
        {
            ViewBox = [0, 0, parsed.ViewBox[2] * sx, parsed.ViewBox[3] * sy],
            Paths = parsed
                .Paths.Select(p => new SymbolPathEntry
                {
                    D = ScalePathD(p.D, sx, sy, cx, cy),
                    Style = p.Style,
                })
                .ToArray(),
            Terminals = parsed.Terminals.ToDictionary(
                kvp => kvp.Key,
                kvp => new SymbolTerminalEntry
                {
                    X = (kvp.Value.X - cx) * sx,
                    Y = (kvp.Value.Y - cy) * sy,
                },
                StringComparer.Ordinal
            ),
        };
    }

    /// <summary>
    /// Transforms an SVG path <c>d</c> string by centering on <paramref name="cx"/>,<paramref name="cy"/>
    /// and scaling by <paramref name="sx"/>,<paramref name="sy"/>.
    /// Absolute coordinates are mapped as <c>(x-cx)*sx</c>; relative coordinates are scaled without offset.
    /// </summary>
    internal static string ScalePathD(
        string d,
        double sx,
        double sy,
        double cx,
        double cy
    )
    {
        // Tokenize into command letters and numbers
        var tokens = new List<object>(); // string for commands, double for numbers
        foreach (Match m in SvgPathTokenRegex().Matches(d))
        {
            if (m.Groups[1].Success)
                tokens.Add(m.Groups[1].Value);
            else
                tokens.Add(
                    double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)
                );
        }

        var result = new StringBuilder();
        var i = 0;

        double Num() => (double)tokens[i++];

        static string Fmt(double v) =>
            Math.Round(v, 4).ToString("G", CultureInfo.InvariantCulture);

        void Emit(string s)
        {
            if (result.Length > 0)
                result.Append(' ');
            result.Append(s);
        }

        while (i < tokens.Count)
        {
            if (tokens[i] is not string cmd)
            {
                i++;
                continue;
            }
            i++;

            var isRel = char.IsLower(cmd[0]);
            Emit(cmd);

            switch (cmd.ToUpperInvariant())
            {
                case "M":
                case "L":
                case "T":
                    while (i < tokens.Count && tokens[i] is double)
                    {
                        var x = Num();
                        var y = Num();
                        if (isRel)
                        {
                            x *= sx;
                            y *= sy;
                        }
                        else
                        {
                            x = (x - cx) * sx;
                            y = (y - cy) * sy;
                        }
                        Emit($"{Fmt(x)} {Fmt(y)}");
                    }
                    break;

                case "H":
                    while (i < tokens.Count && tokens[i] is double)
                    {
                        var x = Num();
                        x = isRel ? x * sx : (x - cx) * sx;
                        Emit(Fmt(x));
                    }
                    break;

                case "V":
                    while (i < tokens.Count && tokens[i] is double)
                    {
                        var y = Num();
                        y = isRel ? y * sy : (y - cy) * sy;
                        Emit(Fmt(y));
                    }
                    break;

                case "C":
                    while (i < tokens.Count && tokens[i] is double)
                    {
                        var x1 = Num();
                        var y1 = Num();
                        var x2 = Num();
                        var y2 = Num();
                        var x = Num();
                        var y = Num();
                        if (isRel)
                        {
                            x1 *= sx;
                            y1 *= sy;
                            x2 *= sx;
                            y2 *= sy;
                            x *= sx;
                            y *= sy;
                        }
                        else
                        {
                            x1 = (x1 - cx) * sx;
                            y1 = (y1 - cy) * sy;
                            x2 = (x2 - cx) * sx;
                            y2 = (y2 - cy) * sy;
                            x = (x - cx) * sx;
                            y = (y - cy) * sy;
                        }
                        Emit(
                            $"{Fmt(x1)} {Fmt(y1)} {Fmt(x2)} {Fmt(y2)} {Fmt(x)} {Fmt(y)}"
                        );
                    }
                    break;

                case "S":
                    while (i < tokens.Count && tokens[i] is double)
                    {
                        var x2 = Num();
                        var y2 = Num();
                        var x = Num();
                        var y = Num();
                        if (isRel)
                        {
                            x2 *= sx;
                            y2 *= sy;
                            x *= sx;
                            y *= sy;
                        }
                        else
                        {
                            x2 = (x2 - cx) * sx;
                            y2 = (y2 - cy) * sy;
                            x = (x - cx) * sx;
                            y = (y - cy) * sy;
                        }
                        Emit($"{Fmt(x2)} {Fmt(y2)} {Fmt(x)} {Fmt(y)}");
                    }
                    break;

                case "Q":
                    while (i < tokens.Count && tokens[i] is double)
                    {
                        var x1 = Num();
                        var y1 = Num();
                        var x = Num();
                        var y = Num();
                        if (isRel)
                        {
                            x1 *= sx;
                            y1 *= sy;
                            x *= sx;
                            y *= sy;
                        }
                        else
                        {
                            x1 = (x1 - cx) * sx;
                            y1 = (y1 - cy) * sy;
                            x = (x - cx) * sx;
                            y = (y - cy) * sy;
                        }
                        Emit($"{Fmt(x1)} {Fmt(y1)} {Fmt(x)} {Fmt(y)}");
                    }
                    break;

                case "A":
                    while (i < tokens.Count && tokens[i] is double)
                    {
                        var rx = Num();
                        var ry = Num();
                        var rotation = Num();
                        var largeArc = Num();
                        var sweep = Num();
                        var x = Num();
                        var y = Num();
                        rx *= sx;
                        ry *= sy;
                        if (isRel)
                        {
                            x *= sx;
                            y *= sy;
                        }
                        else
                        {
                            x = (x - cx) * sx;
                            y = (y - cy) * sy;
                        }
                        Emit(
                            $"{Fmt(rx)} {Fmt(ry)} {Fmt(rotation)} {Fmt(largeArc)} {Fmt(sweep)} {Fmt(x)} {Fmt(y)}"
                        );
                    }
                    break;

                case "Z":
                    // No parameters
                    break;
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Constructs a LayoutDevice for the specified placed device, including its position, orientation, and bounding box.
    /// </summary>
    /// <param name="circuit">The circuit data used to look up device metadata.</param>
    /// <param name="deviceId">The identifier of the device to build layout for.</param>
    /// <param name="cell">The grid cell where the device is placed.</param>
    /// <param name="placement">Coarse placement results used to compute device bounding boxes and orientation defaults.</param>
    /// <param name="renderByName">Lookup of render entities by name; when present for the device, its orientation overrides defaults.</param>
    /// <param name="terminalsByDevice">Routing terminal positions grouped by device ID, used to compute the terminal centroid as device position.</param>
    /// <returns>A LayoutDevice with Id, Position (terminal centroid in render units), Orientation (from render entity or cell), and computed Bbox.</returns>
    private static LayoutDevice BuildLayoutDevice(
        Circuit circuit,
        string deviceId,
        GridCell cell,
        CoarseGridResult placement,
        IReadOnlyDictionary<string, RenderEntity> renderByName,
        IReadOnlyDictionary<string, TerminalPosition[]> terminalsByDevice
    )
    {
        var device = circuit.Fill?.Devices.FirstOrDefault(d => d.Id == deviceId);
        var type = device?.DeviceType.ToLowerInvariant() ?? "unknown";

        var defaultOrientation = BuildDefaultDeviceOrientation(type, deviceId, cell, placement);
        var orientation =
            renderByName.TryGetValue(deviceId, out var render) && render.Orientation is not null
                ? new OrientationValue
                {
                    Rotate = render.Orientation.Rotate,
                    MirrorX = render.Orientation.MirrorX,
                }
                : defaultOrientation;

        // Position is the centroid of the device's routing terminal positions.
        // This ensures catalog terminal offsets (also centered at terminal centroid)
        // align exactly with routing-derived wire endpoints.
        PointValue position;
        if (
            terminalsByDevice.TryGetValue(deviceId, out var terminals)
            && terminals.Length > 0
        )
        {
            position = new PointValue
            {
                X = terminals.Average(t => ToRenderUnitsExact(t.X)),
                Y = terminals.Average(t => ToRenderUnitsExact(t.Y)),
            };
        }
        else
        {
            // Fallback to cell center when no routing terminals are available
            position = new PointValue
            {
                X = ToRenderUnitsExact(
                    (int)Math.Round(
                        DeviceGeometry.GetCellCenterX(cell.Column),
                        MidpointRounding.AwayFromZero
                    )
                ),
                Y = ToRenderUnitsExact(
                    (int)Math.Round(
                        DeviceGeometry.GetCellCenterY(cell.Row),
                        MidpointRounding.AwayFromZero
                    )
                ),
            };
        }

        return new LayoutDevice
        {
            Id = deviceId,
            Position = position,
            Orientation = orientation,
            Bbox = BuildDeviceBbox(type, position, placement, deviceId),
        };
    }

    /// <summary>
    /// Builds a LayoutPort for the given port by locating its routed terminal and determining the port side and position in render units.
    /// </summary>
    /// <param name="portName">The name of the port to build.</param>
    /// <param name="circuit">The circuit containing port metadata (used for side inference if not provided by render data).</param>
    /// <param name="renderByName">Mapping from render entity names to RenderEntity objects; if an entry for the port contains a Side, that side is used.</param>
    /// <param name="routing">Routing result containing terminal positions used to locate the port's routed terminal.</param>
    /// <returns>A LayoutPort containing the port's Name, Position (converted to render units), and Side.</returns>
    /// <exception cref="ApiException">Thrown with code "CASAPI-INVALID-REQUEST" when no routed terminal exists for the specified port.</exception>
    private static LayoutPort BuildLayoutPort(
        string portName,
        Circuit circuit,
        IReadOnlyDictionary<string, RenderEntity> renderByName,
        RoutingResult routing
    )
    {
        var terminal = routing.TerminalPositions.FirstOrDefault(t =>
            t.DeviceId == $"PORT_{portName}"
        );
        if (terminal is null)
        {
            throw new ApiException(
                "CASAPI-INVALID-REQUEST",
                $"Missing routed terminal for port '{portName}' in circuit '{circuit.Name}'."
            );
        }

        var side =
            renderByName.TryGetValue(portName, out var render) && render.Side is not null
                ? render.Side.Value.ToString().ToLowerInvariant()
                : InferPortSide(circuit, portName);
        var orientation = BuildPortOrientation(side);

        return new LayoutPort
        {
            Name = portName,
            Position = new PointValue
            {
                X = ToRenderUnitsExact(terminal.X),
                Y = ToRenderUnitsExact(terminal.Y),
            },
            Side = side,
            Orientation = orientation,
        };
    }

    /// <summary>
    /// Computes API orientation defaults from placer output when no explicit render orientation exists.
    /// </summary>
    private static OrientationValue BuildDefaultDeviceOrientation(
        string deviceType,
        string deviceId,
        GridCell cell,
        CoarseGridResult placement
    )
    {
        if (deviceType == "instance")
        {
            return new OrientationValue { Rotate = 0, MirrorX = false };
        }

        if (deviceType is "resistor" or "capacitor" or "inductor")
        {
            var horizontal = placement.HorizontalPassiveIds.Contains(deviceId);
            return new OrientationValue { Rotate = horizontal ? 0 : 90, MirrorX = false };
        }

        return new OrientationValue { Rotate = 0, MirrorX = cell.MirrorX };
    }

    /// <summary>
    /// Maps layout side hints to a frontend orientation transform for port symbols.
    /// </summary>
    private static OrientationValue BuildPortOrientation(string side)
    {
        return side switch
        {
            "right" => new OrientationValue { Rotate = 0, MirrorX = true },
            "top" => new OrientationValue { Rotate = 270, MirrorX = false },
            "bottom" => new OrientationValue { Rotate = 90, MirrorX = false },
            _ => new OrientationValue { Rotate = 0, MirrorX = false },
        };
    }

    /// <summary>
    /// Determines whether a point lies on an axis-aligned wire segment (inclusive of the segment endpoints).
    /// </summary>
    /// <param name="point">The point to test, given in grid coordinates.</param>
    /// <param name="segment">The wire segment with From and To endpoints; only horizontal or vertical segments are considered.</param>
    /// <returns>`true` if the point lies on the segment (including endpoints), `false` otherwise.</returns>
    private static bool IsPointOnSegment(GridPoint point, WireSegment segment)
    {
        if (segment.From.X == segment.To.X)
        {
            if (point.X != segment.From.X)
            {
                return false;
            }

            var minY = Math.Min(segment.From.Y, segment.To.Y);
            var maxY = Math.Max(segment.From.Y, segment.To.Y);
            return point.Y >= minY && point.Y <= maxY;
        }

        if (segment.From.Y == segment.To.Y)
        {
            if (point.Y != segment.From.Y)
            {
                return false;
            }

            var minX = Math.Min(segment.From.X, segment.To.X);
            var maxX = Math.Max(segment.From.X, segment.To.X);
            return point.X >= minX && point.X <= maxX;
        }

        return false;
    }

    /// <summary>
    /// Determine which side of the schematic a port should be placed for rendering.
    /// </summary>
    /// <param name="circuit">The circuit that contains the port definitions.</param>
    /// <param name="portName">The name of the port to infer the side for.</param>
    /// <returns>`right` if the named port exists and its direction is Output, `left` if it exists and is not Output, or `auto` if the port is not found.</returns>
    private static string InferPortSide(Circuit circuit, string portName)
    {
        var port = circuit.Ports.FirstOrDefault(p => p.Name == portName);
        if (port is null)
        {
            return "auto";
        }

        return port.Direction == PortDirection.Output ? "right" : "left";
    }

    /// <summary>
    /// Compute the device bounding box in render units, centered on the device position.
    /// </summary>
    /// <param name="deviceType">Device type string (e.g., "resistor", "capacitor", or other types such as MOSFET).</param>
    /// <param name="position">Device position in render units (terminal centroid).</param>
    /// <param name="placement">Coarse placement result used to determine horizontal passive membership.</param>
    /// <param name="deviceId">Identifier of the device; used to look up placement-specific decisions (for example, horizontal passive placement).</param>
    /// <returns>A <see cref="BboxValue"/> whose X, Y, Width, and Height are expressed in render units.</returns>
    private static BboxValue BuildDeviceBbox(
        string deviceType,
        PointValue position,
        CoarseGridResult placement,
        string deviceId
    )
    {
        const double rp = DeviceGeometry.RoutingPitch;

        if (deviceType is "resistor" or "capacitor")
        {
            var horizontal = placement.HorizontalPassiveIds.Contains(deviceId);
            // Symbol SVG is canonical horizontal (PassiveWidth x PassiveHeight).
            // Vertical passives are rotated 90°, swapping width/height.
            var width = horizontal
                ? DeviceGeometry.PassiveWidth / rp
                : DeviceGeometry.PassiveHeight / rp;
            var height = horizontal
                ? DeviceGeometry.PassiveHeight / rp
                : DeviceGeometry.PassiveWidth / rp;
            return new BboxValue
            {
                X = position.X - width / 2,
                Y = position.Y - height / 2,
                Width = width,
                Height = height,
            };
        }

        var mosW = DeviceGeometry.MosfetWidth / rp;
        var mosH = DeviceGeometry.MosfetHeight / rp;
        // The MOSFET symbol is asymmetric: Gate sits at topLeft+0.5px while
        // Drain/Source sit at topLeft+16.5px. Centering the bbox on the centroid
        // clips the Gate terminal. Use GetMosfetBboxOrigin to derive the correct
        // top-left from the centroid and the device's mirror state.
        var mirrorX = placement.DevicePlacements.TryGetValue(deviceId, out var gridCell) && gridCell.MirrorX;
        var (bboxX, bboxY) = DeviceGeometry.GetMosfetBboxOrigin(position.X, position.Y, mirrorX);
        return new BboxValue
        {
            X = bboxX,
            Y = bboxY,
            Width = mosW,
            Height = mosH,
        };
    }

    /// <summary>
    /// Convert a pixel measurement to exact render units (no rounding).
    /// Used for positions, terminals, and wire endpoints where sub-pixel precision
    /// is needed to align symbols with routing.
    /// </summary>
    private static double ToRenderUnitsExact(int pixels)
    {
        return pixels / (double)DeviceGeometry.RoutingPitch;
    }

    /// <inheritdoc cref="ToRenderUnitsExact(int)"/>
    private static double ToRenderUnitsExact(double pixels)
    {
        return pixels / DeviceGeometry.RoutingPitch;
    }

    [GeneratedRegex(@"([MmLlHhVvCcSsQqTtAaZz])|([+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?)")]
    private static partial Regex SvgPathTokenRegex();
}
