using System;
using System.Collections.Generic;
using System.Linq;
using Antlr4.Runtime;

namespace Cascode.Language;

internal static class SchematicSourceParser
{
    public static ParsedSchematicSource Parse(string path, string text, string? circuitName)
    {
        var semanticDocument = ParseSemanticDocument(path, text);
        var semanticCircuit = SelectCircuit(semanticDocument, circuitName);
        var root = ParseRoot(path, text);
        var circuitContext = FindCircuitContext(root, semanticCircuit.Name);
        return new ParsedSchematicSource
        {
            Text = text,
            LineEnding = SchematicSourceText.DetectLineEnding(text),
            Circuit = BuildCircuitInfo(text, circuitContext, semanticCircuit),
        };
    }

    private static CascodeDocument ParseSemanticDocument(string path, string text)
    {
        var parsed = CascodeReader.TryParse(text, path);
        if (parsed.Success && parsed.Document is not null)
        {
            return parsed.Document;
        }

        var firstError = parsed.Diagnostics.FirstOrDefault(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
        );
        throw new CascodeParseException(
            firstError?.Message ?? "Failed to parse Cascode source.",
            parsed.Diagnostics
        );
    }

    private static CascodeParser.DocumentContext ParseRoot(string path, string text)
    {
        var diagnostics = new List<Diagnostic>();
        var lexer = new CascodeLexer(CharStreams.fromString(text));
        var tokens = new CommonTokenStream(lexer);
        var parser = new CascodeParser(tokens);
        lexer.RemoveErrorListeners();
        parser.RemoveErrorListeners();
        var listener = new CascodeErrorListener(path, diagnostics);
        lexer.AddErrorListener(listener);
        parser.AddErrorListener(listener);
        var root = parser.document();
        var firstError = diagnostics.FirstOrDefault(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
        );
        if (firstError is not null)
        {
            throw new CascodeParseException(firstError.Message, diagnostics);
        }

        return root;
    }

    private static Circuit SelectCircuit(CascodeDocument document, string? requestedName)
    {
        if (!string.IsNullOrWhiteSpace(requestedName))
        {
            var requested = document.Circuits.FirstOrDefault(circuit =>
                circuit.Name == requestedName
            );
            if (requested is not null)
            {
                return requested;
            }
        }

        var selected = document.Circuits.FirstOrDefault(circuit =>
            !circuit.Inline && circuit.Level is CascodeLevel.EL or CascodeLevel.ML
        );
        if (selected is not null)
        {
            return selected;
        }

        throw new InvalidOperationException("No non-inline EL/ML circuit available.");
    }

    private static CascodeParser.CircuitContext FindCircuitContext(
        CascodeParser.DocumentContext root,
        string circuitName
    )
    {
        var circuit = root.topLevelDecl()
            .Select(declaration => declaration.circuit())
            .FirstOrDefault(declaration =>
                declaration is not null && declaration.name.Text == circuitName
            );
        return circuit
            ?? throw new InvalidOperationException($"Circuit '{circuitName}' was not found.");
    }

    private static CircuitSourceInfo BuildCircuitInfo(
        string text,
        CascodeParser.CircuitContext context,
        Circuit semanticCircuit
    )
    {
        return new CircuitSourceInfo
        {
            Name = semanticCircuit.Name,
            CloseBraceOffset = context.Stop.StartIndex,
            Supplies = BuildRailIndex(text, context, supply: true),
            Grounds = BuildRailIndex(text, context, supply: false),
            Fill = BuildFillInfo(text, context),
            Render = BuildRenderInfo(text, context),
            SemanticCircuit = semanticCircuit,
        };
    }

    private static Dictionary<string, RailSourceInfo> BuildRailIndex(
        string text,
        CascodeParser.CircuitContext context,
        bool supply
    )
    {
        var map = new Dictionary<string, RailSourceInfo>(StringComparer.Ordinal);
        foreach (var member in context.circuitMember())
        {
            if (supply)
            {
                if (member is not CascodeParser.SupplyDeclContext supplyDecl)
                {
                    continue;
                }

                var supplyName = supplyDecl.IDENT().GetText();
                map[supplyName] = new RailSourceInfo
                {
                    Name = supplyName,
                    FullLineSpan = SchematicSourceText.ExpandToLine(text, SpanFor(member)),
                };
                continue;
            }

            if (member is not CascodeParser.GroundDeclContext groundDecl)
            {
                continue;
            }

            var groundName = groundDecl.IDENT().GetText();
            map[groundName] = new RailSourceInfo
            {
                Name = groundName,
                FullLineSpan = SchematicSourceText.ExpandToLine(text, SpanFor(member)),
            };
        }

        return map;
    }

    private static FillSourceInfo? BuildFillInfo(string text, CascodeParser.CircuitContext context)
    {
        var fill = context
            .circuitMember()
            .OfType<CascodeParser.FillSectionContext>()
            .FirstOrDefault();
        if (fill is null)
        {
            return null;
        }

        var devices = new Dictionary<string, DeviceSourceInfo>(StringComparer.Ordinal);
        var connections = new List<ConnectionSourceInfo>();
        foreach (var statement in fill.fillStatement())
        {
            switch (statement)
            {
                case CascodeParser.FillDeviceDeclContext deviceDecl:
                    var device = deviceDecl.deviceDecl();
                    var deviceId = string.Join(
                        ".",
                        device.deviceId().idPart().Select(part => part.GetText())
                    );
                    devices[deviceId] = new DeviceSourceInfo
                    {
                        Id = deviceId,
                        Span = SpanFor(device),
                        FullLineSpan = SchematicSourceText.ExpandToLine(text, SpanFor(device)),
                        SizeArgumentSpan = SpanFor(device.sizeArg()),
                    };
                    break;

                case CascodeParser.FillConnectDeclContext connectionDecl:
                    var pins = connectionDecl.pinRef();
                    connections.Add(
                        new ConnectionSourceInfo
                        {
                            From = pins[0].GetText(),
                            To = pins[1].GetText(),
                            FullLineSpan = SchematicSourceText.ExpandToLine(
                                text,
                                SpanFor(connectionDecl)
                            ),
                        }
                    );
                    break;
            }
        }

        return new FillSourceInfo
        {
            Span = SpanFor(fill),
            CloseBraceOffset = fill.Stop.StartIndex,
            Devices = devices,
            Connections = connections,
        };
    }

    private static RenderSourceInfoIndex? BuildRenderInfo(
        string text,
        CascodeParser.CircuitContext context
    )
    {
        var render = context
            .circuitMember()
            .OfType<CascodeParser.RenderSectionContext>()
            .FirstOrDefault();
        if (render is null)
        {
            return null;
        }

        var entities = new Dictionary<string, RenderEntitySourceInfo>(StringComparer.Ordinal);
        foreach (var entity in render.renderEntity())
        {
            var name = entity.renderEntityRef().GetText();
            entities[name] = BuildRenderEntityInfo(text, entity, name);
        }

        return new RenderSourceInfoIndex
        {
            Span = SpanFor(render),
            CloseBraceOffset = render.Stop.StartIndex,
            ModeSpan = render.renderModeDecl() is { } mode ? SpanFor(mode) : null,
            Entities = entities,
        };
    }

    private static RenderEntitySourceInfo BuildRenderEntityInfo(
        string text,
        CascodeParser.RenderEntityContext context,
        string name
    )
    {
        var span = SpanFor(context);
        if (context.renderOneLiner() is { } oneLiner)
        {
            return new RenderEntitySourceInfo
            {
                Name = name,
                Span = span,
                FullLineSpan = SchematicSourceText.ExpandToLine(text, span),
                Shape = RenderEntityShape.OneLiner,
                Fields = new List<RenderFieldSourceInfo>
                {
                    new(
                        RenderEntityField.Place,
                        SpanFor(oneLiner),
                        SchematicSourceText.ExpandToLine(text, SpanFor(oneLiner))
                    ),
                },
                CloseBraceOffset = span.End,
            };
        }

        return new RenderEntitySourceInfo
        {
            Name = name,
            Span = span,
            FullLineSpan = SchematicSourceText.ExpandToLine(text, span),
            Shape = RenderEntityShape.Block,
            Fields = context.renderField().Select(field => BuildFieldInfo(text, field)).ToList(),
            CloseBraceOffset = context.Stop.StartIndex,
        };
    }

    private static RenderFieldSourceInfo BuildFieldInfo(
        string text,
        CascodeParser.RenderFieldContext field
    )
    {
        var span = SpanFor(field);
        return new RenderFieldSourceInfo(
            ClassifyField(field),
            span,
            SchematicSourceText.ExpandToLine(text, span)
        );
    }

    private static RenderEntityField ClassifyField(CascodeParser.RenderFieldContext field)
    {
        if (field.PLACE_KW() is not null)
        {
            return RenderEntityField.Place;
        }

        if (field.ORIENT_KW() is not null)
        {
            return RenderEntityField.Orientation;
        }

        if (field.SIDE_KW() is not null)
        {
            return RenderEntityField.Side;
        }

        if (field.ROUTE_KW() is not null)
        {
            return RenderEntityField.Route;
        }

        if (field.SEG_KW() is not null)
        {
            return RenderEntityField.Segments;
        }

        return RenderEntityField.ZIndex;
    }

    private static SourceSpan SpanFor(ParserRuleContext context)
    {
        return new SourceSpan(context.Start.StartIndex, context.Stop.StopIndex + 1);
    }
}
