using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language;

internal readonly record struct SourceSpan(int Start, int End)
{
    public int Length => End - Start;
}

internal enum RenderEntityShape
{
    OneLiner,
    Block,
}

internal sealed record RenderFieldSourceInfo(
    RenderEntityField Field,
    SourceSpan Span,
    SourceSpan FullLineSpan
);

internal sealed class RenderEntitySourceInfo
{
    public required string Name { get; init; }
    public required SourceSpan Span { get; init; }
    public required SourceSpan FullLineSpan { get; init; }
    public required RenderEntityShape Shape { get; init; }
    public required List<RenderFieldSourceInfo> Fields { get; init; }
    public required int CloseBraceOffset { get; init; }
}

internal sealed class DeviceSourceInfo
{
    public required string Id { get; init; }
    public required SourceSpan Span { get; init; }
    public required SourceSpan FullLineSpan { get; init; }
    public required SourceSpan SizeArgumentSpan { get; init; }
}

internal sealed class ConnectionSourceInfo
{
    public required string From { get; init; }
    public required string To { get; init; }
    public required SourceSpan FullLineSpan { get; init; }
}

internal sealed class RailSourceInfo
{
    public required string Name { get; init; }
    public required SourceSpan FullLineSpan { get; init; }
}

internal sealed class FillSourceInfo
{
    public required SourceSpan Span { get; init; }
    public required int CloseBraceOffset { get; init; }
    public required Dictionary<string, DeviceSourceInfo> Devices { get; init; }
    public required List<ConnectionSourceInfo> Connections { get; init; }
}

internal sealed class RenderSourceInfoIndex
{
    public required SourceSpan Span { get; init; }
    public required int CloseBraceOffset { get; init; }
    public required SourceSpan? ModeSpan { get; init; }
    public required Dictionary<string, RenderEntitySourceInfo> Entities { get; init; }
}

internal sealed class CircuitSourceInfo
{
    public required string Name { get; init; }
    public required int CloseBraceOffset { get; init; }
    public required Dictionary<string, RailSourceInfo> Supplies { get; init; }
    public required Dictionary<string, RailSourceInfo> Grounds { get; init; }
    public required FillSourceInfo? Fill { get; init; }
    public required RenderSourceInfoIndex? Render { get; init; }
    public required Circuit SemanticCircuit { get; init; }
}

internal sealed class ParsedSchematicSource
{
    public required string Text { get; init; }
    public required string LineEnding { get; init; }
    public required CircuitSourceInfo Circuit { get; init; }
}

internal sealed class TextReplacement
{
    public required SourceSpan Span { get; init; }
    public required string Text { get; init; }
}

internal static class SchematicSourceText
{
    public static string DetectLineEnding(string text)
    {
        return text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    }

    public static SourceSpan ExpandToLine(string text, SourceSpan span)
    {
        var start = FindLineStart(text, span.Start);
        var end = FindLineEnd(text, span.End);
        return new SourceSpan(start, end);
    }

    public static int FindLineStart(string text, int index)
    {
        var cursor = Math.Clamp(index, 0, text.Length);
        while (cursor > 0 && text[cursor - 1] != '\n')
        {
            cursor--;
        }

        return cursor;
    }

    public static int FindLineEnd(string text, int index)
    {
        var cursor = Math.Clamp(index, 0, text.Length);
        while (cursor < text.Length && text[cursor] != '\n')
        {
            cursor++;
        }

        if (cursor < text.Length)
        {
            cursor++;
        }

        return cursor;
    }

    public static string ApplyReplacements(string text, IReadOnlyList<TextReplacement> edits)
    {
        if (edits.Count == 0)
        {
            return text;
        }

        var ordered = edits.OrderByDescending(edit => edit.Span.Start).ToList();
        var rewritten = text;
        foreach (var edit in ordered)
        {
            rewritten = rewritten[..edit.Span.Start] + edit.Text + rewritten[edit.Span.End..];
        }

        return rewritten;
    }
}
