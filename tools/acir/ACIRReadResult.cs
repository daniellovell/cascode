using System;
using System.Collections.Generic;
using System.Linq;
using Cascode.Parser;

namespace Cascode.ACIR;

/// <summary>
/// Result of reading an ACIR document, containing the parsed document and any diagnostics.
/// </summary>
/// <remarks>
/// Follows the same pattern as <see cref="Cascode.Compiler.CompileResult"/> for consistency
/// across the toolchain. Diagnostics use the standard <see cref="Diagnostic"/> type from
/// <see cref="Cascode.Parser"/> with ACIR-specific error codes.
/// </remarks>
public sealed class ACIRReadResult
{
    /// <summary>
    /// Parsed ACIR document when reading succeeds; <c>null</c> if any fatal diagnostics were emitted.
    /// </summary>
    public ACIRDocument? Document { get; init; }

    /// <summary>
    /// Diagnostics collected during parsing in source order.
    /// </summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = Array.Empty<Diagnostic>();

    /// <summary>
    /// True if the document was successfully parsed with no errors.
    /// </summary>
    public bool Success =>
        Document != null && !Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>
    /// True if there are any errors (fatal diagnostics).
    /// </summary>
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>
    /// True if there are any warnings.
    /// </summary>
    public bool HasWarnings => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Warning);

    /// <summary>
    /// Count of errors only.
    /// </summary>
    public int ErrorCount => Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>
    /// Count of warnings only.
    /// </summary>
    public int WarningCount => Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
}
