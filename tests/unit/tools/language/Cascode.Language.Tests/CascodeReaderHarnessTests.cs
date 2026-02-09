using System.Linq;
using Cascode.Language;

namespace Cascode.Language.Tests;

public class CascodeReaderHarnessTests
{
    [Fact]
    public void TryParse_HarnessWithSweep_ParsesSweepCondition()
    {
        var content =
            $@"VERSION {CascodeVersion.Current}
circuit Test implements SingleEndedAmp {{
  level EL
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog
  harness {{
    supply VDD = 1.8 V
    sweep InputDCBias [0.3 V:100 mV:1.5 V]
    load OUT C=1 pF
  }}
}}
";
        var result = CascodeReader.TryParse(content);
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
            $@"VERSION {CascodeVersion.Current}
circuit Test {{
  level EL
  harness {{
    supply VDD = 1.8V
    bias VTAIL = 0.6V
    load OUT C=1p F
    source IN Z=50
  }}
}}
";
        var result = CascodeReader.TryParse(content);
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
            $@"VERSION {CascodeVersion.Current}
circuit Test implements SingleEndedAmp {{
  level EL
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog
  harness {{
    supply VDD = 1.8 V
    sweep InputDCBias [Auto]
  }}
}}
";
        var result = CascodeReader.TryParse(content);
        Assert.True(result.Success);
        var sweep = result.Document!.Circuits[0].Harness!.Sweeps[0];
        Assert.True(sweep.IsAuto);
        Assert.Equal("InputDCBias", sweep.Name);
    }

    [Fact]
    public void TryParse_HarnessWithAutoStepSweep_ParsesWithoutStep()
    {
        var content =
            $@"VERSION {CascodeVersion.Current}
circuit Test implements SingleEndedAmp {{
  level EL
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog
  harness {{
    sweep InputDCBias [0.3 V:1.5 V]
  }}
}}
";
        var result = CascodeReader.TryParse(content);
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
            $@"VERSION {CascodeVersion.Current}
circuit Test {{
  level EL
  harness {{
    load OUT (C=1 pF || R=1 MOhm)
  }}
}}
";
        var result = CascodeReader.TryParse(content);
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
            $@"VERSION {CascodeVersion.Current}
circuit Test {{
  level EL
  harness {{
    load OUT (R=10 kOhm || C=10 pF)
  }}
}}
";
        var result = CascodeReader.TryParse(content);
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
            $@"VERSION {CascodeVersion.Current}
circuit Test {{
  level EL
  harness {{
    load OUT (C=1pF || R=1MOhm || C=15pF)
  }}
}}
";
        var result = CascodeReader.TryParse(content);
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
            $@"VERSION {CascodeVersion.Current}
circuit Test {{
  level EL
  harness {{
    load OUT (C=1 pF || )
    load OUT (|| R=1 MOhm)
    load OUT (C=1 pF R=1 MOhm)
    load OUT C=1 pF || R=1 MOhm
    load OUT (C= || R=1 MOhm)
  }}
}}
";
        var result = CascodeReader.TryParse(content);
        Assert.False(result.Success);
        // With ANTLR, malformed load syntax produces CAS0001 errors
        Assert.True(
            result.Diagnostics.Count(d =>
                d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS0001")
            ) >= 1,
            "Expected at least one CAS0001 error for malformed load syntax"
        );
    }

    [Fact]
    public void TryParse_HarnessWithInvalidSweepRange_EmitsDiagnosticError()
    {
        var content =
            $@"VERSION {CascodeVersion.Current}
circuit Test implements SingleEndedAmp {{
  level EL
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog
  harness {{
    sweep InputDCBias []
  }}
}}
";
        var result = CascodeReader.TryParse(content, "test.cas");

        Assert.False(result.Success);
        // With ANTLR, empty sweep range produces a syntax error (CAS0001)
        var errorDiag = result.Diagnostics.FirstOrDefault(d =>
            d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS0001")
        );
        Assert.NotNull(errorDiag);
        Assert.True(errorDiag.Line >= 9, "Error should be on or after line 9 (sweep line)");
    }

    [Fact]
    public void TryParse_HarnessWithMultipleSweeps_ParsesAll()
    {
        var content =
            $@"VERSION {CascodeVersion.Current}
circuit Test {{
  level EL
  supply VDD
  ground GND
  input IN_P : analog
  input IN_N : analog
  output OUT : analog
  harness {{
    sweep InputDCCommonMode [0.4V:100mV:1.4V]
    sweep OutputDCCommonMode [0.5V:1.3V]
  }}
}}
";
        var result = CascodeReader.TryParse(content);
        Assert.True(result.Success);
        var sweeps = result.Document!.Circuits[0].Harness!.Sweeps;
        Assert.Equal(2, sweeps.Count);
        Assert.Equal("InputDCCommonMode", sweeps[0].Name);
        Assert.Equal("OutputDCCommonMode", sweeps[1].Name);
    }
}
