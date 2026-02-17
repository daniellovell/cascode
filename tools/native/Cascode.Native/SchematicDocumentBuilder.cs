using Cascode.Language;

namespace Cascode.Native;

internal static class SchematicDocumentBuilder
{
    /// <summary>
    /// Builds a SchematicDocumentResponse for the current circuit in the provided DocumentState using the specified render mode and relaxation setting.
    /// </summary>
    /// <param name="state">The current DocumentState containing the document, circuits, selected circuit name, document id, and revision.</param>
    /// <param name="mode">Controls how existing render information is treated when computing the schematic (e.g., respect, reflow, or re-render from scratch).</param>
    /// <param name="allowRelaxation">If true, permits the constraint resolver to relax placement/routing constraints to produce a valid render.</param>
    /// <returns>
    /// A SchematicDocumentResponse containing document identifiers, the circuit name, render source metadata, structural projection, layout projection, render cache, and diagnostics.
    /// </returns>
    /// <exception cref="ApiException">Thrown with code "CASAPI-INVALID-REQUEST" when the circuit named by state.CircuitName is not found in state.Document.</exception>
    public static SchematicDocumentResponse Build(
        DocumentState state,
        RenderSchematicMode mode,
        bool allowRelaxation
    )
    {
        var circuit = state.Document.Circuits.FirstOrDefault(c => c.Name == state.CircuitName);
        if (circuit is null)
        {
            throw new ApiException(
                "CASAPI-INVALID-REQUEST",
                $"Circuit '{state.CircuitName}' was not found in document '{state.DocumentId}'."
            );
        }

        var effectiveRender = BuildEffectiveRender(circuit.Render, mode);

        var render = SchematicConstraintResolver.ComputeRender(
            state.Document,
            circuit,
            effectiveRender,
            allowRelaxation
        );

        if (mode == RenderSchematicMode.ReflowUnlocked)
        {
            // ReflowUnlocked intentionally mutates the shared circuit.Render reference so
            // downstream consumers of state.Document observe the immediate reflow update.
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

    /// <summary>
    /// Map a <see cref="RenderSchematicMode"/> value to the string used in the API.
    /// </summary>
    /// <returns>A string representing the mode: "respectRenderBlock", "reflowUnlocked", or "rerenderFromScratch".</returns>
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

    /// <summary>
    /// Produces an effective render block filtered according to the requested render mode.
    /// </summary>
    /// <param name="render">The existing render block to filter; may be null.</param>
    /// <param name="mode">The render mode that determines how the render block is treated.</param>
    /// <returns>
    /// A RenderBlock containing only entities that preserve hard placement or routing constraints (and their waypoints), or null if the mode forces a full re-render, the input is null, or no entities remain after filtering.
    /// </returns>
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
