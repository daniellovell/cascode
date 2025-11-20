using System.Collections.Generic;

namespace Cascode.Parser;

/// <summary>
/// Immutable wrapper around the parsed syntax tree and diagnostics for a compilation unit.
/// </summary>
public sealed class CascodeSyntaxTree
{
    public CascodeSyntaxTree(CompilationUnitSyntax root, IReadOnlyList<Diagnostic> diagnostics)
    {
        Root = root;
        Diagnostics = diagnostics;
    }

    /// <summary>Root node of the parsed compilation unit.</summary>
    public CompilationUnitSyntax Root { get; }

    /// <summary>Diagnostics produced during lexing and parsing.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
}
