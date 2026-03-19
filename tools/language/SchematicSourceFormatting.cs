using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language;

internal static class SchematicSourceFormatting
{
    public static string FormatSizePack(IReadOnlyDictionary<string, string> entries)
    {
        var parts = entries
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => $"{entry.Key}={entry.Value}");
        return $"size({string.Join(", ", parts)})";
    }

    public static string FormatRenderEntity(
        RenderEntity entity,
        string lineEnding,
        bool preferBlock
    )
    {
        if (!preferBlock && CanWriteOneLiner(entity))
        {
            return $"    {entity.Name} place {FormatPoint(entity.Place!.Point)}{FormatStrength(entity.Place.Strength)}";
        }

        var lines = new List<string> { $"    {entity.Name} {{" };
        if (entity.Place is not null)
        {
            lines.Add(
                $"      place {FormatPoint(entity.Place.Point)}{FormatStrength(entity.Place.Strength)}"
            );
        }

        if (entity.Orientation is not null)
        {
            var mirror = entity.Orientation.MirrorX ? " mirror" : string.Empty;
            lines.Add($"      orient {entity.Orientation.Rotate}{mirror}");
        }

        if (entity.Side is not null)
        {
            lines.Add($"      side {FormatPortSide(entity.Side.Value)}");
        }

        if (entity.Route is not null)
        {
            lines.Add(
                $"      route {FormatRouteMode(entity.Route.Mode)}{FormatStrength(entity.Route.Strength)}"
            );
        }

        lines.AddRange(
            entity.Segments.Select(segment =>
                $"      seg {FormatPoint(segment.From)} {FormatPoint(segment.To)}"
            )
        );

        if (entity.ZIndex is not null)
        {
            lines.Add($"      zindex {entity.ZIndex.Value}");
        }

        lines.Add("    }");
        return string.Join(lineEnding, lines);
    }

    public static string FormatRenderField(
        RenderEntityField field,
        RenderEntity entity,
        string lineEnding
    )
    {
        return field switch
        {
            RenderEntityField.Place when entity.Place is not null =>
                $"place {FormatPoint(entity.Place.Point)}{FormatStrength(entity.Place.Strength)}",
            RenderEntityField.Orientation when entity.Orientation is not null => entity
                .Orientation
                .MirrorX
                ? $"orient {entity.Orientation.Rotate} mirror"
                : $"orient {entity.Orientation.Rotate}",
            RenderEntityField.Side when entity.Side is not null =>
                $"side {FormatPortSide(entity.Side.Value)}",
            RenderEntityField.Route when entity.Route is not null =>
                $"route {FormatRouteMode(entity.Route.Mode)}{FormatStrength(entity.Route.Strength)}",
            RenderEntityField.Segments => string.Join(
                lineEnding,
                entity.Segments.Select(segment =>
                    $"seg {FormatPoint(segment.From)} {FormatPoint(segment.To)}"
                )
            ),
            RenderEntityField.ZIndex when entity.ZIndex is not null =>
                $"zindex {entity.ZIndex.Value}",
            _ => string.Empty,
        };
    }

    public static bool CanWriteOneLiner(RenderEntity entity)
    {
        return entity.Place is not null
            && entity.Orientation is null
            && entity.Side is null
            && entity.Route is null
            && entity.ZIndex is null
            && entity.Segments.Count == 0;
    }

    private static string FormatPoint(RenderPointExpression point)
    {
        return point switch
        {
            RenderAbsPoint abs => $"abs {abs.X} {abs.Y}",
            RenderRefPoint @ref when @ref.Dx == 0 && @ref.Dy == 0 => $"ref {@ref.Anchor}",
            RenderRefPoint @ref => $"ref {@ref.Anchor} {@ref.Dx} {@ref.Dy}",
            RenderRelPoint rel => $"rel {rel.Dx} {rel.Dy}",
            _ => throw new InvalidOperationException(
                $"Unhandled render point: {point.GetType().Name}"
            ),
        };
    }

    private static string FormatStrength(RenderConstraintStrength? strength)
    {
        return strength switch
        {
            RenderConstraintStrength.Hard => " hard",
            RenderConstraintStrength.Soft => " soft",
            RenderConstraintStrength.Hint => " hint",
            _ => string.Empty,
        };
    }

    private static string FormatRouteMode(RenderRouteMode mode)
    {
        return mode switch
        {
            RenderRouteMode.Auto => "auto",
            RenderRouteMode.Ortho => "ortho",
            _ => throw new InvalidOperationException($"Unhandled route mode: {mode}"),
        };
    }

    private static string FormatPortSide(RenderPortSide side)
    {
        return side switch
        {
            RenderPortSide.Left => "left",
            RenderPortSide.Right => "right",
            RenderPortSide.Top => "top",
            RenderPortSide.Bottom => "bottom",
            RenderPortSide.Auto => "auto",
            _ => throw new InvalidOperationException($"Unhandled port side: {side}"),
        };
    }
}
