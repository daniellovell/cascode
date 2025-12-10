using System.IO;
using System.Linq;
using Cascode.ACIR;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.ACIR.Tests;

public class SpiceEmitterTests
{
    [Fact]
    public void EmitDesign_OTA5TSingleEnded_MatchesGolden()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var acirPath = Path.Combine(repoRoot, "tests/golden/acir/ota/OTA5TSingleEnded.el.cir");
        var goldenSpicePath = Path.Combine(repoRoot, "tests/golden/spice/ota/OTA5TSingleEnded.sp");

        // Read ACIR
        using var acirReader = File.OpenText(acirPath);
        var doc = ACIRReader.Read(acirReader);

        Assert.Single(doc.Circuits);
        var circuit = doc.Circuits[0];
        Assert.Equal(ACIRLevel.EL, circuit.Level);

        // Emit SPICE
        using var spiceWriter = new StringWriter();
        SpiceEmitter.EmitDesign(circuit, spiceWriter);
        var actualSpice = spiceWriter.ToString();

        // Compare to golden
        var expectedSpice = File.ReadAllText(goldenSpicePath);
        Assert.Equal(Normalize(expectedSpice), Normalize(actualSpice));
    }

    [Fact]
    public void EmitDesign_RequiresELLevel()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = ACIRLevel.ML
        };

        using var writer = new StringWriter();
        var ex = Assert.Throws<InvalidOperationException>(() => SpiceEmitter.EmitDesign(circuit, writer));
        Assert.Contains("EL-level", ex.Message);
    }

    [Fact]
    public void EmitDesign_PortOrdering_MatchesDeclarationOrder()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = ACIRLevel.EL,
            Ports = new List<PortDeclaration>
            {
                new() { Name = "A", Type = "analog" },
                new() { Name = "B", Type = "analog" },
                new() { Name = "C", Type = "analog" }
            },
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "VSS" },
            Fill = new FillBlock()
        };

        using var writer = new StringWriter();
        SpiceEmitter.EmitDesign(circuit, writer);
        var output = writer.ToString();

        // Verify subcircuit line has correct port ordering
        Assert.Contains(".subckt TestCircuit A B C VDD VSS", output);
    }

    [Fact]
    public void EmitDesign_DeviceTerminalOrder_DGBS()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = ACIRLevel.EL,
            Ports = new List<PortDeclaration>
            {
                new() { Name = "IN", Type = "analog" },
                new() { Name = "OUT", Type = "analog" }
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
                        Bindings = new Dictionary<string, string>
                        {
                            { "D", "OUT" },
                            { "G", "IN" },
                            { "S", "GND" },
                            { "B", "GND" }
                        },
                        Params = new Dictionary<string, string>
                        {
                            { "W", "1u" },
                            { "L", "180n" },
                            { "M", "1" }
                        },
                        PdkDevice = "nmos"
                    }
                }
            }
        };

        using var writer = new StringWriter();
        SpiceEmitter.EmitDesign(circuit, writer);
        var output = writer.ToString();

        // Verify MOSFET line has DGBS ordering: MM1 <D> <G> <S> <B> <model>
        Assert.Contains("MM1 OUT IN GND GND nmos W=1u L=180n m=1", output);
    }

    [Fact]
    public void ACIRReader_ParsesELCircuit()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var acirPath = Path.Combine(repoRoot, "tests/golden/acir/ota/OTA5TSingleEnded.el.cir");

        using var reader = File.OpenText(acirPath);
        var doc = ACIRReader.Read(reader);

        Assert.Equal(1, doc.Version);
        Assert.Single(doc.Circuits);

        var circuit = doc.Circuits[0];
        Assert.Equal("OTA5TSingleEnded", circuit.Name);
        Assert.Equal(ACIRLevel.EL, circuit.Level);

        // Ports
        Assert.Equal(4, circuit.Ports.Count);
        Assert.Contains(circuit.Ports, p => p.Name == "IN_P");
        Assert.Contains(circuit.Ports, p => p.Name == "IN_N");
        Assert.Contains(circuit.Ports, p => p.Name == "OUT");
        Assert.Contains(circuit.Ports, p => p.Name == "VTAIL");

        // Supplies and grounds
        Assert.Single(circuit.Supplies);
        Assert.Contains("VDD", circuit.Supplies);
        Assert.Single(circuit.Grounds);
        Assert.Contains("GND", circuit.Grounds);

        // Fill block
        Assert.NotNull(circuit.Fill);
        Assert.Equal(2, circuit.Fill.Nets.Count);
        Assert.Equal(5, circuit.Fill.Devices.Count);

        // Harness
        Assert.NotNull(circuit.Harness);
        Assert.Single(circuit.Harness.Supplies);
        Assert.Single(circuit.Harness.Loads);

        // Benches
        Assert.NotNull(circuit.Benches);
        Assert.Single(circuit.Benches.Benches);
        Assert.Equal("SEOpAmpACBench", circuit.Benches.Benches[0].Name);
    }

    [Fact]
    public void EmitTestbench_IncludesHarnessAndDUT()
    {
        var circuit = new Circuit
        {
            Name = "TestAmp",
            Level = ACIRLevel.EL,
            Ports = new List<PortDeclaration>
            {
                new() { Name = "IN", Type = "analog" },
                new() { Name = "OUT", Type = "analog" }
            },
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Fill = new FillBlock(),
            Harness = new HarnessBlock
            {
                Supplies = new List<SupplyValue>
                {
                    new() { Net = "VDD", Value = "1.8V" }
                },
                Loads = new List<LoadValue>
                {
                    new() { Net = "OUT", C = "1p" }
                }
            }
        };

        var bench = new BenchConfig { Name = "ACBench" };

        using var writer = new StringWriter();
        SpiceEmitter.EmitTestbench(circuit, bench, "TestAmp.sp", writer);
        var output = writer.ToString();

        // Verify testbench structure
        Assert.Contains(".title TestAmp_ACBench", output);
        Assert.Contains(".include \"TestAmp.sp\"", output);
        Assert.Contains("VVDD VDD 0 DC 1.8V", output);
        Assert.Contains("COUT_load OUT 0 1p", output);
        Assert.Contains("XDUT IN OUT VDD GND TestAmp", output);
        Assert.Contains(".control", output);
        Assert.Contains("ac dec 100 1 10G", output);
        Assert.Contains(".end", output);
    }

    [Fact]
    public void EmitDesign_CommonSourceAmp_MatchesGolden()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var acirPath = Path.Combine(repoRoot, "tests/golden/acir/cs/CommonSourceAmp.el.cir");
        var goldenSpicePath = Path.Combine(repoRoot, "tests/golden/spice/cs/CommonSourceAmp.sp");

        // Read ACIR
        using var acirReader = File.OpenText(acirPath);
        var doc = ACIRReader.Read(acirReader);

        Assert.Single(doc.Circuits);
        var circuit = doc.Circuits[0];
        Assert.Equal(ACIRLevel.EL, circuit.Level);

        // Emit SPICE
        using var spiceWriter = new StringWriter();
        SpiceEmitter.EmitDesign(circuit, spiceWriter);
        var actualSpice = spiceWriter.ToString();

        // Compare to golden
        var expectedSpice = File.ReadAllText(goldenSpicePath);
        Assert.Equal(Normalize(expectedSpice), Normalize(actualSpice));
    }

    [Fact]
    public void ACIRReader_ParsesCommonSourceAmpWithBias()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var acirPath = Path.Combine(repoRoot, "tests/golden/acir/cs/CommonSourceAmp.el.cir");

        using var reader = File.OpenText(acirPath);
        var doc = ACIRReader.Read(reader);

        Assert.Single(doc.Circuits);
        var circuit = doc.Circuits[0];
        Assert.Equal("CommonSourceAmp", circuit.Name);
        Assert.Equal(ACIRLevel.EL, circuit.Level);

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

        // Bench
        Assert.NotNull(circuit.Benches);
        Assert.Single(circuit.Benches.Benches);
        Assert.Equal("SEAmpACBench", circuit.Benches.Benches[0].Name);
    }

    [Fact]
    public void EmitDesign_CSAmpResistive_MatchesGolden()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var acirPath = Path.Combine(repoRoot, "tests/golden/acir/cs/CSAmpResistive.el.cir");
        var goldenSpicePath = Path.Combine(repoRoot, "tests/golden/spice/cs/CSAmpResistive.sp");

        // Read ACIR
        using var acirReader = File.OpenText(acirPath);
        var doc = ACIRReader.Read(acirReader);

        Assert.Single(doc.Circuits);
        var circuit = doc.Circuits[0];
        Assert.Equal(ACIRLevel.EL, circuit.Level);

        // Emit SPICE
        using var spiceWriter = new StringWriter();
        SpiceEmitter.EmitDesign(circuit, spiceWriter);
        var actualSpice = spiceWriter.ToString();

        // Compare to golden
        var expectedSpice = File.ReadAllText(goldenSpicePath);
        Assert.Equal(Normalize(expectedSpice), Normalize(actualSpice));
    }

    [Fact]
    public void ACIRReader_ParsesCSAmpResistiveWithResistor()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var acirPath = Path.Combine(repoRoot, "tests/golden/acir/cs/CSAmpResistive.el.cir");

        using var reader = File.OpenText(acirPath);
        var doc = ACIRReader.Read(reader);

        Assert.Single(doc.Circuits);
        var circuit = doc.Circuits[0];
        Assert.Equal("CSAmpResistive", circuit.Name);
        Assert.Equal(ACIRLevel.EL, circuit.Level);

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
        Assert.Equal("10k", resistor.Params["R"]);

        // Harness without bias (simpler)
        Assert.NotNull(circuit.Harness);
        Assert.Single(circuit.Harness.Supplies);
        Assert.Empty(circuit.Harness.Biases);

        // Bench
        Assert.NotNull(circuit.Benches);
        Assert.Single(circuit.Benches.Benches);
        Assert.Equal("SEAmpACBench", circuit.Benches.Benches[0].Name);
    }

    private static string Normalize(string text)
        => text.Replace("\r\n", "\n").Trim();
}
