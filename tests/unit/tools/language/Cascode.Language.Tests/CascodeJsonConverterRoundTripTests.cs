using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Cascode.Language;
using Cascode.Language.Json;
using Cascode.TestSupport;

namespace Cascode.Language.Tests;

public class CascodeJsonConverterRoundTripTests
{
    [Fact]
    public void RoundTrip_PreservesCircuitName()
    {
        var original = CreateSimpleElCircuit();

        var json = CascodeJsonConverter.ToJson(original);
        var result = CascodeJsonConverter.FromJson(json);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var roundTripped = result.Document!;

        Assert.Equal(original.Circuits[0].Name, roundTripped.Circuits[0].Name);
    }

    [Fact]
    public void RoundTrip_PreservesPortDirection()
    {
        var original = CreateSimpleElCircuit();

        var json = CascodeJsonConverter.ToJson(original);
        var result = CascodeJsonConverter.FromJson(json);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var roundTripped = result.Document!;

        Assert.Equal(
            original.Circuits[0].Ports[0].Direction,
            roundTripped.Circuits[0].Ports[0].Direction
        );
        Assert.Equal(
            original.Circuits[0].Ports[1].Direction,
            roundTripped.Circuits[0].Ports[1].Direction
        );
    }

    [Fact]
    public void RoundTrip_PreservesDevices()
    {
        var original = CreateSimpleElCircuit();

        var json = CascodeJsonConverter.ToJson(original);
        var result = CascodeJsonConverter.FromJson(json);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var roundTripped = result.Document!;

        var originalDevice = original.Circuits[0].Fill!.Devices[0];
        var roundTrippedDevice = roundTripped.Circuits[0].Fill!.Devices[0];

        Assert.Equal(originalDevice.DeviceType, roundTrippedDevice.DeviceType);
        Assert.Equal(originalDevice.Id, roundTrippedDevice.Id);
        Assert.Equal(originalDevice.Bindings["G"], roundTrippedDevice.Bindings["G"]);
    }

    [Fact]
    public void RoundTrip_PreservesSomeRequestDeclaredType()
    {
        var original = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Circuits =
            [
                new Circuit
                {
                    Name = "Top",
                    Level = CascodeLevel.EL,
                    Fill = new FillBlock
                    {
                        Instances =
                        [
                            new InstanceDeclaration
                            {
                                Id = "frontend",
                                DeclaredType = "Some",
                                Type = "AnalogFrontend",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["IN"] = "VIN",
                                    ["OUT"] = "VOUT",
                                },
                            },
                        ],
                    },
                },
            ],
        };

        var json = CascodeJsonConverter.ToJson(original);
        var result = CascodeJsonConverter.FromJson(json);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));

        var instance = Assert.Single(result.Document!.Circuits[0].Fill!.Instances);
        Assert.True(instance.IsSomeRequest);
        Assert.Equal("AnalogFrontend", instance.Type);
    }

    [Fact]
    public void RoundTrip_PreservesConstraintSemantics()
    {
        var original = CreateCircuitWithConstraints();

        var json = CascodeJsonConverter.ToJson(original);
        var result = CascodeJsonConverter.FromJson(json);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var roundTripped = result.Document!;

        var originalConstraint = original.Circuits[0].Constraints!.Bench[0];
        var roundTrippedConstraint = roundTripped.Circuits[0].Constraints!.Bench[0];

        Assert.Equal(originalConstraint.Id, roundTrippedConstraint.Id);
        Assert.Equal(originalConstraint.Bench, roundTrippedConstraint.Bench);
        Assert.Equal(originalConstraint.Metric, roundTrippedConstraint.Metric);
        Assert.Equal(originalConstraint.Node?.ToString(), roundTrippedConstraint.Node?.ToString());
        Assert.Equal(originalConstraint.Op, roundTrippedConstraint.Op);
        Assert.Equal(originalConstraint.Value, roundTrippedConstraint.Value);
        Assert.Equal(originalConstraint.Unit, roundTrippedConstraint.Unit);
    }

    [Fact]
    public void RoundTrip_PreservesMultipleSuppliesAndGrounds()
    {
        var original = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Circuits =
            [
                new Circuit
                {
                    Name = "MultiRail",
                    Level = CascodeLevel.EL,
                    Supplies = ["VDD", "VDDA"],
                    Grounds = ["GND", "GNDA"],
                    Ports = [],
                    Fill = new FillBlock { Devices = [] },
                },
            ],
        };

        var json = CascodeJsonConverter.ToJson(original);
        var result = CascodeJsonConverter.FromJson(json);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var roundTripped = result.Document!;

        Assert.Equal(2, roundTripped.Circuits[0].Supplies.Count);
        Assert.Equal("VDD", roundTripped.Circuits[0].Supplies[0]);
        Assert.Equal("VDDA", roundTripped.Circuits[0].Supplies[1]);
        Assert.Equal(2, roundTripped.Circuits[0].Grounds.Count);
        Assert.Equal("GND", roundTripped.Circuits[0].Grounds[0]);
        Assert.Equal("GNDA", roundTripped.Circuits[0].Grounds[1]);
    }

    [Fact]
    public void RoundTrip_WithBenchDefinitions_PreservesBenchNames()
    {
        var original = CreateCircuitWithBenches();

        var json = CascodeJsonConverter.ToJson(original);
        var result = CascodeJsonConverter.FromJson(json);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var roundTripped = result.Document!;

        Assert.Equal(2, roundTripped.BenchDefinitions.Count);
        Assert.Equal("vdd_pwr", roundTripped.BenchDefinitions[0].Name);
        Assert.Equal("transfer_bench", roundTripped.BenchDefinitions[1].Name);
    }

    [Fact]
    public void ToJson_WithBenchDefinitions_EmitsBenchDefinitionsArray()
    {
        var doc = CreateCircuitWithBenches();

        var json = CascodeJsonConverter.ToJson(doc);
        var parsed = JsonDocument.Parse(json);

        var benches = parsed.RootElement.GetProperty("benchDefinitions");
        Assert.Equal(2, benches.GetArrayLength());
        Assert.Equal("vdd_pwr", benches[0].GetProperty("name").GetString());
        Assert.Equal("transfer_bench", benches[1].GetProperty("name").GetString());
    }

    [Fact]
    public void RoundTrip_WithNets_PreservesNets()
    {
        var original = CreateCircuitWithNets();

        var json = CascodeJsonConverter.ToJson(original);
        var result = CascodeJsonConverter.FromJson(json);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var roundTripped = result.Document!;

        Assert.NotNull(roundTripped.Circuits[0].Fill);
        Assert.Equal(2, roundTripped.Circuits[0].Fill!.Nets.Count);
        Assert.Equal("tnode", roundTripped.Circuits[0].Fill!.Nets[0].Id);
        Assert.Equal("analog", roundTripped.Circuits[0].Fill!.Nets[0].Domain);
        Assert.Equal("bias_node", roundTripped.Circuits[0].Fill!.Nets[1].Id);
        Assert.Equal("analog", roundTripped.Circuits[0].Fill!.Nets[1].Domain);
    }

    [Fact]
    public void RoundTrip_TelescopicCascodeAttach_PreservesAttachesAndBiases()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var cascodePath = Path.Combine(
            repoRoot,
            "tests/golden/cas/hierarchy/TelescopicCascodeFullyDiff_Attach.el.cai"
        );

        CascodeDocument doc;
        using (var reader = File.OpenText(cascodePath))
        {
            doc = CascodeReader.Read(reader, cascodePath);
        }

        var json = CascodeJsonConverter.ToJson(doc, "TelescopicCascodeFullyDiff_Attach");
        var result = CascodeJsonConverter.FromJson(json);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var roundTripped = result.Document!;

        var fill = roundTripped.Circuits[0].Fill!;
        Assert.NotNull(fill.Attaches);
        Assert.Equal(2, fill.Attaches.Count);
        Assert.Equal("nl", fill.Attaches[0].SourceInstance);
        Assert.Equal("dp", fill.Attaches[0].TargetInstances[0]);
        Assert.Equal("pl2", fill.Attaches[1].SourceInstance);
        Assert.Equal(2, fill.Attaches[1].TargetInstances.Count);
        Assert.Equal("pl1", fill.Attaches[1].TargetInstances[0]);
        Assert.Equal("nl", fill.Attaches[1].TargetInstances[1]);

        Assert.NotNull(roundTripped.Circuits[0].Harness);
        Assert.Equal(4, roundTripped.Circuits[0].Harness!.Biases.Count);
    }

    [Fact]
    public void ToJson_WithNets_EmitsNetsArray()
    {
        var doc = CreateCircuitWithNets();

        var json = CascodeJsonConverter.ToJson(doc);
        var parsed = JsonDocument.Parse(json);

        var nets = parsed.RootElement.GetProperty("nets");
        Assert.Equal(2, nets.GetArrayLength());
        Assert.Equal("tnode", nets[0].GetProperty("name").GetString());
        Assert.Equal("analog", nets[0].GetProperty("kind").GetString());
        Assert.Equal("bias_node", nets[1].GetProperty("name").GetString());
        Assert.Equal("analog", nets[1].GetProperty("kind").GetString());
    }

    private static CascodeDocument CreateSimpleElCircuit() => TestFixtures.CreateSimpleElCircuit();

    private static CascodeDocument CreateCircuitWithConstraints()
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
                    },
                },
            ],
        };
    }

    private static CascodeDocument CreateCircuitWithBenches()
    {
        return new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            BenchDefinitions =
            [
                new BenchDefinition
                {
                    Name = "vdd_pwr",
                    Terminals =
                    [
                        new BenchTerminal(BenchTerminalRole.Stim, "IN", "analog"),
                        new BenchTerminal(BenchTerminalRole.Resp, "OUT", "analog"),
                    ],
                    Analyses =
                    [
                        new AnalysisDeclaration
                        {
                            Type = BenchValueType.DCAnalysis,
                            Name = "dc",
                            Parameters = new Dictionary<string, MeasurementExpr>
                            {
                                ["start"] = new MeasurementQuantity("0V"),
                                ["stop"] = new MeasurementQuantity("1V"),
                                ["steps"] = new MeasurementNumber("2"),
                            },
                        },
                    ],
                    Measurements =
                    [
                        new MeasurementDefinition
                        {
                            Name = "QuiescentPower",
                            Unit = "W",
                            Body = [new BenchReturn(new MeasurementQuantity("0W"))],
                        },
                    ],
                },
                new BenchDefinition
                {
                    Name = "transfer_bench",
                    Terminals =
                    [
                        new BenchTerminal(BenchTerminalRole.Stim, "IN", "analog"),
                        new BenchTerminal(BenchTerminalRole.Resp, "OUT", "analog"),
                    ],
                    Analyses =
                    [
                        new AnalysisDeclaration
                        {
                            Type = BenchValueType.ACAnalysis,
                            Name = "ac",
                            Parameters = new Dictionary<string, MeasurementExpr>
                            {
                                ["space"] = new MeasurementPath("Log"),
                                ["samples"] = new MeasurementNumber("10"),
                                ["start"] = new MeasurementQuantity("1Hz"),
                                ["stop"] = new MeasurementQuantity("1kHz"),
                            },
                        },
                    ],
                    Measurements =
                    [
                        new MeasurementDefinition
                        {
                            Name = "GainBandwidth",
                            Unit = "Hz",
                            Body = [new BenchReturn(new MeasurementQuantity("0Hz"))],
                        },
                        new MeasurementDefinition
                        {
                            Name = "PassbandGain",
                            Unit = "dB",
                            Body = [new BenchReturn(new MeasurementQuantity("0dB"))],
                        },
                    ],
                },
            ],
            Circuits =
            [
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = CascodeLevel.EL,
                    Traits = ["SingleEndedOpAmp"],
                    Supplies = ["VDD"],
                    Grounds = ["GND"],
                    Ports = [],
                    Fill = new FillBlock { Devices = [] },
                },
            ],
        };
    }

    private static CascodeDocument CreateCircuitWithNets()
    {
        return new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Circuits =
            [
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = CascodeLevel.EL,
                    Supplies = ["VDD"],
                    Grounds = ["GND"],
                    Ports = [],
                    Fill = new FillBlock
                    {
                        Nets =
                        [
                            new NetDeclaration { Id = "tnode", Domain = "analog" },
                            new NetDeclaration { Id = "bias_node", Domain = "analog" },
                        ],
                        Devices = [],
                    },
                },
            ],
        };
    }
}
