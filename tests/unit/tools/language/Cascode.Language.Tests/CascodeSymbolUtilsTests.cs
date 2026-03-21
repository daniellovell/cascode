using Cascode.Language;
using Xunit;

namespace Cascode.Language.Tests;

public sealed class CascodeSymbolUtilsTests
{
    [Fact]
    public void ContainsPrimitiveDecl_MatchesPrimitiveDeclarationShapesIncludingInductor()
    {
        const string content = """
            primitive nfet_01v8(size(W=1u, L=180n, M=1)) implements NMOS
            primitive pfet_01v8(size(W=1u, L=180n, M=1)) implements PMOS
            primitive rpoly(size(R=1k)) implements Resistor
            primitive mimcap(size(C=1p)) implements Capacitor
            primitive ndiode(size(A=1u)) implements Diode
            primitive lind(size(L=10n)) implements Inductor
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
            primitive rpoly implements Resistor
            primitive alias implements Diode
            """;

        Assert.False(CascodeSymbolUtils.ContainsPrimitiveDecl(content, "nfet_01v8"));
        Assert.False(CascodeSymbolUtils.ContainsPrimitiveDecl(content, "pfet_01v8"));
        Assert.False(CascodeSymbolUtils.ContainsPrimitiveDecl(content, "rpoly"));
        Assert.False(CascodeSymbolUtils.ContainsPrimitiveDecl(content, "alias"));
    }

    [Fact]
    public void MightDefineAnySymbol_UsesContainsPrimitiveDecl()
    {
        const string content = "primitive nfet_01v8(size(W=1u, L=180n, M=1)) implements NMOS";
        Assert.True(CascodeSymbolUtils.MightDefineAnySymbol(content, "nfet_01v8"));
    }
}
