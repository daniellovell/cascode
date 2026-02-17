using Cascode.Language;
using Xunit;

namespace Cascode.Language.Tests;

public sealed class CascodeSymbolUtilsTests
{
    [Fact]
    public void ContainsPrimitiveDecl_MatchesPrimitiveDeclarationShapesIncludingInductor()
    {
        const string content = """
            primitive NMOS nfet_01v8(size(W=1u, L=180n, M=1))
            primitive PMOS pfet_01v8(size(W=1u, L=180n, M=1))
            primitive Resistor rpoly(size(R=1k))
            primitive Capacitor mimcap(size(C=1p))
            primitive Diode ndiode(size(A=1u))
            primitive Inductor lind(size(L=10n))
            """;

        Assert.True(CascodeSymbolUtils.ContainsPrimitiveDecl(content, "nfet_01v8"));
        Assert.True(CascodeSymbolUtils.ContainsPrimitiveDecl(content, "pfet_01v8"));
        Assert.True(CascodeSymbolUtils.ContainsPrimitiveDecl(content, "rpoly"));
        Assert.True(CascodeSymbolUtils.ContainsPrimitiveDecl(content, "mimcap"));
        Assert.True(CascodeSymbolUtils.ContainsPrimitiveDecl(content, "ndiode"));
        Assert.True(CascodeSymbolUtils.ContainsPrimitiveDecl(content, "lind"));
    }

    [Fact]
    public void ContainsPrimitiveDecl_DoesNotMatchWhenPrimitivePrefixOrShapeIsMissing()
    {
        const string content = """
            NMOS nfet_01v8(size(W=1u, L=180n, M=1))
            device PMOS pfet_01v8(size(W=1u, L=180n, M=1))
            primitive Resistor rpoly
            primitive Diode alias
            """;

        Assert.False(CascodeSymbolUtils.ContainsPrimitiveDecl(content, "nfet_01v8"));
        Assert.False(CascodeSymbolUtils.ContainsPrimitiveDecl(content, "pfet_01v8"));
        Assert.False(CascodeSymbolUtils.ContainsPrimitiveDecl(content, "rpoly"));
        Assert.False(CascodeSymbolUtils.ContainsPrimitiveDecl(content, "alias"));
    }

    [Fact]
    public void MightDefineAnySymbol_UsesContainsPrimitiveDecl()
    {
        const string content = "primitive NMOS nfet_01v8(size(W=1u, L=180n, M=1))";
        Assert.True(CascodeSymbolUtils.MightDefineAnySymbol(content, "nfet_01v8"));
    }
}
