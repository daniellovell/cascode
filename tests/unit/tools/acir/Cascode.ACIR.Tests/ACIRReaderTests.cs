using System.IO;
using Cascode.ACIR;
using Cascode.Parser;

namespace Cascode.ACIR.Tests;

public class ACIRReaderTests
{
    [Fact]
    public void TryRead_ValidDocument_ReturnsSuccess()
    {
        var acir =
            @"ACIR 1.0

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
        var acir =
            @"ACIR 1.0

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
        var acir =
            @"ACIR invalid

circuit TestCircuit
  level EL
";

        using var reader = new StringReader(acir);
        var result = ACIRReader.TryRead(reader, "test.cir");

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("ACIR0002")
        );
    }

    [Fact]
    public void TryRead_MalformedDeviceDeclaration_ReturnsError()
    {
        var acir =
            @"ACIR 1.0

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

        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("ACIR0004")
        );
    }

    [Fact]
    public void TryRead_MalformedBinding_ReturnsWarning()
    {
        var acir =
            @"ACIR 1.0

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

        Assert.Contains(
            result.Diagnostics,
            d =>
                d.Severity == DiagnosticSeverity.Warning
                && d.Message.Contains("ACIR0005")
                && d.Message.Contains("bad_binding")
        );
    }

    [Fact]
    public void TryRead_DiagnosticsIncludeLineNumbers()
    {
        var acir =
            @"ACIR 1.0

circuit TestCircuit
  level EL
  supply VDD
  ground GND
  fill:
    nmos M1 invalid_syntax
";

        using var reader = new StringReader(acir);
        var result = ACIRReader.TryRead(reader, "test.cir");

        var errorDiag = result.Diagnostics.FirstOrDefault(d =>
            d.Severity == DiagnosticSeverity.Error
        );
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
        var acir =
            @"// This is a comment
// Another comment
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
        var acir =
            @"circuit TestCircuit
  level EL
  supply VDD
  ground GND
";

        using var reader = new StringReader(acir);
        var result = ACIRReader.TryRead(reader, "test.cir");

        // Should still parse but with a warning
        Assert.NotNull(result.Document);
        Assert.Contains(
            result.Diagnostics,
            d =>
                d.Severity == DiagnosticSeverity.Warning
                && d.Message.Contains("ACIR0002")
                && d.Message.Contains("Missing version")
        );
    }

    [Fact]
    public void ACIRReadResult_ErrorCount_ReflectsErrors()
    {
        var acir =
            @"ACIR invalid

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
        var acir =
            @"ACIR 1.0

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
        var content =
            @"ACIR 1.0
circuit Test : SingleEndedAmp
  level EL
  supply VDD
  ground GND
  port IN : analog
  port OUT : analog
  harness:
    supply VDD = 1.8 V
    sweep InputDCBias [0.3 V:100 mV:1.5 V]
    load OUT C=1 pF
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
    public void TryParse_HarnessWithLegacyFormat_NormalizesToCompactSI()
    {
        var content =
            @"ACIR 1.0
circuit Test
  level EL
  harness:
    supply VDD = 1.8V
    bias VTAIL = 0.6V
    load OUT C=1p F
    source IN Z=50
";
        var result = ACIRReader.TryParse(content);
        Assert.True(result.Success);
        var harness = result.Document!.Circuits[0].Harness!;
        Assert.Equal("1.8V", harness.Supplies[0].Value);
        Assert.Equal("0.6V", harness.Biases[0].Value);
        Assert.Single(harness.Loads[0].Elements);
        Assert.Equal("C", harness.Loads[0].Elements[0].Type);
        Assert.Equal("1pF", harness.Loads[0].Elements[0].Value);
        Assert.Equal("50Ohm", harness.Sources[0].Z);
    }

    [Fact]
    public void TryParse_HarnessWithAutoSweep_ParsesAutoFlag()
    {
        var content =
            @"ACIR 1.0
circuit Test : SingleEndedAmp
  level EL
  supply VDD
  ground GND
  port IN : analog
  port OUT : analog
  harness:
    supply VDD = 1.8 V
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
        var content =
            @"ACIR 1.0
circuit Test : SingleEndedAmp
  level EL
  supply VDD
  ground GND
  port IN : analog
  port OUT : analog
  harness:
    sweep InputDCBias [0.3 V:1.5 V]
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
    public void TryParse_HarnessWithParallelLoad_ParsesBothComponents()
    {
        var content =
            @"ACIR 1.0
circuit Test
  level EL
  harness:
    load OUT (C=1 pF || R=1 MOhm)
";
        var result = ACIRReader.TryParse(content);
        Assert.True(result.Success);
        var load = result.Document!.Circuits[0].Harness!.Loads[0];
        Assert.Equal(2, load.Elements.Count);
        Assert.Equal("C", load.Elements[0].Type);
        Assert.Equal("1pF", load.Elements[0].Value);
        Assert.Equal("R", load.Elements[1].Type);
        Assert.Equal("1MOhm", load.Elements[1].Value);
    }

    [Fact]
    public void TryParse_HarnessWithParallelLoadReverseOrder_ParsesBothComponents()
    {
        var content =
            @"ACIR 1.0
circuit Test
  level EL
  harness:
    load OUT (R=10 kOhm || C=10 pF)
";
        var result = ACIRReader.TryParse(content);
        Assert.True(result.Success);
        var load = result.Document!.Circuits[0].Harness!.Loads[0];
        Assert.Equal(2, load.Elements.Count);
        Assert.Equal("R", load.Elements[0].Type);
        Assert.Equal("10kOhm", load.Elements[0].Value);
        Assert.Equal("C", load.Elements[1].Type);
        Assert.Equal("10pF", load.Elements[1].Value);
    }

    [Fact]
    public void TryParse_HarnessWithMultipleSameTypeElements_ParsesAll()
    {
        var content =
            @"ACIR 1.0
circuit Test
  level EL
  harness:
    load OUT (C=1pF || R=1MOhm || C=15pF)
";
        var result = ACIRReader.TryParse(content);
        Assert.True(result.Success);
        var load = result.Document!.Circuits[0].Harness!.Loads[0];
        Assert.Equal(3, load.Elements.Count);
        Assert.Equal("C", load.Elements[0].Type);
        Assert.Equal("1pF", load.Elements[0].Value);
        Assert.Equal("R", load.Elements[1].Type);
        Assert.Equal("1MOhm", load.Elements[1].Value);
        Assert.Equal("C", load.Elements[2].Type);
        Assert.Equal("15pF", load.Elements[2].Value);
    }

    [Fact]
    public void TryParse_MalformedParallelLoad_EmitsDiagnostics()
    {
        var content =
            @"ACIR 1.0
circuit Test
  level EL
  harness:
    load OUT (C=1 pF || )
    load OUT (|| R=1 MOhm)
    load OUT (C=1 pF R=1 MOhm)
    load OUT C=1 pF || R=1 MOhm
    load OUT (C= || R=1 MOhm)
";
        var result = ACIRReader.TryParse(content);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("ACIR0010")); // Missing parens
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("ACIR0011")); // Missing ||
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("ACIR0012")); // Missing first
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("ACIR0013")); // Missing second
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("ACIR0014")); // Missing value
    }

    [Fact]
    public void ACIRWriter_ParallelLoad_EmitsCanonicalFormat()
    {
        var circuit = new Circuit
        {
            Name = "Test",
            Level = ACIRLevel.EL,
            Harness = new HarnessBlock
            {
                Loads = new List<LoadValue>
                {
                    new()
                    {
                        Net = "OUT",
                        Elements = new List<LoadElement>
                        {
                            new LoadElement("C", "1pF"),
                            new LoadElement("R", "1MOhm"),
                        },
                    },
                },
            },
        };
        var doc = new ACIRDocument { Circuits = new List<Circuit> { circuit } };
        using var writer = new StringWriter();
        ACIRWriter.Write(doc, writer);
        var output = writer.ToString();

        Assert.Contains("load OUT (C=1pF || R=1MOhm)", output);
    }

    [Fact]
    public void ACIRWriter_WithBiases_EmitsBiasLines()
    {
        var circuit = new Circuit
        {
            Name = "TestWithBias",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new() { Name = "VTAIL", Type = "bias" },
                new() { Name = "OUT", Type = "analog" },
            },
            Fill = new FillBlock(),
            Harness = new HarnessBlock
            {
                Supplies = new List<SupplyValue>
                {
                    new() { Net = "VDD", Value = "1.8V" },
                },
                Biases = new List<BiasValue>
                {
                    new() { Net = "VTAIL", Value = "0.7V" },
                    new() { Net = "VBIAS", Value = "0.5V" },
                },
                Loads = new List<LoadValue>
                {
                    new()
                    {
                        Net = "OUT",
                        Elements = new List<LoadElement> { new LoadElement("C", "100fF") },
                    },
                },
            },
        };
        var doc = new ACIRDocument { Circuits = new List<Circuit> { circuit } };
        using var writer = new StringWriter();
        ACIRWriter.Write(doc, writer);
        var output = writer.ToString();

        Assert.Contains("bias VTAIL = 0.7V", output);
        Assert.Contains("bias VBIAS = 0.5V", output);
        Assert.Contains("supply VDD = 1.8V", output);
        Assert.Contains("load OUT C=100fF", output);
    }

    [Fact]
    public void TryParse_HarnessWithInvalidSweepRange_EmitsDiagnosticErrorIncludingLineAndRangeSpec()
    {
        var content =
            @"ACIR 1.0
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
            d.Severity == DiagnosticSeverity.Error
            && d.Message.Contains("ACIR0006")
            && d.Message.Contains("sweep InputDCBias []")
            && d.Message.Contains("''")
        );
        Assert.NotNull(errorDiag);
        Assert.Equal(9, errorDiag.Line);
    }

    [Fact]
    public void TryParse_HarnessWithMultipleSweeps_ParsesAll()
    {
        var content =
            @"ACIR 1.0
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

    [Fact]
    public void TryParse_ConstraintsWithInlineComments_ParsesCorrectly()
    {
        var content =
            @"ACIR 1.0
circuit Test
  level EL
  supply VDD
  ground GND
  port IN : analog
  port OUT : analog
  constraints:
    numeric:
      c_gbw : GainBandwidth @ OUT >= 100M Hz  // target gain-bandwidth product
      c_gain : PassbandGain @ OUT >= 40 dB  // minimum gain requirement
      c_pm : PhaseMargin @ OUT >= 60 deg  // phase margin for stability
      c_pwr : Power <= 500u W
    tech:
      t_lmin : L >= 180n m on *  // minimum length per tech rules
    measure:
      m_gbw : SEOpAmpACBench GainBandwidth @ OUT  // measure GBW
      m_gain : SEOpAmpACBench PassbandGain @ OUT
";
        var result = ACIRReader.TryParse(content);
        Assert.True(result.Success);
        Assert.NotNull(result.Document);

        var circuit = result.Document.Circuits[0];
        var constraints = circuit.Constraints;
        Assert.NotNull(constraints);

        // All numeric constraints should be parsed despite inline comments
        Assert.Equal(4, constraints.Numeric.Count);
        Assert.Contains(constraints.Numeric, c => c.Id == "c_gbw" && c.Metric == "GainBandwidth");
        Assert.Contains(constraints.Numeric, c => c.Id == "c_gain" && c.Metric == "PassbandGain");
        Assert.Contains(constraints.Numeric, c => c.Id == "c_pm" && c.Metric == "PhaseMargin");
        Assert.Contains(constraints.Numeric, c => c.Id == "c_pwr" && c.Metric == "Power");

        // Tech constraint should be parsed despite inline comment
        Assert.Single(constraints.Tech);
        Assert.Equal("t_lmin", constraints.Tech[0].Id);
        Assert.Equal("L", constraints.Tech[0].Param);

        // Measure intents should be parsed despite inline comment
        Assert.Equal(2, constraints.Measure.Count);
        Assert.Contains(constraints.Measure, m => m.Id == "m_gbw");
        Assert.Contains(constraints.Measure, m => m.Id == "m_gain");
    }

    [Fact]
    public void TryParse_FullLineComments_AreIgnored()
    {
        var content =
            @"ACIR 1.0
circuit Test
  level EL
  supply VDD
  ground GND
  port OUT : analog
  constraints:
    numeric:
      // This is a full line comment
      // This is another full line comment
      c_test : Metric @ OUT >= 100M Hz
";
        var result = ACIRReader.TryParse(content);
        Assert.True(result.Success);
        Assert.NotNull(result.Document);

        var constraints = result.Document.Circuits[0].Constraints;
        Assert.NotNull(constraints);
        Assert.Single(constraints.Numeric);
        Assert.Equal("c_test", constraints.Numeric[0].Id);
    }
}
