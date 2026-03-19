using System.Text.Json;
using System.Text.Json.Nodes;
using Cascode.Language;

namespace Cascode.Native;

internal static class SourceApiDispatcher
{
    public static string RewriteSchematic(string requestJson)
    {
        using var document = JsonDocument.Parse(requestJson);
        var root = document.RootElement;
        var path = root.TryGetString("path") ?? "<api>";
        var text = root.RequireString("text");
        var circuit = root.TryGetString("circuit");
        var operations = ParseOperations(root.RequireProperty("operations"));

        try
        {
            var rewritten = SchematicSourceToolkit.Rewrite(path, text, operations, circuit);
            rewritten = RefreshManualRoutingIfNeeded(path, rewritten, operations, circuit);
            return new JsonObject
            {
                ["schema"] = "cascode.source.rewrite/1.0",
                ["sourceText"] = rewritten.SourceText,
            }.ToJsonString(ApiJson.Options);
        }
        catch (CascodeParseException ex)
        {
            throw ToParseFailure(ex);
        }
    }

    private static SchematicSourceRewriteResult RefreshManualRoutingIfNeeded(
        string path,
        SchematicSourceRewriteResult rewritten,
        IReadOnlyList<SchematicSourceOperation> operations,
        string? circuitName
    )
    {
        if (!RequiresManualRoutingRefresh(operations))
        {
            return rewritten;
        }

        var document = CascodeReader.Parse(rewritten.SourceText, path);
        var circuit = SelectCircuit(document, circuitName);
        if (circuit.Render?.Mode != RenderLayoutMode.Manual)
        {
            return rewritten;
        }

        var state = new DocumentState
        {
            DocumentId = "source-rewrite",
            SourceText = rewritten.SourceText,
            Document = document,
            CircuitName = circuit.Name,
            Revision = 1,
            ChangedEntities = Array.Empty<string>(),
        };
        var snapshot = ManualRenderSnapshotBuilder.BuildWithExactPlacementRouting(state, circuit);
        return SchematicSourceToolkit.Rewrite(
            path,
            rewritten.SourceText,
            new SchematicSourceOperation[]
            {
                new ApplyRenderSnapshotSourceOperation(RenderLayoutMode.Manual, snapshot.Entities),
            },
            circuit.Name
        );
    }

    private static bool RequiresManualRoutingRefresh(
        IReadOnlyList<SchematicSourceOperation> operations
    )
    {
        return operations.Any(operation =>
            operation switch
            {
                SetRenderModeSourceOperation mode => mode.Mode == RenderLayoutMode.Manual,
                PatchRenderEntitySourceOperation patch => PatchAffectsManualRouting(patch.Patch),
                SetDeviceParamSourceOperation => true,
                InsertRailSourceOperation => true,
                RemoveRailSourceOperation => true,
                DeleteDeviceSourceOperation => true,
                ConnectEndpointsSourceOperation => true,
                DisconnectEndpointsSourceOperation => true,
                _ => false,
            }
        );
    }

    private static bool PatchAffectsManualRouting(RenderEntityPatch patch)
    {
        if (patch.Place is not null || patch.Orientation is not null || patch.Side is not null)
        {
            return true;
        }

        if (patch.ClearFields is null)
        {
            return false;
        }

        return patch.ClearFields.Contains(RenderEntityField.Place)
            || patch.ClearFields.Contains(RenderEntityField.Orientation)
            || patch.ClearFields.Contains(RenderEntityField.Side);
    }

    private static Circuit SelectCircuit(CascodeDocument document, string? requestedName)
    {
        if (!string.IsNullOrWhiteSpace(requestedName))
        {
            var requested = document.Circuits.FirstOrDefault(circuit =>
                circuit.Name == requestedName
            );
            if (requested is not null)
            {
                return requested;
            }
        }

        var selected = document.Circuits.FirstOrDefault(circuit =>
            !circuit.Inline && circuit.Level is CascodeLevel.EL or CascodeLevel.ML
        );
        if (selected is not null)
        {
            return selected;
        }

        throw new InvalidOperationException("No non-inline EL/ML circuit available.");
    }

    private static ApiException ToParseFailure(CascodeParseException error)
    {
        var first = error.Diagnostics.FirstOrDefault(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
        );
        return new ApiException(
            "CASAPI-PARSE-FAILED",
            first?.Message ?? error.Message,
            new JsonObject
            {
                ["line"] = first?.Line,
                ["column"] = first?.Column,
                ["diagnostics"] = new JsonArray(
                    error.Diagnostics.Select(diagnostic => (JsonNode?)diagnostic.Message).ToArray()
                ),
            }
        );
    }

    private static IReadOnlyList<SchematicSourceOperation> ParseOperations(JsonElement operations)
    {
        if (operations.ValueKind != JsonValueKind.Array)
        {
            throw new ApiException("CASAPI-INVALID-REQUEST", "Expected 'operations' array.");
        }

        return operations.EnumerateArray().Select(ParseOperation).ToArray();
    }

    private static SchematicSourceOperation ParseOperation(JsonElement operation)
    {
        return operation.RequireString("type") switch
        {
            "setRenderMode" => new SetRenderModeSourceOperation(
                ParseRenderLayoutMode(operation.RequireString("mode"))
            ),
            "patchRenderEntity" => new PatchRenderEntitySourceOperation(
                operation.RequireString("name"),
                ParseRenderEntityPatch(operation)
            ),
            "applyRenderSnapshot" => new ApplyRenderSnapshotSourceOperation(
                ParseRenderLayoutMode(operation.RequireString("mode")),
                ParseRenderEntities(operation.RequireProperty("entities"))
            ),
            "removeRenderEntities" => new RemoveRenderEntitiesSourceOperation(
                ParseStringArray(operation.RequireProperty("names"))
            ),
            "setDeviceParam" => new SetDeviceParamSourceOperation(
                operation.RequireString("deviceId"),
                operation.RequireString("param"),
                operation.RequireString("value")
            ),
            "insertRail" => new InsertRailSourceOperation(
                ParseRailKind(operation.RequireString("kind")),
                operation.RequireString("name")
            ),
            "removeRail" => new RemoveRailSourceOperation(
                ParseRailKind(operation.RequireString("kind")),
                operation.RequireString("name")
            ),
            "deleteDevice" => new DeleteDeviceSourceOperation(operation.RequireString("deviceId")),
            "connectEndpoints" => new ConnectEndpointsSourceOperation(
                operation.RequireString("from"),
                operation.RequireString("to")
            ),
            "disconnectEndpoints" => new DisconnectEndpointsSourceOperation(
                operation.RequireString("from"),
                operation.RequireString("to")
            ),
            var kind => throw new ApiException(
                "CASAPI-INVALID-REQUEST",
                $"Unsupported source operation '{kind}'."
            ),
        };
    }

    private static RenderEntityPatch ParseRenderEntityPatch(JsonElement operation)
    {
        return new RenderEntityPatch
        {
            Place = operation.TryGetProperty("place", out var place) ? ParsePlacement(place) : null,
            Orientation = operation.TryGetProperty("orientation", out var orientation)
                ? ParseOrientation(orientation)
                : null,
            Side = operation.TryGetProperty("side", out var side)
                ? ParsePortSide(side.GetString())
                : null,
            Route = operation.TryGetProperty("route", out var route) ? ParseRoute(route) : null,
            Segments = operation.TryGetProperty("segments", out var segments)
                ? ParseSegments(segments)
                : null,
            ZIndex =
                operation.TryGetProperty("zIndex", out var zIndex)
                && zIndex.TryGetInt32(out var value)
                    ? value
                    : null,
            ClearFields = operation.TryGetProperty("clear", out var clear)
                ? ParseClearFields(clear)
                : null,
        };
    }

    private static IReadOnlyList<RenderEntity> ParseRenderEntities(JsonElement entities)
    {
        if (entities.ValueKind != JsonValueKind.Array)
        {
            throw new ApiException("CASAPI-INVALID-REQUEST", "Expected 'entities' array.");
        }

        return entities.EnumerateArray().Select(ParseRenderEntity).ToArray();
    }

    private static RenderEntity ParseRenderEntity(JsonElement element)
    {
        var entity = new RenderEntity { Name = element.RequireString("name") };
        if (element.TryGetProperty("place", out var place))
        {
            entity.Place = ParsePlacement(place);
        }

        if (element.TryGetProperty("orientation", out var orientation))
        {
            entity.Orientation = ParseOrientation(orientation);
        }

        if (element.TryGetProperty("side", out var side))
        {
            entity.Side = ParsePortSide(side.GetString());
        }

        if (element.TryGetProperty("route", out var route))
        {
            entity.Route = ParseRoute(route);
        }

        if (element.TryGetProperty("segments", out var segments))
        {
            foreach (var segment in ParseSegments(segments))
            {
                entity.Segments.Add(segment);
            }
        }

        if (element.TryGetProperty("zIndex", out var zIndex) && zIndex.TryGetInt32(out var value))
        {
            entity.ZIndex = value;
        }

        return entity;
    }

    private static RenderPlacement ParsePlacement(JsonElement element)
    {
        return new RenderPlacement
        {
            Point = ParsePoint(element.RequireProperty("point")),
            Strength = element.TryGetProperty("strength", out var strength)
                ? ParseStrength(strength.GetString())
                : null,
        };
    }

    private static RenderOrientation ParseOrientation(JsonElement element)
    {
        return new RenderOrientation
        {
            Rotate = element.RequireInt("rotate"),
            MirrorX =
                element.TryGetProperty("mirrorX", out var mirror)
                && mirror.ValueKind == JsonValueKind.True,
        };
    }

    private static RenderRoute ParseRoute(JsonElement element)
    {
        return new RenderRoute
        {
            Mode = ParseRouteMode(element.RequireString("mode")),
            Strength = element.TryGetProperty("strength", out var strength)
                ? ParseStrength(strength.GetString())
                : null,
        };
    }

    private static IReadOnlyList<RenderSegment> ParseSegments(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new ApiException("CASAPI-INVALID-REQUEST", "Expected 'segments' array.");
        }

        return element
            .EnumerateArray()
            .Select(segment => new RenderSegment
            {
                From = ParsePoint(segment.RequireProperty("from")),
                To = ParsePoint(segment.RequireProperty("to")),
            })
            .ToArray();
    }

    private static RenderPointExpression ParsePoint(JsonElement element)
    {
        return element.RequireString("kind") switch
        {
            "abs" => new RenderAbsPoint(element.RequireInt("x"), element.RequireInt("y")),
            "ref" => new RenderRefPoint(
                element.RequireString("anchor"),
                element.TryGetProperty("dx", out var dx) ? ReadInt(dx, "dx") : 0,
                element.TryGetProperty("dy", out var dy) ? ReadInt(dy, "dy") : 0
            ),
            "rel" => new RenderRelPoint(element.RequireInt("dx"), element.RequireInt("dy")),
            var kind => throw new ApiException(
                "CASAPI-INVALID-REQUEST",
                $"Unsupported point kind '{kind}'."
            ),
        };
    }

    private static IReadOnlySet<RenderEntityField> ParseClearFields(JsonElement clear)
    {
        if (clear.ValueKind != JsonValueKind.Array)
        {
            throw new ApiException("CASAPI-INVALID-REQUEST", "Expected 'clear' array.");
        }

        return clear
            .EnumerateArray()
            .Select(entry => ParseRenderEntityField(entry.GetString()))
            .ToHashSet();
    }

    private static IReadOnlyList<string> ParseStringArray(JsonElement values)
    {
        if (values.ValueKind != JsonValueKind.Array)
        {
            throw new ApiException("CASAPI-INVALID-REQUEST", "Expected array value.");
        }

        return values
            .EnumerateArray()
            .Select(entry =>
                entry.ValueKind == JsonValueKind.String
                    ? entry.GetString()!
                    : throw new ApiException(
                        "CASAPI-INVALID-REQUEST",
                        "Expected string array value."
                    )
            )
            .ToArray();
    }

    private static int ReadInt(JsonElement element, string name)
    {
        return element.TryGetInt32(out var value)
            ? value
            : throw new ApiException("CASAPI-INVALID-REQUEST", $"Missing integer field '{name}'.");
    }

    private static RenderLayoutMode ParseRenderLayoutMode(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "auto" => RenderLayoutMode.Auto,
            "manual" => RenderLayoutMode.Manual,
            _ => throw new ApiException(
                "CASAPI-INVALID-REQUEST",
                $"Unsupported render mode '{value}'."
            ),
        };
    }

    private static SchematicRailKind ParseRailKind(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "supply" => SchematicRailKind.Supply,
            "ground" => SchematicRailKind.Ground,
            _ => throw new ApiException(
                "CASAPI-INVALID-REQUEST",
                $"Unsupported rail kind '{value}'."
            ),
        };
    }

    private static RenderPortSide? ParsePortSide(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "left" => RenderPortSide.Left,
            "right" => RenderPortSide.Right,
            "top" => RenderPortSide.Top,
            "bottom" => RenderPortSide.Bottom,
            "auto" => RenderPortSide.Auto,
            null => null,
            _ => throw new ApiException(
                "CASAPI-INVALID-REQUEST",
                $"Unsupported port side '{value}'."
            ),
        };
    }

    private static RenderRouteMode ParseRouteMode(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "auto" => RenderRouteMode.Auto,
            "ortho" => RenderRouteMode.Ortho,
            _ => throw new ApiException(
                "CASAPI-INVALID-REQUEST",
                $"Unsupported route mode '{value}'."
            ),
        };
    }

    private static RenderConstraintStrength? ParseStrength(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "hard" => RenderConstraintStrength.Hard,
            "soft" => RenderConstraintStrength.Soft,
            "hint" => RenderConstraintStrength.Hint,
            null => null,
            _ => throw new ApiException(
                "CASAPI-INVALID-REQUEST",
                $"Unsupported strength '{value}'."
            ),
        };
    }

    private static RenderEntityField ParseRenderEntityField(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "place" => RenderEntityField.Place,
            "orientation" => RenderEntityField.Orientation,
            "side" => RenderEntityField.Side,
            "route" => RenderEntityField.Route,
            "segments" => RenderEntityField.Segments,
            "zindex" => RenderEntityField.ZIndex,
            _ => throw new ApiException(
                "CASAPI-INVALID-REQUEST",
                $"Unsupported render field '{value}'."
            ),
        };
    }
}
