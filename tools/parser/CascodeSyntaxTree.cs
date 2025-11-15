using System.Collections.Generic;

namespace Cascode.Parser;

public sealed class CascodeSyntaxTree
{
    public CascodeSyntaxTree(CompilationUnitSyntax root, IReadOnlyList<Diagnostic> diagnostics)
    {
        Root = root;
        Diagnostics = diagnostics;
    }

    public CompilationUnitSyntax Root { get; }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }
}

