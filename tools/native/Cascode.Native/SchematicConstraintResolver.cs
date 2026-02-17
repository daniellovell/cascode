using System.Text.Json.Nodes;
using Cascode.Language;
using Cascode.Render.Analysis;
using Cascode.Render.Layout;
using Cascode.Render.Placement;
using Cascode.Render.Routing;

namespace Cascode.Native;

internal static class SchematicConstraintResolver
{
    public static RenderComputationState ComputeRender(
        CascodeDocument document,
        Circuit circuit,
        RenderBlock? render,
        bool allowRelaxation
    )
    {
        var diagnostics = new List<string>();
        var attach = new AttachResolver(document).Resolve();
        var resolution = attach.CircuitResults.GetValueOrDefault(circuit.Name);
        var flattened = CircuitFlattener.Flatten(circuit, document, resolution);
        var graph = CircuitGraph.Build(flattened);
        var topology = TopologyAnalyzer.Analyze(graph);

        var baselinePlacement = CoarseGridPlacer.Place(topology, graph);
        var baselineRouting = MazeRouter.Route(baselinePlacement, graph);
        var baselineAnchors = BuildAnchorMap(circuit, baselinePlacement, baselineRouting);

        var placementConstraints = BuildPlacementConstraints(
            render,
            baselineAnchors,
            allowRelaxation,
            diagnostics
        );

        CoarseGridResult placement;
        try
        {
            placement = CoarseGridPlacer.Place(topology, graph, placementConstraints);
        }
        catch (RenderConstraintUnsatException ex)
        {
            throw new ApiException(
                "CASAPI-SOLVER-UNSAT",
                ex.Message,
                new JsonObject
                {
                    ["entities"] = new JsonArray(
                        ex.Entities.Select(entity => (JsonNode?)entity).ToArray()
                    ),
                }
            );
        }

        var routingBaseline = MazeRouter.Route(placement, graph);
        var anchors = BuildAnchorMap(circuit, placement, routingBaseline);
        var routeConstraints = BuildRouteConstraints(render, anchors, allowRelaxation, diagnostics);
        var routing = MazeRouter.Route(placement, graph, routeConstraints);

        return new RenderComputationState
        {
            Graph = graph,
            Placement = placement,
            Routing = routing,
            Diagnostics = diagnostics,
        };
    }

    private static PlacementConstraintSet? BuildPlacementConstraints(
        RenderBlock? render,
        IReadOnlyDictionary<string, PointValue> anchors,
        bool allowRelaxation,
        List<string> diagnostics
    )
    {
        if (render is null)
        {
            return null;
        }

        var constraints = new List<DevicePlacementConstraint>();
        foreach (
            var entity in render.Entities.Where(entry => entry.Kind == RenderEntityKind.Device)
        )
        {
            if (entity.Place is null)
            {
                continue;
            }

            var point = EvaluatePoint(entity.Place.Point, anchors, previous: null);
            if (point is null)
            {
                diagnostics.Add($"Could not resolve placement point for '{entity.Name}'.");
                continue;
            }

            constraints.Add(
                new DevicePlacementConstraint(
                    entity.Name,
                    point.X,
                    point.Y,
                    entity.Place.Strength ?? RenderConstraintStrength.Soft
                )
            );
        }

        if (constraints.Count == 0)
        {
            return null;
        }

        return new PlacementConstraintSet
        {
            DevicePlacements = constraints,
            AllowConstraintRelaxation = allowRelaxation,
        };
    }

    private static RouteConstraintSet? BuildRouteConstraints(
        RenderBlock? render,
        IReadOnlyDictionary<string, PointValue> anchors,
        bool allowRelaxation,
        List<string> diagnostics
    )
    {
        if (render is null)
        {
            return null;
        }

        var netRoutes = new Dictionary<string, NetRouteConstraint>(StringComparer.Ordinal);
        foreach (var entity in render.Entities.Where(entry => entry.Kind == RenderEntityKind.Net))
        {
            if (entity.Waypoints.Count == 0)
            {
                continue;
            }

            PointValue? previous = null;
            var points = new List<GridPoint>();
            foreach (var waypoint in entity.Waypoints)
            {
                var resolved = EvaluatePoint(waypoint, anchors, previous);
                if (resolved is null)
                {
                    diagnostics.Add($"Could not resolve waypoint for net '{entity.Name}'.");
                    continue;
                }

                previous = resolved;
                points.Add(new GridPoint(ToPixels(resolved.X), ToPixels(resolved.Y)));
            }

            if (points.Count == 0)
            {
                continue;
            }

            netRoutes[entity.Name] = new NetRouteConstraint(
                entity.Name,
                points,
                entity.Route?.Strength ?? RenderConstraintStrength.Soft,
                entity.Route?.Mode ?? RenderRouteMode.Auto
            );
        }

        if (netRoutes.Count == 0)
        {
            return null;
        }

        return new RouteConstraintSet
        {
            NetRoutes = netRoutes,
            AllowConstraintRelaxation = allowRelaxation,
        };
    }

    private static PointValue? EvaluatePoint(
        RenderPointExpression point,
        IReadOnlyDictionary<string, PointValue> anchors,
        PointValue? previous
    )
    {
        switch (point)
        {
            case RenderAbsPoint abs:
                return new PointValue { X = abs.X, Y = abs.Y };

            case RenderRefPoint @ref:
                if (!anchors.TryGetValue(@ref.Anchor, out var anchor))
                {
                    return null;
                }

                return new PointValue { X = anchor.X + @ref.Dx, Y = anchor.Y + @ref.Dy };

            case RenderRelPoint rel when previous is not null:
                return new PointValue { X = previous.X + rel.Dx, Y = previous.Y + rel.Dy };

            default:
                return null;
        }
    }

    internal static IReadOnlyDictionary<string, PointValue> BuildAnchorMap(
        Circuit circuit,
        CoarseGridResult placement,
        RoutingResult routing
    )
    {
        var map = new Dictionary<string, PointValue>(StringComparer.Ordinal)
        {
            ["canvas origin"] = new PointValue { X = 0, Y = 0 },
            ["canvas center"] = new PointValue
            {
                X = ToRenderUnits(routing.CanvasWidth / 2),
                Y = ToRenderUnits(routing.CanvasHeight / 2),
            },
        };

        foreach (var (deviceId, cell) in placement.DevicePlacements)
        {
            map[deviceId] = new PointValue
            {
                X = ToRenderUnits((int)Math.Round(DeviceGeometry.GetCellCenterX(cell.Column))),
                Y = ToRenderUnits((int)Math.Round(DeviceGeometry.GetCellCenterY(cell.Row))),
            };
        }

        foreach (var terminal in routing.TerminalPositions)
        {
            if (terminal.DeviceId.StartsWith("PORT_", StringComparison.Ordinal))
            {
                var portName = terminal.DeviceId[5..];
                map[portName] = new PointValue
                {
                    X = ToRenderUnits(terminal.X),
                    Y = ToRenderUnits(terminal.Y),
                };
                continue;
            }

            map[$"{terminal.DeviceId}.{terminal.Terminal}"] = new PointValue
            {
                X = ToRenderUnits(terminal.X),
                Y = ToRenderUnits(terminal.Y),
            };
        }

        foreach (var port in circuit.Ports)
        {
            map.TryAdd(port.Name, new PointValue { X = 0, Y = 0 });
        }

        return map;
    }

    private static int ToRenderUnits(int pixels)
    {
        return (int)
            Math.Round(pixels / (double)DeviceGeometry.RoutingPitch, MidpointRounding.AwayFromZero);
    }

    private static int ToPixels(int renderUnits)
    {
        return renderUnits * DeviceGeometry.RoutingPitch;
    }
}
