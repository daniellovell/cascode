using System.Collections.Generic;
using System.IO;
using Antlr4.Runtime;

namespace Cascode.Parser;

internal sealed class CollectingErrorListener : IAntlrErrorListener<IToken>
{
    private readonly string _filePath;
    private readonly List<Diagnostic> _diagnostics;

    public CollectingErrorListener(string filePath, List<Diagnostic> diagnostics)
    {
        _filePath = filePath;
        _diagnostics = diagnostics;
    }

    public void SyntaxError(
        TextWriter output,
        IRecognizer recognizer,
        IToken offendingSymbol,
        int line,
        int charPositionInLine,
        string msg,
        RecognitionException e)
    {
        _diagnostics.Add(new Diagnostic(
            msg,
            DiagnosticSeverity.Error,
            _filePath,
            line,
            charPositionInLine + 1));
    }
}
