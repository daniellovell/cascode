using System.Text.Json;
using System.Text.Json.Nodes;
using Cascode.Language;
using Cascode.Render.Analysis;
using Cascode.Render.Layout;
using Cascode.Render.Routing;

namespace Cascode.Native;

internal sealed record RouteEndpoint(string Kind, string? Token, int X, int Y);

internal static class SchematicWorkflowService
{
    public static void ApplyPlacementEdits(
        DocumentState state,
        IReadOnlyList<JsonElement> operations,
        HashSet<string> changed
    )
    {
        if (operations.Count == 0)
        {
            return;
        }

        ManualRenderSnapshotService.EnsureManualRender(state);
        foreach (var operation in operations)
        {
            SchematicOperationApplier.Apply(state, operation, changed);
        }
        ManualRenderSnapshotService.RefreshManualRender(state);
    }

    public static void ApplyRouteEdit(
        DocumentState state,
        string mode,
        RouteEndpoint start,
        RouteEndpoint end,
        HashSet<string> changed
    )
    {
        var operation = BuildConnectionOperation(mode, start, end);
        SchematicOperationApplier.Apply(state, operation, changed);
        ManualRenderSnapshotService.RefreshManualRender(state);
    }

    public static RoutePreviewResponse PreviewRoute(
        DocumentState state,
        string mode,
        RouteEndpoint start,
        RouteEndpoint target
    )
    {
        if (target.Kind == "point")
        {
            return BuildPointPreview(start, target);
        }

        try
        {
            var previewState = DocumentStateTransactions.Clone(state);
            var operation = BuildConnectionOperation(mode, start, target);
            SchematicOperationApplier.Apply(
                previewState,
                operation,
                new HashSet<string>(StringComparer.Ordinal)
            );
            ManualRenderSnapshotService.RefreshManualRender(previewState);

            var circuit = FindCircuit(previewState);
            var render = SchematicConstraintResolver.ComputeRender(
                previewState.Document,
                circuit,
                circuit.Render,
                allowRelaxation: false
            );
            var affectedNets = ResolveAffectedNetNames(render.Graph, start, target);
            var nets = BuildNetGroups(render.Routing, affectedNets);
            var segments = nets.SelectMany(net => net.Segments).ToArray();
            return segments.Length > 0
                ? new RoutePreviewResponse
                {
                    Valid = true,
                    Segments = segments,
                    Nets = nets,
                }
                : new RoutePreviewResponse
                {
                    Valid = false,
                    Diagnostic = "No routed preview is available for the current target.",
                    Segments = Array.Empty<SegmentValue>(),
                    Nets = Array.Empty<RoutePreviewNet>(),
                };
        }
        catch (ApiException ex)
        {
            return new RoutePreviewResponse
            {
                Valid = false,
                Diagnostic = ex.Message,
                Segments = Array.Empty<SegmentValue>(),
                Nets = Array.Empty<RoutePreviewNet>(),
            };
        }
        catch (InvalidOperationException ex)
        {
            return new RoutePreviewResponse
            {
                Valid = false,
                Diagnostic = ex.Message,
                Segments = Array.Empty<SegmentValue>(),
                Nets = Array.Empty<RoutePreviewNet>(),
            };
        }
    }

    private static RoutePreviewResponse BuildPointPreview(RouteEndpoint start, RouteEndpoint target)
    {
        if (start.X == target.X && start.Y == target.Y)
        {
            return new RoutePreviewResponse
            {
                Valid = false,
                Diagnostic = "Route preview requires a non-zero-length target.",
                Segments = Array.Empty<SegmentValue>(),
                Nets = Array.Empty<RoutePreviewNet>(),
            };
        }

        if (start.X == target.X || start.Y == target.Y)
        {
            var segment = new SegmentValue
            {
                From = new PointValue { X = start.X, Y = start.Y },
                To = new PointValue { X = target.X, Y = target.Y },
            };
            return new RoutePreviewResponse
            {
                Valid = true,
                Segments = new[] { segment },
                Nets = Array.Empty<RoutePreviewNet>(),
            };
        }

        var first = new SegmentValue
        {
            From = new PointValue { X = start.X, Y = start.Y },
            To = new PointValue { X = target.X, Y = start.Y },
        };
        var second = new SegmentValue
        {
            From = new PointValue { X = target.X, Y = start.Y },
            To = new PointValue { X = target.X, Y = target.Y },
        };
        return new RoutePreviewResponse
        {
            Valid = true,
            Segments = new[] { first, second },
            Nets = Array.Empty<RoutePreviewNet>(),
        };
    }

    private static IReadOnlyList<string> ResolveAffectedNetNames(
        CircuitGraph graph,
        RouteEndpoint start,
        RouteEndpoint end
    )
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        AddResolvedNetName(names, graph, start);
        AddResolvedNetName(names, graph, end);
        return names.OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }

    private static void AddResolvedNetName(
        ISet<string> names,
        CircuitGraph graph,
        RouteEndpoint endpoint
    )
    {
        if (string.IsNullOrWhiteSpace(endpoint.Token))
        {
            return;
        }

        if (graph.NetConnections.ContainsKey(endpoint.Token))
        {
            names.Add(endpoint.Token);
            return;
        }

        var separator = endpoint.Token.IndexOf('.');
        if (separator <= 0 || separator >= endpoint.Token.Length - 1)
        {
            names.Add(endpoint.Token);
            return;
        }

        var deviceId = endpoint.Token[..separator];
        var terminal = endpoint.Token[(separator + 1)..];
        var netName = graph.GetNetForTerminal(deviceId, terminal);
        if (!string.IsNullOrWhiteSpace(netName))
        {
            names.Add(netName);
        }
    }

    private static IReadOnlyList<RoutePreviewNet> BuildNetGroups(
        RoutingResult routing,
        IReadOnlyList<string> netNames
    )
    {
        return netNames
            .Where(netName => routing.SegmentsByNet.ContainsKey(netName))
            .Select(netName => new RoutePreviewNet
            {
                Name = netName,
                Segments = routing
                    .SegmentsByNet[netName]
                    .Select(segment => new SegmentValue
                    {
                        From = new PointValue
                        {
                            X = segment.From.X / (double)DeviceGeometry.RoutingPitch,
                            Y = segment.From.Y / (double)DeviceGeometry.RoutingPitch,
                        },
                        To = new PointValue
                        {
                            X = segment.To.X / (double)DeviceGeometry.RoutingPitch,
                            Y = segment.To.Y / (double)DeviceGeometry.RoutingPitch,
                        },
                    })
                    .ToArray(),
            })
            .ToArray();
    }

    private static JsonElement BuildConnectionOperation(
        string mode,
        RouteEndpoint start,
        RouteEndpoint end
    )
    {
        var from = RequireEndpointToken(start, "start");
        var to = RequireEndpointToken(end, "end");
        if (string.Equals(from, to, StringComparison.Ordinal))
        {
            throw new ApiException(
                "CASAPI-INVALID-REQUEST",
                "Route workflow requires distinct connection endpoints."
            );
        }

        var type = mode switch
        {
            "connect" => "connectTerminals",
            "disconnect" => "disconnectTerminals",
            _ => throw new ApiException(
                "CASAPI-INVALID-REQUEST",
                $"Invalid route workflow mode '{mode}'."
            ),
        };

        var operation = new JsonObject
        {
            ["opId"] = $"workflow-{type}",
            ["type"] = type,
            ["from"] = from,
            ["to"] = to,
        };
        using var document = JsonDocument.Parse(operation.ToJsonString());
        return document.RootElement.Clone();
    }

    private static string RequireEndpointToken(RouteEndpoint endpoint, string fieldName)
    {
        if (
            endpoint.Kind is "terminal" or "netAnchor"
            && !string.IsNullOrWhiteSpace(endpoint.Token)
        )
        {
            return endpoint.Token;
        }

        throw new ApiException(
            "CASAPI-INVALID-REQUEST",
            $"Route workflow requires '{fieldName}' to reference a terminal or net anchor."
        );
    }

    private static Circuit FindCircuit(DocumentState state)
    {
        var circuit = state.Document.Circuits.FirstOrDefault(c => c.Name == state.CircuitName);
        if (circuit is null)
        {
            throw new ApiException(
                "CASAPI-INVALID-REQUEST",
                $"Circuit '{state.CircuitName}' was not found in document '{state.DocumentId}'."
            );
        }

        return circuit;
    }
}
