using System;
using System.Collections.Generic;
using Antlr4.Runtime;
using Cascode.Parser;

namespace Cascode.ACIR;

/// <summary>
/// Entry point for parsing ACIR source text into an ACIRDocument using ANTLR.
/// </summary>
public static class ACIRParserFacade
{
    /// <summary>
    /// Parses the provided ACIR source text using the ANTLR-generated lexer and parser.
    /// </summary>
    /// <param name="path">File path used for diagnostic reporting.</param>
    /// <param name="text">Source text to parse.</param>
    /// <returns>An ACIRReadResult containing the parsed document and any diagnostics.</returns>
    public static ACIRReadResult Parse(string path, string text)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(text);

        var diagnostics = new List<Diagnostic>();

        try
        {
            var inputStream = CharStreams.fromString(text);
            var lexer = new ACIRLexer(inputStream);
            var tokens = new CommonTokenStream(lexer);
            var parser = new ACIRParser(tokens);

            lexer.RemoveErrorListeners();
            parser.RemoveErrorListeners();

            var listener = new ACIRErrorListener(path, diagnostics);
            lexer.AddErrorListener(listener);
            parser.AddErrorListener(listener);

            var rootContext = parser.document();

            // If there are syntax errors, return early with null document
            if (
                diagnostics.Count > 0
                && diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)
            )
            {
                return new ACIRReadResult { Document = null, Diagnostics = diagnostics };
            }

            var builder = new ACIRAstBuilder(path, diagnostics);
            var document = builder.Build(rootContext);

            // Apply bundle desugaring
            var desugared = BundleDesugarer.Desugar(document);

            return new ACIRReadResult { Document = desugared, Diagnostics = diagnostics };
        }
        catch (Exception ex)
        {
            diagnostics.Add(
                new Diagnostic(
                    $"ACIR0001: Failed to parse ACIR: {ex.Message}",
                    DiagnosticSeverity.Error,
                    path,
                    1,
                    1
                )
            );
            return new ACIRReadResult { Document = null, Diagnostics = diagnostics };
        }
    }
}
