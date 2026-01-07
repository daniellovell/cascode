using System.IO;
using Cascode.ACIR;
using Cascode.Parser;

namespace Cascode.ACIR.Tests;

public class ACIRReaderHierarchyTests
{
    [Fact]
    public void TryRead_TraitDefinition_ParsesSuccessfully()
    {
        var acir =
            $@"ACIR {ACIRVersion.Current}

trait CurrentMirror:
  port IN : analog
  port OUT : analog
  port BIAS : analog

circuit TestCircuit
  level EL
  supply VDD
  ground GND
";

        var result = ACIRReader.TryParse(acir, "test.cir");

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
        var acir =
            $@"ACIR {ACIRVersion.Current}

trait CurrentMirror:
  port IN : analog
  port OUT : analog
  connectors:
    to LoadBranch:
      OUT -> IN

circuit TestCircuit
  level EL
  supply VDD
  ground GND
";

        var result = ACIRReader.TryParse(acir, "test.cir");

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
        var acir =
            $@"ACIR {ACIRVersion.Current}

circuit CurrentMirror
  level EL
  inline
  supply VDD
  ground GND
  port IN : analog
  port OUT : analog
";

        var result = ACIRReader.TryParse(acir, "test.cir");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        Assert.Single(result.Document!.Circuits);
        Assert.True(result.Document.Circuits[0].Inline);
    }

    [Fact]
    public void TryRead_CircuitWithoutInline_DefaultsFalse()
    {
        var acir =
            $@"ACIR {ACIRVersion.Current}

circuit TestCircuit
  level EL
  supply VDD
  ground GND
";

        var result = ACIRReader.TryParse(acir, "test.cir");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        Assert.False(result.Document!.Circuits[0].Inline);
    }

    [Fact]
    public void TryRead_CircuitParameters_ParsesSuccessfully()
    {
        var acir =
            $@"ACIR {ACIRVersion.Current}

circuit TestCircuit
  level EL
  param ratio : real = 2
  param width : real
  param count : int = 4
  supply VDD
  ground GND
";

        var result = ACIRReader.TryParse(acir, "test.cir");

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
        var acir =
            $@"ACIR {ACIRVersion.Current}

circuit TestCircuit
  level EL
  param width : real = $Auto
  supply VDD
  ground GND
";

        var result = ACIRReader.TryParse(acir, "test.cir");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        var param = result.Document!.Circuits[0].Parameters.First();
        Assert.Equal("$Auto", param.Default!.Symbolic);
    }

    [Fact]
    public void TryRead_InstanceDeclaration_ParsesSuccessfully()
    {
        var acir =
            $@"ACIR {ACIRVersion.Current}

circuit TestCircuit
  level ML
  supply VDD
  ground GND
  port IN : analog
  port OUT : analog
  fill:
    inst cm (IN->IN, OUT->OUT) : CurrentMirror
";

        var result = ACIRReader.TryParse(acir, "test.cir");

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
        var acir =
            $@"ACIR {ACIRVersion.Current}

circuit TestCircuit
  level ML
  supply VDD
  ground GND
  fill:
    inst cm : CurrentMirror
";

        var result = ACIRReader.TryParse(acir, "test.cir");

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
        var acir =
            $@"ACIR {ACIRVersion.Current}

circuit TestCircuit
  level EL
  supply VDD
  ground GND
  fill:
    attach cm1 to load1 via CurrentMirror::LoadBranch
";

        var result = ACIRReader.TryParse(acir, "test.cir");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        Assert.NotNull(result.Document!.Circuits[0].Fill);
        Assert.Single(result.Document.Circuits[0].Fill!.Attaches);
        var attach = result.Document.Circuits[0].Fill!.Attaches[0];
        Assert.Equal("cm1", attach.SourceInstance);
        Assert.Equal("load1", attach.TargetInstance);
        Assert.Equal("CurrentMirror::LoadBranch", attach.Via);
        Assert.Null(attach.Anchor);
    }

    [Fact]
    public void TryRead_AttachStatementWithAnchor_ParsesSuccessfully()
    {
        var acir =
            $@"ACIR {ACIRVersion.Current}

circuit TestCircuit
  level EL
  supply VDD
  ground GND
  fill:
    attach cm1 to load1 via CurrentMirror::LoadBranch as bias_net
";

        var result = ACIRReader.TryParse(acir, "test.cir");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        var attach = result.Document!.Circuits[0].Fill!.Attaches[0];
        Assert.Equal("bias_net", attach.Anchor);
    }

    [Fact]
    public void TryRead_InvalidInstanceDeclaration_ReturnsError()
    {
        var acir =
            $@"ACIR {ACIRVersion.Current}

circuit TestCircuit
  level ML
  supply VDD
  ground GND
  fill:
    inst bad syntax here
";

        var result = ACIRReader.TryParse(acir, "test.cir");

        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("ACIR0015")
        );
    }

    [Fact]
    public void TryRead_InvalidAttachStatement_ReturnsError()
    {
        var acir =
            $@"ACIR {ACIRVersion.Current}

circuit TestCircuit
  level EL
  supply VDD
  ground GND
  fill:
    attach bad syntax
";

        var result = ACIRReader.TryParse(acir, "test.cir");

        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("ACIR0016")
        );
    }

    [Fact]
    public void TryRead_CircuitImplementsTrait_ParsesSuccessfully()
    {
        var acir =
            $@"ACIR {ACIRVersion.Current}

trait CurrentMirror:
  port IN : analog
  port OUT : analog

circuit CMirror : CurrentMirror
  level EL
  supply VDD
  ground GND
  port IN : analog
  port OUT : analog
";

        var result = ACIRReader.TryParse(acir, "test.cir");

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
        var acir =
            $@"ACIR {ACIRVersion.Current}

circuit CMirror : CurrentMirror, Foldable
  level EL
  supply VDD
  ground GND
";

        var result = ACIRReader.TryParse(acir, "test.cir");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        var circuit = result.Document!.Circuits[0];
        Assert.NotNull(circuit.Traits);
        Assert.Equal(2, circuit.Traits.Count);
        Assert.Contains("CurrentMirror", circuit.Traits);
        Assert.Contains("Foldable", circuit.Traits);
    }
}
