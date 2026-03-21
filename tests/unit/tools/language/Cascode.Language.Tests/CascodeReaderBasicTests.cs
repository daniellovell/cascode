using System.IO;
using System.Linq;
using Cascode.Language;

namespace Cascode.Language.Tests;

public class CascodeReaderBasicTests
{
    [Fact]
    public void TryRead_ValidDocument_ReturnsSuccess()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog
  fill {{
    NMOS M1 = new NMOS_Level1(size(W=1u, L=180n)) {{
      .G--IN
      .D--OUT
      .S--GND
      .B--GND
    }}
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Single(result.Document!.Circuits);
        Assert.Equal("TestCircuit", result.Document.Circuits[0].Name);
    }

    [Fact]
    public void TryParse_ValidDocument_ReturnsSuccess()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
  input IN : analog
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
    }

    [Fact]
    public void TryParse_PrimitiveDeclarationWithImplements_ReturnsSuccess()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

primitive NMOS_Level1(size primSize) implements NMOS {{
  device ""nmos_level1""
  params {{
    W = primSize.W
    L = primSize.L
    m = primSize.M
  }}
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        var primitive = Assert.Single(result.Document.Primitives);
        Assert.Equal("NMOS_Level1", primitive.Name);
        Assert.Equal("NMOS", primitive.Kind);
    }

    [Fact]
    public void TryParse_LegacyPrimitiveDeclarationShape_IsRejected()
    {
        const string legacyKind = "NMOS";
        var cascode =
            $@"VERSION {CascodeVersion.Current}

primitive {legacyKind} NMOS_Level1(size primSize) {{
  device ""nmos_level1""
  params {{
    W = primSize.W
    L = primSize.L
    m = primSize.M
  }}
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS0001")
        );
    }

    [Fact]
    public void TryRead_InvalidVersionDeclaration_ReturnsError()
    {
        var cascode =
            @"VERSION invalid

circuit TestCircuit {
  level EL
}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS0001")
        );
    }

    [Fact]
    public void TryRead_MalformedDeviceDeclaration_ReturnsError()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
  input IN : analog
  fill {{
    NMOS M1 bad_syntax here
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");

        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS0001")
        );
    }

    [Fact]
    public void TryRead_MalformedBinding_ReturnsError()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog
  fill {{
    NMOS M1 = new NMOS_Level1(size(W=1u, L=180n)) {{
      .G--IN
      bad_binding
      .D--OUT
      .S--GND
      .B--GND
    }}
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");

        // With ANTLR, malformed bindings are syntax errors (CAS0001) rather than warnings
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS0001")
        );
    }

    [Fact]
    public void TryRead_LegacyArrowBinding_IsRejected()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog
  fill {{
    NMOS M1 = new NMOS_Level1(size(W=1u, L=180n)) {{
      .G->IN
      .D--OUT
      .S--GND
      .B--GND
    }}
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS0001")
        );
    }

    [Fact]
    public void TryRead_LegacyConnectKeyword_IsRejected()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit TestCircuit {{
  level EL
  fill {{
    net a : analog
    net b : analog
    connect a -> b
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS0001")
        );
    }

    [Fact]
    public void TryRead_DiagnosticsIncludeLineNumbers()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
  fill {{
    NMOS M1 invalid_syntax
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");

        var errorDiag = result.Diagnostics.FirstOrDefault(d =>
            d.Severity == DiagnosticSeverity.Error
        );
        Assert.NotNull(errorDiag);
        Assert.Equal("test.cas", errorDiag.FilePath);
        Assert.True(errorDiag.Line > 0);
    }

    [Fact]
    public void TryRead_EmptyDocument_ReturnsEmptyResult()
    {
        var cascode = @"";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        Assert.Empty(result.Document!.Circuits);
    }

    [Fact]
    public void TryRead_CommentsOnly_ReturnsEmptyResult()
    {
        var cascode =
            @"// This is a comment
// Another comment
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        Assert.Empty(result.Document!.Circuits);
    }

    [Fact]
    public void TryRead_MissingVersionDeclaration_ReturnsWarning()
    {
        var cascode =
            @"circuit TestCircuit {
  level EL
  supply VDD
  ground GND
}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");

        // Should still parse but with a warning
        Assert.NotNull(result.Document);
        Assert.Contains(
            result.Diagnostics,
            d =>
                d.Severity == DiagnosticSeverity.Warning
                && d.Message.Contains("CAS0002")
                && d.Message.Contains("Missing version")
        );
    }

    [Fact]
    public void TryParse_InvalidLevel_EmitsError()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit Test {{
  level XL
  supply VDD
  ground GND
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.False(result.Success);
        // With ANTLR, invalid level is a syntax error (CAS0001)
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS0001")
        );
    }

    [Fact]
    public void CascodeReadResult_ErrorCount_ReflectsErrors()
    {
        var cascode =
            @"VERSION invalid

circuit TestCircuit {
  level EL
  fill {
    NMOS M1 bad
    NMOS M2 also_bad
  }
}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");

        Assert.True(result.HasErrors);
        Assert.True(result.ErrorCount > 0);
    }

    [Fact]
    public void CascodeReadResult_WarningCount_ReflectsWarnings()
    {
        // With ANTLR parser, missing version declaration produces a warning (CAS0002)
        var cascode =
            @"circuit TestCircuit {
  level EL
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog
}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");

        Assert.True(result.HasWarnings);
        Assert.True(result.WarningCount >= 1);
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("CAS0002")
        );
    }

    #region Attach Override Parsing

    [Fact]
    public void TryRead_AttachWithInlineOverrides_ParsesOverrides()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

interface CurrentMirrorLike {{
  io SENSE : analog
  connectors {{
    to DiffPairLike {{
      SENSE--OUT.P
    }}
  }}
}}

interface DiffPairLike {{
  io OUT.P : analog
  io OUT.N : analog
}}

circuit Test {{
  level EL
  supply VDD
  ground GND
  fill {{
    attach cm to dp via CurrentMirrorLike::DiffPairLike {{
      .SENSE--OUT.N
    }}
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");

        Assert.True(
            result.Success,
            $"Parse failed: {string.Join(", ", result.Diagnostics.Select(d => d.Message))}"
        );
        Assert.NotNull(result.Document);

        var circuit = result.Document!.Circuits.First();
        Assert.NotNull(circuit.Fill);
        Assert.Single(circuit.Fill!.Attaches);

        var attach = circuit.Fill.Attaches[0];
        Assert.NotNull(attach.Overrides);
        Assert.Single(attach.Overrides);
        Assert.Equal("SENSE", attach.Overrides[0].SourcePort);
        Assert.Equal("OUT.N", attach.Overrides[0].TargetPort);
    }

    [Fact]
    public void TryRead_AttachWithAnchorAndOverrides_ParsesBoth()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

interface CurrentMirrorLike {{
  io SENSE : analog
  connectors {{
    to DiffPairLike {{
      SENSE--OUT.P
    }}
  }}
}}

interface DiffPairLike {{
  io OUT.P : analog
  io OUT.N : analog
}}

circuit Test {{
  level EL
  supply VDD
  ground GND
  fill {{
    attach cm to dp via CurrentMirrorLike::DiffPairLike as mirror_node {{
      .SENSE--OUT.N
    }}
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");

        Assert.True(
            result.Success,
            $"Parse failed: {string.Join(", ", result.Diagnostics.Select(d => d.Message))}"
        );
        Assert.NotNull(result.Document);

        var circuit = result.Document!.Circuits.First();
        var attach = circuit.Fill!.Attaches[0];

        Assert.Equal("mirror_node", attach.Anchor);
        Assert.NotNull(attach.Overrides);
        Assert.Single(attach.Overrides);
        Assert.Equal("SENSE", attach.Overrides[0].SourcePort);
        Assert.Equal("OUT.N", attach.Overrides[0].TargetPort);
    }

    [Fact]
    public void TryRead_AttachWithMultipleOverrides_ParsesAll()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

interface CurrentMirrorLike {{
  io SENSE : analog
  io TAP : analog
  connectors {{
    to DiffPairLike {{
      SENSE--OUT.P
      TAP--OUT.N
    }}
  }}
}}

interface DiffPairLike {{
  io OUT.P : analog
  io OUT.N : analog
}}

circuit Test {{
  level EL
  supply VDD
  ground GND
  fill {{
    attach cm to dp via CurrentMirrorLike::DiffPairLike {{
      .SENSE--OUT.N
      .TAP--OUT.P
    }}
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");

        Assert.True(
            result.Success,
            $"Parse failed: {string.Join(", ", result.Diagnostics.Select(d => d.Message))}"
        );
        Assert.NotNull(result.Document);

        var circuit = result.Document!.Circuits.First();
        var attach = circuit.Fill!.Attaches[0];

        Assert.NotNull(attach.Overrides);
        Assert.Equal(2, attach.Overrides.Count);
    }

    [Fact]
    public void TryRead_AttachWithoutOverrides_HasNullOverrides()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

interface CurrentMirrorLike {{
  io SENSE : analog
  connectors {{
    to DiffPairLike {{
      SENSE--OUT.P
    }}
  }}
}}

interface DiffPairLike {{
  io OUT.P : analog
}}

circuit Test {{
  level EL
  supply VDD
  ground GND
  fill {{
    attach cm to dp via CurrentMirrorLike::DiffPairLike
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);

        var circuit = result.Document!.Circuits.First();
        var attach = circuit.Fill!.Attaches[0];

        Assert.Null(attach.Overrides);
    }

    #endregion

    #region Arrow Whitespace Tolerance

    [Theory]
    [InlineData(".G--IN", ".D--OUT", ".S--GND", ".B--GND")] // no whitespace (canonical)
    [InlineData(".G -- IN", ".D -- OUT", ".S -- GND", ".B -- GND")] // spaces around operator
    [InlineData(".G--  IN", ".D--  OUT", ".S--  GND", ".B--  GND")] // trailing space only
    [InlineData(".G  --IN", ".D  --OUT", ".S  --GND", ".B  --GND")] // leading space only
    public void TryRead_DeviceBinding_ToleratesWhitespaceAroundWireOperator(
        string gBinding,
        string dBinding,
        string sBinding,
        string bBinding
    )
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog
  fill {{
    NMOS M1 = new NMOS_Level1(size(W=1u, L=180n)) {{
      {gBinding}
      {dBinding}
      {sBinding}
      {bBinding}
    }}
  }}
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.True(
            result.Success,
            $"Parse failed: {string.Join(", ", result.Diagnostics.Select(d => d.Message))}"
        );
        Assert.NotNull(result.Document);

        var circuit = result.Document!.Circuits.First();
        Assert.NotNull(circuit.Fill);
        Assert.Single(circuit.Fill!.Devices);

        var device = circuit.Fill.Devices[0];
        Assert.Equal("M1", device.Id);
        Assert.Equal("IN", device.Bindings["G"]);
        Assert.Equal("OUT", device.Bindings["D"]);
        Assert.Equal("GND", device.Bindings["S"]);
        Assert.Equal("GND", device.Bindings["B"]);
    }

    [Theory]
    [InlineData("SENSE--OUT.P")] // no whitespace
    [InlineData("SENSE -- OUT.P")] // spaces around operator
    [InlineData("SENSE--  OUT.P")] // trailing space only
    [InlineData("SENSE  --OUT.P")] // leading space only
    public void TryRead_ConnectorMapping_ToleratesWhitespaceAroundWireOperator(string mapping)
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

interface CurrentMirrorLike {{
  io SENSE : analog
  connectors {{
    to DiffPairLike {{
      {mapping}
    }}
  }}
}}

interface DiffPairLike {{
  io OUT.P : analog
}}

circuit Test {{
  level EL
  supply VDD
  ground GND
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.True(
            result.Success,
            $"Parse failed: {string.Join(", ", result.Diagnostics.Select(d => d.Message))}"
        );
        Assert.NotNull(result.Document);

        var interfaceDef = result.Document!.Traits.First(t => t.Name == "CurrentMirrorLike");
        Assert.Single(interfaceDef.Connectors);
        Assert.Single(interfaceDef.Connectors[0].Mappings);
        Assert.Equal("SENSE", interfaceDef.Connectors[0].Mappings[0].SourcePort);
        Assert.Equal("OUT.P", interfaceDef.Connectors[0].Mappings[0].TargetPort);
    }

    [Theory]
    [InlineData("dp.IN--IN")] // no whitespace
    [InlineData("dp.IN -- IN")] // spaces around operator
    [InlineData("dp.IN--  IN")] // trailing space only
    [InlineData("dp.IN  --IN")] // leading space only
    public void TryRead_FillConnect_ToleratesWhitespaceAroundWireOperator(string connect)
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
  input IN : analog
  fill {{
    DiffPair dp = new DiffPair {{ .VDD--VDD, .GND--GND }}
    {connect}
  }}
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.True(
            result.Success,
            $"Parse failed: {string.Join(", ", result.Diagnostics.Select(d => d.Message))}"
        );
        Assert.NotNull(result.Document);

        var circuit = result.Document!.Circuits.First();
        Assert.NotNull(circuit.Fill);
        Assert.Single(circuit.Fill!.Instances);
        Assert.Single(circuit.Fill!.Connections);

        var conn = circuit.Fill!.Connections[0];
        Assert.Equal("dp.IN", conn.From);
        Assert.Equal("IN", conn.To);
    }

    [Fact]
    public void TryRead_InvalidMeasurementNamedArgument_DoesNotInferDeclaredMeasurementType()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench ArgumentValidationBench {{
  measurements {{
    measurement GainAt(Frequency f) : dB {{
      return 1dB
    }}
  }}

  function GainSpectrumFromCall() : GainSpectrum {{
    return GainAt(freq=1GHz)
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d =>
                d.Code == "CAS2004"
                && d.Message.Contains("Return type 'Scalar'", StringComparison.Ordinal)
                && d.Message.Contains("expected 'GainSpectrum'", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void TryRead_MeasurementCall_RejectsPositionalAfterNamed()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench ArgumentOrderingBench {{
  measurements {{
    measurement GainAt(Time t, Frequency f) : V/rtHz {{
      return noise(1)
    }}

    measurement UsesMixedArgs : V/rtHz {{
      return GainAt(f=1GHz, 1ns)
    }}
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d =>
                d.Code == "CAS2004"
                && d.Message.Contains("Return type 'Scalar'", StringComparison.Ordinal)
                && d.Message.Contains("expected 'NoiseSpectralDensity'", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void TryRead_BareZeroArgMeasurementReferences_ParticipateInCycleDetection()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench BarePathCycleBench {{
  measurements {{
    measurement A : Hz {{
      return B
    }}

    measurement B : Hz {{
      return A
    }}
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == "CAS2007");
    }

    [Fact]
    public void TryRead_ZeroArgMeasurementDependencies_IgnoreParameterShadowing()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench ParameterShadowBench {{
  measurements {{
    measurement A(Frequency B) : Hz {{
      return B
    }}

    measurement B : Hz {{
      return A(1Hz)
    }}
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");

        Assert.True(
            result.Success,
            $"Unexpected diagnostics: {string.Join(", ", result.Diagnostics.Select(d => d.Code + ":" + d.Message))}"
        );
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "CAS2007");
    }

    [Fact]
    public void TryRead_NoiseDensityLiteralsInBenchMeasurements_ParseSuccessfully()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench NoiseLiteralBench {{
  measurements {{
    measurement SpotNoiseTarget : V/rtHz {{
      return 9nV/rtHz
    }}
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "noise_literal_bench.cas");

        Assert.True(
            result.Success,
            $"Unexpected diagnostics: {string.Join(", ", result.Diagnostics.Select(d => d.Code + ":" + d.Message))}"
        );
        Assert.NotNull(result.Document);
    }

    #endregion
}
