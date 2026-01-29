using System.IO;
using Cascode.Language;
using Cascode.Parser;

namespace Cascode.Language.Tests;

public class ACIRReaderBasicTests
{
    [Fact]
    public void TryRead_ValidDocument_ReturnsSuccess()
    {
        var acir =
            $@"VERSION {ACIRVersion.Current}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog
  fill {{
    nmos M1 = new Level1_NMOS(size(W=1u, L=180n)) {{
      .G--IN
      .D--OUT
      .S--GND
      .B--GND
    }}
  }}
}}
";

        using var reader = new StringReader(acir);
        var result = ACIRReader.TryRead(reader, "test.cas");

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
            $@"VERSION {ACIRVersion.Current}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
  input IN : analog
}}
";

        var result = ACIRReader.TryParse(acir, "test.cas");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
    }

    [Fact]
    public void TryRead_InvalidVersionDeclaration_ReturnsError()
    {
        var acir =
            @"VERSION invalid

circuit TestCircuit {
  level EL
}
";

        using var reader = new StringReader(acir);
        var result = ACIRReader.TryRead(reader, "test.cas");

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS0001")
        );
    }

    [Fact]
    public void TryRead_MalformedDeviceDeclaration_ReturnsError()
    {
        var acir =
            $@"VERSION {ACIRVersion.Current}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
  input IN : analog
  fill {{
    nmos M1 bad_syntax here
  }}
}}
";

        using var reader = new StringReader(acir);
        var result = ACIRReader.TryRead(reader, "test.cas");

        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS0001")
        );
    }

    [Fact]
    public void TryRead_MalformedBinding_ReturnsError()
    {
        var acir =
            $@"VERSION {ACIRVersion.Current}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog
  fill {{
    nmos M1 = new Level1_NMOS(size(W=1u, L=180n)) {{
      .G--IN
      bad_binding
      .D--OUT
      .S--GND
      .B--GND
    }}
  }}
}}
";

        using var reader = new StringReader(acir);
        var result = ACIRReader.TryRead(reader, "test.cas");

        // With ANTLR, malformed bindings are syntax errors (CAS0001) rather than warnings
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS0001")
        );
    }

    [Fact]
    public void TryRead_LegacyArrowBinding_IsRejected()
    {
        var acir =
            $@"VERSION {ACIRVersion.Current}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog
  fill {{
    nmos M1 = new Level1_NMOS(size(W=1u, L=180n)) {{
      .G->IN
      .D--OUT
      .S--GND
      .B--GND
    }}
  }}
}}
";

        using var reader = new StringReader(acir);
        var result = ACIRReader.TryRead(reader, "test.cas");

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS0001")
        );
    }

    [Fact]
    public void TryRead_LegacyConnectKeyword_IsRejected()
    {
        var acir =
            $@"VERSION {ACIRVersion.Current}

circuit TestCircuit {{
  level EL
  fill {{
    net a : analog
    net b : analog
    connect a -> b
  }}
}}
";

        using var reader = new StringReader(acir);
        var result = ACIRReader.TryRead(reader, "test.cas");

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS0001")
        );
    }

    [Fact]
    public void TryRead_DiagnosticsIncludeLineNumbers()
    {
        var acir =
            $@"VERSION {ACIRVersion.Current}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
  fill {{
    nmos M1 invalid_syntax
  }}
}}
";

        using var reader = new StringReader(acir);
        var result = ACIRReader.TryRead(reader, "test.cas");

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
        var acir = @"";

        using var reader = new StringReader(acir);
        var result = ACIRReader.TryRead(reader, "test.cas");

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
        var result = ACIRReader.TryRead(reader, "test.cas");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        Assert.Empty(result.Document!.Circuits);
    }

    [Fact]
    public void TryRead_MissingVersionDeclaration_ReturnsWarning()
    {
        var acir =
            @"circuit TestCircuit {
  level EL
  supply VDD
  ground GND
}
";

        using var reader = new StringReader(acir);
        var result = ACIRReader.TryRead(reader, "test.cas");

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
        var acir =
            $@"VERSION {ACIRVersion.Current}

circuit Test {{
  level XL
  supply VDD
  ground GND
}}
";

        var result = ACIRReader.TryParse(acir, "test.cas");

        Assert.False(result.Success);
        // With ANTLR, invalid level is a syntax error (CAS0001)
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS0001")
        );
    }

    [Fact]
    public void ACIRReadResult_ErrorCount_ReflectsErrors()
    {
        var acir =
            @"VERSION invalid

circuit TestCircuit {
  level EL
  fill {
    nmos M1 bad
    nmos M2 also_bad
  }
}
";

        using var reader = new StringReader(acir);
        var result = ACIRReader.TryRead(reader, "test.cas");

        Assert.True(result.HasErrors);
        Assert.True(result.ErrorCount > 0);
    }

    [Fact]
    public void ACIRReadResult_WarningCount_ReflectsWarnings()
    {
        // With ANTLR parser, missing version declaration produces a warning (CAS0002)
        var acir =
            @"circuit TestCircuit {
  level EL
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog
}
";

        using var reader = new StringReader(acir);
        var result = ACIRReader.TryRead(reader, "test.cas");

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
        var acir =
            $@"VERSION {ACIRVersion.Current}

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

        using var reader = new StringReader(acir);
        var result = ACIRReader.TryRead(reader, "test.cas");

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
        var acir =
            $@"VERSION {ACIRVersion.Current}

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

        using var reader = new StringReader(acir);
        var result = ACIRReader.TryRead(reader, "test.cas");

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
        var acir =
            $@"VERSION {ACIRVersion.Current}

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

        using var reader = new StringReader(acir);
        var result = ACIRReader.TryRead(reader, "test.cas");

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
        var acir =
            $@"VERSION {ACIRVersion.Current}

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

        using var reader = new StringReader(acir);
        var result = ACIRReader.TryRead(reader, "test.cas");

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
        var acir =
            $@"VERSION {ACIRVersion.Current}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog
  fill {{
    nmos M1 = new Level1_NMOS(size(W=1u, L=180n)) {{
      {gBinding}
      {dBinding}
      {sBinding}
      {bBinding}
    }}
  }}
}}
";

        var result = ACIRReader.TryParse(acir, "test.cas");

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
        var acir =
            $@"VERSION {ACIRVersion.Current}

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

        var result = ACIRReader.TryParse(acir, "test.cas");

        Assert.True(
            result.Success,
            $"Parse failed: {string.Join(", ", result.Diagnostics.Select(d => d.Message))}"
        );
        Assert.NotNull(result.Document);

        var trait = result.Document!.Traits.First(t => t.Name == "CurrentMirrorLike");
        Assert.Single(trait.Connectors);
        Assert.Single(trait.Connectors[0].Mappings);
        Assert.Equal("SENSE", trait.Connectors[0].Mappings[0].SourcePort);
        Assert.Equal("OUT.P", trait.Connectors[0].Mappings[0].TargetPort);
    }

    [Theory]
    [InlineData("dp.IN--IN")] // no whitespace
    [InlineData("dp.IN -- IN")] // spaces around operator
    [InlineData("dp.IN--  IN")] // trailing space only
    [InlineData("dp.IN  --IN")] // leading space only
    public void TryRead_FillConnect_ToleratesWhitespaceAroundWireOperator(string connect)
    {
        var acir =
            $@"VERSION {ACIRVersion.Current}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
  input IN : analog
  fill {{
    dp = new DiffPair {{ .VDD--VDD, .GND--GND }}
    {connect}
  }}
}}
";

        var result = ACIRReader.TryParse(acir, "test.cas");

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

    #endregion
}
