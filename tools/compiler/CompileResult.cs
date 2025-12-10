using System.Collections.Generic;
using Cascode.ACIR;
using Cascode.Parser;

namespace Cascode.Compiler;

/// <summary>
/// Result produced by the compiler for a set of source units.
/// </summary>
public sealed class CompileResult
{
    /// <summary>
    /// Lowered ACIR document when compilation succeeds; <c>null</c> if any fatal diagnostics were emitted.
    /// </summary>
    public ACIRDocument? ACIR { get; init; }

    /// <summary>
    /// Diagnostics collected during parsing, elaboration, and lowering in source order.
    /// </summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = Array.Empty<Diagnostic>();
}
