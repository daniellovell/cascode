using System;

namespace Cascode.Parser;

/// <summary>
/// Represents a parser or compiler diagnostic tied to a specific source location.
/// </summary>
public sealed class Diagnostic
{
    public Diagnostic(
        string message,
        DiagnosticSeverity severity,
        string filePath,
        int line,
        int column,
        string? code = null
    )
    {
        Message = message ?? throw new ArgumentNullException(nameof(message));
        Severity = severity;
        FilePath = filePath ?? string.Empty;
        if (line < 1)
            throw new ArgumentOutOfRangeException(
                nameof(line),
                line,
                "Line must be a positive integer (>= 1)."
            );
        if (column < 1)
            throw new ArgumentOutOfRangeException(
                nameof(column),
                column,
                "Column must be a positive integer (>= 1)."
            );
        Line = line;
        Column = column;
        Code = string.IsNullOrWhiteSpace(code)
            ? InferCodeFromMessage(message) ?? string.Empty
            : code;
    }

    /// <summary>Machine-readable diagnostic code (e.g., "ACIR0001").</summary>
    public string Code { get; }

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

    private static string? InferCodeFromMessage(string message)
    {
        var colonIndex = message.IndexOf(':');
        if (colonIndex <= 0)
        {
            return null;
        }

        var candidate = message[..colonIndex].Trim();
        if (candidate.Length == 0)
        {
            return null;
        }

        foreach (var c in candidate)
        {
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
            {
                return null;
            }
        }

        return candidate;
    }
}

/// <summary>
/// Severity levels for compiler diagnostics.
/// </summary>
public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error,
}
