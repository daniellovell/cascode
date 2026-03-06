using Cascode.Language;
using Cascode.Render.Analysis;
using Cascode.Render.Placement;

namespace Cascode.Render.Tests;

public class InstanceBlockTests
{
    private static Circuit MakeSubCircuit() =>
        new()
        {
            Name = "Mirror",
            Level = CascodeLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "SENSE",
                    Type = "analog",
                },
                new()
                {
                    Direction = PortDirection.Output,
                    Name = "TAP",
                    Type = "analog",
                },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        Id = "Ms",
                        DeviceType = "pmos",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "SENSE",
                            ["G"] = "SENSE",
                            ["S"] = "VDD",
                        },
                    },
                    new()
                    {
                        Id = "Mt",
                        DeviceType = "pmos",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "TAP",
                            ["G"] = "SENSE",
                            ["S"] = "VDD",
                        },
                    },
                },
            },
        };

    private static (Circuit Parent, CascodeDocument Doc) MakeParentWithNonInlineInstance()
    {
        var sub = MakeSubCircuit();
        var parent = new Circuit
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
                    Type = "signal",
                },
                new()
                {
                    Direction = PortDirection.Output,
                    Name = "OUT",
                    Type = "signal",
                },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        Id = "Mn",
                        DeviceType = "nmos",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "OUT",
                            ["G"] = "IN",
                            ["S"] = "GND",
                        },
                    },
                },
                Instances = new List<InstanceDeclaration>
                {
                    new()
                    {
                        Id = "cm",
                        Type = "Mirror",
                        Bindings = new Dictionary<string, string>
                        {
                            ["VDD"] = "VDD",
                            ["GND"] = "GND",
                            ["SENSE"] = "OUT",
                            ["TAP"] = "OUT",
                        },
                    },
                },
            },
        };

        var doc = new CascodeDocument
        {
            Circuits = new List<Circuit> { parent, sub },
        };
        return (parent, doc);
    }

    [Fact]
    public void Flatten_NonInlineInstance_CreatesSyntheticDevice()
    {
        var (parent, doc) = MakeParentWithNonInlineInstance();

        var flattened = CircuitFlattener.Flatten(parent, doc);

        Assert.True(flattened.Devices.ContainsKey("cm"));
        Assert.Equal("instance", flattened.Devices["cm"].DeviceType);
    }

    [Fact]
    public void Flatten_NonInlineInstance_ResolvesBindings()
    {
        var (parent, doc) = MakeParentWithNonInlineInstance();

        var flattened = CircuitFlattener.Flatten(parent, doc);

        var bindings = flattened.Devices["cm"].Bindings;
        Assert.Equal("VDD", bindings["VDD"]);
        Assert.Equal("GND", bindings["GND"]);
        Assert.Equal("OUT", bindings["SENSE"]);
        Assert.Equal("OUT", bindings["TAP"]);
    }

    [Fact]
    public void Flatten_NonInlineInstance_CreatesInstanceBlockInfo()
    {
        var (parent, doc) = MakeParentWithNonInlineInstance();

        var flattened = CircuitFlattener.Flatten(parent, doc);

        Assert.Single(flattened.InstanceBlocks);
        var block = flattened.InstanceBlocks[0];
        Assert.Equal("cm", block.InstanceId);
        Assert.Equal("Mirror", block.CircuitType);
        Assert.Contains("SENSE", block.SignalPortNames);
        Assert.Contains("TAP", block.SignalPortNames);
    }

    [Fact]
    public void Flatten_NonInlineInstance_SignalPortsExcludeSupplyGround()
    {
        var (parent, doc) = MakeParentWithNonInlineInstance();

        var flattened = CircuitFlattener.Flatten(parent, doc);

        var block = flattened.InstanceBlocks[0];
        Assert.DoesNotContain("VDD", block.SignalPortNames);
        Assert.DoesNotContain("GND", block.SignalPortNames);
    }

    [Fact]
    public void CircuitGraph_InstanceBlock_AppearsInDevices()
    {
        var (parent, doc) = MakeParentWithNonInlineInstance();
        var flattened = CircuitFlattener.Flatten(parent, doc);

        var graph = CircuitGraph.Build(flattened);

        Assert.True(graph.Devices.ContainsKey("cm"));
        Assert.Equal("instance", graph.Devices["cm"].DeviceType);
    }

    [Fact]
    public void CircuitGraph_InstanceBlock_RegistersNetConnections()
    {
        var (parent, doc) = MakeParentWithNonInlineInstance();
        var flattened = CircuitFlattener.Flatten(parent, doc);

        var graph = CircuitGraph.Build(flattened);

        Assert.Equal("OUT", graph.GetNetForTerminal("cm", "SENSE"));
        Assert.Equal("VDD", graph.GetNetForTerminal("cm", "VDD"));
    }

    [Fact]
    public void CircuitGraph_InstanceBlocks_Propagated()
    {
        var (parent, doc) = MakeParentWithNonInlineInstance();
        var flattened = CircuitFlattener.Flatten(parent, doc);

        var graph = CircuitGraph.Build(flattened);

        Assert.Single(graph.InstanceBlocks);
        Assert.Equal("cm", graph.InstanceBlocks[0].InstanceId);
    }

    [Fact]
    public void Topology_InstanceBlock_AssignedDifferentRowThanConnectedDevices()
    {
        var (parent, doc) = MakeParentWithNonInlineInstance();
        var flattened = CircuitFlattener.Flatten(parent, doc);
        var graph = CircuitGraph.Build(flattened);
        var topology = TopologyAnalyzer.Analyze(graph);
        var placement = CoarseGridPlacer.Place(topology, graph);

        Assert.True(placement.DevicePlacements.ContainsKey("cm"));
        Assert.True(placement.DevicePlacements.ContainsKey("Mn"));

        var cmRow = placement.DevicePlacements["cm"].Row;
        var cmCol = placement.DevicePlacements["cm"].Column;
        var mnRow = placement.DevicePlacements["Mn"].Row;
        var mnCol = placement.DevicePlacements["Mn"].Column;
        Assert.True(
            cmRow != mnRow || cmCol != mnCol,
            "Instance block and connected NMOS must not overlap the same cell."
        );
    }
}
