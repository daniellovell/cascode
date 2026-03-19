using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language;

internal static class SchematicStructuralRewriter
{
    public static string SetDeviceParam(
        ParsedSchematicSource parsed,
        string deviceId,
        string param,
        string value
    )
    {
        var fill =
            parsed.Circuit.Fill
            ?? throw new InvalidOperationException("Circuit has no fill block.");
        if (!fill.Devices.TryGetValue(deviceId, out var source))
        {
            throw new InvalidOperationException($"Unknown device '{deviceId}'.");
        }

        var semantic =
            parsed.Circuit.SemanticCircuit.Fill?.Devices.FirstOrDefault(device =>
                device.Id == deviceId
            ) ?? throw new InvalidOperationException($"Unknown device '{deviceId}'.");
        var entries = semantic.Size is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(semantic.Size.Entries, StringComparer.Ordinal);
        entries[param] = value;
        return SchematicSourceText.ApplyReplacements(
            parsed.Text,
            [
                new TextReplacement
                {
                    Span = source.SizeArgumentSpan,
                    Text = SchematicSourceFormatting.FormatSizePack(entries),
                },
            ]
        );
    }

    public static string InsertRail(
        ParsedSchematicSource parsed,
        SchematicRailKind kind,
        string name
    )
    {
        var rails =
            kind == SchematicRailKind.Supply ? parsed.Circuit.Supplies : parsed.Circuit.Grounds;
        if (rails.ContainsKey(name))
        {
            return parsed.Text;
        }

        var insertOffset = FindRailInsertOffset(parsed, kind);
        var keyword = kind == SchematicRailKind.Supply ? "supply" : "ground";
        return SchematicSourceText.ApplyReplacements(
            parsed.Text,
            [
                new TextReplacement
                {
                    Span = new SourceSpan(insertOffset, insertOffset),
                    Text = $"  {keyword} {name}{parsed.LineEnding}",
                },
            ]
        );
    }

    public static string RemoveRail(
        ParsedSchematicSource parsed,
        SchematicRailKind kind,
        string name
    )
    {
        var rails =
            kind == SchematicRailKind.Supply ? parsed.Circuit.Supplies : parsed.Circuit.Grounds;
        if (!rails.TryGetValue(name, out var rail))
        {
            return parsed.Text;
        }

        var edits = new List<TextReplacement>
        {
            new() { Span = rail.FullLineSpan, Text = string.Empty },
        };
        if (parsed.Circuit.Render?.Entities.TryGetValue(name, out var renderEntity) == true)
        {
            edits.Add(
                new TextReplacement { Span = renderEntity.FullLineSpan, Text = string.Empty }
            );
        }

        return SchematicSourceText.ApplyReplacements(parsed.Text, edits);
    }

    public static string DeleteDevice(ParsedSchematicSource parsed, string deviceId)
    {
        var fill =
            parsed.Circuit.Fill
            ?? throw new InvalidOperationException("Circuit has no fill block.");
        if (!fill.Devices.TryGetValue(deviceId, out var device))
        {
            throw new InvalidOperationException($"Unknown device '{deviceId}'.");
        }

        var edits = new List<TextReplacement>
        {
            new() { Span = device.FullLineSpan, Text = string.Empty },
        };

        edits.AddRange(
            fill.Connections.Where(connection =>
                    ReferencesDevice(connection.From, deviceId)
                    || ReferencesDevice(connection.To, deviceId)
                )
                .Select(connection => new TextReplacement
                {
                    Span = connection.FullLineSpan,
                    Text = string.Empty,
                })
        );

        if (parsed.Circuit.Render?.Entities.TryGetValue(deviceId, out var renderEntity) == true)
        {
            edits.Add(
                new TextReplacement { Span = renderEntity.FullLineSpan, Text = string.Empty }
            );
        }

        return SchematicSourceText.ApplyReplacements(parsed.Text, edits);
    }

    public static string Connect(ParsedSchematicSource parsed, string from, string to)
    {
        var fill =
            parsed.Circuit.Fill
            ?? throw new InvalidOperationException("Circuit has no fill block.");
        var exists = fill.Connections.Any(connection => MatchesEndpoints(connection, from, to));
        if (exists)
        {
            return parsed.Text;
        }

        return SchematicSourceText.ApplyReplacements(
            parsed.Text,
            [
                new TextReplacement
                {
                    Span = new SourceSpan(fill.CloseBraceOffset, fill.CloseBraceOffset),
                    Text = $"    {from}--{to}{parsed.LineEnding}",
                },
            ]
        );
    }

    public static string Disconnect(ParsedSchematicSource parsed, string from, string to)
    {
        var fill =
            parsed.Circuit.Fill
            ?? throw new InvalidOperationException("Circuit has no fill block.");
        var edits = fill
            .Connections.Where(connection => MatchesEndpoints(connection, from, to))
            .Select(connection => new TextReplacement
            {
                Span = connection.FullLineSpan,
                Text = string.Empty,
            })
            .ToList();
        return edits.Count == 0
            ? parsed.Text
            : SchematicSourceText.ApplyReplacements(parsed.Text, edits);
    }

    private static int FindRailInsertOffset(ParsedSchematicSource parsed, SchematicRailKind kind)
    {
        var existingRails =
            kind == SchematicRailKind.Supply
                ? parsed.Circuit.Supplies.Values
                : parsed.Circuit.Grounds.Values;
        var lastRail = existingRails.OrderBy(rail => rail.FullLineSpan.Start).LastOrDefault();
        if (lastRail is not null)
        {
            return lastRail.FullLineSpan.End;
        }

        if (parsed.Circuit.Fill is not null)
        {
            return parsed.Circuit.Fill.Span.Start;
        }

        if (parsed.Circuit.Render is not null)
        {
            return parsed.Circuit.Render.Span.Start;
        }

        return parsed.Circuit.CloseBraceOffset;
    }

    private static bool ReferencesDevice(string endpoint, string deviceId)
    {
        return endpoint.Equals(deviceId, StringComparison.Ordinal)
            || endpoint.StartsWith(deviceId + ".", StringComparison.Ordinal);
    }

    private static bool MatchesEndpoints(ConnectionSourceInfo connection, string from, string to)
    {
        return (connection.From == from && connection.To == to)
            || (connection.From == to && connection.To == from);
    }
}
