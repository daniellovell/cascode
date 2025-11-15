using System;
using System.Collections.Generic;
using Antlr4.Runtime;

namespace Cascode.Parser;

public static class CascodeParserFacade
{
    public static CascodeSyntaxTree Parse(string path, string text)
    {
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
