using System;
using System.Collections.Generic;
using System.Linq;
using Antlr4.Runtime;

namespace Cascode.Language;

internal sealed partial class CascodeAstBuilder
{
    internal static bool TryParseMeasurementExprText(
        string text,
        out MeasurementExpr? expr,
        out IReadOnlyList<Diagnostic> diagnostics
    )
    {
        expr = null;
        var diags = new List<Diagnostic>();

        if (string.IsNullOrWhiteSpace(text))
        {
            diags.Add(
                new Diagnostic(
                    "CAS4001: Empty measurement expression.",
                    DiagnosticSeverity.Error,
                    "<expr>",
                    1,
                    1
                )
            );
            diagnostics = diags;
            return false;
        }

        var inputStream = CharStreams.fromString(text);
        var lexer = new CascodeLexer(inputStream);
        var tokens = new CommonTokenStream(lexer);
        var parser = new CascodeParser(tokens);

        lexer.RemoveErrorListeners();
        parser.RemoveErrorListeners();
        var listener = new CascodeErrorListener("<expr>", diags);
        lexer.AddErrorListener(listener);
        parser.AddErrorListener(listener);

        var ctx = parser.measurementExpr();
        if (
            diags.Any(d => d.Severity == DiagnosticSeverity.Error)
            || parser.NumberOfSyntaxErrors > 0
            || tokens.LA(1) != TokenConstants.EOF
        )
        {
            diagnostics = diags;
            return false;
        }

        try
        {
            var builder = new CascodeAstBuilder("<expr>", diags);
            expr = builder.BuildMeasurementExpr(ctx);
            diagnostics = diags;
            return !diags.Any(d => d.Severity == DiagnosticSeverity.Error);
        }
        catch (Exception ex)
        {
            diags.Add(
                new Diagnostic(
                    $"CAS4002: Failed to parse measurement expression '{text}': {ex.Message}",
                    DiagnosticSeverity.Error,
                    "<expr>",
                    1,
                    1
                )
            );
            diagnostics = diags;
            return false;
        }
    }
}
