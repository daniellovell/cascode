using Cascode.ACIR;
using Cascode.Render.Analysis;

namespace Cascode.Render.Tests;

public class CircuitGraphTests
{
    [Fact]
    public void Build_WithSimpleInverter_CreatesCorrectConnectivity()
    {
        // Arrange
        var circuit = new Circuit
        {
            Name = "inverter",
            Level = ACIRLevel.EL,
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
                    new()
                    {
                        Id = "Mp",
                        DeviceType = "pmos",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "OUT",
                            ["G"] = "IN",
                            ["S"] = "VDD",
                        },
                    },
                },
            },
        };

        // Act
        var graph = CircuitGraph.Build(circuit);

        // Assert
        Assert.Equal(2, graph.Devices.Count);
        Assert.Contains("VDD", graph.Supplies);
        Assert.Contains("GND", graph.Grounds);
        Assert.Contains("IN", graph.InputPorts);
        Assert.Contains("OUT", graph.OutputPorts);
        Assert.Equal(4, graph.NetConnections.Count); // VDD, GND, IN, OUT
    }

    [Fact]
    public void GetNetForTerminal_ReturnsCorrectNet()
    {
        // Arrange
        var circuit = new Circuit
        {
            Name = "test",
            Level = ACIRLevel.EL,
            Ports = new List<PortDeclaration>(),
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        Id = "M1",
                        DeviceType = "nmos",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "drain_net",
                            ["G"] = "gate_net",
                            ["S"] = "source_net",
                        },
                    },
                },
            },
        };

        var graph = CircuitGraph.Build(circuit);

        // Act & Assert
        Assert.Equal("drain_net", graph.GetNetForTerminal("M1", "D"));
        Assert.Equal("gate_net", graph.GetNetForTerminal("M1", "G"));
        Assert.Equal("source_net", graph.GetNetForTerminal("M1", "S"));
        Assert.Null(graph.GetNetForTerminal("M1", "B")); // Non-existent terminal
    }

    [Fact]
    public void GetDevicesOnNet_ReturnsAllConnectedDevices()
    {
        // Arrange
        var circuit = new Circuit
        {
            Name = "test",
            Level = ACIRLevel.EL,
            Ports = new List<PortDeclaration>(),
            Fill = new FillBlock
            {
                Nets = new List<NetDeclaration>
                {
                    new() { Id = "shared", Domain = "signal" },
                    new() { Id = "other", Domain = "signal" },
                },
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        Id = "M1",
                        DeviceType = "nmos",
                        Bindings = new Dictionary<string, string> { ["D"] = "shared" },
                    },
                    new()
                    {
                        Id = "M2",
                        DeviceType = "pmos",
                        Bindings = new Dictionary<string, string> { ["D"] = "shared" },
                    },
                    new()
                    {
                        Id = "M3",
                        DeviceType = "nmos",
                        Bindings = new Dictionary<string, string> { ["D"] = "other" },
                    },
                },
            },
        };

        var graph = CircuitGraph.Build(circuit);

        // Act
        var devicesOnShared = graph.GetDevicesOnNet("shared").ToList();
        var devicesOnOther = graph.GetDevicesOnNet("other").ToList();

        // Assert
        Assert.Equal(2, devicesOnShared.Count);
        Assert.Contains(devicesOnShared, d => d.Id == "M1");
        Assert.Contains(devicesOnShared, d => d.Id == "M2");
        Assert.Single(devicesOnOther);
        Assert.Contains(devicesOnOther, d => d.Id == "M3");
    }

    [Fact]
    public void Build_IdentifiesInternalNets()
    {
        // Arrange
        var circuit = new Circuit
        {
            Name = "test",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "IN",
                    Type = "signal",
                },
            },
            Fill = new FillBlock
            {
                Nets = new List<NetDeclaration>
                {
                    new() { Id = "internal", Domain = "signal" },
                },
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        Id = "M1",
                        DeviceType = "nmos",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "internal",
                            ["G"] = "IN",
                            ["S"] = "VDD",
                        },
                    },
                },
            },
        };

        // Act
        var graph = CircuitGraph.Build(circuit);

        // Assert
        Assert.Contains("internal", graph.InternalNets);
        Assert.DoesNotContain("VDD", graph.InternalNets);
        Assert.DoesNotContain("IN", graph.InternalNets);
    }
}
