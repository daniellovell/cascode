using System;
using System.Collections.Generic;

namespace Cascode.Language;

/// <summary>
/// Exception thrown when Cascode parsing fails.
/// </summary>
public sealed class CascodeParseException : Exception
{
    /// <summary>
    /// Diagnostics collected during parsing.
    /// </summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CascodeParseException"/> class.
    /// </summary>
    /// <param name="message">Error message.</param>
    /// <param name="diagnostics">Diagnostics collected during parsing.</param>
    public CascodeParseException(string message, IReadOnlyList<Diagnostic> diagnostics)
        : base(message)
    {
        Diagnostics = diagnostics;
    }
}
