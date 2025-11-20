using System;
using System.Collections.Generic;
using Antlr4.Runtime;

namespace Cascode.Parser;

/// <summary>
/// Entry point for parsing Cascode source text into a syntax tree plus diagnostics.
/// </summary>
public static class CascodeParserFacade
{
    /// <summary>
    /// Parses the provided source text using the ANTLR-generated lexer and parser.
    /// </summary>
    /// <param name="path">File path used for diagnostic reporting.</param>
    /// <param name="text">Source text to parse.</param>
    /// <returns>A syntax tree with collected diagnostics.</returns>
    public static CascodeSyntaxTree Parse(string path, string text)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }
        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        var diagnostics = new List<Diagnostic>();

        var inputStream = CharStreams.fromString(text);
        var lexer = new CascodeLexer(inputStream);
        var tokens = new CommonTokenStream(lexer);
        var parser = new CascodeParser(tokens);

        lexer.RemoveErrorListeners();
        parser.RemoveErrorListeners();

        var listener = new CollectingErrorListener(path, diagnostics);
        parser.AddErrorListener(listener);

        var rootContext = parser.compilationUnit();

        var builder = new CascodeAstBuilder(path);
        var root = builder.Build(rootContext);

        return new CascodeSyntaxTree(root, diagnostics);
    }
}
