using Cascode.Language;
using Cascode.Language.Validation;

namespace Cascode.Language.Tests;

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
        var circuit = new Circuit { Name = "TestCircuit", Level = CascodeLevel.ML };

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
            Level = CascodeLevel.EL,
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
                            // Missing G
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
            Level = CascodeLevel.EL,
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
                        DeviceType = "pmos",
                        Id = "M1",
                        Primitive = "Level1_PMOS",
                        Bindings = new Dictionary<string, string>
                        {
                            { "D", "OUT" },
                            { "G", "IN" },
                            { "S", "VDD" },
                            // Missing B
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
            Level = CascodeLevel.EL,
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
                            { "G", "NONEXISTENT_NET" }, // Invalid reference
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

        var result = EmissionValidator.Validate(circuit);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Diagnostics,
            e => e.Code == "EMIT-002" && e.Message.Contains("NONEXISTENT_NET")
        );
    }

    [Fact]
    public void Validate_BundleAliasReference_ReturnsEMIT002()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = CascodeLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "IN.P",
                    Type = "analog",
                },
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "IN.N",
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
                            { "G", "IN_P" },
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

        var result = EmissionValidator.Validate(circuit);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Diagnostics,
            e =>
                e.Code == "EMIT-002"
                && e.Message.Contains("IN_P")
                && e.Message.Contains("IN.P")
                && e.Message.Contains("dot notation")
        );
    }

    [Fact]
    public void Validate_BundleMemberCaseMismatch_ReturnsEMIT002()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = CascodeLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Output,
                    Name = "OUT.P",
                    Type = "analog",
                },
                new()
                {
                    Direction = PortDirection.Output,
                    Name = "OUT.N",
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
                            { "D", "OUT.p" },
                            { "G", "OUT.N" },
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

        var result = EmissionValidator.Validate(circuit);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Diagnostics,
            e => e.Code == "EMIT-002" && e.Message.Contains("OUT.p")
        );
    }

    [Fact]
    public void Validate_MissingSizeReference_ReturnsEMIT007()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = CascodeLevel.EL,
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
                    },
                },
            },
        };

        var result = EmissionValidator.Validate(circuit);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Diagnostics,
            e => e.Code == "EMIT-007" && e.Message.Contains("size")
        );
    }

    [Fact]
    public void Validate_NamedSizePackMissingWOrL_ReturnsEMIT007()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = CascodeLevel.EL,
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
            Sizes = new List<SizeDeclaration>
            {
                new()
                {
                    Name = "Small",
                    Default = new SizePack
                    {
                        Entries = new Dictionary<string, string> { { "L", "180n" } },
                    },
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
                        SizeName = "Small",
                    },
                },
            },
        };

        var result = EmissionValidator.Validate(circuit);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Diagnostics,
            e => e.Code == "EMIT-007" && e.Message.Contains("missing required W or L")
        );
    }

    [Fact]
    public void Validate_InlineSizePackWithUnsizedValue_ReturnsEMIT007()
    {
        var circuit = CreateValidCircuit();
        var device = circuit.Fill!.Devices[0];
        device.Size!.Entries["W"] = "??";

        var result = EmissionValidator.Validate(circuit);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Diagnostics,
            e => e.Code == "EMIT-007" && e.Message.Contains("missing required W or L")
        );
    }

    [Fact]
    public void Validate_InlineSizePackMissingMultiplier_IsAllowed()
    {
        var circuit = CreateValidCircuit();
        circuit.Fill!.Devices[0].Size!.Entries.Remove("M");

        var document = new CascodeDocument
        {
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
        };

        var result = EmissionValidator.Validate(circuit, document);
        Assert.True(
            result.IsValid,
            $"Validation failed: {string.Join(", ", result.GetErrors().Select(e => e.Message))}"
        );
    }

    [Fact]
    public void Validate_InlineSizePackMissingNf_IsAllowed()
    {
        var circuit = CreateValidCircuit();

        var document = new CascodeDocument
        {
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
                        ["nf"] = "primSize.NF",
                    },
                },
            ],
        };

        var result = EmissionValidator.Validate(circuit, document);
        Assert.True(
            result.IsValid,
            $"Validation failed: {string.Join(", ", result.GetErrors().Select(e => e.Message))}"
        );
    }

    [Fact]
    public void Validate_UnknownDeviceType_ReturnsEMIT004()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = CascodeLevel.EL,
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
                        DeviceType = "unknown_device",
                        Id = "X1",
                        Bindings = new Dictionary<string, string> { { "A", "IN" }, { "B", "OUT" } },
                    },
                },
            },
        };

        var result = EmissionValidator.Validate(circuit);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Diagnostics,
            e => e.Code == "EMIT-004" && e.Message.Contains("unknown_device")
        );
    }

    [Fact]
    public void Validate_Resistor_RequiresPNTerminals()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = CascodeLevel.EL,
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
                        DeviceType = "resistor",
                        Id = "R1",
                        Primitive = "Ideal_Resistor",
                        Bindings = new Dictionary<string, string>
                        {
                            { "P", "VDD" },
                            // Missing N
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string> { { "R", "10k" } },
                        },
                    },
                },
            },
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
            Level = CascodeLevel.EL,
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
                        DeviceType = "resistor",
                        Id = "R1",
                        Primitive = "ResistorPrim",
                        Bindings = new Dictionary<string, string>
                        {
                            { "P", "VDD" },
                            { "N", "OUT" },
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string> { { "R", "10k" } },
                        },
                        // Primitive is missing R mapping.
                    },
                },
            },
        };
        var document = new CascodeDocument
        {
            Primitives = new List<PrimitiveDefinition>
            {
                new()
                {
                    Name = "ResistorPrim",
                    Kind = "resistor",
                    Device = "resistor",
                    SizeParameter = "primSize",
                    Params = new Dictionary<string, string>(),
                },
            },
        };

        var result = EmissionValidator.Validate(circuit, document);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, e => e.Code == "EMIT-003" && e.Message.Contains("'R'"));
    }

    [Fact]
    public void Validate_ValidResistor_Succeeds()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = CascodeLevel.EL,
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
                        DeviceType = "resistor",
                        Id = "R1",
                        Primitive = "ResistorPrim",
                        Bindings = new Dictionary<string, string>
                        {
                            { "P", "VDD" },
                            { "N", "OUT" },
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string> { { "R", "10k" } },
                        },
                    },
                },
            },
        };
        var document = new CascodeDocument
        {
            Primitives = new List<PrimitiveDefinition>
            {
                new()
                {
                    Name = "ResistorPrim",
                    Kind = "resistor",
                    Device = "resistor",
                    SizeParameter = "primSize",
                    Params = new Dictionary<string, string> { { "R", "primSize.R" } },
                },
            },
        };

        var result = EmissionValidator.Validate(circuit, document);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_InternalNetsAreValid()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = CascodeLevel.EL,
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
                    new() { Id = "internal_node", Domain = "analog" },
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
                            { "D", "internal_node" }, // Using internal net
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

        var result = EmissionValidator.Validate(circuit);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_MultipleErrors_ReportsAll()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = CascodeLevel.EL,
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
                            // Missing G
                            { "S", "INVALID_NET" }, // Invalid reference
                            // Missing B
                        },
                    },
                },
            },
        };

        var result = EmissionValidator.Validate(circuit);

        Assert.False(result.IsValid);
        Assert.True(result.ErrorCount >= 4); // G, B, W, L, INVALID_NET
    }

    [Fact]
    public void Validate_ELWithAutoSweep_ReturnsEMIT006()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = CascodeLevel.EL,
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
            Harness = new HarnessBlock
            {
                Sweeps = new List<SweepCondition>
                {
                    new() { Name = "InputDCBias", IsAuto = true },
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
        var result = EmissionValidator.Validate(circuit);
        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, e => e.Code == "EMIT-006");
        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public void Validate_ELWithConcreteSweep_Passes()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Level = CascodeLevel.EL,
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
            Harness = new HarnessBlock
            {
                Sweeps = new List<SweepCondition>
                {
                    new()
                    {
                        Name = "InputDCBias",
                        Start = "0.3V",
                        Stop = "1.5V",
                        Step = "100mV",
                        IsAuto = false,
                    },
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
        var result = EmissionValidator.Validate(circuit);
        Assert.True(result.IsValid);
        Assert.Empty(result.Diagnostics);
    }

    private static Circuit CreateValidCircuit()
    {
        return new Circuit
        {
            Name = "ValidCircuit",
            Level = CascodeLevel.EL,
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
}
