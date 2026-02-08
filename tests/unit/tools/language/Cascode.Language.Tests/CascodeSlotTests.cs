using System.IO;
using System.Linq;
using Cascode.Language;
using Cascode.TestSupport;

namespace Cascode.Language.Tests;

public class CascodeSlotTests
{
    [Fact]
    public void TryRead_BareSlot_ParsesSuccessfully()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit TestCircuit {{
  level HL
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog

  slot
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        var circuit = Assert.Single(result.Document!.Circuits);
        Assert.NotNull(circuit.Slot);
        Assert.Empty(circuit.Slot!.Nets);
        Assert.Empty(circuit.Slot.Instances);
        Assert.Empty(circuit.Slot.Connections);
    }

    [Fact]
    public void TryRead_SlotBlock_ParsesInstances()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit Inner {{
  level HL
  supply VDD
  ground GND
  input A : analog
  output B : analog
  slot
}}

circuit Outer {{
  level HL
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog

  slot {{
    net mid : analog
    Inner sub = new Inner() {{
      .VDD--VDD
      .GND--GND
      .A--IN
      .B--mid
    }}
  }}
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        var outer = result.Document!.Circuits.First(c => c.Name == "Outer");
        Assert.NotNull(outer.Slot);
        Assert.Single(outer.Slot!.Nets);
        Assert.Equal("mid", outer.Slot.Nets[0].Id);
        Assert.Equal("analog", outer.Slot.Nets[0].Domain);
        Assert.Single(outer.Slot.Instances);
        Assert.Equal("sub", outer.Slot.Instances[0].Id);
        Assert.Equal("Inner", outer.Slot.Instances[0].Type);
        Assert.Equal("Inner", outer.Slot.Instances[0].DeclaredType);
        Assert.Equal(4, outer.Slot.Instances[0].Bindings.Count);
    }

    [Fact]
    public void TryRead_SlotBlockWithSome_ParsesSuccessfully()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit AnalogFrontend {{
  level HL
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog
  slot
}}

circuit Top {{
  level HL
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog

  slot {{
    Some frontend = new AnalogFrontend() {{
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
        var top = result.Document!.Circuits.First(c => c.Name == "Top");
        var instance = Assert.Single(top.Slot!.Instances);
        Assert.Equal("frontend", instance.Id);
        Assert.Equal("Some", instance.DeclaredType);
        Assert.Equal("AnalogFrontend", instance.Type);
    }

    [Fact]
    public void TryRead_FillBlockWithSome_ReturnsSyntaxError()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit TestCircuit {{
  level EL
  supply VDD
  ground GND
  fill {{
    Some src = new VDC(V=1.8V) {{ .P--VDD, .N--GND }}
  }}
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS0001")
        );
    }

    [Fact]
    public void TryRead_SlotBlockWithConnections_ParsesSuccessfully()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit TestCircuit {{
  level HL
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog

  slot {{
    net mid : analog
    IN--mid
    mid--OUT
  }}
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        var circuit = Assert.Single(result.Document!.Circuits);
        Assert.NotNull(circuit.Slot);
        Assert.Single(circuit.Slot!.Nets);
        Assert.Equal(2, circuit.Slot.Connections.Count);
        Assert.Equal("IN", circuit.Slot.Connections[0].From);
        Assert.Equal("mid", circuit.Slot.Connections[0].To);
    }

    [Fact]
    public void Write_BareSlot_EmitsSlotKeyword()
    {
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "Test",
                    Level = CascodeLevel.HL,
                    Slot = new SlotBlock(),
                },
            },
        };

        using var writer = new StringWriter();
        CascodeWriter.Write(doc, writer);
        var output = writer.ToString();

        Assert.Contains("  slot", output);
        Assert.DoesNotContain("slot {", output);
    }

    [Fact]
    public void Write_SlotBlock_EmitsBlockWithInstances()
    {
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "Test",
                    Level = CascodeLevel.HL,
                    Slot = new SlotBlock
                    {
                        Nets = new List<NetDeclaration>
                        {
                            new NetDeclaration { Id = "mid", Domain = "analog" },
                        },
                        Instances = new List<InstanceDeclaration>
                        {
                            new InstanceDeclaration
                            {
                                Id = "lna",
                                Type = "MyLNA",
                                Params = new Dictionary<string, ParamValue>
                                {
                                    ["stages"] = new ParamValue { Numeric = "2" },
                                },
                                Bindings = new Dictionary<string, string>
                                {
                                    ["VDD"] = "VDD",
                                    ["IN"] = "RF_IN",
                                    ["OUT"] = "mid",
                                },
                            },
                        },
                        Connections = new List<ConnectionStatement>
                        {
                            new ConnectionStatement { From = "a", To = "b" },
                        },
                    },
                },
            },
        };

        using var writer = new StringWriter();
        CascodeWriter.Write(doc, writer);
        var output = writer.ToString();

        Assert.Contains("slot {", output);
        Assert.Contains("net mid : analog", output);
        Assert.Contains("MyLNA lna = new MyLNA(stages=2) {", output);
        Assert.Contains("a--b", output);
    }

    [Fact]
    public void Write_SlotBlockWithSome_EmitsSomeDeclaredType()
    {
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "Test",
                    Level = CascodeLevel.HL,
                    Slot = new SlotBlock
                    {
                        Instances = new List<InstanceDeclaration>
                        {
                            new InstanceDeclaration
                            {
                                Id = "frontend",
                                Type = "AnalogFrontend",
                                DeclaredType = "Some",
                            },
                        },
                    },
                },
            },
        };

        using var writer = new StringWriter();
        CascodeWriter.Write(doc, writer);
        var output = writer.ToString();

        Assert.Contains("Some frontend = new AnalogFrontend", output);
    }

    [Fact]
    public void RoundTrip_BareSlot_PreservesData()
    {
        var original =
            $@"VERSION {CascodeVersion.Current}

circuit TestCircuit {{
  level HL
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog

  slot
}}
";

        var readResult = CascodeReader.TryParse(original, "test.cas");
        Assert.True(readResult.Success);

        using var writer = new StringWriter();
        CascodeWriter.Write(readResult.Document!, writer);
        var output = writer.ToString();

        var reReadResult = CascodeReader.TryParse(output, "test.cas");
        Assert.True(reReadResult.Success);
        var circuit = Assert.Single(reReadResult.Document!.Circuits);
        Assert.NotNull(circuit.Slot);
        Assert.Empty(circuit.Slot!.Nets);
        Assert.Empty(circuit.Slot.Instances);
        Assert.Empty(circuit.Slot.Connections);
    }

    [Fact]
    public void RoundTrip_SlotBlock_PreservesData()
    {
        var original =
            $@"VERSION {CascodeVersion.Current}

circuit Inner {{
  level HL
  supply VDD
  ground GND
  input A : analog
  output B : analog
  slot
}}

circuit Outer {{
  level HL
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog

  slot {{
    net mid : analog
    Inner sub = new Inner() {{
      .VDD--VDD
      .GND--GND
      .A--IN
      .B--mid
    }}
  }}
}}
";

        var readResult = CascodeReader.TryParse(original, "test.cas");
        Assert.True(readResult.Success);

        using var writer = new StringWriter();
        CascodeWriter.Write(readResult.Document!, writer);
        var output = writer.ToString();

        var reReadResult = CascodeReader.TryParse(output, "test.cas");
        Assert.True(reReadResult.Success);
        var outer = reReadResult.Document!.Circuits.First(c => c.Name == "Outer");
        Assert.NotNull(outer.Slot);
        Assert.Single(outer.Slot!.Nets);
        Assert.Single(outer.Slot.Instances);
        Assert.Equal("sub", outer.Slot.Instances[0].Id);
        Assert.Equal("Inner", outer.Slot.Instances[0].Type);
    }

    [Fact]
    public void RoundTrip_HLComposition_Golden()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var goldenPath = Path.Combine(repoRoot, "tests/golden/cas/hl/HLComposition.hl.cai");

        using var reader = File.OpenText(goldenPath);
        var readResult = CascodeReader.TryRead(reader, goldenPath);
        Assert.True(
            readResult.Success,
            string.Join("; ", readResult.Diagnostics.Select(d => d.Message))
        );

        using var writer = new StringWriter();
        CascodeWriter.Write(readResult.Document!, writer);
        var output = writer.ToString();

        var reReadResult = CascodeReader.TryParse(output, "roundtrip.cas");
        Assert.True(
            reReadResult.Success,
            string.Join("; ", reReadResult.Diagnostics.Select(d => d.Message))
        );

        Assert.Equal(readResult.Document!.Circuits.Count, reReadResult.Document!.Circuits.Count);

        var myLna = readResult.Document.Circuits.First(c => c.Name == "MyLNA");
        var myLna2 = reReadResult.Document.Circuits.First(c => c.Name == "MyLNA");
        Assert.NotNull(myLna.Slot);
        Assert.NotNull(myLna2.Slot);
        Assert.Empty(myLna.Slot!.Instances);
        Assert.Empty(myLna2.Slot!.Instances);

        var receiver = readResult.Document.Circuits.First(c => c.Name == "MyReceiver");
        var receiver2 = reReadResult.Document.Circuits.First(c => c.Name == "MyReceiver");
        Assert.NotNull(receiver.Slot);
        Assert.NotNull(receiver2.Slot);
        Assert.Equal(receiver.Slot!.Nets.Count, receiver2.Slot!.Nets.Count);
        Assert.Equal(receiver.Slot.Instances.Count, receiver2.Slot.Instances.Count);
    }

    [Fact]
    public void RoundTrip_HLSlotSome_Golden()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var goldenPath = Path.Combine(repoRoot, "tests/golden/cas/hl/HLSlotSome.hl.cai");

        using var reader = File.OpenText(goldenPath);
        var readResult = CascodeReader.TryRead(reader, goldenPath);
        Assert.True(
            readResult.Success,
            string.Join("; ", readResult.Diagnostics.Select(d => d.Message))
        );

        var top = readResult.Document!.Circuits.First(c => c.Name == "SensorTop");
        var instance = Assert.Single(top.Slot!.Instances);
        Assert.Equal("Some", instance.DeclaredType);
        Assert.Equal("AnalogFrontend", instance.Type);

        using var writer = new StringWriter();
        CascodeWriter.Write(readResult.Document, writer);
        var output = writer.ToString();
        Assert.Contains("Some frontend = new AnalogFrontend", output);

        var reReadResult = CascodeReader.TryParse(output, "roundtrip.cas");
        Assert.True(
            reReadResult.Success,
            string.Join("; ", reReadResult.Diagnostics.Select(d => d.Message))
        );
        var top2 = reReadResult.Document!.Circuits.First(c => c.Name == "SensorTop");
        var instance2 = Assert.Single(top2.Slot!.Instances);
        Assert.Equal("Some", instance2.DeclaredType);
        Assert.Equal("AnalogFrontend", instance2.Type);
    }
}
