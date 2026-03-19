using System;
using System.IO;
using System.Linq;

namespace Cascode.Language;

public static partial class CascodeWriter
{
    /// <summary>
    /// Writes a textual "render" block representing the provided RenderBlock to the specified TextWriter.
    /// </summary>
    /// <param name="render">The render block whose entities will be emitted.</param>
    /// <param name="writer">The destination TextWriter to receive the rendered output.</param>
    /// <remarks>
    /// Entities are emitted in ordinal name order. An entity that only specifies a place is written as a single-line
    /// entry; otherwise the entity is written as a multi-line block containing any present fields: place, orient, side,
    /// route, explicit segments, and zindex.
    /// </remarks>
    private static void WriteRenderBlock(RenderBlock render, TextWriter writer)
    {
        writer.WriteLine("  render {");

        if (render.Mode == RenderLayoutMode.Manual)
        {
            writer.WriteLine("    mode manual");
        }

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

            foreach (var segment in entity.Segments)
            {
                writer.WriteLine(
                    $"      seg {FormatPointExpr(segment.From)} {FormatPointExpr(segment.To)}"
                );
            }

            if (entity.ZIndex is not null)
            {
                writer.WriteLine($"      zindex {entity.ZIndex.Value}");
            }

            writer.WriteLine("    }");
        }

        writer.WriteLine("  }");
    }

    /// <summary>
    /// Determines whether a render entity can be emitted as a single-line statement.
    /// </summary>
    /// <param name="entity">The render entity to inspect for single-line emission.</param>
    /// <returns>`true` if the entity has a Place and none of Orientation, Side, Route, ZIndex, or Segments; `false` otherwise.</returns>
    private static bool CanWriteOneLiner(RenderEntity entity)
    {
        return entity.Place is not null
            && entity.Orientation is null
            && entity.Side is null
            && entity.Route is null
            && entity.ZIndex is null
            && entity.Segments.Count == 0;
    }

    /// <summary>
    /// Formats a render point expression into its textual representation.
    /// </summary>
    /// <param name="point">The render point expression to format.</param>
    /// <returns>The formatted point expression: an `abs`, `ref`, or `rel` string suitable for output.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the point expression subtype is unrecognized.</exception>
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

    /// <summary>
    /// Formats a reference point into the Cascode text form.
    /// </summary>
    /// <param name="point">Reference point with an anchor name and integer X/Y offsets.</param>
    /// <returns>"ref {Anchor}" when both offsets are 0; otherwise "ref {Anchor} {Dx} {Dy}".</returns>
    private static string FormatRefPoint(RenderRefPoint point)
    {
        var anchor = point.Anchor;
        if (point.Dx == 0 && point.Dy == 0)
        {
            return $"ref {anchor}";
        }

        return $"ref {anchor} {point.Dx} {point.Dy}";
    }

    /// <summary>
    /// Converts a nullable render constraint strength into the textual suffix used in render output.
    /// </summary>
    /// <param name="strength">The optional constraint strength to format; null produces no suffix.</param>
    /// <returns>A string containing a leading space followed by the strength name (" hard", " soft", or " hint"), or an empty string if <paramref name="strength"/> is null or unrecognized.</returns>
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

    /// <summary>
    /// Map a RenderRouteMode value to the textual route mode used in the output.
    /// </summary>
    /// <param name="mode">The route mode to format.</param>
    /// <returns>"auto" for <see cref="RenderRouteMode.Auto"/>, "ortho" for <see cref="RenderRouteMode.Ortho"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="mode"/> is not a recognized route mode.</exception>
    private static string FormatRouteMode(RenderRouteMode mode) =>
        mode switch
        {
            RenderRouteMode.Auto => "auto",
            RenderRouteMode.Ortho => "ortho",
            _ => throw new InvalidOperationException($"Unhandled route mode: {mode}"),
        };

    /// <summary>
    /// Converts a RenderPortSide value to its textual representation used in render output.
    /// </summary>
    /// <param name="side">The port side enum to convert.</param>
    /// <returns>"left", "right", "top", "bottom", or "auto" corresponding to the provided side.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="side"/> has an unhandled value.</exception>
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
