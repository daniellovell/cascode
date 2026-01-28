using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Cascode.ACIR;
using Cascode.ACIR.Json;
using Cascode.TestSupport;

namespace Cascode.ACIR.Tests;

public class AcirJsonConverterRoundTripTests
{
    [Fact]
    public void RoundTrip_PreservesCircuitName()
    {
        var original = CreateSimpleElCircuit();

        var json = AcirJsonConverter.ToJson(original);
        var result = AcirJsonConverter.FromJson(json);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var roundTripped = result.Document!;

        Assert.Equal(original.Circuits[0].Name, roundTripped.Circuits[0].Name);
    }

    [Fact]
    public void RoundTrip_PreservesPortDirection()
    {
        var original = CreateSimpleElCircuit();

        var json = AcirJsonConverter.ToJson(original);
        var result = AcirJsonConverter.FromJson(json);
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

        var json = AcirJsonConverter.ToJson(original);
        var result = AcirJsonConverter.FromJson(json);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var roundTripped = result.Document!;

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
        var result = AcirJsonConverter.FromJson(json);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var roundTripped = result.Document!;

        var originalConstraint = original.Circuits[0].Constraints!.Numeric[0];
        var roundTrippedConstraint = roundTripped.Circuits[0].Constraints!.Numeric[0];

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
        var result = AcirJsonConverter.FromJson(json);
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

        var json = AcirJsonConverter.ToJson(original);
        var result = AcirJsonConverter.FromJson(json);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var roundTripped = result.Document!;

        Assert.Equal(2, roundTripped.BenchDefinitions.Count);
        Assert.Equal("DCBench", roundTripped.BenchDefinitions[0].Name);
        Assert.Equal("ACBench", roundTripped.BenchDefinitions[1].Name);
    }

    [Fact]
    public void ToJson_WithBenchDefinitions_EmitsBenchDefinitionsArray()
    {
        var doc = CreateCircuitWithBenches();

        var json = AcirJsonConverter.ToJson(doc);
        var parsed = JsonDocument.Parse(json);

        var benches = parsed.RootElement.GetProperty("benchDefinitions");
        Assert.Equal(2, benches.GetArrayLength());
        Assert.Equal("DCBench", benches[0].GetProperty("name").GetString());
        Assert.Equal("ACBench", benches[1].GetProperty("name").GetString());
    }

    [Fact]
    public void RoundTrip_WithNets_PreservesNets()
    {
        var original = CreateCircuitWithNets();

        var json = AcirJsonConverter.ToJson(original);
        var result = AcirJsonConverter.FromJson(json);
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
        var acirPath = Path.Combine(
            repoRoot,
            "tests/golden/acir/hierarchy/TelescopicCascodeFullyDiff_Attach.el.cir"
        );

        ACIRDocument doc;
        using (var reader = File.OpenText(acirPath))
        {
            doc = ACIRReader.Read(reader, acirPath);
        }

        var json = AcirJsonConverter.ToJson(doc, "TelescopicCascodeFullyDiff_Attach");
        var result = AcirJsonConverter.FromJson(json);
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

        var json = AcirJsonConverter.ToJson(doc);
        var parsed = JsonDocument.Parse(json);

        var nets = parsed.RootElement.GetProperty("nets");
        Assert.Equal(2, nets.GetArrayLength());
        Assert.Equal("tnode", nets[0].GetProperty("name").GetString());
        Assert.Equal("analog", nets[0].GetProperty("kind").GetString());
        Assert.Equal("bias_node", nets[1].GetProperty("name").GetString());
        Assert.Equal("analog", nets[1].GetProperty("kind").GetString());
    }

    private static ACIRDocument CreateSimpleElCircuit()
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
                                    },
                                },
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

    private static ACIRDocument CreateCircuitWithBenches()
    {
        return new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            BenchDefinitions =
            [
                new BenchDefinition
                {
                    Name = "DCBench",
                    Trait = "SingleEndedOpAmp",
                    Builtin = "SEOpAmpDCBench",
                    Outputs = ["QuiescentPower"],
                },
                new BenchDefinition
                {
                    Name = "ACBench",
                    Trait = "SingleEndedOpAmp",
                    Builtin = "SEOpAmpACBench",
                    Outputs = ["GainBandwidth", "PassbandGain"],
                },
            ],
            Circuits =
            [
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = ACIRLevel.EL,
                    Traits = ["SingleEndedOpAmp"],
                    Supplies = ["VDD"],
                    Grounds = ["GND"],
                    Ports = [],
                    Fill = new FillBlock { Devices = [] },
                },
            ],
        };
    }

    private static ACIRDocument CreateCircuitWithNets()
    {
        return new ACIRDocument
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
