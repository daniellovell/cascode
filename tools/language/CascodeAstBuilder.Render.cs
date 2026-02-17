using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cascode.Language;

internal sealed partial class CascodeAstBuilder
{
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
                entity.Place = BuildRenderPlacement(oneLiner.pointExpr(), oneLiner.strengthLevel());
                block.Entities.Add(entity);
                continue;
            }

            foreach (var fieldCtx in entityCtx.renderField())
            {
                if (fieldCtx.PLACE_KW() is not null)
                {
                    entity.Place = BuildRenderPlacement(
                        fieldCtx.pointExpr(0),
                        fieldCtx.strengthLevel()
                    );
                    continue;
                }

                if (fieldCtx.ORIENT_KW() is not null)
                {
                    var rotate = ParseSignedInt(
                        fieldCtx.signedInt(),
                        fieldCtx,
                        "orientation rotation"
                    );
                    entity.Orientation = new RenderOrientation
                    {
                        Rotate = rotate,
                        MirrorX = fieldCtx.MIRROR_KW() is not null,
                    };
                    continue;
                }

                if (fieldCtx.ZINDEX_KW() is not null)
                {
                    entity.ZIndex = ParseSignedInt(fieldCtx.signedInt(), fieldCtx, "zindex");
                    continue;
                }

                if (fieldCtx.SIDE_KW() is not null)
                {
                    if (!TryParsePortSide(fieldCtx.IDENT().GetText(), out var side))
                    {
                        AddDiagnostic(
                            fieldCtx,
                            DiagnosticSeverity.Error,
                            $"CAS3202: Invalid render side '{fieldCtx.IDENT().GetText()}'."
                        );
                        continue;
                    }

                    entity.Side = side;
                    continue;
                }

                if (fieldCtx.ROUTE_KW() is not null)
                {
                    if (!TryParseRouteMode(fieldCtx.IDENT().GetText(), out var routeMode))
                    {
                        AddDiagnostic(
                            fieldCtx,
                            DiagnosticSeverity.Error,
                            $"CAS3203: Invalid render route mode '{fieldCtx.IDENT().GetText()}'."
                        );
                        continue;
                    }

                    entity.Route = new RenderRoute
                    {
                        Mode = routeMode,
                        Strength = BuildStrength(fieldCtx.strengthLevel()),
                    };
                    continue;
                }

                if (fieldCtx.WP_KW() is not null)
                {
                    entity.Waypoints.Clear();
                    foreach (var pointCtx in fieldCtx.pointExpr())
                    {
                        entity.Waypoints.Add(BuildPointExpression(pointCtx));
                    }
                }
            }

            block.Entities.Add(entity);
        }

        return block;
    }

    private static string BuildRenderEntityRef(CascodeParser.RenderEntityRefContext ctx)
    {
        return string.Join(".", ctx.idPart().Select(part => part.GetText()));
    }

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

    private static string BuildRenderAnchorRef(CascodeParser.RenderAnchorRefContext ctx)
    {
        if (ctx.CANVAS_KW() is not null)
        {
            return ctx.ORIGIN_KW() is not null ? "canvas origin" : "canvas center";
        }

        return BuildPinRef(ctx.pinRef());
    }

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
