using System.Collections.Generic;
using Cascode.Casir;
using Cascode.Parser;

namespace Cascode.Compiler;

public sealed class CompileResult
{
    public CasirDocument? Casir { get; init; }

    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = Array.Empty<Diagnostic>();
}

