using System;

namespace Cascode.Parser;

public sealed class Diagnostic
{
    public Diagnostic(string message, DiagnosticSeverity severity, string filePath, int line, int column)
    {
        Message = message ?? throw new ArgumentNullException(nameof(message));
        Severity = severity;
        FilePath = filePath ?? string.Empty;
        Line = line;
        Column = column;
    }

    public string Message { get; }
    public DiagnosticSeverity Severity { get; }
    public string FilePath { get; }
    public int Line { get; }
    public int Column { get; }
}

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}

