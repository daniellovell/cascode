using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cascode.Language;

internal sealed partial class CascodeAstBuilder
{
    /// <summary>
    /// Builds a RenderBlock from a render section parse context.
    /// </summary>
    /// <param name="ctx">Parser context for a render section containing one or more render entities.</param>
    /// <returns>A RenderBlock whose Entities list contains the parsed RenderEntity objects in source order; duplicate entity names are skipped and corresponding diagnostics are emitted.</returns>
    private RenderBlock BuildRenderBlock(CascodeParser.RenderSectionContext ctx)
    {
        var block = new RenderBlock();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entityCtx in ctx.renderEntity())
        {
            var entityName = BuildRenderEntityRef(entityCtx.renderEntityRef());
            if (!seen.Add(entityName))
            {
                AddDiagnostic(
                    entityCtx,
                    DiagnosticSeverity.Error,
                    $"CAS3201: Duplicate render entity '{entityName}'."
                );
                continue;
            }

            var entity = new RenderEntity { Name = entityName };

            if (entityCtx.renderOneLiner() is { } oneLiner)
            {
                HandleRenderOneLiner(entity, oneLiner);
                block.Entities.Add(entity);
                continue;
            }

            foreach (var fieldCtx in entityCtx.renderField())
            {
                ProcessRenderField(entity, fieldCtx);
            }

            block.Entities.Add(entity);
        }

        return block;
    }

    /// <summary>
    /// Sets the entity's placement using the one-liner's point expression and strength level.
    /// </summary>
    /// <param name="entity">The render entity to modify.</param>
    /// <param name="oneLiner">The one-liner parse context containing the point expression and optional strength level.</param>
    private void HandleRenderOneLiner(
        RenderEntity entity,
        CascodeParser.RenderOneLinerContext oneLiner
    )
    {
        entity.Place = BuildRenderPlacement(oneLiner.pointExpr(), oneLiner.strengthLevel());
    }

    /// <summary>
    /// Applies the render field described by <paramref name="fieldCtx"/> to the specified <paramref name="entity"/>.
    /// </summary>
    /// <param name="entity">The render entity to modify.</param>
    /// <param name="fieldCtx">Parser context for a single render field; recognized field kinds include place, orient, z-index, side, route, and waypoints.</param>
    private void ProcessRenderField(RenderEntity entity, CascodeParser.RenderFieldContext fieldCtx)
    {
        if (fieldCtx.PLACE_KW() is not null)
        {
            ApplyPlace(entity, fieldCtx.pointExpr(0), fieldCtx.strengthLevel());
            return;
        }

        if (fieldCtx.ORIENT_KW() is not null)
        {
            ApplyOrientation(
                entity,
                fieldCtx.signedInt(),
                fieldCtx.MIRROR_KW() is not null,
                fieldCtx
            );
            return;
        }

        if (fieldCtx.ZINDEX_KW() is not null)
        {
            ApplyZIndex(entity, fieldCtx.signedInt(), fieldCtx);
            return;
        }

        if (fieldCtx.SIDE_KW() is not null)
        {
            ApplySide(entity, fieldCtx.IDENT().GetText(), fieldCtx);
            return;
        }

        if (fieldCtx.ROUTE_KW() is not null)
        {
            ApplyRoute(entity, fieldCtx.IDENT().GetText(), fieldCtx.strengthLevel(), fieldCtx);
            return;
        }

        if (fieldCtx.WP_KW() is not null)
        {
            ApplyWaypoints(entity, fieldCtx.pointExpr());
        }
    }

    /// <summary>
    /// Set the entity's placement using the specified point expression and optional strength level.
    /// </summary>
    /// <param name="entity">The render entity to update.</param>
    /// <param name="pointCtx">The point expression context used to build the placement.</param>
    /// <param name="strengthCtx">Optional strength level context that influences the placement constraint.</param>
    private void ApplyPlace(
        RenderEntity entity,
        CascodeParser.PointExprContext pointCtx,
        CascodeParser.StrengthLevelContext? strengthCtx
    )
    {
        entity.Place = BuildRenderPlacement(pointCtx, strengthCtx);
    }

    /// <summary>
    /// Set the entity's orientation by parsing a rotation value from the provided signed-int context and applying the horizontal mirror flag.
    /// </summary>
    /// <param name="entity">The render entity whose <c>Orientation</c> will be assigned.</param>
    /// <param name="signedIntCtx">Parser context that contains the rotation value to parse.</param>
    /// <param name="mirrorX">Whether the entity should be mirrored horizontally.</param>
    /// <param name="diagCtx">Parser context used as the location for reporting diagnostics if the rotation cannot be parsed.</param>
    private void ApplyOrientation(
        RenderEntity entity,
        CascodeParser.SignedIntContext signedIntCtx,
        bool mirrorX,
        Antlr4.Runtime.ParserRuleContext diagCtx
    )
    {
        var rotate = ParseSignedInt(signedIntCtx, diagCtx, "orientation rotation");
        entity.Orientation = new RenderOrientation { Rotate = rotate, MirrorX = mirrorX };
    }

    /// <summary>
    /// Assigns the entity's ZIndex by parsing the provided signed integer context.
    /// </summary>
    /// <param name="entity">The render entity to update.</param>
    /// <param name="signedIntCtx">Parser context containing the z-index integer literal.</param>
    /// <param name="diagCtx">Parser context used as the diagnostic location if parsing fails.</param>
    private void ApplyZIndex(
        RenderEntity entity,
        CascodeParser.SignedIntContext signedIntCtx,
        Antlr4.Runtime.ParserRuleContext diagCtx
    )
    {
        entity.ZIndex = ParseSignedInt(signedIntCtx, diagCtx, "zindex");
    }

    /// <summary>
    /// Validates a raw side identifier and assigns the parsed port side to the render entity or reports an error.
    /// </summary>
    /// <param name="entity">The render entity to update.</param>
    /// <param name="rawSide">The raw side string to parse (e.g., "left", "right", "top", "bottom", "auto").</param>
    /// <param name="diagCtx">Parser context used to report a diagnostic if parsing fails.</param>
    private void ApplySide(
        RenderEntity entity,
        string rawSide,
        Antlr4.Runtime.ParserRuleContext diagCtx
    )
    {
        if (!TryParsePortSide(rawSide, out var side))
        {
            AddDiagnostic(
                diagCtx,
                DiagnosticSeverity.Error,
                $"CAS3202: Invalid render side '{rawSide}'."
            );
            return;
        }

        entity.Side = side;
    }

    /// <summary>
    /// Sets the RenderEntity's Route based on a route mode string and optional strength, and reports an error if the mode is invalid.
    /// </summary>
    /// <param name="entity">The render entity to modify.</param>
    /// <param name="rawRouteMode">The raw route mode identifier text to parse (e.g., "auto", "ortho").</param>
    /// <param name="strengthCtx">Optional parse context for the strength level used to build the route's Strength; may be null.</param>
    /// <param name="diagCtx">Parser context used to locate diagnostics if the route mode is invalid.</param>
    private void ApplyRoute(
        RenderEntity entity,
        string rawRouteMode,
        CascodeParser.StrengthLevelContext? strengthCtx,
        Antlr4.Runtime.ParserRuleContext diagCtx
    )
    {
        if (!TryParseRouteMode(rawRouteMode, out var routeMode))
        {
            AddDiagnostic(
                diagCtx,
                DiagnosticSeverity.Error,
                $"CAS3203: Invalid render route mode '{rawRouteMode}'."
            );
            return;
        }

        entity.Route = new RenderRoute { Mode = routeMode, Strength = BuildStrength(strengthCtx) };
    }

    /// <summary>
    /// Populate the entity's Waypoints by converting each provided point expression into a RenderPointExpression.
    /// </summary>
    /// <param name="entity">The RenderEntity whose Waypoints will be replaced.</param>
    /// <param name="pointExprs">Sequence of point expression contexts to convert into waypoint expressions, applied in order.</param>
    private void ApplyWaypoints(
        RenderEntity entity,
        IEnumerable<CascodeParser.PointExprContext> pointExprs
    )
    {
        entity.Waypoints.Clear();
        foreach (var pointCtx in pointExprs)
        {
            entity.Waypoints.Add(BuildPointExpression(pointCtx));
        }
    }

    /// <summary>
    /// Constructs a fully qualified render-entity name by joining the identifier parts with dots.
    /// </summary>
    /// <param name="ctx">Parser context containing one or more identifier parts for the render entity.</param>
    /// <returns>The concatenated render entity name, with id parts separated by '.'</returns>
    private static string BuildRenderEntityRef(CascodeParser.RenderEntityRefContext ctx)
    {
        return string.Join(".", ctx.idPart().Select(part => part.GetText()));
    }

    /// <summary>
    /// Create a RenderPlacement from a point expression and an optional strength level.
    /// </summary>
    /// <param name="pointCtx">The parse context for the point expression used to build the placement's Point.</param>
    /// <param name="strengthCtx">Optional parse context for the strength level; when null the placement's Strength will be null.</param>
    /// <returns>A RenderPlacement with Point and Strength derived from the provided contexts.</returns>
    private RenderPlacement BuildRenderPlacement(
        CascodeParser.PointExprContext pointCtx,
        CascodeParser.StrengthLevelContext? strengthCtx
    )
    {
        return new RenderPlacement
        {
            Point = BuildPointExpression(pointCtx),
            Strength = BuildStrength(strengthCtx),
        };
    }

    /// <summary>
    /// Builds a RenderPointExpression from the given point expression parse context.
    /// </summary>
    /// <param name="ctx">The parse context containing an absolute, reference, or relative point expression.</param>
    /// <returns>
    /// A RenderPointExpression representing the parsed point. If the context is invalid, emits diagnostic CAS3204 and returns a RenderAbsPoint at (0, 0).
    /// </returns>
    private RenderPointExpression BuildPointExpression(CascodeParser.PointExprContext ctx)
    {
        if (ctx.absPoint() is { } abs)
        {
            var x = ParseSignedInt(abs.signedInt(0), abs, "abs x");
            var y = ParseSignedInt(abs.signedInt(1), abs, "abs y");
            return new RenderAbsPoint(x, y);
        }

        if (ctx.refPoint() is { } @ref)
        {
            var anchor = BuildRenderAnchorRef(@ref.renderAnchorRef());
            var signedInts = @ref.signedInt();
            var dx = signedInts.Length > 0 ? ParseSignedInt(signedInts[0], @ref, "ref dx") : 0;
            var dy = signedInts.Length > 1 ? ParseSignedInt(signedInts[1], @ref, "ref dy") : 0;
            return new RenderRefPoint(anchor, dx, dy);
        }

        if (ctx.relPoint() is { } rel)
        {
            var dx = ParseSignedInt(rel.signedInt(0), rel, "rel dx");
            var dy = ParseSignedInt(rel.signedInt(1), rel, "rel dy");
            return new RenderRelPoint(dx, dy);
        }

        AddDiagnostic(ctx, DiagnosticSeverity.Error, "CAS3204: Invalid render point expression.");
        return new RenderAbsPoint(0, 0);
    }

    /// <summary>
    /// Builds a textual anchor reference from a render anchor parse context.
    /// </summary>
    /// <param name="ctx">The parse context representing a render anchor reference.</param>
    /// <returns>
    /// The anchor reference string: "canvas origin" when the canvas origin keyword is present,
    /// "canvas center" when the canvas keyword is present without origin, or a pin reference string
    /// derived from the pin reference in the context.
    /// </returns>
    private static string BuildRenderAnchorRef(CascodeParser.RenderAnchorRefContext ctx)
    {
        if (ctx.CANVAS_KW() is not null)
        {
            return ctx.ORIGIN_KW() is not null ? "canvas origin" : "canvas center";
        }

        return BuildPinRef(ctx.pinRef());
    }

    /// <summary>
    /// Maps a parsed strength-level context to a corresponding RenderConstraintStrength value.
    /// </summary>
    /// <returns>
    /// `RenderConstraintStrength.Hard`, `RenderConstraintStrength.Soft`, or `RenderConstraintStrength.Hint` for recognized tokens; `null` if <paramref name="ctx"/> is null or the token is unrecognized.
    /// In the unrecognized-token case a CAS3205 diagnostic is emitted.
    /// </returns>
    private RenderConstraintStrength? BuildStrength(CascodeParser.StrengthLevelContext? ctx)
    {
        if (ctx is null)
        {
            return null;
        }

        if (ctx.HARD_KW() is not null)
        {
            return RenderConstraintStrength.Hard;
        }

        if (ctx.SOFT_KW() is not null)
        {
            return RenderConstraintStrength.Soft;
        }

        if (ctx.HINT_KW() is not null)
        {
            return RenderConstraintStrength.Hint;
        }

        AddDiagnostic(ctx, DiagnosticSeverity.Error, "CAS3205: Invalid render strength level.");
        return null;
    }

    /// <summary>
    /// Parses a port-side identifier string into a <see cref="RenderPortSide"/> value.
    /// </summary>
    /// <param name="raw">The input identifier to parse (case-insensitive). Valid values: "left", "right", "top", "bottom", "auto".</param>
    /// <param name="side">On success, receives the corresponding <see cref="RenderPortSide"/> value; otherwise receives the default value.</param>
    /// <returns>`true` if <paramref name="raw"/> matches one of the valid values; `false` otherwise.</returns>
    private static bool TryParsePortSide(string raw, out RenderPortSide side)
    {
        var normalized = raw.ToLowerInvariant();
        side = normalized switch
        {
            "left" => RenderPortSide.Left,
            "right" => RenderPortSide.Right,
            "top" => RenderPortSide.Top,
            "bottom" => RenderPortSide.Bottom,
            "auto" => RenderPortSide.Auto,
            _ => default,
        };

        return normalized is "left" or "right" or "top" or "bottom" or "auto";
    }

    /// <summary>
    /// Attempts to parse a route mode from the given string value.
    /// </summary>
    /// <param name="raw">The input string representing a route mode (case-insensitive).</param>
    /// <param name="mode">When this method returns, contains the parsed <see cref="RenderRouteMode"/> if parsing succeeded; otherwise the default value.</param>
    /// <returns>`true` if <paramref name="raw"/> corresponds to a supported mode (`"auto"` or `"ortho"`, case-insensitive); `false` otherwise.</returns>
    private static bool TryParseRouteMode(string raw, out RenderRouteMode mode)
    {
        var normalized = raw.ToLowerInvariant();
        mode = normalized switch
        {
            "auto" => RenderRouteMode.Auto,
            "ortho" => RenderRouteMode.Ortho,
            _ => default,
        };

        return normalized is "auto" or "ortho";
    }

    /// <summary>
    /// Parses an integer from the given signed-int parser context and reports a diagnostic if parsing fails.
    /// </summary>
    /// <param name="ctx">The parser context containing the signed integer token.</param>
    /// <param name="diagCtx">The parser context used as the location for any emitted diagnostic.</param>
    /// <param name="label">A human-readable label included in the diagnostic message on failure.</param>
    /// <returns>The parsed integer value, or 0 if parsing failed (a CAS3206 diagnostic is emitted).</returns>
    private int ParseSignedInt(
        CascodeParser.SignedIntContext ctx,
        Antlr4.Runtime.ParserRuleContext diagCtx,
        string label
    )
    {
        var text = ctx.GetText();
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        AddDiagnostic(
            diagCtx,
            DiagnosticSeverity.Error,
            $"CAS3206: Render {label} must be an integer literal, got '{text}'."
        );
        return 0;
    }
}