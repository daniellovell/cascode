using System.Collections.Generic;

namespace Cascode.Compiler;

/// <summary>
/// Compiles Cascode source files to CasIR.
/// </summary>
public interface ICascodeCompiler
{
    /// <summary>
    /// Compiles the provided source units to CasIR.
    /// </summary>
    /// <param name="sources">Source units to compile.</param>
    /// <param name="options">Compilation options.</param>
    /// <returns>Compilation result containing CasIR and diagnostics.</returns>
    /// <remarks>
    /// In v0, only the first motif declaration in the first source file is considered.
    /// </remarks>
    CompileResult CompileToCasir(
        IReadOnlyList<SourceUnit> sources,
        CompileOptions options);
}

