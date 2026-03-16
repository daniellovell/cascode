using System.Collections.Generic;
using System.Text.Json;
using Cascode.Language;
using Cascode.Language.Json;

namespace Cascode.Language.Tests;

public class CascodeJsonConverterConstraintsTests
{
    [Fact]
    public void ToJson_SerializesBenchConstraints()
    {
        var doc = CreateCircuitWithConstraints();

        var json = CascodeJsonConverter.ToJson(doc);

        var parsed = JsonDocument.Parse(json);
        var constraints = parsed.RootElement.GetProperty("constraints");
        var bench = constraints.GetProperty("bench");
        Assert.Single(bench.EnumerateArray());
        Assert.Equal("c_gbw", bench[0].GetProperty("id").GetString());
        Assert.Equal("transfer_bench", bench[0].GetProperty("bench").GetString());
        Assert.Equal("net::OUT", bench[0].GetProperty("node").GetString());
        Assert.Equal(">=", bench[0].GetProperty("op").GetString());
        Assert.Equal(20000000, bench[0].GetProperty("value").GetDouble());
        Assert.Equal("Hz", bench[0].GetProperty("unit").GetString());
    }

    [Fact]
    public void FromJson_WithBenchConstraints_ParsesCorrectly()
    {
        var json =
            $@"{{
            ""cascodeVersion"": ""{CascodeVersion.Current}"",
            ""circuit"": {{ ""name"": ""Test"", ""level"": ""EL"" }},
            ""supplies"": [],
            ""grounds"": [],
            ""ports"": [],
            ""nets"": [],
            ""components"": [],
            ""constraints"": {{
                ""bench"": [{{
                    ""id"": ""c_gbw"",
                    ""bench"": ""transfer_bench"",
                    ""metric"": ""GainBandwidth"",
                    ""node"": ""net::OUT"",
                    ""op"": "">="",
                    ""value"": 20000000,
                    ""unit"": ""Hz""
                }}],
                ""spec"": [],
                ""physical"": []
            }},
            ""benchDefinitions"": []
        }}";

        var result = CascodeJsonConverter.FromJson(json);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var doc = result.Document!;

        var constraint = doc.Circuits[0].Constraints!.Bench[0];
        Assert.Equal("c_gbw", constraint.Id);
        Assert.Equal("transfer_bench", constraint.Bench);
        Assert.Equal("GainBandwidth", constraint.Metric);
        Assert.Equal("net::OUT", constraint.Node?.ToString());
        Assert.Equal(">=", constraint.Op);
        Assert.Equal("20M", constraint.Value);
        Assert.Equal("Hz", constraint.Unit);
    }

    [Fact]
    public void ToJson_SerializesPhysicalConstraints()
    {
        var doc = CreateCircuitWithAllConstraintTypes();

        var json = CascodeJsonConverter.ToJson(doc);

        var parsed = JsonDocument.Parse(json);
        var constraints = parsed.RootElement.GetProperty("constraints");
        var physical = constraints.GetProperty("physical");
        Assert.Single(physical.EnumerateArray());
        var physicalConstraint = physical[0];
        Assert.Equal("t_lmin", physicalConstraint.GetProperty("id").GetString());
        Assert.Equal("L", physicalConstraint.GetProperty("metric").GetString());
        Assert.Equal(">=", physicalConstraint.GetProperty("op").GetString());
        Assert.Equal(180e-9, physicalConstraint.GetProperty("value").GetDouble(), precision: 15);
        Assert.Equal("m", physicalConstraint.GetProperty("unit").GetString());
        Assert.Equal("*", physicalConstraint.GetProperty("scope").GetString());
    }

    [Fact]
    public void FromJson_WithPhysicalConstraints_ParsesCorrectly()
    {
        var json =
            $@"{{
            ""cascodeVersion"": ""{CascodeVersion.Current}"",
            ""circuit"": {{ ""name"": ""Test"", ""level"": ""EL"" }},
            ""supplies"": [],
            ""grounds"": [],
            ""ports"": [],
            ""nets"": [],
            ""components"": [],
            ""constraints"": {{
                ""bench"": [],
                ""spec"": [],
                ""physical"": [{{
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

        var result = CascodeJsonConverter.FromJson(json);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var doc = result.Document!;

        var constraint = doc.Circuits[0].Constraints!.Physical[0];
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

        var json = CascodeJsonConverter.ToJson(original);
        var result = CascodeJsonConverter.FromJson(json);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var roundTripped = result.Document!;

        var constraints = roundTripped.Circuits[0].Constraints!;
        Assert.Single(constraints.Bench);
        Assert.Equal("c_gbw", constraints.Bench[0].Id);

        Assert.Single(constraints.Physical);
        Assert.Equal("t_lmin", constraints.Physical[0].Id);
    }

    private static CascodeDocument CreateCircuitWithConstraints()
    {
        return new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Primitives = [TestPrimitives.GetLevel1Nmos()],
            Circuits =
            [
                new Circuit
                {
                    Name = "OTA5T",
                    Level = CascodeLevel.EL,
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
                                Primitive = "NMOS_Level1",
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
                        Bench =
                        [
                            new MetricConstraint
                            {
                                Id = "c_gbw",
                                Bench = "transfer_bench",
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

    private static CascodeDocument CreateCircuitWithAllConstraintTypes()
    {
        return new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Circuits =
            [
                new Circuit
                {
                    Name = "OTA5T",
                    Level = CascodeLevel.EL,
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
                        Bench =
                        [
                            new MetricConstraint
                            {
                                Id = "c_gbw",
                                Bench = "transfer_bench",
                                Metric = "GainBandwidth",
                                Node = new NodeRef { Scope = "net", Path = "OUT" },
                                Op = ">=",
                                Value = "20M",
                                Unit = "Hz",
                            },
                        ],
                        Physical =
                        [
                            new PhysicalConstraint
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
