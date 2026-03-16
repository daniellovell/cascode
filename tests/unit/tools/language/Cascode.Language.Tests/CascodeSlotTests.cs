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
        var circuit = Assert.Single(result.Document!.Circuits);
        Assert.NotNull(circuit.Slot);
        Assert.Empty(circuit.Slot!.Nets);
        Assert.Empty(circuit.Slot.Instances);
        Assert.Empty(circuit.Slot.Connections);
    }

    [Fact]
    public void TryRead_HlSlotBlock_ReturnsLevelError()
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

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS3034")
        );
    }

    [Fact]
    public void TryRead_OldSlotSomeSyntax_ReturnsSyntaxError()
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

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS0001")
        );
    }

    [Fact]
    public void TryRead_MlFillWithSomeInterface_ParsesSuccessfully()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

interface AnalogFrontend {{
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog
}}

circuit Top {{
  level ML
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog

  fill {{
    Some frontend : AnalogFrontend {{
      .VDD--VDD
      .GND--GND
      .IN--IN
      .OUT--OUT
    }}
  }}
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var top = result.Document!.Circuits.First(c => c.Name == "Top");
        var instance = Assert.Single(top.Fill!.Instances);
        Assert.True(instance.IsSomeRequest);
        Assert.Equal("Some", instance.DeclaredType);
        Assert.Equal("AnalogFrontend", instance.Type);
        Assert.Equal("OUT", instance.Bindings["OUT"]);
    }

    [Fact]
    public void TryRead_MlFillWithLegacySomeOrder_ReturnsSyntaxError()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

interface AnalogFrontend {{
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog
}}

circuit Top {{
  level ML
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog

  fill {{
    Some AnalogFrontend frontend {{
      .VDD--VDD
      .GND--GND
      .IN--IN
      .OUT--OUT
    }}
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
    public void TryRead_ElFillWithSomeInterface_ReturnsLevelError()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

interface BiasSource {{
  output OUT : analog
}}

circuit TestCircuit {{
  level EL
  output OUT : analog

  fill {{
    Some src : BiasSource {{
      .OUT--OUT
    }}
  }}
}}
";

        var result = CascodeReader.TryParse(cascode, "test.cas");

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS3036")
        );
    }

    [Fact]
    public void Write_MlFillWithSome_EmitsExistentialSyntax()
    {
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "Top",
                    Level = CascodeLevel.ML,
                    Fill = new FillBlock
                    {
                        Instances = new List<InstanceDeclaration>
                        {
                            new InstanceDeclaration
                            {
                                Id = "frontend",
                                Type = "AnalogFrontend",
                                DeclaredType = "Some",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["IN"] = "VIN",
                                    ["OUT"] = "VOUT",
                                },
                            },
                        },
                    },
                },
            },
        };

        using var writer = new StringWriter();
        CascodeWriter.Write(doc, writer);
        var output = writer.ToString();

        Assert.Contains("Some frontend : AnalogFrontend {", output);
        Assert.DoesNotContain("= new AnalogFrontend", output);
    }

    [Fact]
    public void RoundTrip_MlFillWithSome_PreservesData()
    {
        var original =
            $@"VERSION {CascodeVersion.Current}

interface AnalogFrontend {{
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog
}}

circuit Top {{
  level ML
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog

  fill {{
    Some frontend : AnalogFrontend {{
      .VDD--VDD
      .GND--GND
      .IN--IN
      .OUT--OUT
    }}
  }}
}}
";

        var readResult = CascodeReader.TryParse(original, "test.cas");
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

        var instance = Assert.Single(reReadResult.Document!.Circuits.Single().Fill!.Instances);
        Assert.True(instance.IsSomeRequest);
        Assert.Equal("AnalogFrontend", instance.Type);
        Assert.Equal("OUT", instance.Bindings["OUT"]);
    }

    [Fact]
    public void RoundTrip_SensorFrontendHlGolden_PreservesStructure()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var goldenPath = Path.Combine(repoRoot, "tests/golden/cas/pcb/SensorFrontendPCB.hl.cas");

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

        var circuit = Assert.Single(reReadResult.Document!.Circuits);
        Assert.Equal(CascodeLevel.HL, circuit.Level);
        Assert.NotNull(circuit.Slot);
        Assert.Empty(circuit.Slot!.Instances);
    }

    [Fact]
    public void RoundTrip_SensorFrontendMlGolden_PreservesSomeRequests()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var goldenPath = Path.Combine(repoRoot, "tests/golden/cas/pcb/SensorFrontendPCB.ml.cas");

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

        var circuit = Assert.Single(reReadResult.Document!.Circuits);
        Assert.Equal(CascodeLevel.ML, circuit.Level);
        Assert.Equal(3, circuit.Fill!.Instances.Count);
        Assert.All(circuit.Fill.Instances, instance => Assert.True(instance.IsSomeRequest));
    }
}
