using System;

namespace Cascode.Parser;

public sealed class Diagnostic
{
    public Diagnostic(string message, DiagnosticSeverity severity, string filePath, int line, int column)
    {
        Message = message ?? throw new ArgumentNullException(nameof(message));
        Severity = severity;
        FilePath = filePath ?? string.Empty;
        if (line < 1)
            throw new ArgumentOutOfRangeException(nameof(line), line, "Line must be a positive integer (>= 1).");
        if (column < 1)
            throw new ArgumentOutOfRangeException(nameof(column), column, "Column must be a positive integer (>= 1).");
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

