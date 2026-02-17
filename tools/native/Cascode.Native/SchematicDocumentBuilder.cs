using Cascode.Language;

namespace Cascode.Native;

internal static class SchematicDocumentBuilder
{
    public static SchematicDocumentResponse Build(
        DocumentState state,
        RenderSchematicMode mode,
        bool allowRelaxation
    )
    {
        var circuit = state.Document.Circuits.First(c => c.Name == state.CircuitName);
        var effectiveRender = BuildEffectiveRender(circuit.Render, mode);

        var render = SchematicConstraintResolver.ComputeRender(
            state.Document,
            circuit,
            effectiveRender,
            allowRelaxation
        );

        if (mode == RenderSchematicMode.ReflowUnlocked)
        {
            circuit.Render = effectiveRender;
        }

        return new SchematicDocumentResponse
        {
            DocumentId = state.DocumentId,
            Revision = state.Revision,
            Circuit = circuit.Name,
            RenderSource = new RenderSourceInfo
            {
                HasRenderBlock = effectiveRender is not null,
                Mode = FormatMode(mode),
            },
            Structural = SchematicLayoutProjection.BuildStructural(circuit, render.Graph),
            Layout = SchematicLayoutProjection.BuildLayout(
                circuit,
                effectiveRender,
                render.Placement,
                render.Routing
            ),
            RenderCache = SchematicLayoutProjection.BuildRenderCache(
                circuit,
                render.Placement,
                render.Routing
            ),
            Diagnostics = render
                .Diagnostics.Select(message => new ApiDiagnostic
                {
                    Code = "CASAPI-DIAGNOSTIC",
                    Message = message,
                })
                .ToArray(),
        };
    }

    private static string FormatMode(RenderSchematicMode mode)
    {
        return mode switch
        {
            RenderSchematicMode.RespectRenderBlock => "respectRenderBlock",
            RenderSchematicMode.ReflowUnlocked => "reflowUnlocked",
            RenderSchematicMode.RerenderFromScratch => "rerenderFromScratch",
            _ => "respectRenderBlock",
        };
    }

    private static RenderBlock? BuildEffectiveRender(RenderBlock? render, RenderSchematicMode mode)
    {
        if (mode == RenderSchematicMode.RerenderFromScratch)
        {
            return null;
        }

        if (render is null || mode == RenderSchematicMode.RespectRenderBlock)
        {
            return render;
        }

        var filtered = new List<RenderEntity>();
        foreach (var entity in render.Entities)
        {
            var clone = new RenderEntity
            {
                Name = entity.Name,
                Kind = entity.Kind,
                Orientation = entity.Orientation,
                Side = entity.Side,
                ZIndex = entity.ZIndex,
            };

            if (entity.Place?.Strength == RenderConstraintStrength.Hard)
            {
                clone.Place = entity.Place;
            }

            if (entity.Route?.Strength == RenderConstraintStrength.Hard)
            {
                clone.Route = entity.Route;
            }

            if (
                entity.Route?.Strength == RenderConstraintStrength.Hard
                && entity.Waypoints.Count > 0
            )
            {
                clone.Waypoints.AddRange(entity.Waypoints);
            }

            if (clone.Place is not null || clone.Route is not null || clone.Waypoints.Count > 0)
            {
                filtered.Add(clone);
            }
        }

        return filtered.Count == 0 ? null : new RenderBlock { Entities = filtered };
    }
}
