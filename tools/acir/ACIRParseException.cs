using System;
using System.Collections.Generic;
using Cascode.Parser;

namespace Cascode.ACIR;

/// <summary>
/// Exception thrown when ACIR parsing fails.
/// </summary>
public sealed class ACIRParseException : Exception
{
    /// <summary>
    /// Diagnostics collected during parsing.
    /// </summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ACIRParseException"/> class.
    /// </summary>
    /// <param name="message">Error message.</param>
    /// <param name="diagnostics">Diagnostics collected during parsing.</param>
    public ACIRParseException(string message, IReadOnlyList<Diagnostic> diagnostics)
        : base(message)
    {
        Diagnostics = diagnostics;
    }
}
