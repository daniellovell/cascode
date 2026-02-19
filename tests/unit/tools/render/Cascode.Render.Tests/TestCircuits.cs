using Cascode.Language;

namespace Cascode.Render.Tests;

/// <summary>
/// Factory methods for creating test circuit fixtures used across render tests.
/// </summary>
internal static class TestCircuits
{
    public static Circuit SimpleCircuit() =>
        new()
        {
            Name = "simple",
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
                        Id = "M1",
                        DeviceType = "nmos",
                        Primitive = "Level1_NMOS",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "OUT",
                            ["G"] = "IN",
                            ["S"] = "GND",
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string>
                            {
                                ["W"] = "1u",
                                ["L"] = "100n",
                            },
                        },
                    },
                },
            },
        };

    public static Circuit BottomDevice() =>
        new()
        {
            Name = "bottom_device",
            Level = CascodeLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "BIAS",
                    Type = "bias",
                },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        Id = "M_TAIL",
                        DeviceType = "nmos",
                        Primitive = "Level1_NMOS",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "tail_node",
                            ["G"] = "BIAS",
                            ["S"] = "GND",
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string>
                            {
                                ["W"] = "10u",
                                ["L"] = "500n",
                            },
                        },
                    },
                },
                Nets = new List<NetDeclaration>
                {
                    new() { Id = "tail_node", Domain = "signal" },
                },
            },
        };

    public static Circuit TopDevice() =>
        new()
        {
            Name = "top_device",
            Level = CascodeLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
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
                        Id = "M_LOAD",
                        DeviceType = "pmos",
                        Primitive = "Level1_PMOS",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "OUT",
                            ["G"] = "OUT",
                            ["S"] = "VDD",
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string>
                            {
                                ["W"] = "5u",
                                ["L"] = "200n",
                            },
                        },
                    },
                },
            },
        };

    public static Circuit TwoDevices() =>
        new()
        {
            Name = "two_devices",
            Level = CascodeLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "IN.P",
                    Type = "signal",
                },
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "IN.N",
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
                        Id = "M1",
                        DeviceType = "nmos",
                        Primitive = "Level1_NMOS",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "OUT",
                            ["G"] = "IN.P",
                            ["S"] = "tail",
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string>
                            {
                                ["W"] = "2u",
                                ["L"] = "100n",
                            },
                        },
                    },
                    new()
                    {
                        Id = "M2",
                        DeviceType = "nmos",
                        Primitive = "Level1_NMOS",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "OUT",
                            ["G"] = "IN.N",
                            ["S"] = "tail",
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string>
                            {
                                ["W"] = "2u",
                                ["L"] = "100n",
                            },
                        },
                    },
                },
                Nets = new List<NetDeclaration>
                {
                    new() { Id = "tail", Domain = "signal" },
                },
            },
        };

    public static Circuit FullyDiffOtaWithTwoBiasPorts() =>
        new()
        {
            Name = "fully_diff_ota",
            Level = CascodeLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "IN.P",
                    Type = "signal",
                },
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "IN.N",
                    Type = "signal",
                },
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "VBIAS1",
                    Type = "bias",
                },
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "VBIAS2",
                    Type = "bias",
                },
                new()
                {
                    Direction = PortDirection.Output,
                    Name = "OUT_P",
                    Type = "signal",
                },
                new()
                {
                    Direction = PortDirection.Output,
                    Name = "OUT_N",
                    Type = "signal",
                },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        Id = "M_INP",
                        DeviceType = "nmos",
                        Primitive = "Level1_NMOS",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "out_p_int",
                            ["G"] = "IN.P",
                            ["S"] = "tail",
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string>
                            {
                                ["W"] = "2u",
                                ["L"] = "180n",
                            },
                        },
                    },
                    new()
                    {
                        Id = "M_INN",
                        DeviceType = "nmos",
                        Primitive = "Level1_NMOS",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "out_n_int",
                            ["G"] = "IN.N",
                            ["S"] = "tail",
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string>
                            {
                                ["W"] = "2u",
                                ["L"] = "180n",
                            },
                        },
                    },
                    new()
                    {
                        Id = "M_TAIL",
                        DeviceType = "nmos",
                        Primitive = "Level1_NMOS",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "tail",
                            ["G"] = "VBIAS2",
                            ["S"] = "GND",
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string>
                            {
                                ["W"] = "4u",
                                ["L"] = "180n",
                            },
                        },
                    },
                },
                Nets = new List<NetDeclaration>
                {
                    new() { Id = "tail", Domain = "signal" },
                    new() { Id = "out_p_int", Domain = "signal" },
                    new() { Id = "out_n_int", Domain = "signal" },
                },
            },
        };

    public static Circuit CmfbResistors() =>
        new()
        {
            Name = "cmfb_resistors",
            Level = CascodeLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Output,
                    Name = "OUT_P",
                    Type = "signal",
                },
                new()
                {
                    Direction = PortDirection.Output,
                    Name = "OUT_N",
                    Type = "signal",
                },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        Id = "R_CMFB_P",
                        DeviceType = "resistor",
                        Primitive = "Ideal_Resistor",
                        Bindings = new Dictionary<string, string>
                        {
                            ["P"] = "OUT_P",
                            ["N"] = "vcm_sense",
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string> { ["R"] = "500k" },
                        },
                    },
                    new()
                    {
                        Id = "R_CMFB_N",
                        DeviceType = "resistor",
                        Primitive = "Ideal_Resistor",
                        Bindings = new Dictionary<string, string>
                        {
                            ["P"] = "OUT_N",
                            ["N"] = "vcm_sense",
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string> { ["R"] = "500k" },
                        },
                    },
                },
                Nets = new List<NetDeclaration>
                {
                    new() { Id = "vcm_sense", Domain = "signal" },
                },
            },
        };

    /// <summary>
    /// RC lowpass filter: two passive devices (R1, C1) with no symmetric groups.
    /// Matches the topology of a typical sample.cas file.
    /// </summary>
    public static Circuit RcLowpass() =>
        new()
        {
            Name = "rc_lowpass",
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
                        Id = "R1",
                        DeviceType = "resistor",
                        Primitive = "Ideal_Resistor",
                        Bindings = new Dictionary<string, string>
                        {
                            ["P"] = "IN",
                            ["N"] = "OUT",
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string> { ["R"] = "4.5k" },
                        },
                    },
                    new()
                    {
                        Id = "C1",
                        DeviceType = "capacitor",
                        Primitive = "Ideal_Capacitor",
                        Bindings = new Dictionary<string, string>
                        {
                            ["P"] = "OUT",
                            ["N"] = "GND",
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string> { ["C"] = "10n" },
                        },
                    },
                },
            },
        };
}
