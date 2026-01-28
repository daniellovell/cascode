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
        Assert.Equal("ACBench", numeric[0].GetProperty("bench").GetString());
        Assert.Equal("net::OUT", numeric[0].GetProperty("node").GetString());
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
                    ""bench"": ""ACBench"",
                    ""metric"": ""GainBandwidth"",
                    ""node"": ""net::OUT"",
                    ""op"": "">="",
                    ""value"": 20000000,
                    ""unit"": ""Hz""
                }}],
                ""tech"": []
            }},
            ""benchDefinitions"": []
        }}";

        var result = AcirJsonConverter.FromJson(json);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var doc = result.Document!;

        var constraint = doc.Circuits[0].Constraints!.Numeric[0];
        Assert.Equal("c_gbw", constraint.Id);
        Assert.Equal("ACBench", constraint.Bench);
        Assert.Equal("GainBandwidth", constraint.Metric);
        Assert.Equal("net::OUT", constraint.Node?.ToString());
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
                }}]
            }},
            ""benchDefinitions"": []
        }}";

        var result = AcirJsonConverter.FromJson(json);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var doc = result.Document!;

        var constraint = doc.Circuits[0].Constraints!.Tech[0];
        Assert.Equal("t_lmin", constraint.Id);
        Assert.Equal("L", constraint.Param);
        Assert.Equal(">=", constraint.Op);
        Assert.Equal("180n", constraint.Value);
        Assert.Equal("m", constraint.Unit);
        Assert.Equal("*", constraint.Scope);
    }

    [Fact]
    public void RoundTrip_AllConstraintTypes_PreservesAll()
    {
        var original = CreateCircuitWithAllConstraintTypes();

        var json = AcirJsonConverter.ToJson(original);
        var result = AcirJsonConverter.FromJson(json);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var roundTripped = result.Document!;

        var constraints = roundTripped.Circuits[0].Constraints!;
        Assert.Single(constraints.Numeric);
        Assert.Equal("c_gbw", constraints.Numeric[0].Id);

        Assert.Single(constraints.Tech);
        Assert.Equal("t_lmin", constraints.Tech[0].Id);
    }

    private static ACIRDocument CreateCircuitWithConstraints()
    {
        return new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
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
                    Name = "OTA5T",
                    Level = ACIRLevel.EL,
                    Supplies = ["VDD"],
                    Grounds = ["GND"],
                    Ports =
                    [
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
                    ],
                    Fill = new FillBlock
                    {
                        Devices =
                        [
                            new DeviceDeclaration
                            {
                                DeviceType = "nmos",
                                Id = "M1",
                                Primitive = "Level1_NMOS",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["G"] = "IN",
                                    ["D"] = "OUT",
                                    ["S"] = "GND",
                                    ["B"] = "GND",
                                },
                                Size = new SizePack
                                {
                                    Entries = new Dictionary<string, string>
                                    {
                                        ["W"] = "1u",
                                        ["L"] = "180n",
                                        ["M"] = "1",
                                    },
                                },
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
                                Bench = "ACBench",
                                Metric = "GainBandwidth",
                                Node = new NodeRef { Scope = "net", Path = "OUT" },
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
                    Ports =
                    [
                        new PortDeclaration
                        {
                            Direction = PortDirection.Output,
                            Name = "OUT",
                            Type = "analog",
                        },
                    ],
                    Fill = new FillBlock { Devices = [] },
                    Constraints = new ConstraintsBlock
                    {
                        Numeric =
                        [
                            new NumericConstraint
                            {
                                Id = "c_gbw",
                                Bench = "ACBench",
                                Metric = "GainBandwidth",
                                Node = new NodeRef { Scope = "net", Path = "OUT" },
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
                    },
                },
            ],
        };
    }
}
