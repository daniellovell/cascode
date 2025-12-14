using Cascode.ACIR;
using Cascode.ACIR.Validation;

namespace Cascode.ACIR.Tests;

public class EmissionValidatorTests
{
    [Fact]
    public void Validate_ValidELCircuit_ReturnsSuccess()
    {
        var circuit = CreateValidCircuit();
        var result = EmissionValidator.Validate(circuit);

        Assert.True(result.IsValid);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Validate_NonELCircuit_ReturnsEMIT005()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = ACIRLevel.ML
        };

        var result = EmissionValidator.Validate(circuit);

        Assert.False(result.IsValid);
        Assert.Single(result.Diagnostics);
        Assert.Equal("EMIT-005", result.Diagnostics[0].Code);
        Assert.Contains("ML", result.Diagnostics[0].Message);
    }

    [Fact]
    public void Validate_MissingGateTerminal_ReturnsEMIT001()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new() { Name = "IN", Type = "analog" },
                new() { Name = "OUT", Type = "analog" }
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        DeviceType = "nmos",
                        Id = "M1",
                        Bindings = new Dictionary<string, string>
                        {
                            { "D", "OUT" },
                            // Missing G
                            { "S", "GND" },
                            { "B", "GND" }
                        },
                        Params = new Dictionary<string, string>
                        {
                            { "W", "1u" },
                            { "L", "180n" }
                        }
                    }
                }
            }
        };

        var result = EmissionValidator.Validate(circuit);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, e => e.Code == "EMIT-001" && e.Message.Contains("'G'"));
    }

    [Fact]
    public void Validate_MissingBulkTerminal_ReturnsEMIT001()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new() { Name = "IN", Type = "analog" },
                new() { Name = "OUT", Type = "analog" }
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        DeviceType = "pmos",
                        Id = "M1",
                        Bindings = new Dictionary<string, string>
                        {
                            { "D", "OUT" },
                            { "G", "IN" },
                            { "S", "VDD" }
                            // Missing B
                        },
                        Params = new Dictionary<string, string>
                        {
                            { "W", "1u" },
                            { "L", "180n" }
                        }
                    }
                }
            }
        };

        var result = EmissionValidator.Validate(circuit);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, e => e.Code == "EMIT-001" && e.Message.Contains("'B'"));
    }

    [Fact]
    public void Validate_InvalidNetReference_ReturnsEMIT002()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new() { Name = "IN", Type = "analog" },
                new() { Name = "OUT", Type = "analog" }
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        DeviceType = "nmos",
                        Id = "M1",
                        Bindings = new Dictionary<string, string>
                        {
                            { "D", "OUT" },
                            { "G", "NONEXISTENT_NET" }, // Invalid reference
                            { "S", "GND" },
                            { "B", "GND" }
                        },
                        Params = new Dictionary<string, string>
                        {
                            { "W", "1u" },
                            { "L", "180n" }
                        }
                    }
                }
            }
        };

        var result = EmissionValidator.Validate(circuit);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, e => e.Code == "EMIT-002" && e.Message.Contains("NONEXISTENT_NET"));
    }

    [Fact]
    public void Validate_MissingWidthParam_ReturnsEMIT003()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new() { Name = "IN", Type = "analog" },
                new() { Name = "OUT", Type = "analog" }
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        DeviceType = "nmos",
                        Id = "M1",
                        Bindings = new Dictionary<string, string>
                        {
                            { "D", "OUT" },
                            { "G", "IN" },
                            { "S", "GND" },
                            { "B", "GND" }
                        },
                        Params = new Dictionary<string, string>
                        {
                            // Missing W
                            { "L", "180n" }
                        }
                    }
                }
            }
        };

        var result = EmissionValidator.Validate(circuit);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, e => e.Code == "EMIT-003" && e.Message.Contains("'W'"));
    }

    [Fact]
    public void Validate_MissingLengthParam_ReturnsEMIT003()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new() { Name = "IN", Type = "analog" },
                new() { Name = "OUT", Type = "analog" }
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        DeviceType = "pmos",
                        Id = "M1",
                        Bindings = new Dictionary<string, string>
                        {
                            { "D", "OUT" },
                            { "G", "IN" },
                            { "S", "VDD" },
                            { "B", "VDD" }
                        },
                        Params = new Dictionary<string, string>
                        {
                            { "W", "2u" }
                            // Missing L
                        }
                    }
                }
            }
        };

        var result = EmissionValidator.Validate(circuit);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, e => e.Code == "EMIT-003" && e.Message.Contains("'L'"));
    }

    [Fact]
    public void Validate_UnknownDeviceType_ReturnsEMIT004()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new() { Name = "IN", Type = "analog" },
                new() { Name = "OUT", Type = "analog" }
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        DeviceType = "unknown_device",
                        Id = "X1",
                        Bindings = new Dictionary<string, string>
                        {
                            { "A", "IN" },
                            { "B", "OUT" }
                        },
                        Params = new Dictionary<string, string>()
                    }
                }
            }
        };

        var result = EmissionValidator.Validate(circuit);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, e => e.Code == "EMIT-004" && e.Message.Contains("unknown_device"));
    }

    [Fact]
    public void Validate_Resistor_RequiresPNTerminals()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new() { Name = "OUT", Type = "analog" }
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        DeviceType = "resistor",
                        Id = "R1",
                        Bindings = new Dictionary<string, string>
                        {
                            { "P", "VDD" }
                            // Missing N
                        },
                        Params = new Dictionary<string, string>
                        {
                            { "R", "10k" }
                        }
                    }
                }
            }
        };

        var result = EmissionValidator.Validate(circuit);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, e => e.Code == "EMIT-001" && e.Message.Contains("'N'"));
    }

    [Fact]
    public void Validate_Resistor_RequiresRParam()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new() { Name = "OUT", Type = "analog" }
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        DeviceType = "resistor",
                        Id = "R1",
                        Bindings = new Dictionary<string, string>
                        {
                            { "P", "VDD" },
                            { "N", "OUT" }
                        },
                        Params = new Dictionary<string, string>()
                        // Missing R
                    }
                }
            }
        };

        var result = EmissionValidator.Validate(circuit);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, e => e.Code == "EMIT-003" && e.Message.Contains("'R'"));
    }

    [Fact]
    public void Validate_ValidResistor_Succeeds()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new() { Name = "OUT", Type = "analog" }
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        DeviceType = "resistor",
                        Id = "R1",
                        Bindings = new Dictionary<string, string>
                        {
                            { "P", "VDD" },
                            { "N", "OUT" }
                        },
                        Params = new Dictionary<string, string>
                        {
                            { "R", "10k" }
                        }
                    }
                }
            }
        };

        var result = EmissionValidator.Validate(circuit);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_InternalNetsAreValid()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new() { Name = "IN", Type = "analog" },
                new() { Name = "OUT", Type = "analog" }
            },
            Fill = new FillBlock
            {
                Nets = new List<NetDeclaration>
                {
                    new() { Id = "internal_node", Domain = "analog" }
                },
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        DeviceType = "nmos",
                        Id = "M1",
                        Bindings = new Dictionary<string, string>
                        {
                            { "D", "internal_node" }, // Using internal net
                            { "G", "IN" },
                            { "S", "GND" },
                            { "B", "GND" }
                        },
                        Params = new Dictionary<string, string>
                        {
                            { "W", "1u" },
                            { "L", "180n" }
                        }
                    }
                }
            }
        };

        var result = EmissionValidator.Validate(circuit);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_MultipleErrors_ReportsAll()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new() { Name = "IN", Type = "analog" },
                new() { Name = "OUT", Type = "analog" }
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        DeviceType = "nmos",
                        Id = "M1",
                        Bindings = new Dictionary<string, string>
                        {
                            { "D", "OUT" },
                            // Missing G
                            { "S", "INVALID_NET" }, // Invalid reference
                            // Missing B
                        },
                        Params = new Dictionary<string, string>
                        {
                            // Missing W and L
                        }
                    }
                }
            }
        };

        var result = EmissionValidator.Validate(circuit);

        Assert.False(result.IsValid);
        Assert.True(result.ErrorCount >= 4); // G, B, W, L, INVALID_NET
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
                new() { Name = "IN", Type = "analog" },
                new() { Name = "OUT", Type = "analog" }
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        DeviceType = "nmos",
                        Id = "M1",
                        Bindings = new Dictionary<string, string>
                        {
                            { "D", "OUT" },
                            { "G", "IN" },
                            { "S", "GND" },
                            { "B", "GND" }
                        },
                        Params = new Dictionary<string, string>
                        {
                            { "W", "1u" },
                            { "L", "180n" }
                        },
                        PdkDevice = "nmos"
                    }
                }
            }
        };
    }
}
