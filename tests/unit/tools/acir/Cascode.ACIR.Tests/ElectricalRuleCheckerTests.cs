using Cascode.ACIR;
using Cascode.ACIR.Validation;

namespace Cascode.ACIR.Tests;

public class ElectricalRuleCheckerTests
{
    [Fact]
    public void Check_ValidCircuit_ReturnsSuccess()
    {
        var circuit = CreateValidCircuit();
        var result = ElectricalRuleChecker.Check(circuit);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Check_FloatingGate_ReturnsERC001()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Output,
                    Name = "OUT",
                    Type = "analog",
                },
            },
            Fill = new FillBlock
            {
                Nets = new List<NetDeclaration>
                {
                    new() { Id = "floating_net", Domain = "analog" },
                },
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        DeviceType = "nmos",
                        Id = "M1",
                        Primitive = "Level1_NMOS",
                        Bindings = new Dictionary<string, string>
                        {
                            { "D", "OUT" },
                            { "G", "floating_net" }, // Not driven by anything
                            { "S", "GND" },
                            { "B", "GND" },
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string>
                            {
                                { "W", "1u" },
                                { "L", "180n" },
                                { "M", "1" },
                            },
                        },
                    },
                },
            },
        };

        var result = ElectricalRuleChecker.Check(circuit);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, e => e.Code == "ERC-001" && e.Message.Contains("M1"));
    }

    [Fact]
    public void Check_GateConnectedToPort_IsNotFloating()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "IN",
                    Type = "analog",
                },
                new()
                {
                    Direction = PortDirection.Output,
                    Name = "OUT",
                    Type = "analog",
                },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        DeviceType = "nmos",
                        Id = "M1",
                        Primitive = "Level1_NMOS",
                        Bindings = new Dictionary<string, string>
                        {
                            { "D", "OUT" },
                            { "G", "IN" }, // Connected to port - OK
                            { "S", "GND" },
                            { "B", "GND" },
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string>
                            {
                                { "W", "1u" },
                                { "L", "180n" },
                                { "M", "1" },
                            },
                        },
                    },
                },
            },
        };

        var result = ElectricalRuleChecker.Check(circuit);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Check_GateConnectedToDrain_IsNotFloating()
    {
        // Diode-connected transistor (gate tied to drain)
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Output,
                    Name = "OUT",
                    Type = "analog",
                },
            },
            Fill = new FillBlock
            {
                Nets = new List<NetDeclaration>
                {
                    new() { Id = "diode_node", Domain = "analog" },
                },
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        DeviceType = "nmos",
                        Id = "M1",
                        Primitive = "Level1_NMOS",
                        Bindings = new Dictionary<string, string>
                        {
                            { "D", "diode_node" },
                            { "G", "diode_node" }, // Diode connection - driven by its own drain
                            { "S", "GND" },
                            { "B", "GND" },
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string>
                            {
                                { "W", "1u" },
                                { "L", "180n" },
                                { "M", "1" },
                            },
                        },
                    },
                    new()
                    {
                        DeviceType = "pmos",
                        Id = "M2",
                        Primitive = "Level1_PMOS",
                        Bindings = new Dictionary<string, string>
                        {
                            { "D", "OUT" },
                            { "G", "diode_node" }, // Connected to driven net
                            { "S", "VDD" },
                            { "B", "VDD" },
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string>
                            {
                                { "W", "2u" },
                                { "L", "180n" },
                                { "M", "1" },
                            },
                        },
                    },
                },
            },
        };

        var result = ElectricalRuleChecker.Check(circuit);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Check_VddGndShort_ReturnsERC002()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "IN",
                    Type = "analog",
                },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        DeviceType = "nmos",
                        Id = "M_short",
                        Primitive = "Level1_NMOS",
                        Bindings = new Dictionary<string, string>
                        {
                            { "D", "VDD" }, // Drain to VDD
                            { "G", "IN" },
                            { "S", "GND" }, // Source to GND - SHORT!
                            { "B", "GND" },
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string>
                            {
                                { "W", "1u" },
                                { "L", "180n" },
                                { "M", "1" },
                            },
                        },
                    },
                },
            },
        };

        var result = ElectricalRuleChecker.Check(circuit);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Diagnostics,
            e => e.Code == "ERC-002" && e.Message.Contains("M_short")
        );
    }

    [Fact]
    public void Check_VddGndShortReverse_ReturnsERC002()
    {
        // Same short but with GND on drain and VDD on source
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "IN",
                    Type = "analog",
                },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        DeviceType = "pmos",
                        Id = "M_short",
                        Primitive = "Level1_PMOS",
                        Bindings = new Dictionary<string, string>
                        {
                            { "D", "GND" }, // Drain to GND
                            { "G", "IN" },
                            { "S", "VDD" }, // Source to VDD - SHORT!
                            { "B", "VDD" },
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string>
                            {
                                { "W", "1u" },
                                { "L", "180n" },
                                { "M", "1" },
                            },
                        },
                    },
                },
            },
        };

        var result = ElectricalRuleChecker.Check(circuit);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, e => e.Code == "ERC-002");
    }

    [Fact]
    public void Check_DuplicateSupply_ReturnsERC003()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD", "VDD" }, // Duplicate
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "IN",
                    Type = "analog",
                },
                new()
                {
                    Direction = PortDirection.Output,
                    Name = "OUT",
                    Type = "analog",
                },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    CreateValidNmos("M1", "OUT", "IN", "GND", "GND"),
                },
            },
        };

        var result = ElectricalRuleChecker.Check(circuit);

        Assert.Contains(result.Diagnostics, e => e.Code == "ERC-003" && e.Message.Contains("VDD"));
    }

    [Fact]
    public void Check_SupplyGroundCollision_ReturnsERC003()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VSS" },
            Grounds = new List<string> { "VSS" }, // Same name as supply
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "IN",
                    Type = "analog",
                },
                new()
                {
                    Direction = PortDirection.Output,
                    Name = "OUT",
                    Type = "analog",
                },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    CreateValidNmos("M1", "OUT", "IN", "VSS", "VSS"),
                },
            },
        };

        var result = ElectricalRuleChecker.Check(circuit);

        Assert.Contains(
            result.Diagnostics,
            e => e.Code == "ERC-003" && e.Message.Contains("both supply and ground")
        );
    }

    [Fact]
    public void Check_DanglingNet_ReturnsERC004Warning()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "IN",
                    Type = "analog",
                },
                new()
                {
                    Direction = PortDirection.Output,
                    Name = "OUT",
                    Type = "analog",
                },
            },
            Fill = new FillBlock
            {
                Nets = new List<NetDeclaration>
                {
                    new() { Id = "unused_net", Domain = "analog" }, // Not connected to anything
                },
                Devices = new List<DeviceDeclaration>
                {
                    CreateValidNmos("M1", "OUT", "IN", "GND", "GND"),
                },
            },
        };

        var result = ElectricalRuleChecker.Check(circuit);

        // Dangling net is a warning, not error
        Assert.True(result.IsValid);
        Assert.Contains(
            result.GetWarnings(),
            e => e.Code == "ERC-004" && e.Message.Contains("unused_net")
        );
    }

    [Fact]
    public void Check_NetInHarness_IsNotDangling()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "IN",
                    Type = "analog",
                },
                new()
                {
                    Direction = PortDirection.Output,
                    Name = "OUT",
                    Type = "analog",
                },
            },
            Fill = new FillBlock
            {
                Nets = new List<NetDeclaration>
                {
                    new() { Id = "harness_net", Domain = "analog" },
                },
                Devices = new List<DeviceDeclaration>
                {
                    CreateValidNmos("M1", "OUT", "IN", "GND", "GND"),
                },
            },
            Harness = new HarnessBlock
            {
                Loads = new List<LoadValue>
                {
                    new()
                    {
                        Net = "harness_net",
                        Elements = new List<LoadElement> { new LoadElement("C", "1p") },
                    },
                },
            },
        };

        var result = ElectricalRuleChecker.Check(circuit);

        // Should not report ERC-004 for harness_net
        Assert.DoesNotContain(
            result.Diagnostics,
            e => e.Code == "ERC-004" && e.Message.Contains("harness_net")
        );
    }

    [Fact]
    public void Check_MissingPdkDevice_DefaultIsWarning()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "IN",
                    Type = "analog",
                },
                new()
                {
                    Direction = PortDirection.Output,
                    Name = "OUT",
                    Type = "analog",
                },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        DeviceType = "nmos",
                        Id = "M1",
                        Primitive = "Level1_NMOS",
                        Bindings = new Dictionary<string, string>
                        {
                            { "D", "OUT" },
                            { "G", "IN" },
                            { "S", "GND" },
                            { "B", "GND" },
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string>
                            {
                                { "W", "1u" },
                                { "L", "180n" },
                                { "M", "1" },
                            },
                        },
                    },
                },
            },
        };

        var result = ElectricalRuleChecker.Check(circuit, requirePdkDevice: false);

        Assert.True(result.IsValid); // Warning, not error
        Assert.Contains(result.GetWarnings(), e => e.Code == "ERC-005");
    }

    [Fact]
    public void Check_MissingPdkDevice_RequiredIsError()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "IN",
                    Type = "analog",
                },
                new()
                {
                    Direction = PortDirection.Output,
                    Name = "OUT",
                    Type = "analog",
                },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        DeviceType = "nmos",
                        Id = "M1",
                        Primitive = "Level1_NMOS",
                        Bindings = new Dictionary<string, string>
                        {
                            { "D", "OUT" },
                            { "G", "IN" },
                            { "S", "GND" },
                            { "B", "GND" },
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string>
                            {
                                { "W", "1u" },
                                { "L", "180n" },
                                { "M", "1" },
                            },
                        },
                    },
                },
            },
        };

        var result = ElectricalRuleChecker.Check(circuit, requirePdkDevice: true);

        Assert.False(result.IsValid);
        Assert.Contains(result.GetErrors(), e => e.Code == "ERC-005");
    }

    [Fact]
    public void Check_StructurallyInvalidCircuit_ReturnsEmissionErrors()
    {
        // Circuit with missing terminals should return emission errors, not ERC errors
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "IN",
                    Type = "analog",
                },
                new()
                {
                    Direction = PortDirection.Output,
                    Name = "OUT",
                    Type = "analog",
                },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        DeviceType = "nmos",
                        Id = "M1",
                        Primitive = "Level1_NMOS",
                        Bindings = new Dictionary<string, string>
                        {
                            { "D", "OUT" },
                            // Missing G, S, B
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string>
                            {
                                { "W", "1u" },
                                { "L", "180n" },
                                { "M", "1" },
                            },
                        },
                    },
                },
            },
        };

        var result = ElectricalRuleChecker.Check(circuit);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, e => e.Code.StartsWith("EMIT-")); // Emission errors
    }

    [Fact]
    public void Check_ResistorBridgingRails_ReturnsERC007()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "IN",
                    Type = "analog",
                },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        DeviceType = "resistor",
                        Id = "R_short",
                        Primitive = "Ideal_Resistor",
                        Bindings = new Dictionary<string, string>
                        {
                            { "P", "VDD" },
                            { "N", "GND" },
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string> { { "R", "1k" } },
                        },
                    },
                },
            },
        };

        var result = ElectricalRuleChecker.Check(circuit);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Diagnostics,
            e => e.Code == "ERC-007" && e.Message.Contains("R_short")
        );
    }

    [Fact]
    public void Check_CapacitorBridgingRails_ReturnsERC007()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "IN",
                    Type = "analog",
                },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        DeviceType = "capacitor",
                        Id = "C_short",
                        Primitive = "Ideal_Capacitor",
                        Bindings = new Dictionary<string, string>
                        {
                            { "P", "GND" },
                            { "N", "VDD" }, // Reversed order, still a short
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string> { { "C", "1p" } },
                        },
                    },
                },
            },
        };

        var result = ElectricalRuleChecker.Check(circuit);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Diagnostics,
            e => e.Code == "ERC-007" && e.Message.Contains("C_short")
        );
    }

    [Fact]
    public void Check_InductorBridgingRails_ReturnsERC007()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "IN",
                    Type = "analog",
                },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        DeviceType = "inductor",
                        Id = "L_short",
                        Primitive = "Ideal_Inductor",
                        Bindings = new Dictionary<string, string>
                        {
                            { "P", "VDD" },
                            { "N", "GND" },
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string> { { "L", "1n" } },
                        },
                    },
                },
            },
        };

        var result = ElectricalRuleChecker.Check(circuit);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Diagnostics,
            e => e.Code == "ERC-007" && e.Message.Contains("L_short")
        );
    }

    [Fact]
    public void Check_ResistorNotBridgingRails_IsValid()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "IN",
                    Type = "analog",
                },
                new()
                {
                    Direction = PortDirection.Output,
                    Name = "OUT",
                    Type = "analog",
                },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        DeviceType = "resistor",
                        Id = "R_load",
                        Primitive = "Ideal_Resistor",
                        Bindings = new Dictionary<string, string>
                        {
                            { "P", "VDD" },
                            { "N", "OUT" }, // Not GND, so not a short
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string> { { "R", "10k" } },
                        },
                    },
                    CreateValidNmos("M1", "OUT", "IN", "GND", "GND"),
                },
            },
        };

        var result = ElectricalRuleChecker.Check(circuit);

        Assert.DoesNotContain(result.Diagnostics, e => e.Code == "ERC-007");
    }

    // ML-level ERC tests (topology checks work on unsized circuits)

    [Fact]
    public void Check_ML_ValidCircuit_ReturnsSuccess()
    {
        var circuit = CreateValidMLCircuit();
        var result = ElectricalRuleChecker.Check(circuit);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Check_ML_FloatingGate_ReturnsERC001()
    {
        var circuit = new Circuit
        {
            Name = "MLTestCircuit",
            Level = ACIRLevel.ML,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Output,
                    Name = "OUT",
                    Type = "analog",
                },
            },
            Fill = new FillBlock
            {
                Nets = new List<NetDeclaration>
                {
                    new() { Id = "floating_net", Domain = "analog" },
                },
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        DeviceType = "nmos",
                        Id = "M1",
                        Primitive = "Level1_NMOS",
                        Bindings = new Dictionary<string, string>
                        {
                            { "D", "OUT" },
                            { "G", "floating_net" },
                            { "S", "GND" },
                            { "B", "GND" },
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string>
                            {
                                { "W", "??" },
                                { "L", "??" },
                                { "M", "??" },
                            },
                        },
                    },
                },
            },
        };

        var result = ElectricalRuleChecker.Check(circuit);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, e => e.Code == "ERC-001" && e.Message.Contains("M1"));
    }

    [Fact]
    public void Check_ML_MissingGateBinding_ReturnsERC001()
    {
        var circuit = new Circuit
        {
            Name = "MLTestCircuit",
            Level = ACIRLevel.ML,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Output,
                    Name = "OUT",
                    Type = "analog",
                },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        DeviceType = "pmos",
                        Id = "M_SENSE",
                        Primitive = "Level1_PMOS",
                        Bindings = new Dictionary<string, string>
                        {
                            { "D", "OUT" },
                            // G is missing entirely
                            { "S", "VDD" },
                            { "B", "VDD" },
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string>
                            {
                                { "W", "??" },
                                { "L", "??" },
                                { "M", "??" },
                            },
                        },
                    },
                },
            },
        };

        var result = ElectricalRuleChecker.Check(circuit);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Diagnostics,
            e => e.Code == "ERC-001" && e.Message.Contains("Missing gate binding")
        );
        Assert.Contains(result.Diagnostics, e => e.Message.Contains("M_SENSE"));
    }

    [Fact]
    public void Check_ML_VddGndShort_ReturnsERC002()
    {
        var circuit = new Circuit
        {
            Name = "MLTestCircuit",
            Level = ACIRLevel.ML,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "IN",
                    Type = "analog",
                },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        DeviceType = "nmos",
                        Id = "M_short",
                        Primitive = "Level1_NMOS",
                        Bindings = new Dictionary<string, string>
                        {
                            { "D", "VDD" },
                            { "G", "IN" },
                            { "S", "GND" },
                            { "B", "GND" },
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string>
                            {
                                { "W", "??" },
                                { "L", "??" },
                                { "M", "??" },
                            },
                        },
                    },
                },
            },
        };

        var result = ElectricalRuleChecker.Check(circuit);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Diagnostics,
            e => e.Code == "ERC-002" && e.Message.Contains("M_short")
        );
    }

    [Fact]
    public void Check_ML_PassiveBridgingRails_ReturnsERC007()
    {
        var circuit = new Circuit
        {
            Name = "MLTestCircuit",
            Level = ACIRLevel.ML,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "IN",
                    Type = "analog",
                },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        DeviceType = "resistor",
                        Id = "R_short",
                        Primitive = "Ideal_Resistor",
                        Bindings = new Dictionary<string, string>
                        {
                            { "P", "VDD" },
                            { "N", "GND" },
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string> { { "R", "1k" } },
                        },
                    },
                },
            },
        };

        var result = ElectricalRuleChecker.Check(circuit);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Diagnostics,
            e => e.Code == "ERC-007" && e.Message.Contains("R_short")
        );
    }

    [Fact]
    public void Check_ML_NoPdkWarning_SinceMLIsPdkAgnostic()
    {
        // ML level is PDK-agnostic, so ERC-005 should not be raised
        var circuit = CreateValidMLCircuit();
        var result = ElectricalRuleChecker.Check(circuit, requirePdkDevice: false);

        Assert.True(result.IsValid);
        Assert.DoesNotContain(result.Diagnostics, e => e.Code == "ERC-005");
    }

    private static Circuit CreateValidCircuit()
    {
        return new Circuit
        {
            Name = "ValidCircuit",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "IN",
                    Type = "analog",
                },
                new()
                {
                    Direction = PortDirection.Output,
                    Name = "OUT",
                    Type = "analog",
                },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        DeviceType = "nmos",
                        Id = "M1",
                        Primitive = "Level1_NMOS",
                        Bindings = new Dictionary<string, string>
                        {
                            { "D", "OUT" },
                            { "G", "IN" },
                            { "S", "GND" },
                            { "B", "GND" },
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string>
                            {
                                { "W", "1u" },
                                { "L", "180n" },
                                { "M", "1" },
                            },
                        },
                    },
                },
            },
        };
    }

    private static Circuit CreateValidMLCircuit()
    {
        return new Circuit
        {
            Name = "ValidMLCircuit",
            Level = ACIRLevel.ML,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "IN",
                    Type = "analog",
                },
                new()
                {
                    Direction = PortDirection.Output,
                    Name = "OUT",
                    Type = "analog",
                },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        DeviceType = "nmos",
                        Id = "M1",
                        Primitive = "Level1_NMOS",
                        Bindings = new Dictionary<string, string>
                        {
                            { "D", "OUT" },
                            { "G", "IN" },
                            { "S", "GND" },
                            { "B", "GND" },
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string>
                            {
                                { "W", "??" },
                                { "L", "??" },
                                { "M", "??" },
                            },
                        },
                    },
                },
            },
        };
    }

    private static DeviceDeclaration CreateValidNmos(
        string id,
        string drain,
        string gate,
        string source,
        string bulk
    )
    {
        return new DeviceDeclaration
        {
            DeviceType = "nmos",
            Id = id,
            Primitive = "Level1_NMOS",
            Bindings = new Dictionary<string, string>
            {
                { "D", drain },
                { "G", gate },
                { "S", source },
                { "B", bulk },
            },
            Size = new SizePack
            {
                Entries = new Dictionary<string, string>
                {
                    { "W", "1u" },
                    { "L", "180n" },
                    { "M", "1" },
                },
            },
        };
    }
}
