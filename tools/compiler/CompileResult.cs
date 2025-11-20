using System.Collections.Generic;
using Cascode.CasIR;
using Cascode.Parser;

namespace Cascode.Compiler;

public sealed class CompileResult
{
    public CasirDocument? CasIR { get; init; }

    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = Array.Empty<Diagnostic>();
}

