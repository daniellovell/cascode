using System;
using System.Collections.Generic;
using Antlr4.Runtime;

namespace Cascode.Language;

/// <summary>
/// Entry point for parsing Cascode source text into an CascodeDocument using ANTLR.
/// </summary>
public static class CascodeParserFacade
{
    /// <summary>
    /// Parses the provided Cascode source text using the ANTLR-generated lexer and parser.
    /// </summary>
    /// <param name="path">File path used for diagnostic reporting.</param>
    /// <param name="text">Source text to parse.</param>
    /// <returns>An CascodeReadResult containing the parsed document and any diagnostics.</returns>
    public static CascodeReadResult Parse(string path, string text)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(text);

        var diagnostics = new List<Diagnostic>();

        try
        {
            var inputStream = CharStreams.fromString(text);
            var lexer = new CascodeLexer(inputStream);
            var tokens = new CommonTokenStream(lexer);
            var parser = new CascodeParser(tokens);

            lexer.RemoveErrorListeners();
            parser.RemoveErrorListeners();

            var listener = new CascodeErrorListener(path, diagnostics);
            lexer.AddErrorListener(listener);
            parser.AddErrorListener(listener);

            var rootContext = parser.document();

            // If there are syntax errors, return early with null document
            if (
                diagnostics.Count > 0
                && diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)
            )
            {
                return new CascodeReadResult { Document = null, Diagnostics = diagnostics };
            }

            var builder = new CascodeAstBuilder(path, diagnostics);
            var document = builder.Build(rootContext);

            // Apply bundle desugaring
            var desugared = BundleDesugarer.Desugar(document);

            return new CascodeReadResult { Document = desugared, Diagnostics = diagnostics };
        }
        catch (Exception ex)
        {
            diagnostics.Add(
                new Diagnostic(
                    $"CAS0001: Failed to parse Cascode: {ex.Message}",
                    DiagnosticSeverity.Error,
                    path,
                    1,
                    1
                )
            );
            return new CascodeReadResult { Document = null, Diagnostics = diagnostics };
        }
    }
}
