using System.IO;
using System.Linq;
using Cascode.Language;

namespace Cascode.Language.Tests;

public class CascodeReaderHierarchyTests
{
    [Fact]
    public void TryRead_TraitDefinition_ParsesSuccessfully()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

interface CurrentMirror {{
  input IN : analog
  output OUT : analog
  input BIAS : analog
}}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        Assert.Single(result.Document!.Traits);
        var trait = result.Document.Traits[0];
        Assert.Equal("CurrentMirror", trait.Name);
        Assert.Equal(3, trait.Ports.Count);
        Assert.Contains(trait.Ports, p => p.Name == "IN" && p.Type == "analog");
        Assert.Contains(trait.Ports, p => p.Name == "OUT" && p.Type == "analog");
        Assert.Contains(trait.Ports, p => p.Name == "BIAS" && p.Type == "analog");
    }

    [Fact]
    public void TryRead_TraitWithConnectors_ParsesSuccessfully()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

interface CurrentMirror {{
  input IN : analog
  output OUT : analog
  connectors {{
    to LoadBranch {{
      OUT--IN
    }}
  }}
}}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        Assert.Single(result.Document!.Traits);
        var trait = result.Document.Traits[0];
        Assert.Single(trait.Connectors);
        var connector = trait.Connectors[0];
        Assert.Equal("LoadBranch", connector.TargetTrait);
        Assert.Single(connector.Mappings);
        Assert.Equal("OUT", connector.Mappings[0].SourcePort);
        Assert.Equal("IN", connector.Mappings[0].TargetPort);
    }

    [Fact]
    public void TryRead_CircuitWithInline_ParsesSuccessfully()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit CurrentMirror {{
  level EL
  inline
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        Assert.Single(result.Document!.Circuits);
        Assert.True(result.Document.Circuits[0].Inline);
    }

    [Fact]
    public void TryRead_CircuitWithoutInline_DefaultsFalse()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        Assert.False(result.Document!.Circuits[0].Inline);
    }

    [Fact]
    public void TryRead_CircuitParameters_ParsesSuccessfully()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit TestCircuit(real ratio=2, real width, int count=4) {{
  level EL
  supply VDD
  ground GND
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        Assert.Equal(3, result.Document!.Circuits[0].Parameters.Count);

        var ratioParam = result.Document.Circuits[0].Parameters.First(p => p.Name == "ratio");
        Assert.Equal("real", ratioParam.Type);
        Assert.NotNull(ratioParam.Default);
        Assert.Equal("2", ratioParam.Default!.Numeric);

        var widthParam = result.Document.Circuits[0].Parameters.First(p => p.Name == "width");
        Assert.Equal("real", widthParam.Type);
        Assert.Null(widthParam.Default);

        var countParam = result.Document.Circuits[0].Parameters.First(p => p.Name == "count");
        Assert.Equal("int", countParam.Type);
        Assert.NotNull(countParam.Default);
        Assert.Equal("4", countParam.Default!.Numeric);
    }

    [Fact]
    public void TryRead_CircuitParameterWithSymbolic_ParsesSuccessfully()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit TestCircuit(real width=Auto) {{
  level EL
  supply VDD
  ground GND
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        var param = result.Document!.Circuits[0].Parameters.First();
        Assert.Equal("Auto", param.Default!.Symbolic);
    }

    [Fact]
    public void TryRead_InstanceDeclaration_ParsesSuccessfully()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit TestCircuit {{
  level ML
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog
  fill {{
    cm = new CurrentMirror {{ .IN--IN, .OUT--OUT }}
  }}
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        Assert.NotNull(result.Document!.Circuits[0].Fill);
        Assert.Single(result.Document.Circuits[0].Fill!.Instances);
        var inst = result.Document.Circuits[0].Fill!.Instances[0];
        Assert.Equal("cm", inst.Id);
        Assert.Equal("CurrentMirror", inst.Type);
        Assert.Equal(2, inst.Bindings.Count);
        Assert.Equal("IN", inst.Bindings["IN"]);
        Assert.Equal("OUT", inst.Bindings["OUT"]);
    }

    [Fact]
    public void TryRead_InstanceWithoutBindings_ParsesSuccessfully()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit TestCircuit {{
  level ML
  supply VDD
  ground GND
  fill {{
    cm = new CurrentMirror {{ }}
  }}
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        var inst = result.Document!.Circuits[0].Fill!.Instances[0];
        Assert.Equal("cm", inst.Id);
        Assert.Equal("CurrentMirror", inst.Type);
        Assert.Empty(inst.Bindings);
    }

    [Fact]
    public void TryRead_AttachStatement_ParsesSuccessfully()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
  fill {{
    attach cm1 to load1 via CurrentMirror::LoadBranch
  }}
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        Assert.NotNull(result.Document!.Circuits[0].Fill);
        Assert.Single(result.Document.Circuits[0].Fill!.Attaches);
        var attach = result.Document.Circuits[0].Fill!.Attaches[0];
        Assert.Equal("cm1", attach.SourceInstance);
        Assert.Equal("load1", attach.TargetInstances.Single());
        Assert.Equal("CurrentMirror::LoadBranch", attach.Via);
        Assert.Null(attach.Anchor);
    }

    [Fact]
    public void TryRead_AttachStatementWithAnchor_ParsesSuccessfully()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
  fill {{
    attach cm1 to load1 via CurrentMirror::LoadBranch as bias_net
  }}
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        var attach = result.Document!.Circuits[0].Fill!.Attaches[0];
        Assert.Equal("bias_net", attach.Anchor);
    }

    [Fact]
    public void TryRead_AttachWithInlineOverrides_ParsesSuccessfully()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
  fill {{
    attach cm1 to load1 via CurrentMirror::LoadBranch {{ .SENSE--OUT.N }}
  }}
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.True(result.Success);
        var attach = result.Document!.Circuits[0].Fill!.Attaches[0];
        Assert.NotNull(attach.Overrides);
        Assert.Single(attach.Overrides!);
        Assert.Equal("SENSE", attach.Overrides![0].SourcePort);
        Assert.Equal("OUT.N", attach.Overrides![0].TargetPort);
    }

    [Fact]
    public void TryRead_AttachWithMultilineOverrides_ParsesSuccessfully()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
  fill {{
    attach cm1 to load1 via CurrentMirror::LoadBranch {{
      .SENSE--OUT.N
      .OUT--IN
    }}
  }}
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.True(result.Success);
        var attach = result.Document!.Circuits[0].Fill!.Attaches[0];
        Assert.NotNull(attach.Overrides);
        Assert.Equal(2, attach.Overrides!.Count);
        Assert.Equal("SENSE", attach.Overrides![0].SourcePort);
        Assert.Equal("OUT.N", attach.Overrides![0].TargetPort);
        Assert.Equal("OUT", attach.Overrides![1].SourcePort);
        Assert.Equal("IN", attach.Overrides![1].TargetPort);
    }

    [Fact]
    public void TryRead_AttachChain_ParsesSuccessfully()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
  fill {{
    attach a to b to c via CurrentMirror::LoadBranch
  }}
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.True(result.Success);
        var attach = result.Document!.Circuits[0].Fill!.Attaches[0];
        Assert.Equal("a", attach.SourceInstance);
        Assert.Equal(new[] { "b", "c" }, attach.TargetInstances);
    }

    [Fact]
    public void TryRead_AttachChainWithAnchor_ParsesSuccessfully()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
  fill {{
    attach a to b to c via CurrentMirror::LoadBranch as bias_net
  }}
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.True(result.Success);
        var attach = result.Document!.Circuits[0].Fill!.Attaches[0];
        Assert.Equal("bias_net", attach.Anchor);
        Assert.Equal(new[] { "b", "c" }, attach.TargetInstances);
    }

    [Fact]
    public void TryRead_AttachChainWithOverrides_ParsesSuccessfully()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
  fill {{
    attach a to b to c via CurrentMirror::LoadBranch {{
      .SENSE--OUT.N
    }}
  }}
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.True(result.Success);
        var attach = result.Document!.Circuits[0].Fill!.Attaches[0];
        Assert.Equal(new[] { "b", "c" }, attach.TargetInstances);
        Assert.NotNull(attach.Overrides);
        Assert.Single(attach.Overrides!);
    }

    [Fact]
    public void TryRead_AttachCombined_ParsesSuccessfully()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
  fill {{
    attach a to b to c via CurrentMirror::LoadBranch as bias_net {{
      .SENSE--OUT.N
    }}
  }}
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.True(result.Success);
        var attach = result.Document!.Circuits[0].Fill!.Attaches[0];
        Assert.Equal("bias_net", attach.Anchor);
        Assert.Equal(new[] { "b", "c" }, attach.TargetInstances);
        Assert.NotNull(attach.Overrides);
        Assert.Single(attach.Overrides!);
    }

    [Fact]
    public void TryRead_InvalidInstanceDeclaration_ReturnsError()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit TestCircuit {{
  level ML
  supply VDD
  ground GND
  fill {{
    bad = new
  }}
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        // With ANTLR, invalid instance declaration is a syntax error (CAS0001)
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS0001")
        );
    }

    [Fact]
    public void TryRead_InvalidAttachStatement_ReturnsError()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
  fill {{
    attach bad syntax
  }}
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        // With ANTLR, invalid attach statement is a syntax error (CAS0001)
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS0001")
        );
    }

    [Fact]
    public void TryRead_CircuitImplementsTrait_ParsesSuccessfully()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

interface CurrentMirror {{
  input IN : analog
  output OUT : analog
}}

circuit CMirror implements CurrentMirror {{
  level EL
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        var circuit = result.Document!.Circuits[0];
        Assert.NotNull(circuit.Traits);
        Assert.Single(circuit.Traits);
        Assert.Equal("CurrentMirror", circuit.Traits[0]);
    }

    [Fact]
    public void TryRead_CircuitImplementsMultipleTraits_ParsesSuccessfully()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit CMirror implements CurrentMirror, Foldable {{
  level EL
  supply VDD
  ground GND
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        var circuit = result.Document!.Circuits[0];
        Assert.NotNull(circuit.Traits);
        Assert.Equal(2, circuit.Traits.Count);
        Assert.Contains("CurrentMirror", circuit.Traits);
        Assert.Contains("Foldable", circuit.Traits);
    }

    [Fact]
    public void TryRead_InstanceWithBodyBindings_ParsesSuccessfully()
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
    dp = new DiffPair {{
      .VDD--VDD
      .GND--GND
      .IN--IN
      .OUT--OUT
    }}
  }}
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        var instance = result.Document!.Circuits[0].Fill!.Instances.Single();
        Assert.Equal(4, instance.Bindings.Count);
        Assert.Equal("VDD", instance.Bindings["VDD"]);
        Assert.Equal("GND", instance.Bindings["GND"]);
        Assert.Equal("IN", instance.Bindings["IN"]);
        Assert.Equal("OUT", instance.Bindings["OUT"]);
    }

    [Fact]
    public void TryRead_InstanceWithSizeAndNestedBindings_ParsesAll()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
  input IN.P : analog
  input IN.N : analog
  output OUT : analog
  input VTAIL : bias
  fill {{
    dp = new DiffPair(InputPair=size(W=2u, L=180n, M=1), Tail=size(W=4u, L=180n, M=1)) {{
      .GND--GND
      .VDD--VDD
      .IN.P--IN.P
      .IN.N--IN.N
      .OUT.P--OUT
      .TAIL--VTAIL
    }}
  }}
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.True(result.Success);
        var inst = result.Document!.Circuits[0].Fill!.Instances[0];
        Assert.Equal(2, inst.Sizes.Count);
        Assert.True(
            inst.Bindings.Count == 6,
            $"Expected 6 bindings, got {inst.Bindings.Count}: {string.Join(", ", inst.Bindings.Keys.OrderBy(k => k))}"
        );
        Assert.Equal("IN.P", inst.Bindings["IN.P"]);
        Assert.Equal("IN.N", inst.Bindings["IN.N"]);
        Assert.Equal("OUT", inst.Bindings["OUT.P"]);
        Assert.Equal("VTAIL", inst.Bindings["TAIL"]);
    }

    [Fact]
    public void TryRead_InlineBindingWithoutInstancePrefix_ParsesSuccessfully()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
  fill {{
    dp = new DiffPair {{ .VDD--VDD, .GND--GND }}
  }}
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.True(result.Success);
        var inst = result.Document!.Circuits[0].Fill!.Instances[0];
        Assert.Equal(2, inst.Bindings.Count);
        Assert.True(inst.Bindings.ContainsKey("VDD"));
        Assert.True(inst.Bindings.ContainsKey("GND"));
    }

    [Fact]
    public void TryRead_InstanceWithSizeAndBindings_ParsesSuccessfully()
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
    dp = new DiffPair(InputPair=size(W=2u, L=180n, M=1)) {{
      .VDD--VDD
      .GND--GND
      .IN--IN
      .OUT--OUT
    }}
  }}
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.True(result.Success);
        var inst = result.Document!.Circuits[0].Fill!.Instances[0];
        Assert.Equal(4, inst.Bindings.Count);
        Assert.Single(inst.Sizes);
    }
}
