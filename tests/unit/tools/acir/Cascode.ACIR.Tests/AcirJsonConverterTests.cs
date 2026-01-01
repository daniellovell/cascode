using System.IO;
using System.Text.Json;
using Cascode.ACIR;
using Cascode.ACIR.Json;

namespace Cascode.ACIR.Tests;

public class AcirJsonConverterTests
{
    [Fact]
    public void ToJson_SimpleElCircuit_ProducesValidJson()
    {
        var doc = CreateSimpleElCircuit();

        var json = AcirJsonConverter.ToJson(doc);

        Assert.NotEmpty(json);
        var parsed = JsonDocument.Parse(json);
        Assert.Equal(
            "OTA5T",
            parsed.RootElement.GetProperty("circuit").GetProperty("name").GetString()
        );
        Assert.Equal(
            "EL",
            parsed.RootElement.GetProperty("circuit").GetProperty("level").GetString()
        );
    }

    [Fact]
    public void ToJson_IncludesVersion()
    {
        var doc = CreateSimpleElCircuit();

        var json = AcirJsonConverter.ToJson(doc);

        var parsed = JsonDocument.Parse(json);
        Assert.Equal(
            ACIRVersion.Current,
            parsed.RootElement.GetProperty("acirVersion").GetString()
        );
    }

    [Fact]
    public void ToJson_SerializesPorts()
    {
        var doc = CreateSimpleElCircuit();

        var json = AcirJsonConverter.ToJson(doc);

        var parsed = JsonDocument.Parse(json);
        var ports = parsed.RootElement.GetProperty("ports");
        Assert.Equal(2, ports.GetArrayLength());
        Assert.Equal("IN", ports[0].GetProperty("name").GetString());
        Assert.Equal("analog", ports[0].GetProperty("kind").GetString());
    }

    [Fact]
    public void ToJson_SerializesComponents()
    {
        var doc = CreateSimpleElCircuit();

        var json = AcirJsonConverter.ToJson(doc);

        var parsed = JsonDocument.Parse(json);
        var components = parsed.RootElement.GetProperty("components");
        Assert.Single(components.EnumerateArray());
        var component = components[0];
        Assert.Equal("nmos", component.GetProperty("kind").GetString());
        Assert.Equal("M1", component.GetProperty("name").GetString());
        Assert.Equal("IN", component.GetProperty("connections").GetProperty("G").GetString());
        Assert.Equal("1u", component.GetProperty("params").GetProperty("W").GetString());
    }

    [Fact]
    public void ToJson_SerializesConstraints()
    {
        var doc = CreateCircuitWithConstraints();

        var json = AcirJsonConverter.ToJson(doc);

        var parsed = JsonDocument.Parse(json);
        var constraints = parsed.RootElement.GetProperty("constraints");
        var numeric = constraints.GetProperty("numeric");
        Assert.Single(numeric.EnumerateArray());
        Assert.Equal("c_gbw", numeric[0].GetProperty("id").GetString());
        Assert.Equal(">=", numeric[0].GetProperty("op").GetString());
        Assert.Equal(20000000, numeric[0].GetProperty("value").GetDouble());
        Assert.Equal("Hz", numeric[0].GetProperty("unit").GetString());
    }

    [Fact]
    public void ToJson_SerializesHarness()
    {
        var doc = CreateCircuitWithHarness();

        var json = AcirJsonConverter.ToJson(doc);

        var parsed = JsonDocument.Parse(json);
        var harness = parsed.RootElement.GetProperty("harness");
        var supply = harness.GetProperty("supply");
        Assert.Equal("VDD", supply.GetProperty("net").GetString());
        Assert.Equal(1.8, supply.GetProperty("voltage").GetDouble());
    }

    [Fact]
    public void ToJsonDocument_NoElCircuit_Throws()
    {
        var doc = new ACIRDocument
        {
            Circuits = [new Circuit { Name = "TestML", Level = ACIRLevel.ML }],
        };

        var ex = Assert.Throws<ArgumentException>(() => AcirJsonConverter.ToJsonDocument(doc));
        Assert.Contains("No EL-level circuit", ex.Message);
    }

    [Fact]
    public void ToJsonDocument_SpecificCircuitNotFound_Throws()
    {
        var doc = CreateSimpleElCircuit();

        var ex = Assert.Throws<ArgumentException>(() =>
            AcirJsonConverter.ToJsonDocument(doc, "NonExistent")
        );
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public void FromJson_ValidJson_ProducesAcirDocument()
    {
        var json =
            @"{
            ""acirVersion"": ""1.1"",
            ""circuit"": { ""name"": ""TestCircuit"", ""level"": ""EL"" },
            ""supplies"": [""VDD""],
            ""grounds"": [""GND""],
            ""ports"": [{ ""name"": ""IN"", ""kind"": ""analog"" }],
            ""nets"": [],
            ""components"": [],
            ""benches"": []
        }";

        var doc = AcirJsonConverter.FromJson(json);

        Assert.NotNull(doc);
        Assert.Single(doc.Circuits);
        Assert.Equal("TestCircuit", doc.Circuits[0].Name);
        Assert.Equal(ACIRLevel.EL, doc.Circuits[0].Level);
        Assert.Equal("VDD", doc.Circuits[0].Supplies[0]);
    }

    [Fact]
    public void FromJson_WithComponents_ParsesCorrectly()
    {
        var json =
            @"{
            ""acirVersion"": ""1.1"",
            ""circuit"": { ""name"": ""Test"", ""level"": ""EL"" },
            ""supplies"": [""VDD""],
            ""grounds"": [],
            ""ports"": [],
            ""nets"": [],
            ""components"": [{
                ""kind"": ""nmos"",
                ""name"": ""M1"",
                ""connections"": { ""G"": ""IN"", ""D"": ""OUT"", ""S"": ""GND"", ""B"": ""GND"" },
                ""params"": { ""W"": ""1u"", ""L"": ""180n"" },
                ""process"": ""nfet_01v8""
            }],
            ""benches"": []
        }";

        var doc = AcirJsonConverter.FromJson(json);

        var device = doc.Circuits[0].Fill!.Devices[0];
        Assert.Equal("nmos", device.DeviceType);
        Assert.Equal("M1", device.Id);
        Assert.Equal("IN", device.Bindings["G"]);
        Assert.Equal("1u", device.Params["W"]);
        Assert.Equal("nfet_01v8", device.PdkDevice);
    }

    [Fact]
    public void FromJson_WithConstraints_ParsesCorrectly()
    {
        var json =
            @"{
            ""acirVersion"": ""1.1"",
            ""circuit"": { ""name"": ""Test"", ""level"": ""EL"" },
            ""supplies"": [],
            ""grounds"": [],
            ""ports"": [],
            ""nets"": [],
            ""components"": [],
            ""constraints"": {
                ""numeric"": [{
                    ""id"": ""c_gbw"",
                    ""metric"": ""GainBandwidth"",
                    ""node"": ""OUT"",
                    ""op"": "">="",
                    ""value"": 20000000,
                    ""unit"": ""Hz""
                }],
                ""tech"": [],
                ""measure"": []
            },
            ""benches"": []
        }";

        var doc = AcirJsonConverter.FromJson(json);

        var constraint = doc.Circuits[0].Constraints!.Numeric[0];
        Assert.Equal("c_gbw", constraint.Id);
        Assert.Equal("GainBandwidth", constraint.Metric);
        Assert.Equal(">=", constraint.Op);
        Assert.Equal("20M", constraint.Value);
        Assert.Equal("Hz", constraint.Unit);
    }

    [Fact]
    public void RoundTrip_PreservesCircuitName()
    {
        var original = CreateSimpleElCircuit();

        var json = AcirJsonConverter.ToJson(original);
        var roundTripped = AcirJsonConverter.FromJson(json);

        Assert.Equal(original.Circuits[0].Name, roundTripped.Circuits[0].Name);
    }

    [Fact]
    public void RoundTrip_PreservesDevices()
    {
        var original = CreateSimpleElCircuit();

        var json = AcirJsonConverter.ToJson(original);
        var roundTripped = AcirJsonConverter.FromJson(json);

        var originalDevice = original.Circuits[0].Fill!.Devices[0];
        var roundTrippedDevice = roundTripped.Circuits[0].Fill!.Devices[0];

        Assert.Equal(originalDevice.DeviceType, roundTrippedDevice.DeviceType);
        Assert.Equal(originalDevice.Id, roundTrippedDevice.Id);
        Assert.Equal(originalDevice.Bindings["G"], roundTrippedDevice.Bindings["G"]);
    }

    [Fact]
    public void RoundTrip_PreservesConstraintSemantics()
    {
        var original = CreateCircuitWithConstraints();

        var json = AcirJsonConverter.ToJson(original);
        var roundTripped = AcirJsonConverter.FromJson(json);

        var originalConstraint = original.Circuits[0].Constraints!.Numeric[0];
        var roundTrippedConstraint = roundTripped.Circuits[0].Constraints!.Numeric[0];

        Assert.Equal(originalConstraint.Id, roundTrippedConstraint.Id);
        Assert.Equal(originalConstraint.Metric, roundTrippedConstraint.Metric);
        Assert.Equal(originalConstraint.Op, roundTrippedConstraint.Op);
        Assert.Equal(originalConstraint.Unit, roundTrippedConstraint.Unit);
    }

    [Fact]
    public void FromJson_MultipleSuppliesAndGrounds_ParsesAllEntries()
    {
        var json =
            @"{
            ""acirVersion"": ""1.1"",
            ""circuit"": { ""name"": ""TestCircuit"", ""level"": ""EL"" },
            ""supplies"": [""VDD"", ""VDDA"", ""VDDD""],
            ""grounds"": [""GND"", ""GNDA"", ""GNDD""],
            ""ports"": [],
            ""nets"": [],
            ""components"": [],
            ""benches"": []
        }";

        var doc = AcirJsonConverter.FromJson(json);

        Assert.Equal(3, doc.Circuits[0].Supplies.Count);
        Assert.Equal("VDD", doc.Circuits[0].Supplies[0]);
        Assert.Equal("VDDA", doc.Circuits[0].Supplies[1]);
        Assert.Equal("VDDD", doc.Circuits[0].Supplies[2]);
        Assert.Equal(3, doc.Circuits[0].Grounds.Count);
        Assert.Equal("GND", doc.Circuits[0].Grounds[0]);
        Assert.Equal("GNDA", doc.Circuits[0].Grounds[1]);
        Assert.Equal("GNDD", doc.Circuits[0].Grounds[2]);
    }

    [Fact]
    public void RoundTrip_PreservesMultipleSuppliesAndGrounds()
    {
        var original = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Circuits =
            [
                new Circuit
                {
                    Name = "MultiRail",
                    Level = ACIRLevel.EL,
                    Supplies = ["VDD", "VDDA"],
                    Grounds = ["GND", "GNDA"],
                    Ports = [],
                    Fill = new FillBlock { Devices = [] },
                },
            ],
        };

        var json = AcirJsonConverter.ToJson(original);
        var roundTripped = AcirJsonConverter.FromJson(json);

        Assert.Equal(2, roundTripped.Circuits[0].Supplies.Count);
        Assert.Equal("VDD", roundTripped.Circuits[0].Supplies[0]);
        Assert.Equal("VDDA", roundTripped.Circuits[0].Supplies[1]);
        Assert.Equal(2, roundTripped.Circuits[0].Grounds.Count);
        Assert.Equal("GND", roundTripped.Circuits[0].Grounds[0]);
        Assert.Equal("GNDA", roundTripped.Circuits[0].Grounds[1]);
    }

    private static ACIRDocument CreateSimpleElCircuit()
    {
        return new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Circuits =
            [
                new Circuit
                {
                    Name = "OTA5T",
                    Level = ACIRLevel.EL,
                    Supplies = ["VDD"],
                    Grounds = ["GND"],
                    Ports =
                    [
                        new PortDeclaration { Name = "IN", Type = "analog" },
                        new PortDeclaration { Name = "OUT", Type = "analog" },
                    ],
                    Fill = new FillBlock
                    {
                        Devices =
                        [
                            new DeviceDeclaration
                            {
                                DeviceType = "nmos",
                                Id = "M1",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["G"] = "IN",
                                    ["D"] = "OUT",
                                    ["S"] = "GND",
                                    ["B"] = "GND",
                                },
                                Params = new Dictionary<string, string>
                                {
                                    ["W"] = "1u",
                                    ["L"] = "180n",
                                },
                                PdkDevice = "nmos",
                            },
                        ],
                    },
                },
            ],
        };
    }

    private static ACIRDocument CreateCircuitWithConstraints()
    {
        return new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Circuits =
            [
                new Circuit
                {
                    Name = "OTA5T",
                    Level = ACIRLevel.EL,
                    Supplies = ["VDD"],
                    Grounds = ["GND"],
                    Ports =
                    [
                        new PortDeclaration { Name = "IN", Type = "analog" },
                        new PortDeclaration { Name = "OUT", Type = "analog" },
                    ],
                    Fill = new FillBlock
                    {
                        Devices =
                        [
                            new DeviceDeclaration
                            {
                                DeviceType = "nmos",
                                Id = "M1",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["G"] = "IN",
                                    ["D"] = "OUT",
                                    ["S"] = "GND",
                                    ["B"] = "GND",
                                },
                                Params = new Dictionary<string, string>
                                {
                                    ["W"] = "1u",
                                    ["L"] = "180n",
                                },
                                PdkDevice = "nmos",
                            },
                        ],
                    },
                    Constraints = new ConstraintsBlock
                    {
                        Numeric =
                        [
                            new NumericConstraint
                            {
                                Id = "c_gbw",
                                Metric = "GainBandwidth",
                                Node = "OUT",
                                Op = ">=",
                                Value = "20M",
                                Unit = "Hz",
                            },
                        ],
                    },
                },
            ],
        };
    }

    [Fact]
    public void ToJson_MultipleLoadElements_PreservesAllElements()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Circuits =
            [
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = ACIRLevel.EL,
                    Supplies = ["VDD"],
                    Grounds = ["GND"],
                    Ports = [],
                    Fill = new FillBlock { Devices = [] },
                    Harness = new HarnessBlock
                    {
                        Loads =
                        [
                            new LoadValue
                            {
                                Net = "OUT",
                                Elements =
                                [
                                    new LoadElement("C", "1pF"),
                                    new LoadElement("C", "500fF"),
                                    new LoadElement("R", "1MOhm"),
                                    new LoadElement("R", "10MOhm"),
                                ],
                            },
                        ],
                    },
                },
            ],
        };

        var json = AcirJsonConverter.ToJson(doc);
        var parsed = JsonDocument.Parse(json);

        var loads = parsed.RootElement.GetProperty("harness").GetProperty("loads");
        Assert.Single(loads.EnumerateArray());

        var load = loads[0];
        Assert.Equal("OUT", load.GetProperty("net").GetString());

        var capacitances = load.GetProperty("capacitances");
        Assert.Equal(2, capacitances.GetArrayLength());
        Assert.Equal(1e-12, capacitances[0].GetDouble());
        Assert.Equal(500e-15, capacitances[1].GetDouble());

        var resistances = load.GetProperty("resistances");
        Assert.Equal(2, resistances.GetArrayLength());
        Assert.Equal(1e6, resistances[0].GetDouble());
        Assert.Equal(10e6, resistances[1].GetDouble());
    }

    [Fact]
    public void RoundTrip_MultipleLoadElements_PreservesAllElements()
    {
        var original = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Circuits =
            [
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = ACIRLevel.EL,
                    Supplies = ["VDD"],
                    Grounds = ["GND"],
                    Ports = [],
                    Fill = new FillBlock { Devices = [] },
                    Harness = new HarnessBlock
                    {
                        Loads =
                        [
                            new LoadValue
                            {
                                Net = "OUT",
                                Elements =
                                [
                                    new LoadElement("C", "1pF"),
                                    new LoadElement("C", "500fF"),
                                    new LoadElement("R", "1MOhm"),
                                ],
                            },
                        ],
                    },
                },
            ],
        };

        var json = AcirJsonConverter.ToJson(original);
        var roundTripped = AcirJsonConverter.FromJson(json);

        var load = roundTripped.Circuits[0].Harness!.Loads[0];
        Assert.Equal("OUT", load.Net);
        Assert.Equal(3, load.Elements.Count);

        var capacitors = load.Elements.Where(e => e.Type == "C").ToList();
        Assert.Equal(2, capacitors.Count);
        Assert.Equal("1pF", capacitors[0].Value);
        Assert.Equal("500fF", capacitors[1].Value);

        var resistors = load.Elements.Where(e => e.Type == "R").ToList();
        Assert.Single(resistors);
        Assert.Equal("1MOhm", resistors[0].Value);
    }

    private static ACIRDocument CreateCircuitWithHarness()
    {
        return new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Circuits =
            [
                new Circuit
                {
                    Name = "OTA5T",
                    Level = ACIRLevel.EL,
                    Supplies = ["VDD"],
                    Grounds = ["GND"],
                    Ports =
                    [
                        new PortDeclaration { Name = "IN", Type = "analog" },
                        new PortDeclaration { Name = "OUT", Type = "analog" },
                    ],
                    Fill = new FillBlock
                    {
                        Devices =
                        [
                            new DeviceDeclaration
                            {
                                DeviceType = "nmos",
                                Id = "M1",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["G"] = "IN",
                                    ["D"] = "OUT",
                                    ["S"] = "GND",
                                    ["B"] = "GND",
                                },
                                Params = new Dictionary<string, string>
                                {
                                    ["W"] = "1u",
                                    ["L"] = "180n",
                                },
                                PdkDevice = "nmos",
                            },
                        ],
                    },
                    Harness = new HarnessBlock
                    {
                        Supplies = [new SupplyValue { Net = "VDD", Value = "1.8V" }],
                        Loads =
                        [
                            new LoadValue { Net = "OUT", Elements = [new LoadElement("C", "1pF")] },
                        ],
                    },
                },
            ],
        };
    }

    [Fact]
    public void RoundTrip_WithBiases_PreservesBiasValues()
    {
        var original = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Circuits =
            [
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = ACIRLevel.EL,
                    Supplies = ["VDD"],
                    Grounds = ["GND"],
                    Ports =
                    [
                        new PortDeclaration { Name = "VTAIL", Type = "bias" },
                        new PortDeclaration { Name = "OUT", Type = "analog" },
                    ],
                    Fill = new FillBlock { Devices = [] },
                    Harness = new HarnessBlock
                    {
                        Supplies = [new SupplyValue { Net = "VDD", Value = "1.8V" }],
                        Biases =
                        [
                            new BiasValue { Net = "VTAIL", Value = "0.7V" },
                            new BiasValue { Net = "VBIAS", Value = "0.5V" },
                        ],
                        Loads =
                        [
                            new LoadValue
                            {
                                Net = "OUT",
                                Elements = [new LoadElement("C", "100fF")],
                            },
                        ],
                    },
                },
            ],
        };

        var json = AcirJsonConverter.ToJson(original);
        var roundTripped = AcirJsonConverter.FromJson(json);

        var harness = roundTripped.Circuits[0].Harness!;
        Assert.Equal(2, harness.Biases.Count);
        Assert.Equal("VTAIL", harness.Biases[0].Net);
        Assert.Equal("700mV", harness.Biases[0].Value);
        Assert.Equal("VBIAS", harness.Biases[1].Net);
        Assert.Equal("500mV", harness.Biases[1].Value);
    }

    [Fact]
    public void ToJson_WithBiases_IncludesBiasesInHarness()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Circuits =
            [
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = ACIRLevel.EL,
                    Supplies = ["VDD"],
                    Grounds = ["GND"],
                    Ports = [],
                    Fill = new FillBlock { Devices = [] },
                    Harness = new HarnessBlock
                    {
                        Supplies = [new SupplyValue { Net = "VDD", Value = "1.8V" }],
                        Biases = [new BiasValue { Net = "VTAIL", Value = "0.7V" }],
                    },
                },
            ],
        };

        var json = AcirJsonConverter.ToJson(doc);
        var parsed = JsonDocument.Parse(json);

        var biases = parsed.RootElement.GetProperty("harness").GetProperty("biases");
        Assert.Equal(1, biases.GetArrayLength());
        Assert.Equal("VTAIL", biases[0].GetProperty("net").GetString());
        Assert.Equal(0.7, biases[0].GetProperty("voltage").GetDouble());
    }
}
