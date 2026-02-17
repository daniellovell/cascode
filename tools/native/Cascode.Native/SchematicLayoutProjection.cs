using Cascode.Language;
using Cascode.Render.Analysis;
using Cascode.Render.Layout;
using Cascode.Render.Placement;
using Cascode.Render.Routing;

namespace Cascode.Native;

internal static class SchematicLayoutProjection
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

        var devices = placement
            .DevicePlacements.OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry =>
                BuildLayoutDevice(circuit, entry.Key, entry.Value, placement, renderByName)
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
                            X = ToRenderUnits(segment.From.X),
                            Y = ToRenderUnits(segment.From.Y),
                        },
                        To = new PointValue
                        {
                            X = ToRenderUnits(segment.To.X),
                            Y = ToRenderUnits(segment.To.Y),
                        },
                    })
                    .ToArray(),
                Junctions = routing
                    .Junctions.Where(junction =>
                        entry.Value.Any(segment => IsPointOnSegment(junction, segment))
                    )
                    .Select(junction => new PointValue
                    {
                        X = ToRenderUnits(junction.X),
                        Y = ToRenderUnits(junction.Y),
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
                    X = ToRenderUnits(terminal.X),
                    Y = ToRenderUnits(terminal.Y),
                },
                StringComparer.Ordinal
            );
        }

        var bboxes = new Dictionary<string, BboxValue>(StringComparer.Ordinal);
        foreach (var (deviceId, cell) in placement.DevicePlacements)
        {
            var device = circuit.Fill?.Devices.FirstOrDefault(d => d.Id == deviceId);
            var type = device?.DeviceType.ToLowerInvariant() ?? "unknown";
            bboxes[deviceId] = BuildDeviceBbox(type, cell, placement, deviceId);
        }

        return new RenderCacheInfo { TerminalPoints = terminals, ComputedBboxes = bboxes };
    }

    /// <summary>
    /// Constructs a LayoutDevice for the specified placed device, including its position, orientation, and bounding box.
    /// </summary>
    /// <param name="circuit">The circuit data used to look up device metadata.</param>
    /// <param name="deviceId">The identifier of the device to build layout for.</param>
    /// <param name="cell">The grid cell where the device is placed.</param>
    /// <param name="placement">Coarse placement results used to compute device bounding boxes and orientation defaults.</param>
    /// <param name="renderByName">Lookup of render entities by name; when present for the device, its orientation overrides defaults.</param>
    /// <returns>A LayoutDevice with Id, Position (cell center in render units), Orientation (from render entity or cell), and computed Bbox.</returns>
    private static LayoutDevice BuildLayoutDevice(
        Circuit circuit,
        string deviceId,
        GridCell cell,
        CoarseGridResult placement,
        IReadOnlyDictionary<string, RenderEntity> renderByName
    )
    {
        var device = circuit.Fill?.Devices.FirstOrDefault(d => d.Id == deviceId);
        var type = device?.DeviceType.ToLowerInvariant() ?? "unknown";

        var orientation = renderByName.TryGetValue(deviceId, out var render)
            ? new OrientationValue
            {
                Rotate = render.Orientation?.Rotate ?? 0,
                MirrorX = render.Orientation?.MirrorX ?? cell.MirrorX,
            }
            : new OrientationValue { Rotate = 0, MirrorX = cell.MirrorX };

        return new LayoutDevice
        {
            Id = deviceId,
            Position = new PointValue
            {
                X = ToRenderUnits((int)Math.Round(DeviceGeometry.GetCellCenterX(cell.Column))),
                Y = ToRenderUnits((int)Math.Round(DeviceGeometry.GetCellCenterY(cell.Row))),
            },
            Orientation = orientation,
            Bbox = BuildDeviceBbox(type, cell, placement, deviceId),
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

        return new LayoutPort
        {
            Name = portName,
            Position = new PointValue
            {
                X = ToRenderUnits(terminal.X),
                Y = ToRenderUnits(terminal.Y),
            },
            Side = side,
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
    /// Compute the device bounding box in render units using the device type and placement information.
    /// </summary>
    /// <param name="deviceType">Device type string (e.g., "resistor", "capacitor", or other types such as MOSFET).</param>
    /// <param name="cell">Grid cell specifying the device's row, column, and mirror flag.</param>
    /// <param name="placement">Coarse placement result used to determine orientation, symmetry, and horizontal passive membership.</param>
    /// <param name="deviceId">Identifier of the device; used to look up placement-specific decisions (for example, horizontal passive placement).</param>
    /// <returns>A <see cref="BboxValue"/> whose X, Y, Width, and Height are expressed in render units.</returns>
    private static BboxValue BuildDeviceBbox(
        string deviceType,
        GridCell cell,
        CoarseGridResult placement,
        string deviceId
    )
    {
        if (deviceType is "resistor" or "capacitor")
        {
            var horizontal = placement.HorizontalPassiveIds.Contains(deviceId);
            if (horizontal)
            {
                var leftOfAxis = cell.Column < placement.SymmetryAxis;
                var p = DeviceGeometry.GetHorizontalPassivePlacement(
                    cell.Row,
                    cell.Column,
                    placement.ColumnCount,
                    leftOfAxis
                );
                return new BboxValue
                {
                    X = ToRenderUnits((int)Math.Round(p.X)),
                    Y = ToRenderUnits((int)Math.Round(p.Y)),
                    Width = ToRenderUnits((int)Math.Round(DeviceGeometry.PassiveWidth)),
                    Height = ToRenderUnits((int)Math.Round(DeviceGeometry.PassiveHeight)),
                };
            }

            var passive = DeviceGeometry.GetPassivePlacement(cell.Row, cell.Column);
            return new BboxValue
            {
                X = ToRenderUnits((int)Math.Round(passive.X)),
                Y = ToRenderUnits((int)Math.Round(passive.Y)),
                Width = ToRenderUnits((int)Math.Round(DeviceGeometry.PassiveWidth)),
                Height = ToRenderUnits((int)Math.Round(DeviceGeometry.PassiveHeight)),
            };
        }

        var mos = DeviceGeometry.GetMosfetPlacement(cell.Row, cell.Column, cell.MirrorX);
        return new BboxValue
        {
            X = ToRenderUnits((int)Math.Round(mos.X)),
            Y = ToRenderUnits((int)Math.Round(mos.Y)),
            Width = ToRenderUnits((int)Math.Round(DeviceGeometry.MosfetWidth)),
            Height = ToRenderUnits((int)Math.Round(DeviceGeometry.MosfetHeight)),
        };
    }

    /// <summary>
    /// Convert a pixel measurement to render units using the routing pitch.
    /// </summary>
    /// <returns>The number of render units corresponding to the given pixel value, rounded to the nearest integer with midpoint values rounded away from zero.</returns>
    private static int ToRenderUnits(int pixels)
    {
        return (int)
            Math.Round(pixels / (double)DeviceGeometry.RoutingPitch, MidpointRounding.AwayFromZero);
    }
}