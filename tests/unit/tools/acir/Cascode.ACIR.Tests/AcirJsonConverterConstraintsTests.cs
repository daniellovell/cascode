using System.Collections.Generic;
using System.Text.Json;
using Cascode.ACIR;
using Cascode.ACIR.Json;

namespace Cascode.ACIR.Tests;

public class AcirJsonConverterConstraintsTests
{
    [Fact]
    public void ToJson_SerializesNumericConstraints()
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
    public void FromJson_WithNumericConstraints_ParsesCorrectly()
    {
        var json =
            $@"{{
            ""acirVersion"": ""{ACIRVersion.Current}"",
            ""circuit"": {{ ""name"": ""Test"", ""level"": ""EL"" }},
            ""supplies"": [],
            ""grounds"": [],
            ""ports"": [],
            ""nets"": [],
            ""components"": [],
            ""constraints"": {{
                ""numeric"": [{{
                    ""id"": ""c_gbw"",
                    ""metric"": ""GainBandwidth"",
                    ""node"": ""OUT"",
                    ""op"": "">="",
                    ""value"": 20000000,
                    ""unit"": ""Hz""
                }}],
                ""tech"": [],
                ""measure"": []
            }},
            ""benches"": []
        }}";

        var doc = AcirJsonConverter.FromJson(json);

        var constraint = doc.Circuits[0].Constraints!.Numeric[0];
        Assert.Equal("c_gbw", constraint.Id);
        Assert.Equal("GainBandwidth", constraint.Metric);
        Assert.Equal(">=", constraint.Op);
        Assert.Equal("20M", constraint.Value);
        Assert.Equal("Hz", constraint.Unit);
    }

    [Fact]
    public void ToJson_SerializesTechConstraints()
    {
        var doc = CreateCircuitWithAllConstraintTypes();

        var json = AcirJsonConverter.ToJson(doc);

        var parsed = JsonDocument.Parse(json);
        var constraints = parsed.RootElement.GetProperty("constraints");
        var tech = constraints.GetProperty("tech");
        Assert.Single(tech.EnumerateArray());
        var techConstraint = tech[0];
        Assert.Equal("t_lmin", techConstraint.GetProperty("id").GetString());
        Assert.Equal("L", techConstraint.GetProperty("metric").GetString());
        Assert.Equal(">=", techConstraint.GetProperty("op").GetString());
        Assert.Equal(180e-9, techConstraint.GetProperty("value").GetDouble(), precision: 15);
        Assert.Equal("m", techConstraint.GetProperty("unit").GetString());
        Assert.Equal("*", techConstraint.GetProperty("scope").GetString());
    }

    [Fact]
    public void FromJson_WithTechConstraints_ParsesCorrectly()
    {
        var json =
            $@"{{
            ""acirVersion"": ""{ACIRVersion.Current}"",
            ""circuit"": {{ ""name"": ""Test"", ""level"": ""EL"" }},
            ""supplies"": [],
            ""grounds"": [],
            ""ports"": [],
            ""nets"": [],
            ""components"": [],
            ""constraints"": {{
                ""numeric"": [],
                ""tech"": [{{
                    ""id"": ""t_lmin"",
                    ""metric"": ""L"",
                    ""op"": "">="",
                    ""value"": 180e-9,
                    ""unit"": ""m"",
                    ""scope"": ""*""
                }}],
                ""measure"": []
            }},
            ""benches"": []
        }}";

        var doc = AcirJsonConverter.FromJson(json);

        var constraint = doc.Circuits[0].Constraints!.Tech[0];
        Assert.Equal("t_lmin", constraint.Id);
        Assert.Equal("L", constraint.Param);
        Assert.Equal(">=", constraint.Op);
        Assert.Equal("180n", constraint.Value);
        Assert.Equal("m", constraint.Unit);
        Assert.Equal("*", constraint.Scope);
    }

    [Fact]
    public void ToJson_SerializesMeasureIntents()
    {
        var doc = CreateCircuitWithAllConstraintTypes();

        var json = AcirJsonConverter.ToJson(doc);

        var parsed = JsonDocument.Parse(json);
        var constraints = parsed.RootElement.GetProperty("constraints");
        var measure = constraints.GetProperty("measure");
        Assert.Single(measure.EnumerateArray());
        var measureIntent = measure[0];
        Assert.Equal("m_gbw", measureIntent.GetProperty("id").GetString());
        Assert.Equal("SEOpAmpACBench", measureIntent.GetProperty("bench").GetString());
        Assert.Equal("GainBandwidth", measureIntent.GetProperty("metric").GetString());
        Assert.Equal("OUT", measureIntent.GetProperty("node").GetString());
    }

    [Fact]
    public void FromJson_WithMeasureIntents_ParsesCorrectly()
    {
        var json =
            $@"{{
            ""acirVersion"": ""{ACIRVersion.Current}"",
            ""circuit"": {{ ""name"": ""Test"", ""level"": ""EL"" }},
            ""supplies"": [],
            ""grounds"": [],
            ""ports"": [],
            ""nets"": [],
            ""components"": [],
            ""constraints"": {{
                ""numeric"": [],
                ""tech"": [],
                ""measure"": [{{
                    ""id"": ""m_gbw"",
                    ""bench"": ""SEOpAmpACBench"",
                    ""metric"": ""GainBandwidth"",
                    ""node"": ""OUT""
                }}]
            }},
            ""benches"": []
        }}";

        var doc = AcirJsonConverter.FromJson(json);

        var measureIntent = doc.Circuits[0].Constraints!.Measure[0];
        Assert.Equal("m_gbw", measureIntent.Id);
        Assert.Equal("SEOpAmpACBench", measureIntent.Bench);
        Assert.Equal("GainBandwidth", measureIntent.Metric);
        Assert.Equal("OUT", measureIntent.Node);
    }

    [Fact]
    public void RoundTrip_AllConstraintTypes_PreservesAll()
    {
        var original = CreateCircuitWithAllConstraintTypes();

        var json = AcirJsonConverter.ToJson(original);
        var roundTripped = AcirJsonConverter.FromJson(json);

        var constraints = roundTripped.Circuits[0].Constraints!;
        Assert.Single(constraints.Numeric);
        Assert.Equal("c_gbw", constraints.Numeric[0].Id);

        Assert.Single(constraints.Tech);
        Assert.Equal("t_lmin", constraints.Tech[0].Id);

        Assert.Single(constraints.Measure);
        Assert.Equal("m_gbw", constraints.Measure[0].Id);
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

    private static ACIRDocument CreateCircuitWithAllConstraintTypes()
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
                    Ports = [new PortDeclaration { Name = "OUT", Type = "analog" }],
                    Fill = new FillBlock { Devices = [] },
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
                        Tech =
                        [
                            new TechConstraint
                            {
                                Id = "t_lmin",
                                Param = "L",
                                Op = ">=",
                                Value = "180n",
                                Unit = "m",
                                Scope = "*",
                            },
                        ],
                        Measure =
                        [
                            new MeasureIntent
                            {
                                Id = "m_gbw",
                                Bench = "SEOpAmpACBench",
                                Metric = "GainBandwidth",
                                Node = "OUT",
                            },
                        ],
                    },
                },
            ],
        };
    }
}
