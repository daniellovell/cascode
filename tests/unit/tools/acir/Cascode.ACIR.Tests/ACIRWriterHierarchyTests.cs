using System.IO;
using System.Linq;
using Cascode.ACIR;
using Cascode.Parser;

namespace Cascode.ACIR.Tests;

public class ACIRWriterHierarchyTests
{
    [Fact]
    public void Write_TraitDefinition_ProducesValidOutput()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Traits = new List<TraitDefinition>
            {
                new TraitDefinition
                {
                    Name = "CurrentMirror",
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Input,
                            Name = "IN",
                            Type = "analog",
                        },
                        new PortDeclaration
                        {
                            Direction = PortDirection.Output,
                            Name = "OUT",
                            Type = "analog",
                        },
                    },
                },
            },
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                },
            },
        };

        using var writer = new StringWriter();
        ACIRWriter.Write(doc, writer);
        var output = writer.ToString();

        Assert.Contains("interface CurrentMirror", output);
        Assert.Contains("input IN : analog", output);
        Assert.Contains("output OUT : analog", output);
    }

    [Fact]
    public void Write_TraitWithConnectors_ProducesValidOutput()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Traits = new List<TraitDefinition>
            {
                new TraitDefinition
                {
                    Name = "CurrentMirror",
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Output,
                            Name = "OUT",
                            Type = "analog",
                        },
                    },
                    Connectors = new List<TraitConnector>
                    {
                        new TraitConnector
                        {
                            TargetTrait = "LoadBranch",
                            Mappings = new List<ConnectorMapping>
                            {
                                new ConnectorMapping { SourcePort = "OUT", TargetPort = "IN" },
                            },
                        },
                    },
                },
            },
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                },
            },
        };

        using var writer = new StringWriter();
        ACIRWriter.Write(doc, writer);
        var output = writer.ToString();

        Assert.Contains("connectors {", output);
        Assert.Contains("to LoadBranch {", output);
        Assert.Contains("OUT--IN", output);
    }

    [Fact]
    public void Write_CircuitWithInline_ProducesValidOutput()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "CurrentMirror",
                    Level = ACIRLevel.EL,
                    Inline = true,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                },
            },
        };

        using var writer = new StringWriter();
        ACIRWriter.Write(doc, writer);
        var output = writer.ToString();

        Assert.Contains("inline", output);
    }

    [Fact]
    public void Write_CircuitWithoutInline_OmitsInline()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = ACIRLevel.EL,
                    Inline = false,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                },
            },
        };

        using var writer = new StringWriter();
        ACIRWriter.Write(doc, writer);
        var output = writer.ToString();

        Assert.DoesNotContain("inline", output);
    }

    [Fact]
    public void Write_CircuitParameters_ProducesValidOutput()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = ACIRLevel.EL,
                    Parameters = new List<CircuitParameter>
                    {
                        new CircuitParameter
                        {
                            Name = "ratio",
                            Type = "real",
                            Default = new ParamValue { Numeric = "2" },
                        },
                        new CircuitParameter
                        {
                            Name = "width",
                            Type = "real",
                            Default = null,
                        },
                    },
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                },
            },
        };

        using var writer = new StringWriter();
        ACIRWriter.Write(doc, writer);
        var output = writer.ToString();

        Assert.Contains("real ratio = 2", output);
        Assert.Contains("real width", output);
    }

    [Fact]
    public void Write_AttachStatement_ProducesValidOutput()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Attaches = new List<AttachStatement>
                        {
                            new AttachStatement
                            {
                                SourceInstance = "cm1",
                                TargetInstances = new List<string> { "load1" },
                                Via = "CurrentMirror::LoadBranch",
                            },
                        },
                    },
                },
            },
        };

        using var writer = new StringWriter();
        ACIRWriter.Write(doc, writer);
        var output = writer.ToString();

        Assert.Contains("attach cm1 to load1 via CurrentMirror::LoadBranch", output);
    }

    [Fact]
    public void Write_AttachStatementWithAnchor_ProducesValidOutput()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Attaches = new List<AttachStatement>
                        {
                            new AttachStatement
                            {
                                SourceInstance = "cm1",
                                TargetInstances = new List<string> { "load1" },
                                Via = "CurrentMirror::LoadBranch",
                                Anchor = "bias_net",
                            },
                        },
                    },
                },
            },
        };

        using var writer = new StringWriter();
        ACIRWriter.Write(doc, writer);
        var output = writer.ToString();

        Assert.Contains("attach cm1 to load1 via CurrentMirror::LoadBranch as bias_net", output);
    }

    [Fact]
    public void Write_AttachWithOverrides_ProducesValidOutput()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Attaches = new List<AttachStatement>
                        {
                            new AttachStatement
                            {
                                SourceInstance = "cm1",
                                TargetInstances = new List<string> { "load1" },
                                Via = "CurrentMirror::LoadBranch",
                                Overrides = new List<ConnectorMapping>
                                {
                                    new ConnectorMapping
                                    {
                                        SourcePort = "SENSE",
                                        TargetPort = "OUT.N",
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };

        using var writer = new StringWriter();
        ACIRWriter.Write(doc, writer);
        var output = writer.ToString();

        Assert.Contains("attach cm1 to load1 via CurrentMirror::LoadBranch {", output);
        Assert.Contains(".SENSE--OUT.N", output);
        Assert.Contains("    }", output);
    }

    [Fact]
    public void Write_AttachChain_ProducesValidOutput()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Attaches = new List<AttachStatement>
                        {
                            new AttachStatement
                            {
                                SourceInstance = "cm1",
                                TargetInstances = new List<string> { "load1", "load2" },
                                Via = "CurrentMirror::LoadBranch",
                            },
                        },
                    },
                },
            },
        };

        using var writer = new StringWriter();
        ACIRWriter.Write(doc, writer);
        var output = writer.ToString();

        Assert.Contains("attach cm1 to load1 to load2 via CurrentMirror::LoadBranch", output);
    }

    [Fact]
    public void RoundTrip_TraitDefinition_PreservesData()
    {
        var original =
            $@"ACIR {ACIRVersion.Current}

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

        var readResult = ACIRReader.TryParse(original, "test.cir");
        Assert.True(readResult.Success);

        using var writer = new StringWriter();
        ACIRWriter.Write(readResult.Document!, writer);
        var output = writer.ToString();

        var reReadResult = ACIRReader.TryParse(output, "test.cir");
        Assert.True(reReadResult.Success);
        Assert.Single(reReadResult.Document!.Traits);
        Assert.Equal("CurrentMirror", reReadResult.Document.Traits[0].Name);
    }

    [Fact]
    public void RoundTrip_CircuitWithParameters_PreservesData()
    {
        var original =
            $@"ACIR {ACIRVersion.Current}

circuit TestCircuit(real ratio = 2, real width) {{
  level EL
  supply VDD
  ground GND
}}
";

        var readResult = ACIRReader.TryParse(original, "test.cir");
        Assert.True(readResult.Success);

        using var writer = new StringWriter();
        ACIRWriter.Write(readResult.Document!, writer);
        var output = writer.ToString();

        var reReadResult = ACIRReader.TryParse(output, "test.cir");
        Assert.True(reReadResult.Success);
        Assert.Equal(2, reReadResult.Document!.Circuits[0].Parameters.Count);
    }

    [Fact]
    public void RoundTrip_CircuitWithInline_PreservesData()
    {
        var original =
            $@"ACIR {ACIRVersion.Current}

circuit CurrentMirror {{
  level EL
  inline
  supply VDD
  ground GND
}}
";

        var readResult = ACIRReader.TryParse(original, "test.cir");
        Assert.True(readResult.Success);

        using var writer = new StringWriter();
        ACIRWriter.Write(readResult.Document!, writer);
        var output = writer.ToString();

        var reReadResult = ACIRReader.TryParse(output, "test.cir");
        Assert.True(reReadResult.Success);
        Assert.True(reReadResult.Document!.Circuits[0].Inline);
    }

    [Fact]
    public void RoundTrip_AttachStatement_PreservesData()
    {
        var original =
            $@"ACIR {ACIRVersion.Current}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
  fill {{
    attach cm1 to load1 via CurrentMirror::LoadBranch as bias_net
  }}
}}
";

        var readResult = ACIRReader.TryParse(original, "test.cir");
        Assert.True(readResult.Success);

        using var writer = new StringWriter();
        ACIRWriter.Write(readResult.Document!, writer);
        var output = writer.ToString();

        var reReadResult = ACIRReader.TryParse(output, "test.cir");
        Assert.True(reReadResult.Success);
        var attach = reReadResult.Document!.Circuits[0].Fill!.Attaches[0];
        Assert.Equal("cm1", attach.SourceInstance);
        Assert.Equal("load1", attach.TargetInstances.Single());
        Assert.Equal("CurrentMirror::LoadBranch", attach.Via);
        Assert.Equal("bias_net", attach.Anchor);
    }

    [Fact]
    public void RoundTrip_AttachWithOverrides_PreservesData()
    {
        var original =
            $@"ACIR {ACIRVersion.Current}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
  fill {{
    attach cm1 to load1 via CurrentMirror::LoadBranch {{
      .SENSE--OUT.N
    }}
  }}
}}
";

        var readResult = ACIRReader.TryParse(original, "test.cir");
        Assert.True(readResult.Success);

        using var writer = new StringWriter();
        ACIRWriter.Write(readResult.Document!, writer);
        var output = writer.ToString();

        var reReadResult = ACIRReader.TryParse(output, "test.cir");
        Assert.True(reReadResult.Success);
        var attach = reReadResult.Document!.Circuits[0].Fill!.Attaches[0];
        Assert.Equal("cm1", attach.SourceInstance);
        Assert.Equal("load1", attach.TargetInstances.Single());
        Assert.NotNull(attach.Overrides);
        Assert.Single(attach.Overrides!);
        Assert.Equal("SENSE", attach.Overrides![0].SourcePort);
        Assert.Equal("OUT.N", attach.Overrides![0].TargetPort);
    }

    [Fact]
    public void RoundTrip_AttachChain_PreservesData()
    {
        var original =
            $@"ACIR {ACIRVersion.Current}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
  fill {{
    attach a to b to c via CurrentMirror::LoadBranch
  }}
}}
";

        var readResult = ACIRReader.TryParse(original, "test.cir");
        Assert.True(readResult.Success);

        using var writer = new StringWriter();
        ACIRWriter.Write(readResult.Document!, writer);
        var output = writer.ToString();

        var reReadResult = ACIRReader.TryParse(output, "test.cir");
        Assert.True(reReadResult.Success);
        var attach = reReadResult.Document!.Circuits[0].Fill!.Attaches[0];
        Assert.Equal("a", attach.SourceInstance);
        Assert.Equal(new[] { "b", "c" }, attach.TargetInstances);
    }

    [Fact]
    public void RoundTrip_AttachChainWithOverrides_PreservesData()
    {
        var original =
            $@"ACIR {ACIRVersion.Current}

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

        var readResult = ACIRReader.TryParse(original, "test.cir");
        Assert.True(readResult.Success);

        using var writer = new StringWriter();
        ACIRWriter.Write(readResult.Document!, writer);
        var output = writer.ToString();

        var reReadResult = ACIRReader.TryParse(output, "test.cir");
        Assert.True(reReadResult.Success);
        var attach = reReadResult.Document!.Circuits[0].Fill!.Attaches[0];
        Assert.Equal("a", attach.SourceInstance);
        Assert.Equal(new[] { "b", "c" }, attach.TargetInstances);
        Assert.NotNull(attach.Overrides);
        Assert.Single(attach.Overrides!);
    }

    [Fact]
    public void RoundTrip_InstanceDeclaration_PreservesData()
    {
        var original =
            $@"ACIR {ACIRVersion.Current}

circuit TestCircuit {{
  level ML
  supply VDD
  ground GND
  fill {{
    cm = new CurrentMirror {{ .IN--inp, .OUT--outp }}
  }}
}}
";

        var readResult = ACIRReader.TryParse(original, "test.cir");
        Assert.True(readResult.Success);

        using var writer = new StringWriter();
        ACIRWriter.Write(readResult.Document!, writer);
        var output = writer.ToString();

        var reReadResult = ACIRReader.TryParse(output, "test.cir");
        Assert.True(reReadResult.Success);
        var inst = reReadResult.Document!.Circuits[0].Fill!.Instances[0];
        Assert.Equal("cm", inst.Id);
        Assert.Equal("CurrentMirror", inst.Type);
        Assert.Equal(2, inst.Bindings.Count);
    }
}
