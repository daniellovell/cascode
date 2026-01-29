using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cascode.Language;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Language.Tests;

public class SpiceEmitterTests
{
    [Fact]
    public void EmitDesign_RequiresELLevel()
    {
        var circuit = new Circuit { Name = "TestCircuit", Level = CascodeLevel.ML };

        using var writer = new StringWriter();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            SpiceEmitter.EmitDesign(circuit, writer)
        );
        Assert.Contains("EL-level", ex.Message);
    }

    [Fact]
    public void EmitDesign_PortOrdering_MatchesDeclarationOrder()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = CascodeLevel.EL,
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Io,
                    Name = "A",
                    Type = "analog",
                },
                new()
                {
                    Direction = PortDirection.Io,
                    Name = "B",
                    Type = "analog",
                },
                new()
                {
                    Direction = PortDirection.Io,
                    Name = "C",
                    Type = "analog",
                },
            },
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "VSS" },
            Fill = new FillBlock(),
        };

        using var writer = new StringWriter();
        SpiceEmitter.EmitDesign(circuit, writer);
        var output = writer.ToString();

        // Verify subcircuit line has correct port ordering
        Assert.Contains(".subckt TestCircuit A B C VDD VSS", output);
    }

    [Fact]
    public void EmitVariant_FullyResolvesParameters()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

primitive nmos Level1_NMOS(size primSize) {{
  device ""level1_nmos""
  params {{
    W = primSize.W
    L = primSize.L
    m = primSize.M
  }}
}}

circuit Top(size Input=size(W=1u, L=180n, M=1)) {{
  level EL
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog
  fill {{
    size Tail = size(W=Input.W*2, L=Input.L, M=1)
    nmos M1 = new Level1_NMOS(Tail) {{
      .B--GND
      .D--OUT
      .G--IN
      .S--GND
    }}
  }}
}}
";

        var result = CascodeReader.TryParse(cascode, "indirect-sizes.cas");
        Assert.True(result.Success);
        var doc = result.Document!;
        var circuit = doc.Circuits.Single(c => c.Name == "Top");

        using var writer = new StringWriter();
        SpiceEmitter.EmitDesign(circuit, writer, document: doc);
        var output = writer.ToString();

        Assert.DoesNotContain("params:", output);
        Assert.DoesNotContain("{", output);
        Assert.Contains("W=2u", output);
        Assert.Contains("L=180n", output);
        Assert.Contains("m=1", output);
    }

    [Fact]
    public void EmitVariant_ReferencesCorrectVariantName()
    {
        var doc = new CascodeDocument
        {
            Primitives =
            [
                new PrimitiveDefinition
                {
                    Name = "Level1_NMOS",
                    Kind = "nmos",
                    Device = "level1_nmos",
                    SizeParameter = "primSize",
                    Params = new Dictionary<string, string>
                    {
                        ["W"] = "primSize.W",
                        ["L"] = "primSize.L",
                        ["m"] = "primSize.M",
                    },
                },
            ],
            Circuits =
            [
                new Circuit
                {
                    Name = "Child",
                    Level = CascodeLevel.EL,
                    Parameters =
                    [
                        new CircuitParameter
                        {
                            Name = "ratio",
                            Type = "int",
                            Default = new ParamValue { Numeric = "1" },
                        },
                    ],
                    Sizes =
                    [
                        new SizeDeclaration
                        {
                            Name = "Sense",
                            Default = new SizePack
                            {
                                Entries = new Dictionary<string, string>
                                {
                                    ["W"] = "2u",
                                    ["L"] = "180n",
                                    ["M"] = "1",
                                },
                            },
                        },
                    ],
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Ports = new List<PortDeclaration>
                    {
                        new()
                        {
                            Direction = PortDirection.Input,
                            Name = "IN",
                            Type = "analog",
                        },
                        new()
                        {
                            Direction = PortDirection.Output,
                            Name = "OUT",
                            Type = "analog",
                        },
                    },
                    Fill = new FillBlock
                    {
                        Devices = new List<DeviceDeclaration>
                        {
                            new DeviceDeclaration
                            {
                                DeviceType = "nmos",
                                Id = "M1",
                                Primitive = "Level1_NMOS",
                                SizeName = "Sense",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["D"] = "OUT",
                                    ["G"] = "IN",
                                    ["S"] = "GND",
                                    ["B"] = "GND",
                                },
                            },
                        },
                    },
                },
                new Circuit
                {
                    Name = "Top",
                    Level = CascodeLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Ports = new List<PortDeclaration>
                    {
                        new()
                        {
                            Direction = PortDirection.Input,
                            Name = "IN",
                            Type = "analog",
                        },
                        new()
                        {
                            Direction = PortDirection.Output,
                            Name = "OUT",
                            Type = "analog",
                        },
                    },
                    Fill = new FillBlock
                    {
                        Instances = new List<InstanceDeclaration>
                        {
                            new InstanceDeclaration
                            {
                                Id = "u1",
                                Type = "Child",
                                Params = new Dictionary<string, ParamValue>
                                {
                                    ["ratio"] = new ParamValue { Numeric = "2" },
                                },
                                Bindings = new Dictionary<string, string>
                                {
                                    ["IN"] = "IN",
                                    ["OUT"] = "OUT",
                                    ["VDD"] = "VDD",
                                    ["GND"] = "GND",
                                },
                            },
                        },
                    },
                },
            ],
        };

        var top = doc.Circuits.Single(c => c.Name == "Top");
        using var writer = new StringWriter();
        SpiceEmitter.EmitDesign(top, writer, document: doc);
        var output = writer.ToString();

        var variantName = VariantNaming.BuildCanonicalName(
            "Child",
            new Dictionary<string, string> { ["ratio"] = "2" },
            new Dictionary<string, SizePack>
            {
                ["Sense"] = new SizePack
                {
                    Entries = new Dictionary<string, string>
                    {
                        ["W"] = "2u",
                        ["L"] = "180n",
                        ["M"] = "1",
                    },
                },
            }
        );

        Assert.Contains(variantName, output);
    }

    [Fact]
    public void EmitDesign_DeviceTerminalOrder_DGBS()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = CascodeLevel.EL,
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "IN",
                    Type = "analog",
                },
                new()
                {
                    Direction = PortDirection.Output,
                    Name = "OUT",
                    Type = "analog",
                },
            },
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        DeviceType = "nmos",
                        Id = "M1",
                        Primitive = "Level1_NMOS",
                        Bindings = new Dictionary<string, string>
                        {
                            { "D", "OUT" },
                            { "G", "IN" },
                            { "S", "GND" },
                            { "B", "GND" },
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string>
                            {
                                { "W", "1u" },
                                { "L", "180n" },
                                { "M", "1" },
                            },
                        },
                    },
                },
            },
        };

        var document = new CascodeDocument
        {
            Primitives =
            [
                new PrimitiveDefinition
                {
                    Name = "Level1_NMOS",
                    Kind = "nmos",
                    Device = "level1_nmos",
                    SizeParameter = "primSize",
                    Params = new Dictionary<string, string>
                    {
                        ["W"] = "primSize.W",
                        ["L"] = "primSize.L",
                        ["m"] = "primSize.M",
                    },
                },
            ],
        };

        using var writer = new StringWriter();
        SpiceEmitter.EmitDesign(circuit, writer, document: document);
        var output = writer.ToString();

        // Verify MOSFET line has DGBS ordering: MM1 <D> <G> <S> <B> <model>
        Assert.Contains("MM1 OUT IN GND GND level1_nmos L=180n W=1u m=1", output);
    }

    [Fact]
    public void CascodeReader_ParsesELCircuit()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var cascodePath = Path.Combine(repoRoot, "tests/golden/cas/bench/RcLowpass.el.cas");

        using var reader = File.OpenText(cascodePath);
        var doc = CascodeReader.Read(reader);

        Assert.Equal(CascodeVersion.Major, doc.VersionMajor);
        Assert.Equal(CascodeVersion.Minor, doc.VersionMinor);
        Assert.Single(doc.Circuits);

        var circuit = doc.Circuits[0];
        Assert.Equal("RcLowpass", circuit.Name);
        Assert.Equal(CascodeLevel.EL, circuit.Level);

        // Ports
        Assert.Equal(3, circuit.Ports.Count);
        Assert.Contains(circuit.Ports, p => p.Name == "IN.P");
        Assert.Contains(circuit.Ports, p => p.Name == "IN.N");
        Assert.Contains(circuit.Ports, p => p.Name == "OUT");

        // Supplies and grounds
        Assert.Single(circuit.Grounds);
        Assert.Contains("GND", circuit.Grounds);

        // Fill block
        Assert.NotNull(circuit.Fill);
        Assert.Equal(2, circuit.Fill.Devices.Count);

        // Bench definitions
        Assert.Single(doc.BenchDefinitions);
        Assert.Equal("DiffToSELowpass", doc.BenchDefinitions[0].Name);

        // Bench binding
        Assert.Single(circuit.BenchBindings);
        Assert.Equal("lp", circuit.BenchBindings[0].BindingName);
    }

    [Fact]
    public void SpiceEmitter_Emit_EmitsTestbenchForDeclarativeBenchBinding()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var cascodePath = Path.Combine(repoRoot, "tests/golden/cas/bench/RcLowpass.el.cas");

        CascodeDocument doc;
        using (var reader = File.OpenText(cascodePath))
        {
            doc = CascodeReader.Read(reader);
        }

        var outDir = Path.Combine(Path.GetTempPath(), "cascode-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);

        var result = SpiceEmitter.ValidateAndEmit(doc, outDir);
        Assert.True(result.Validation.IsValid);
        Assert.Single(result.Emit.DesignPaths);
        Assert.Single(result.Emit.TestbenchPaths);

        var tb = File.ReadAllText(result.Emit.TestbenchPaths[0]);
        Assert.Contains(".include \"RcLowpass.sp\"", tb);
        Assert.Contains(".control", tb);
        Assert.Contains("ac dec", tb);
    }

    [Fact]
    public void CascodeReader_ParsesCommonSourceAmpWithBias()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var cascodePath = Path.Combine(repoRoot, "tests/golden/cas/cs/CommonSourceAmp.el.cas");

        using var reader = File.OpenText(cascodePath);
        var doc = CascodeReader.Read(reader);

        Assert.Single(doc.Circuits);
        var circuit = doc.Circuits[0];
        Assert.Equal("CommonSourceAmp", circuit.Name);
        Assert.Equal(CascodeLevel.EL, circuit.Level);

        // Ports
        Assert.Equal(3, circuit.Ports.Count);
        Assert.Contains(circuit.Ports, p => p.Name == "IN");
        Assert.Contains(circuit.Ports, p => p.Name == "OUT");
        Assert.Contains(circuit.Ports, p => p.Name == "VBIAS");

        // Fill block with 2 devices
        Assert.NotNull(circuit.Fill);
        Assert.Equal(2, circuit.Fill.Devices.Count);

        // Harness with bias
        Assert.NotNull(circuit.Harness);
        Assert.Single(circuit.Harness.Supplies);
        Assert.Single(circuit.Harness.Biases);
        Assert.Equal("VBIAS", circuit.Harness.Biases[0].Net);
        Assert.Equal("0.7V", circuit.Harness.Biases[0].Value);

        Assert.Empty(doc.BenchDefinitions);
    }

    [Fact]
    public void CascodeReader_ParsesCSAmpResistiveWithResistor()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var cascodePath = Path.Combine(repoRoot, "tests/golden/cas/cs/CSAmpResistive.el.cas");

        using var reader = File.OpenText(cascodePath);
        var doc = CascodeReader.Read(reader);

        Assert.Single(doc.Circuits);
        var circuit = doc.Circuits[0];
        Assert.Equal("CSAmpResistive", circuit.Name);
        Assert.Equal(CascodeLevel.EL, circuit.Level);

        // Ports (no VBIAS - simpler than active load version)
        Assert.Equal(2, circuit.Ports.Count);
        Assert.Contains(circuit.Ports, p => p.Name == "IN");
        Assert.Contains(circuit.Ports, p => p.Name == "OUT");

        // Fill block with 2 devices: 1 NMOS + 1 resistor
        Assert.NotNull(circuit.Fill);
        Assert.Equal(2, circuit.Fill.Devices.Count);

        // Verify we have both device types
        Assert.Contains(circuit.Fill.Devices, d => d.DeviceType == "nmos");
        Assert.Contains(circuit.Fill.Devices, d => d.DeviceType == "resistor");

        // Verify resistor parameters
        var resistor = circuit.Fill.Devices.First(d => d.DeviceType == "resistor");
        Assert.Equal("R_load", resistor.Id);
        Assert.Equal("VDD", resistor.Bindings["P"]);
        Assert.Equal("OUT", resistor.Bindings["N"]);
        Assert.Equal("10k", resistor.Size?.Entries["R"]);

        // Harness without bias (simpler)
        Assert.NotNull(circuit.Harness);
        Assert.Single(circuit.Harness.Supplies);
        Assert.Empty(circuit.Harness.Biases);

        Assert.Empty(doc.BenchDefinitions);
    }
}
