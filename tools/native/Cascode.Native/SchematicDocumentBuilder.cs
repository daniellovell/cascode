using Cascode.Language;

namespace Cascode.Native;

internal static class SchematicDocumentBuilder
{
    /// <summary>
    /// Builds a SchematicDocumentResponse for the current circuit in the provided DocumentState using the specified render mode and relaxation setting.
    /// </summary>
    /// <param name="state">The current DocumentState containing the document, circuits, selected circuit name, document id, and revision.</param>
    /// <param name="mode">Controls whether rendering should respect the source document mode or force auto/manual semantics for this render.</param>
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

        var structural = SchematicLayoutProjection.BuildStructural(circuit, render.Graph);

        return new SchematicDocumentResponse
        {
            DocumentId = state.DocumentId,
            Revision = state.Revision,
            Circuit = circuit.Name,
            RenderSource = new RenderSourceInfo
            {
                HasRenderBlock = effectiveRender is not null,
                Mode = FormatMode(effectiveRender),
            },
            Structural = structural,
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
            SymbolCatalog = SchematicLayoutProjection.BuildSymbolCatalog(structural),
            Diagnostics = render
                .Diagnostics.Select(message => new ApiDiagnostic
                {
                    Severity = "warning",
                    Code = "CASAPI-RENDER-DIAGNOSTIC",
                    Message = message,
                })
                .ToArray(),
        };
    }

    /// <summary>
    /// Map the effective render block mode to the string used in the API.
    /// </summary>
    /// <returns>`manual` when the effective render block is manual; otherwise `auto`.</returns>
    private static string FormatMode(RenderBlock? render)
    {
        return render?.Mode == RenderLayoutMode.Manual ? "manual" : "auto";
    }

    /// <summary>
    /// Produces an effective render block according to the requested render mode.
    /// </summary>
    /// <param name="render">The existing render block to filter; may be null.</param>
    /// <param name="mode">The render mode that determines how the render block is treated.</param>
    /// <returns>
    /// A RenderBlock whose mode reflects the requested render semantics, or null when the source had no render block and auto mode was requested.
    /// </returns>
    private static RenderBlock? BuildEffectiveRender(RenderBlock? render, RenderSchematicMode mode)
    {
        if (mode == RenderSchematicMode.RespectDocument)
        {
            return render;
        }

        if (render is null)
        {
            return mode == RenderSchematicMode.Manual
                ? new RenderBlock { Mode = RenderLayoutMode.Manual }
                : null;
        }

        var forcedMode =
            mode == RenderSchematicMode.Manual ? RenderLayoutMode.Manual : RenderLayoutMode.Auto;
        return new RenderBlock { Mode = forcedMode, Entities = render.Entities.ToList() };
    }
}
