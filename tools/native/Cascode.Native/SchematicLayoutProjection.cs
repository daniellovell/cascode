using Cascode.Language;
using Cascode.Render.Analysis;
using Cascode.Render.Layout;
using Cascode.Render.Placement;
using Cascode.Render.Routing;

namespace Cascode.Native;

internal static class SchematicLayoutProjection
{
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
                    .Junctions.Select(junction => new PointValue
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

    private static LayoutPort BuildLayoutPort(
        string portName,
        Circuit circuit,
        IReadOnlyDictionary<string, RenderEntity> renderByName,
        RoutingResult routing
    )
    {
        var terminal = routing.TerminalPositions.First(t => t.DeviceId == $"PORT_{portName}");
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

    private static string InferPortSide(Circuit circuit, string portName)
    {
        var port = circuit.Ports.FirstOrDefault(p => p.Name == portName);
        if (port is null)
        {
            return "auto";
        }

        return port.Direction == PortDirection.Output ? "right" : "left";
    }

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

    private static int ToRenderUnits(int pixels)
    {
        return (int)
            Math.Round(pixels / (double)DeviceGeometry.RoutingPitch, MidpointRounding.AwayFromZero);
    }
}
