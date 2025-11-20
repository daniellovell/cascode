using System;

namespace Cascode.Parser;

/// <summary>
/// Represents a parser or compiler diagnostic tied to a specific source location.
/// </summary>
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

    /// <summary>Human-readable description of the issue.</summary>
    public string Message { get; }

    /// <summary>Severity level used to gate further compilation steps.</summary>
    public DiagnosticSeverity Severity { get; }

    /// <summary>Path of the source file that produced the diagnostic.</summary>
    public string FilePath { get; }

    /// <summary>1-based line number of the diagnostic.</summary>
    public int Line { get; }

    /// <summary>1-based column number of the diagnostic.</summary>
    public int Column { get; }
}

/// <summary>
/// Severity levels for compiler diagnostics.
/// </summary>
public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}
