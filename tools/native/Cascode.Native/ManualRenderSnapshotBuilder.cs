using Cascode.Language;

namespace Cascode.Native;

internal static class ManualRenderSnapshotBuilder
{
    public static RenderBlock Build(
        DocumentState state,
        Circuit circuit,
        RenderBlock? renderOverride = null
    )
    {
        var render = renderOverride ?? circuit.Render;
        var computation = SchematicConstraintResolver.ComputeRender(
            state.Document,
            circuit,
            render,
            allowRelaxation: false
        );
        return BuildFromComputation(circuit, render, computation);
    }

    public static RenderBlock BuildWithExactPlacementRouting(DocumentState state, Circuit circuit)
    {
        var render =
            circuit.Render
            ?? throw new ApiException(
                "CASAPI-MANUAL-SNAPSHOT-FAILED",
                $"Manual snapshot could not find a render block for circuit '{circuit.Name}'."
            );
        var computation = SchematicConstraintResolver.ComputeExactManualPlacementRouting(
            state.Document,
            circuit,
            render
        );
        return BuildFromComputation(circuit, render, computation);
    }

    private static RenderBlock BuildFromComputation(
        Circuit circuit,
        RenderBlock? render,
        RenderComputationState computation
    )
    {
        var layout = SchematicLayoutProjection.BuildLayout(
            circuit,
            render,
            computation.Placement,
            computation.Routing
        );
        var anchors = SchematicConstraintResolver.BuildAnchorMap(
            circuit,
            computation.Placement,
            computation.Routing
        );
        var layoutDevicesById = layout.Devices.ToDictionary(
            device => device.Id,
            StringComparer.Ordinal
        );
        var entities = new Dictionary<string, RenderEntity>(StringComparer.Ordinal);

        foreach (
            var deviceId in computation.Graph.Devices.Keys.OrderBy(id => id, StringComparer.Ordinal)
        )
        {
            if (!layoutDevicesById.TryGetValue(deviceId, out var device))
            {
                throw new ApiException(
                    "CASAPI-MANUAL-SNAPSHOT-FAILED",
                    $"Manual snapshot could not find computed layout for device '{deviceId}'."
                );
            }

            var entity = GetOrCreateEntity(entities, deviceId, RenderEntityKind.Device);
            entity.Place = BuildPlacement(device.Position);
            entity.Orientation = new RenderOrientation
            {
                Rotate = device.Orientation.Rotate,
                MirrorX = device.Orientation.MirrorX,
            };
        }

        foreach (var port in layout.Ports.OrderBy(entry => entry.Name, StringComparer.Ordinal))
        {
            var entity = GetOrCreateEntity(entities, port.Name, RenderEntityKind.Port);
            entity.Place = BuildPlacement(port.Position);
            entity.Side = ParsePortSide(port.Name, port.Side);
        }

        foreach (var net in layout.Nets.OrderBy(entry => entry.Name, StringComparer.Ordinal))
        {
            var entity = GetOrCreateEntity(entities, net.Name, RenderEntityKind.Net);
            entity.Route = new RenderRoute
            {
                Mode = RenderRouteMode.Ortho,
                Strength = RenderConstraintStrength.Hard,
            };
            entity.Segments.Clear();
            entity.Segments.AddRange(BuildSegments(net.Segments, anchors));
        }

        EnsureSnapshotCompleteness(circuit, computation.Graph, entities);

        return new RenderBlock
        {
            Mode = RenderLayoutMode.Manual,
            Entities = entities
                .Values.OrderBy(entity => entity.Name, StringComparer.Ordinal)
                .ToList(),
        };
    }

    private static RenderEntity GetOrCreateEntity(
        IDictionary<string, RenderEntity> entities,
        string name,
        RenderEntityKind preferredKind
    )
    {
        if (entities.TryGetValue(name, out var existing))
        {
            if (preferredKind == RenderEntityKind.Port || existing.Kind == RenderEntityKind.Unknown)
            {
                existing.Kind = preferredKind;
            }

            return existing;
        }

        var created = new RenderEntity { Name = name, Kind = preferredKind };
        entities[name] = created;
        return created;
    }

    private static RenderPlacement BuildPlacement(PointValue point)
    {
        return new RenderPlacement
        {
            Point = BuildPoint(point),
            Strength = RenderConstraintStrength.Hard,
        };
    }

    private static RenderAbsPoint BuildPoint(PointValue point)
    {
        return new RenderAbsPoint(RoundToRenderUnit(point.X), RoundToRenderUnit(point.Y));
    }

    private static IEnumerable<RenderSegment> BuildSegments(
        IReadOnlyList<SegmentValue> segments,
        IReadOnlyDictionary<string, PointValue> anchors
    )
    {
        return segments
            .Select(segment => BuildSegment(segment, anchors))
            .Where(segment => segment is not null)
            .Select(segment => segment!)
            .GroupBy(segment => $"{DescribePoint(segment.From)}->{DescribePoint(segment.To)}")
            .Select(group => group.First())
            .OrderBy(segment => DescribePoint(segment.From), StringComparer.Ordinal)
            .ThenBy(segment => DescribePoint(segment.To), StringComparer.Ordinal);
    }

    private static RenderSegment? BuildSegment(
        SegmentValue segment,
        IReadOnlyDictionary<string, PointValue> anchors
    )
    {
        var from = BuildSegmentPoint(segment.From, anchors);
        var to = BuildSegmentPoint(segment.To, anchors);
        if (!Equals(from, to))
        {
            return new RenderSegment { From = from, To = to };
        }

        if (segment.From.X == segment.To.X && segment.From.Y == segment.To.Y)
        {
            return null;
        }

        return new RenderSegment { From = from, To = BuildCollapsedSegmentStub(segment) };
    }

    private static RenderAbsPoint BuildCollapsedSegmentStub(SegmentValue segment)
    {
        var x = RoundToRenderUnit(segment.From.X);
        var y = RoundToRenderUnit(segment.From.Y);
        var deltaX = segment.To.X - segment.From.X;
        var deltaY = segment.To.Y - segment.From.Y;

        if (Math.Abs(deltaX) >= Math.Abs(deltaY))
        {
            x += deltaX < 0 ? -1 : 1;
        }
        else
        {
            y += deltaY < 0 ? -1 : 1;
        }

        return new RenderAbsPoint(x, y);
    }

    private static RenderPointExpression BuildSegmentPoint(
        PointValue point,
        IReadOnlyDictionary<string, PointValue> anchors
    )
    {
        var x = RoundToRenderUnit(point.X);
        var y = RoundToRenderUnit(point.Y);
        var anchor = anchors
            .Where(entry =>
                RoundToRenderUnit(entry.Value.X) == x && RoundToRenderUnit(entry.Value.Y) == y
            )
            .OrderBy(entry => GetAnchorPriority(entry.Key))
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .FirstOrDefault();

        return anchor.Key is null ? new RenderAbsPoint(x, y) : new RenderRefPoint(anchor.Key, 0, 0);
    }

    private static int GetAnchorPriority(string anchor)
    {
        if (anchor.Contains('.', StringComparison.Ordinal))
        {
            return 0;
        }

        return anchor.StartsWith("canvas ", StringComparison.Ordinal) ? 2 : 1;
    }

    private static string DescribePoint(RenderPointExpression point)
    {
        return point switch
        {
            RenderAbsPoint abs => $"abs:{abs.X}:{abs.Y}",
            RenderRefPoint reference => $"ref:{reference.Anchor}:{reference.Dx}:{reference.Dy}",
            RenderRelPoint relative => $"rel:{relative.Dx}:{relative.Dy}",
            _ => point.GetType().Name,
        };
    }

    private static int RoundToRenderUnit(double value)
    {
        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private static RenderPortSide ParsePortSide(string portName, string rawSide)
    {
        return rawSide.ToLowerInvariant() switch
        {
            "left" => RenderPortSide.Left,
            "right" => RenderPortSide.Right,
            "top" => RenderPortSide.Top,
            "bottom" => RenderPortSide.Bottom,
            _ => throw new ApiException(
                "CASAPI-MANUAL-SNAPSHOT-FAILED",
                $"Manual snapshot requires an explicit side for port '{portName}'."
            ),
        };
    }

    private static void EnsureSnapshotCompleteness(
        Circuit circuit,
        Cascode.Render.Analysis.CircuitGraph graph,
        IReadOnlyDictionary<string, RenderEntity> entities
    )
    {
        foreach (var deviceId in graph.Devices.Keys)
        {
            if (!entities.TryGetValue(deviceId, out var entity) || entity.Place is null)
            {
                throw new ApiException(
                    "CASAPI-MANUAL-SNAPSHOT-FAILED",
                    $"Manual snapshot requires an explicit place for device '{deviceId}'."
                );
            }
        }

        foreach (var port in CircuitPortExpander.Expand(circuit))
        {
            if (
                !entities.TryGetValue(port.Name, out var entity)
                || entity.Place is null
                || entity.Side is null
            )
            {
                throw new ApiException(
                    "CASAPI-MANUAL-SNAPSHOT-FAILED",
                    $"Manual snapshot requires an explicit place and side for port '{port.Name}'."
                );
            }
        }

        foreach (
            var netName in graph
                .NetConnections.Where(entry => entry.Value.Count > 0)
                .Select(entry => entry.Key)
                .OrderBy(name => name, StringComparer.Ordinal)
        )
        {
            if (!entities.TryGetValue(netName, out var entity) || entity.Segments.Count == 0)
            {
                throw new ApiException(
                    "CASAPI-MANUAL-SNAPSHOT-FAILED",
                    $"Manual snapshot requires at least one seg for net '{netName}'."
                );
            }
        }
    }
}
