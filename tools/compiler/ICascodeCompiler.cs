using System.Collections.Generic;

namespace Cascode.Compiler;

public interface ICascodeCompiler
{
    CompileResult CompileToCasir(
        IReadOnlyList<SourceUnit> sources,
        CompileOptions options);
}

