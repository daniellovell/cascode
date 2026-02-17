using System.Text.Json.Nodes;
using Cascode.Language;
using Cascode.Render.Analysis;
using Cascode.Render.Layout;
using Cascode.Render.Placement;
using Cascode.Render.Routing;

namespace Cascode.Native;

internal static class SchematicConstraintResolver
{
    /// <summary>
    /// Computes placement and routing for a circuit and returns the resulting render state and diagnostics.
    /// </summary>
    /// <param name="document">The source document used to resolve attachments and flatten the circuit.</param>
    /// <param name="circuit">The circuit to compute rendering for.</param>
    /// <param name="render">Optional render block that supplies placement and route specifications; null to compute without user constraints.</param>
    /// <param name="allowRelaxation">When true, allows generated constraint sets to be relaxed to attempt a satisfiable placement/routing.</param>
    /// <returns>
    /// A RenderComputationState containing the circuit graph, final placement, final routing, and any diagnostics produced during computation.
    /// </returns>
    /// <exception cref="ApiException">Thrown with code "CASAPI-SOLVER-UNSAT" when placement constraints are unsatisfiable; the exception's payload includes the related entities.</exception>
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

    /// <summary>
    /// Builds a placement constraint set from the provided render block using resolved anchors.
    /// </summary>
    /// <param name="render">Optional render block containing device placement specifications.</param>
    /// <param name="anchors">Mapping of anchor names to resolved render-space points used to evaluate placement points.</param>
    /// <param name="allowRelaxation">If true, the returned constraint set will allow constraint relaxation.</param>
    /// <param name="diagnostics">Collection to receive human-readable diagnostics for any placement points that could not be resolved.</param>
    /// <returns>The <see cref="PlacementConstraintSet"/> populated with device placement constraints, or <c>null</c> if no placement constraints were produced or <paramref name="render"/> is null.</returns>
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

    /// <summary>
    /// Constructs routing constraints for nets declared in the provided render block by resolving each net's waypoints into grid coordinates.
    /// </summary>
    /// <param name="render">The optional render block containing net route definitions; when null, no constraints are produced.</param>
    /// <param name="anchors">Mapping of anchor names to resolved render-space points used to evaluate waypoint expressions.</param>
    /// <param name="allowRelaxation">When true, allows produced route constraints to be marked as relaxable.</param>
    /// <param name="diagnostics">A list that will be appended with messages for any unresolved waypoints encountered while building constraints.</param>
    /// <returns>
    /// A <see cref="RouteConstraintSet"/> containing net route constraints and the relaxation flag, or null if no net routes were defined or none could be resolved.
    /// </returns>
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

    /// <summary>
    /// Resolves a RenderPointExpression to a render-space PointValue.
    /// </summary>
    /// <param name="point">The expression to resolve (absolute, anchor reference, or relative).</param>
    /// <param name="anchors">Mapping of anchor names to render-space points used for reference lookups.</param>
    /// <param name="previous">The previously resolved point used when evaluating relative expressions; may be null.</param>
    /// <returns>
    /// The resolved PointValue when the expression can be evaluated; `null` if the referenced anchor is missing,
    /// the expression is a relative point with no previous value, or the expression kind is unsupported.
    /// </returns>
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

    /// <summary>
    /// Builds a map of anchor names to render-space coordinates using the circuit, device placement, and routing results.
    /// </summary>
    /// <param name="circuit">The circuit definition whose ports must be present in the resulting anchor map.</param>
    /// <param name="placement">Coarse placement result providing device cell positions.</param>
    /// <param name="routing">Routing result providing canvas dimensions and terminal positions.</param>
    /// <returns>A read-only dictionary that maps anchor names (e.g., "canvas origin", device IDs, port names, or "DeviceId.Terminal") to their corresponding <see cref="PointValue"/> in render units.</returns>
    /// <exception cref="ApiException">Thrown when a circuit port does not have a corresponding routed terminal position in the routing results.</exception>
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
            if (!map.ContainsKey(port.Name))
            {
                throw new ApiException(
                    "CASAPI-INVALID-REQUEST",
                    $"Missing routed terminal position for port '{port.Name}'."
                );
            }
        }

        return map;
    }

    /// <summary>
    /// Converts a length in pixels to render-space routing units.
    /// </summary>
    /// <param name="pixels">Length in pixels.</param>
    /// <returns>The equivalent length in render units, rounded to the nearest integer with .5 values rounded away from zero.</returns>
    private static int ToRenderUnits(int pixels)
    {
        return (int)
            Math.Round(pixels / (double)DeviceGeometry.RoutingPitch, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Convert render units to pixels.
    /// </summary>
    /// <param name="renderUnits">The value in render units.</param>
    /// <returns>The equivalent value in pixels.</returns>
    private static int ToPixels(int renderUnits)
    {
        return renderUnits * DeviceGeometry.RoutingPitch;
    }
}
