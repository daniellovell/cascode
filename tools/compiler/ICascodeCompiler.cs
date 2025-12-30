using System.Collections.Generic;

namespace Cascode.Compiler;

/// <summary>
/// Compiles Cascode source files to ACIR.
/// </summary>
public interface ICascodeCompiler
{
    /// <summary>
    /// Compiles the provided source units to ACIR.
    /// </summary>
    /// <param name="sources">Source units to compile.</param>
    /// <param name="options">Compilation options.</param>
    /// <returns>Compilation result containing ACIR and diagnostics.</returns>
    /// <remarks>
    /// In v0, only the first motif declaration in the first source file is considered.
    /// </remarks>
    CompileResult CompileToACIR(IReadOnlyList<SourceUnit> sources, CompileOptions options);
}
