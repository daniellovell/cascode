using System.IO;
using Cascode.ACIR;
using Cascode.Parser;

namespace Cascode.ACIR.Tests;

public class ACIRReaderTests
{
    [Fact]
    public void TryRead_ValidDocument_ReturnsSuccess()
    {
        var acir = @"ACIR 1

circuit TestCircuit
  level EL
  supply VDD
  ground GND
  port IN : analog
  port OUT : analog
  fill:
    nmos M1 (G->IN, D->OUT, S->GND, B->GND) : W=1u L=180n nmos
";

        using var reader = new StringReader(acir);
        var result = ACIRReader.TryRead(reader, "test.cir");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Single(result.Document!.Circuits);
        Assert.Equal("TestCircuit", result.Document.Circuits[0].Name);
    }

    [Fact]
    public void TryParse_ValidDocument_ReturnsSuccess()
    {
        var acir = @"ACIR 1

circuit TestCircuit
  level EL
  supply VDD
  ground GND
  port IN : analog
";

        var result = ACIRReader.TryParse(acir, "test.cir");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
    }

    [Fact]
    public void TryRead_InvalidVersionDeclaration_ReturnsError()
    {
        var acir = @"ACIR invalid

circuit TestCircuit
  level EL
";

        using var reader = new StringReader(acir);
        var result = ACIRReader.TryRead(reader, "test.cir");

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("ACIR0002"));
    }

    [Fact]
    public void TryRead_MalformedDeviceDeclaration_ReturnsError()
    {
        var acir = @"ACIR 1

circuit TestCircuit
  level EL
  supply VDD
  ground GND
  port IN : analog
  fill:
    nmos M1 bad_syntax here
";

        using var reader = new StringReader(acir);
        var result = ACIRReader.TryRead(reader, "test.cir");

        Assert.Contains(result.Diagnostics, d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("ACIR0004"));
    }

    [Fact]
    public void TryRead_MalformedBinding_ReturnsWarning()
    {
        var acir = @"ACIR 1

circuit TestCircuit
  level EL
  supply VDD
  ground GND
  port IN : analog
  port OUT : analog
  fill:
    nmos M1 (G->IN, bad_binding, D->OUT, S->GND, B->GND) : W=1u L=180n nmos
";

        using var reader = new StringReader(acir);
        var result = ACIRReader.TryRead(reader, "test.cir");

        Assert.Contains(result.Diagnostics, d =>
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("ACIR0005") &&
            d.Message.Contains("bad_binding"));
    }

    [Fact]
    public void TryRead_DiagnosticsIncludeLineNumbers()
    {
        var acir = @"ACIR 1

circuit TestCircuit
  level EL
  supply VDD
  ground GND
  fill:
    nmos M1 invalid_syntax
";

        using var reader = new StringReader(acir);
        var result = ACIRReader.TryRead(reader, "test.cir");

        var errorDiag = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        Assert.NotNull(errorDiag);
        Assert.Equal("test.cir", errorDiag.FilePath);
        Assert.True(errorDiag.Line > 0);
    }

    [Fact]
    public void TryRead_EmptyDocument_ReturnsEmptyResult()
    {
        var acir = @"";

        using var reader = new StringReader(acir);
        var result = ACIRReader.TryRead(reader, "test.cir");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        Assert.Empty(result.Document!.Circuits);
    }

    [Fact]
    public void TryRead_CommentsOnly_ReturnsEmptyResult()
    {
        var acir = @"; This is a comment
; Another comment
";

        using var reader = new StringReader(acir);
        var result = ACIRReader.TryRead(reader, "test.cir");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        Assert.Empty(result.Document!.Circuits);
    }

    [Fact]
    public void TryRead_MissingVersionDeclaration_ReturnsWarning()
    {
        var acir = @"circuit TestCircuit
  level EL
  supply VDD
  ground GND
";

        using var reader = new StringReader(acir);
        var result = ACIRReader.TryRead(reader, "test.cir");

        // Should still parse but with a warning
        Assert.NotNull(result.Document);
        Assert.Contains(result.Diagnostics, d =>
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("ACIR0002") &&
            d.Message.Contains("Missing version"));
    }

    [Fact]
    public void ACIRReadResult_ErrorCount_ReflectsErrors()
    {
        var acir = @"ACIR invalid

circuit TestCircuit
  level EL
  fill:
    nmos M1 bad
    nmos M2 also_bad
";

        using var reader = new StringReader(acir);
        var result = ACIRReader.TryRead(reader, "test.cir");

        Assert.True(result.HasErrors);
        Assert.True(result.ErrorCount > 0);
    }

    [Fact]
    public void ACIRReadResult_WarningCount_ReflectsWarnings()
    {
        var acir = @"ACIR 1

circuit TestCircuit
  level EL
  supply VDD
  ground GND
  port IN : analog
  port OUT : analog
  fill:
    nmos M1 (G->IN, bad1, bad2, D->OUT, S->GND, B->GND) : W=1u L=180n nmos
";

        using var reader = new StringReader(acir);
        var result = ACIRReader.TryRead(reader, "test.cir");

        Assert.True(result.HasWarnings);
        Assert.True(result.WarningCount >= 2);
    }
}
