using System.Collections.Generic;
using System.IO;
using Antlr4.Runtime;

namespace Cascode.Parser;

/// <summary>
/// ANTLR error listener that collects syntax errors into the compiler diagnostic list.
/// </summary>
internal sealed class CollectingErrorListener : IAntlrErrorListener<IToken>
{
    private readonly string _filePath;
    private readonly List<Diagnostic> _diagnostics;

    /// <summary>
    /// Creates an error listener bound to a specific source file path.
    /// </summary>
    /// <param name="filePath">Path used for diagnostic reporting.</param>
    /// <param name="diagnostics">List that accumulates emitted diagnostics.</param>
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
