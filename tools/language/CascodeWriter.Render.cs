using System;
using System.IO;
using System.Linq;

namespace Cascode.Language;

public static partial class CascodeWriter
{
    private static void WriteRenderBlock(RenderBlock render, TextWriter writer)
    {
        writer.WriteLine("  render {");

        foreach (var entity in render.Entities.OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            if (CanWriteOneLiner(entity))
            {
                writer.WriteLine(
                    $"    {entity.Name} place {FormatPointExpr(entity.Place!.Point)}{FormatRenderStrength(entity.Place.Strength)}"
                );
                continue;
            }

            writer.WriteLine($"    {entity.Name} {{");

            if (entity.Place is not null)
            {
                writer.WriteLine(
                    $"      place {FormatPointExpr(entity.Place.Point)}{FormatRenderStrength(entity.Place.Strength)}"
                );
            }

            if (entity.Orientation is not null)
            {
                var mirrorText = entity.Orientation.MirrorX ? " mirror" : string.Empty;
                writer.WriteLine($"      orient {entity.Orientation.Rotate}{mirrorText}");
            }

            if (entity.Side is not null)
            {
                writer.WriteLine($"      side {FormatPortSide(entity.Side.Value)}");
            }

            if (entity.Route is not null)
            {
                writer.WriteLine(
                    $"      route {FormatRouteMode(entity.Route.Mode)}{FormatRenderStrength(entity.Route.Strength)}"
                );
            }

            if (entity.Waypoints.Count > 0)
            {
                var points = string.Join(
                    ", ",
                    entity.Waypoints.Select(point => FormatPointExpr(point))
                );
                writer.WriteLine($"      wp [{points}]");
            }

            if (entity.ZIndex is not null)
            {
                writer.WriteLine($"      zindex {entity.ZIndex.Value}");
            }

            writer.WriteLine("    }");
        }

        writer.WriteLine("  }");
    }

    private static bool CanWriteOneLiner(RenderEntity entity)
    {
        return entity.Place is not null
            && entity.Orientation is null
            && entity.Side is null
            && entity.Route is null
            && entity.ZIndex is null
            && entity.Waypoints.Count == 0;
    }

    private static string FormatPointExpr(RenderPointExpression point)
    {
        return point switch
        {
            RenderAbsPoint abs => $"abs {abs.X} {abs.Y}",
            RenderRefPoint @ref => FormatRefPoint(@ref),
            RenderRelPoint rel => $"rel {rel.Dx} {rel.Dy}",
            _ => throw new InvalidOperationException(
                $"Unhandled render point expression: {point.GetType().Name}"
            ),
        };
    }

    private static string FormatRefPoint(RenderRefPoint point)
    {
        var anchor = point.Anchor;
        if (point.Dx == 0 && point.Dy == 0)
        {
            return $"ref {anchor}";
        }

        return $"ref {anchor} {point.Dx} {point.Dy}";
    }

    private static string FormatRenderStrength(RenderConstraintStrength? strength)
    {
        if (strength is null)
        {
            return string.Empty;
        }

        return strength.Value switch
        {
            RenderConstraintStrength.Hard => " hard",
            RenderConstraintStrength.Soft => " soft",
            RenderConstraintStrength.Hint => " hint",
            _ => string.Empty,
        };
    }

    private static string FormatRouteMode(RenderRouteMode mode) =>
        mode switch
        {
            RenderRouteMode.Auto => "auto",
            RenderRouteMode.Ortho => "ortho",
            _ => throw new InvalidOperationException($"Unhandled route mode: {mode}"),
        };

    private static string FormatPortSide(RenderPortSide side) =>
        side switch
        {
            RenderPortSide.Left => "left",
            RenderPortSide.Right => "right",
            RenderPortSide.Top => "top",
            RenderPortSide.Bottom => "bottom",
            RenderPortSide.Auto => "auto",
            _ => throw new InvalidOperationException($"Unhandled port side: {side}"),
        };
}
