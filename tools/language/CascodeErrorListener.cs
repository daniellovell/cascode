using System.Collections.Generic;
using System.IO;
using Antlr4.Runtime;

namespace Cascode.Language;

/// <summary>
/// ANTLR error listener that collects lexer and parser errors into the Cascode diagnostic list.
/// </summary>
internal sealed class CascodeErrorListener : IAntlrErrorListener<IToken>, IAntlrErrorListener<int>
{
    private readonly string _filePath;
    private readonly List<Diagnostic> _diagnostics;

    /// <summary>
    /// Creates an error listener bound to a specific source file path.
    /// </summary>
    /// <param name="filePath">Path used for diagnostic reporting.</param>
    /// <param name="diagnostics">List that accumulates emitted diagnostics.</param>
    public CascodeErrorListener(string filePath, List<Diagnostic> diagnostics)
    {
        _filePath = filePath;
        _diagnostics = diagnostics;
    }

    private void AddSyntaxDiagnostic(string message, int line, int charPositionInLine)
    {
        _diagnostics.Add(
            new Diagnostic(
                $"CAS0001: {message}",
                DiagnosticSeverity.Error,
                _filePath,
                line,
                charPositionInLine + 1
            )
        );
    }

    /// <summary>
    /// Handles parser errors with offending tokens.
    /// </summary>
    public void SyntaxError(
        TextWriter output,
        IRecognizer recognizer,
        IToken offendingSymbol,
        int line,
        int charPositionInLine,
        string msg,
        RecognitionException e
    )
    {
        AddSyntaxDiagnostic(msg, line, charPositionInLine);
    }

    /// <summary>
    /// Handles lexer errors with offending character codes.
    /// </summary>
    public void SyntaxError(
        TextWriter output,
        IRecognizer recognizer,
        int offendingSymbol,
        int line,
        int charPositionInLine,
        string msg,
        RecognitionException e
    )
    {
        AddSyntaxDiagnostic(msg, line, charPositionInLine);
    }
}
