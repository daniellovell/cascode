using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language;

internal static class SchematicRenderRewriter
{
    private static readonly RenderEntityField[] FieldOrder =
    {
        RenderEntityField.Place,
        RenderEntityField.Orientation,
        RenderEntityField.Side,
        RenderEntityField.Route,
        RenderEntityField.Segments,
        RenderEntityField.ZIndex,
    };

    public static string SetRenderMode(ParsedSchematicSource parsed, RenderLayoutMode mode)
    {
        if (parsed.Circuit.Render is null)
        {
            return mode == RenderLayoutMode.Auto
                ? parsed.Text
                : InsertRenderBlock(parsed, mode, []);
        }

        if (mode == RenderLayoutMode.Auto && parsed.Circuit.Render.ModeSpan is null)
        {
            return parsed.Text;
        }

        if (parsed.Circuit.Render.ModeSpan is { } modeSpan)
        {
            var replacement = mode == RenderLayoutMode.Manual ? "mode manual" : string.Empty;
            var span =
                mode == RenderLayoutMode.Manual
                    ? modeSpan
                    : SchematicSourceText.ExpandToLine(parsed.Text, modeSpan);
            return SchematicSourceText.ApplyReplacements(
                parsed.Text,
                [new TextReplacement { Span = span, Text = replacement }]
            );
        }

        var insertText = $"    mode manual{parsed.LineEnding}";
        return SchematicSourceText.ApplyReplacements(
            parsed.Text,
            [
                new TextReplacement
                {
                    Span = new SourceSpan(
                        parsed.Circuit.Render.Span.Start + "  render {".Length,
                        parsed.Circuit.Render.Span.Start + "  render {".Length
                    ),
                    Text = $"{parsed.LineEnding}{insertText}",
                },
            ]
        );
    }

    public static string PatchEntity(
        ParsedSchematicSource parsed,
        string name,
        RenderEntityPatch patch
    )
    {
        var existing = parsed.Circuit.SemanticCircuit.Render?.Entities.FirstOrDefault(entity =>
            entity.Name == name
        );
        var merged = MergeEntity(existing, name, patch);
        if (parsed.Circuit.Render is null)
        {
            return InsertRenderBlock(parsed, RenderLayoutMode.Auto, [merged]);
        }

        if (!parsed.Circuit.Render.Entities.TryGetValue(name, out var source))
        {
            return InsertEntity(parsed, merged, preferBlock: true);
        }

        return RewriteEntity(parsed, source, merged);
    }

    public static string ApplySnapshot(
        ParsedSchematicSource parsed,
        RenderLayoutMode mode,
        IReadOnlyList<RenderEntity> entities
    )
    {
        var rewritten = parsed.Text;
        if (parsed.Circuit.Render is null)
        {
            return InsertRenderBlock(parsed, mode, entities);
        }

        rewritten = SetRenderMode(parsed, mode);
        var snapshotNames = new HashSet<string>(
            entities.Select(entity => entity.Name),
            StringComparer.Ordinal
        );
        foreach (var entity in entities)
        {
            var result = PatchEntity(
                SchematicSourceParser.Parse("<rewrite>", rewritten, parsed.Circuit.Name),
                entity.Name,
                BuildSnapshotPatch(entity)
            );
            rewritten = result;
        }

        var removeNames = parsed
            .Circuit.Render.Entities.Keys.Where(name => !snapshotNames.Contains(name))
            .ToArray();
        if (removeNames.Length == 0)
        {
            return rewritten;
        }

        return RemoveEntities(
            SchematicSourceParser.Parse("<rewrite>", rewritten, parsed.Circuit.Name),
            removeNames
        );
    }

    public static string RemoveEntities(ParsedSchematicSource parsed, IReadOnlyList<string> names)
    {
        if (parsed.Circuit.Render is null || names.Count == 0)
        {
            return parsed.Text;
        }

        var edits = names
            .Distinct(StringComparer.Ordinal)
            .Where(name => parsed.Circuit.Render.Entities.ContainsKey(name))
            .Select(name => parsed.Circuit.Render.Entities[name])
            .Select(entity => new TextReplacement
            {
                Span = ExpandLeadingCommentSpan(parsed.Text, entity.FullLineSpan),
                Text = string.Empty,
            })
            .ToList();
        return SchematicSourceText.ApplyReplacements(parsed.Text, edits);
    }

    private static string RewriteEntity(
        ParsedSchematicSource parsed,
        RenderEntitySourceInfo source,
        RenderEntity merged
    )
    {
        if (
            source.Shape == RenderEntityShape.OneLiner
            && SchematicSourceFormatting.CanWriteOneLiner(merged)
        )
        {
            var place = source.Fields.Single(field => field.Field == RenderEntityField.Place);
            return SchematicSourceText.ApplyReplacements(
                parsed.Text,
                [
                    new TextReplacement
                    {
                        Span = place.Span,
                        Text =
                            $"place {SchematicSourceFormatting.FormatRenderField(RenderEntityField.Place, merged, parsed.LineEnding).Replace("place ", string.Empty, StringComparison.Ordinal)}",
                    },
                ]
            );
        }

        if (source.Shape == RenderEntityShape.OneLiner)
        {
            return ReplaceWholeEntity(parsed, source, merged, preferBlock: true);
        }

        var edits = BuildFieldEdits(parsed, source, merged);
        return edits.Count == 0
            ? parsed.Text
            : SchematicSourceText.ApplyReplacements(parsed.Text, edits);
    }

    private static List<TextReplacement> BuildFieldEdits(
        ParsedSchematicSource parsed,
        RenderEntitySourceInfo source,
        RenderEntity merged
    )
    {
        var edits = new List<TextReplacement>();
        foreach (var field in FieldOrder)
        {
            var existingFields = source.Fields.Where(entry => entry.Field == field).ToList();
            var nextText = SchematicSourceFormatting.FormatRenderField(
                field,
                merged,
                parsed.LineEnding
            );
            if (field == RenderEntityField.Segments)
            {
                AddSegmentEdits(parsed, edits, existingFields, nextText, source.CloseBraceOffset);
                continue;
            }

            if (existingFields.Count > 0 && string.IsNullOrEmpty(nextText))
            {
                edits.Add(
                    new TextReplacement
                    {
                        Span = existingFields[0].FullLineSpan,
                        Text = string.Empty,
                    }
                );
                continue;
            }

            if (existingFields.Count > 0)
            {
                edits.Add(new TextReplacement { Span = existingFields[0].Span, Text = nextText });
                continue;
            }

            if (!string.IsNullOrEmpty(nextText))
            {
                edits.Add(
                    new TextReplacement
                    {
                        Span = new SourceSpan(
                            FindInsertOffset(source, field),
                            FindInsertOffset(source, field)
                        ),
                        Text = $"      {nextText}{parsed.LineEnding}",
                    }
                );
            }
        }

        return edits;
    }

    private static void AddSegmentEdits(
        ParsedSchematicSource parsed,
        List<TextReplacement> edits,
        IReadOnlyList<RenderFieldSourceInfo> existingFields,
        string nextText,
        int closeBraceOffset
    )
    {
        if (existingFields.Count == 0 && string.IsNullOrEmpty(nextText))
        {
            return;
        }

        if (existingFields.Count == 0)
        {
            edits.Add(
                new TextReplacement
                {
                    Span = new SourceSpan(closeBraceOffset, closeBraceOffset),
                    Text = $"      {nextText}{parsed.LineEnding}",
                }
            );
            return;
        }

        if (string.IsNullOrEmpty(nextText))
        {
            edits.Add(
                new TextReplacement
                {
                    Span = new SourceSpan(
                        existingFields[0].FullLineSpan.Start,
                        existingFields[^1].FullLineSpan.End
                    ),
                    Text = string.Empty,
                }
            );
            return;
        }

        edits.Add(
            new TextReplacement
            {
                Span = new SourceSpan(existingFields[0].Span.Start, existingFields[^1].Span.End),
                Text = nextText,
            }
        );
    }

    private static int FindInsertOffset(RenderEntitySourceInfo source, RenderEntityField field)
    {
        var nextField = source.Fields.FirstOrDefault(existing =>
            FieldIndex(existing.Field) > FieldIndex(field)
        );
        return nextField?.FullLineSpan.Start ?? source.CloseBraceOffset;
    }

    private static int FieldIndex(RenderEntityField field)
    {
        return Array.IndexOf(FieldOrder, field);
    }

    private static string InsertEntity(
        ParsedSchematicSource parsed,
        RenderEntity entity,
        bool preferBlock
    )
    {
        var insertion =
            $"{parsed.LineEnding}{SchematicSourceFormatting.FormatRenderEntity(entity, parsed.LineEnding, preferBlock)}";
        return SchematicSourceText.ApplyReplacements(
            parsed.Text,
            [
                new TextReplacement
                {
                    Span = new SourceSpan(
                        parsed.Circuit.Render!.CloseBraceOffset,
                        parsed.Circuit.Render.CloseBraceOffset
                    ),
                    Text = insertion,
                },
            ]
        );
    }

    private static string ReplaceWholeEntity(
        ParsedSchematicSource parsed,
        RenderEntitySourceInfo source,
        RenderEntity entity,
        bool preferBlock
    )
    {
        return SchematicSourceText.ApplyReplacements(
            parsed.Text,
            [
                new TextReplacement
                {
                    Span = source.Span,
                    Text = SchematicSourceFormatting.FormatRenderEntity(
                        entity,
                        parsed.LineEnding,
                        preferBlock
                    ),
                },
            ]
        );
    }

    private static string InsertRenderBlock(
        ParsedSchematicSource parsed,
        RenderLayoutMode mode,
        IReadOnlyList<RenderEntity> entities
    )
    {
        var lines = new List<string> { "  render {" };
        if (mode == RenderLayoutMode.Manual)
        {
            lines.Add("    mode manual");
        }

        lines.AddRange(
            entities.Select(entity =>
                SchematicSourceFormatting.FormatRenderEntity(
                    entity,
                    parsed.LineEnding,
                    preferBlock: true
                )
            )
        );
        lines.Add("  }");
        var block =
            $"{parsed.LineEnding}{string.Join(parsed.LineEnding, lines)}{parsed.LineEnding}";
        return SchematicSourceText.ApplyReplacements(
            parsed.Text,
            [
                new TextReplacement
                {
                    Span = new SourceSpan(
                        parsed.Circuit.CloseBraceOffset,
                        parsed.Circuit.CloseBraceOffset
                    ),
                    Text = block,
                },
            ]
        );
    }

    private static RenderEntity MergeEntity(
        RenderEntity? existing,
        string name,
        RenderEntityPatch patch
    )
    {
        var merged = CloneEntity(existing, name);
        var clear = patch.ClearFields ?? new HashSet<RenderEntityField>();
        ApplyClearFields(merged, clear);
        if (patch.Place is not null)
        {
            merged.Place = patch.Place;
        }

        if (patch.Orientation is not null)
        {
            merged.Orientation = patch.Orientation;
        }

        if (patch.Side is not null)
        {
            merged.Side = patch.Side;
        }

        if (patch.Route is not null)
        {
            merged.Route = patch.Route;
        }

        if (patch.Segments is not null)
        {
            merged.Segments.Clear();
            merged.Segments.AddRange(patch.Segments.Select(CloneSegment));
        }

        if (patch.ZIndex is not null)
        {
            merged.ZIndex = patch.ZIndex;
        }

        return merged;
    }

    private static RenderEntityPatch BuildSnapshotPatch(RenderEntity entity)
    {
        var clear = new HashSet<RenderEntityField>(FieldOrder);
        foreach (var field in PresentFields(entity))
        {
            clear.Remove(field);
        }

        return new RenderEntityPatch
        {
            Place = entity.Place,
            Orientation = entity.Orientation,
            Side = entity.Side,
            Route = entity.Route,
            Segments = entity.Segments.Select(CloneSegment).ToArray(),
            ZIndex = entity.ZIndex,
            ClearFields = clear,
        };
    }

    private static IEnumerable<RenderEntityField> PresentFields(RenderEntity entity)
    {
        if (entity.Place is not null)
        {
            yield return RenderEntityField.Place;
        }

        if (entity.Orientation is not null)
        {
            yield return RenderEntityField.Orientation;
        }

        if (entity.Side is not null)
        {
            yield return RenderEntityField.Side;
        }

        if (entity.Route is not null)
        {
            yield return RenderEntityField.Route;
        }

        if (entity.Segments.Count > 0)
        {
            yield return RenderEntityField.Segments;
        }

        if (entity.ZIndex is not null)
        {
            yield return RenderEntityField.ZIndex;
        }
    }

    private static void ApplyClearFields(RenderEntity entity, IReadOnlySet<RenderEntityField> clear)
    {
        if (clear.Contains(RenderEntityField.Place))
        {
            entity.Place = null;
        }

        if (clear.Contains(RenderEntityField.Orientation))
        {
            entity.Orientation = null;
        }

        if (clear.Contains(RenderEntityField.Side))
        {
            entity.Side = null;
        }

        if (clear.Contains(RenderEntityField.Route))
        {
            entity.Route = null;
        }

        if (clear.Contains(RenderEntityField.Segments))
        {
            entity.Segments.Clear();
        }

        if (clear.Contains(RenderEntityField.ZIndex))
        {
            entity.ZIndex = null;
        }
    }

    private static RenderEntity CloneEntity(RenderEntity? entity, string name)
    {
        return entity is null
            ? new RenderEntity { Name = name }
            : new RenderEntity
            {
                Name = entity.Name,
                Kind = entity.Kind,
                Place = entity.Place,
                Orientation = entity.Orientation,
                Side = entity.Side,
                Route = entity.Route,
                ZIndex = entity.ZIndex,
                Segments = entity.Segments.Select(CloneSegment).ToList(),
            };
    }

    private static RenderSegment CloneSegment(RenderSegment segment)
    {
        return new RenderSegment { From = segment.From, To = segment.To };
    }

    private static SourceSpan ExpandLeadingCommentSpan(string text, SourceSpan span)
    {
        var start = span.Start;
        while (start > 0)
        {
            var previousLineEnd = start - 1;
            if (previousLineEnd > 0 && text[previousLineEnd - 1] == '\r')
            {
                previousLineEnd--;
            }

            var previousLineStart = SchematicSourceText.FindLineStart(text, previousLineEnd);
            var previousLine = text[previousLineStart..start].Trim();
            if (!previousLine.StartsWith("//", StringComparison.Ordinal))
            {
                break;
            }

            start = previousLineStart;
        }

        return new SourceSpan(start, span.End);
    }
}
