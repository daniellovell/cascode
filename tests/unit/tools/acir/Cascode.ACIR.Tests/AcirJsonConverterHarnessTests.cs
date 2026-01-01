using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Cascode.ACIR;
using Cascode.ACIR.Json;

namespace Cascode.ACIR.Tests;

public class AcirJsonConverterHarnessTests
{
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
}
