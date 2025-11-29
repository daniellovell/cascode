using System.Collections.Generic;
using Cascode.CasIR;

namespace Cascode.Compiler;

/// <summary>
/// Options for Cascode compilation.
/// </summary>
/// <param name="EntryMotifFullName">Full name of the entry motif (unused in v0).</param>
/// <param name="Level">Target CasIR level (e.g., <see cref="CasIRLevel.ML"/>).</param>
/// <remarks>
/// In v0, only the first motif declaration in the first source file is considered,
/// regardless of the EntryMotifFullName value.
/// </remarks>
public sealed record CompileOptions(
    string EntryMotifFullName,
    CasIRLevel Level)
{
    /// <summary>
    /// Root directories to search for imported motif definitions.
    /// Import paths like <c>lib.std.prim.DiffPair</c> are resolved relative to these roots.
    /// </summary>
    public IReadOnlyList<string>? LibraryRoots { get; init; }
}
