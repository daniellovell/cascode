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

    [Fact]
    public void TryParse_HarnessWithSweep_ParsesSweepCondition()
    {
        var content = @"ACIR 1
circuit Test : SingleEndedAmp
  level EL
  supply VDD
  ground GND
  port IN : analog
  port OUT : analog
  harness:
    supply VDD = 1.8V
    sweep InputDCBias [0.3V:100mV:1.5V]
    load OUT C=1p F
";
        var result = ACIRReader.TryParse(content);
        Assert.True(result.Success);
        var circuit = result.Document!.Circuits[0];
        Assert.NotNull(circuit.Harness);
        Assert.Single(circuit.Harness.Sweeps);
        var sweep = circuit.Harness.Sweeps[0];
        Assert.Equal("InputDCBias", sweep.Name);
        Assert.Equal("0.3V", sweep.Start);
        Assert.Equal("1.5V", sweep.Stop);
        Assert.Equal("100mV", sweep.Step);
        Assert.False(sweep.IsAuto);
    }

    [Fact]
    public void TryParse_HarnessWithAutoSweep_ParsesAutoFlag()
    {
        var content = @"ACIR 1
circuit Test : SingleEndedAmp
  level EL
  supply VDD
  ground GND
  port IN : analog
  port OUT : analog
  harness:
    supply VDD = 1.8V
    sweep InputDCBias [Auto]
";
        var result = ACIRReader.TryParse(content);
        Assert.True(result.Success);
        var sweep = result.Document!.Circuits[0].Harness!.Sweeps[0];
        Assert.True(sweep.IsAuto);
        Assert.Equal("InputDCBias", sweep.Name);
    }

    [Fact]
    public void TryParse_HarnessWithAutoStepSweep_ParsesWithoutStep()
    {
        var content = @"ACIR 1
circuit Test : SingleEndedAmp
  level EL
  supply VDD
  ground GND
  port IN : analog
  port OUT : analog
  harness:
    sweep InputDCBias [0.3V:1.5V]
";
        var result = ACIRReader.TryParse(content);
        Assert.True(result.Success);
        var sweep = result.Document!.Circuits[0].Harness!.Sweeps[0];
        Assert.Equal("InputDCBias", sweep.Name);
        Assert.Equal("0.3V", sweep.Start);
        Assert.Equal("1.5V", sweep.Stop);
        Assert.Null(sweep.Step);
        Assert.False(sweep.IsAuto);
    }

    [Fact]
    public void TryParse_HarnessWithInvalidSweepRange_EmitsDiagnosticErrorIncludingLineAndRangeSpec()
    {
        var content = @"ACIR 1
circuit Test : SingleEndedAmp
  level EL
  supply VDD
  ground GND
  port IN : analog
  port OUT : analog
  harness:
    sweep InputDCBias []
";
        var result = ACIRReader.TryParse(content, "test.cir");

        Assert.False(result.Success);
        var errorDiag = result.Diagnostics.FirstOrDefault(d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("ACIR0006") &&
            d.Message.Contains("sweep InputDCBias []") &&
            d.Message.Contains("''"));
        Assert.NotNull(errorDiag);
        Assert.Equal(10, errorDiag.Line);
    }

    [Fact]
    public void TryParse_HarnessWithMultipleSweeps_ParsesAll()
    {
        var content = @"ACIR 1
circuit Test
  level EL
  supply VDD
  ground GND
  port IN_P : analog
  port IN_N : analog
  port OUT : analog
  harness:
    sweep InputDCCommonMode [0.4V:100mV:1.4V]
    sweep OutputDCCommonMode [0.5V:1.3V]
";
        var result = ACIRReader.TryParse(content);
        Assert.True(result.Success);
        var sweeps = result.Document!.Circuits[0].Harness!.Sweeps;
        Assert.Equal(2, sweeps.Count);
        Assert.Equal("InputDCCommonMode", sweeps[0].Name);
        Assert.Equal("OutputDCCommonMode", sweeps[1].Name);
    }
}
