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
                        Primitive = "NMOS_Level1",
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
                        Primitive = "NMOS_Level1",
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
                        Primitive = "PMOS_Level1",
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
                        Primitive = "NMOS_Level1",
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
                        Primitive = "NMOS_Level1",
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
                        Primitive = "NMOS_Level1",
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
                        Primitive = "NMOS_Level1",
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
                        Primitive = "NMOS_Level1",
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

    public static Circuit CurrentMirrorPair() =>
        new()
        {
            Name = "current_mirror_pair",
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
                        Id = "M_REF",
                        DeviceType = "pmos",
                        Primitive = "PMOS_Level1",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "vref",
                            ["G"] = "vref",
                            ["S"] = "VDD",
                        },
                    },
                    new()
                    {
                        Id = "M_OUT",
                        DeviceType = "pmos",
                        Primitive = "PMOS_Level1",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "OUT",
                            ["G"] = "vref",
                            ["S"] = "VDD",
                        },
                    },
                },
                Nets = new List<NetDeclaration>
                {
                    new() { Id = "vref", Domain = "signal" },
                },
            },
        };

    public static Circuit SharedGateDifferentSourcePair() =>
        new()
        {
            Name = "shared_gate_different_source_pair",
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
                        Id = "M_REF",
                        DeviceType = "pmos",
                        Primitive = "PMOS_Level1",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "vref",
                            ["G"] = "vref",
                            ["S"] = "VDD",
                        },
                    },
                    new()
                    {
                        Id = "M_OUT",
                        DeviceType = "pmos",
                        Primitive = "PMOS_Level1",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "OUT",
                            ["G"] = "vref",
                            ["S"] = "vsrc2",
                        },
                    },
                },
                Nets = new List<NetDeclaration>
                {
                    new() { Id = "vref", Domain = "signal" },
                    new() { Id = "vsrc2", Domain = "signal" },
                },
            },
        };

    public static Circuit DrainSourceConnectedPair() =>
        new()
        {
            Name = "drain_source_connected_pair",
            Level = CascodeLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>(),
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        Id = "M_TOP",
                        DeviceType = "pmos",
                        Primitive = "PMOS_Level1",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "nmid",
                            ["G"] = "vg_top",
                            ["S"] = "VDD",
                        },
                    },
                    new()
                    {
                        Id = "M_BOT",
                        DeviceType = "nmos",
                        Primitive = "NMOS_Level1",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "nout",
                            ["G"] = "vg_bot",
                            ["S"] = "nmid",
                        },
                    },
                },
                Nets = new List<NetDeclaration>
                {
                    new() { Id = "nmid", Domain = "signal" },
                    new() { Id = "nout", Domain = "signal" },
                    new() { Id = "vg_top", Domain = "signal" },
                    new() { Id = "vg_bot", Domain = "signal" },
                },
            },
        };

    public static Circuit DrainDrainConnectedPair() =>
        new()
        {
            Name = "drain_drain_connected_pair",
            Level = CascodeLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>(),
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        Id = "M_LEFT",
                        DeviceType = "pmos",
                        Primitive = "PMOS_Level1",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "nshared",
                            ["G"] = "vg_left",
                            ["S"] = "VDD",
                        },
                    },
                    new()
                    {
                        Id = "M_RIGHT",
                        DeviceType = "nmos",
                        Primitive = "NMOS_Level1",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "nshared",
                            ["G"] = "vg_right",
                            ["S"] = "GND",
                        },
                    },
                },
                Nets = new List<NetDeclaration>
                {
                    new() { Id = "nshared", Domain = "signal" },
                    new() { Id = "vg_left", Domain = "signal" },
                    new() { Id = "vg_right", Domain = "signal" },
                },
            },
        };

    public static Circuit SourceSourceConnectedPair() =>
        new()
        {
            Name = "source_source_connected_pair",
            Level = CascodeLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>(),
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        Id = "M_LEFT",
                        DeviceType = "pmos",
                        Primitive = "PMOS_Level1",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "nout_left",
                            ["G"] = "vg_left",
                            ["S"] = "nshared",
                        },
                    },
                    new()
                    {
                        Id = "M_RIGHT",
                        DeviceType = "nmos",
                        Primitive = "NMOS_Level1",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "nout_right",
                            ["G"] = "vg_right",
                            ["S"] = "nshared",
                        },
                    },
                },
                Nets = new List<NetDeclaration>
                {
                    new() { Id = "nshared", Domain = "signal" },
                    new() { Id = "nout_left", Domain = "signal" },
                    new() { Id = "nout_right", Domain = "signal" },
                    new() { Id = "vg_left", Domain = "signal" },
                    new() { Id = "vg_right", Domain = "signal" },
                },
            },
        };

    public static Circuit SharedGatePair() =>
        new()
        {
            Name = "shared_gate_pair",
            Level = CascodeLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>(),
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        Id = "M_LEFT",
                        DeviceType = "pmos",
                        Primitive = "PMOS_Level1",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "nout_l",
                            ["G"] = "vg_shared",
                            ["S"] = "VDD",
                        },
                    },
                    new()
                    {
                        Id = "M_RIGHT",
                        DeviceType = "pmos",
                        Primitive = "PMOS_Level1",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "nout_r",
                            ["G"] = "vg_shared",
                            ["S"] = "vsrc_r",
                        },
                    },
                },
                Nets = new List<NetDeclaration>
                {
                    new() { Id = "nout_l", Domain = "signal" },
                    new() { Id = "nout_r", Domain = "signal" },
                    new() { Id = "vg_shared", Domain = "signal" },
                    new() { Id = "vsrc_r", Domain = "signal" },
                },
            },
        };

    public static Circuit DrainSourceConnectionOverridesSharedGatePair() =>
        new()
        {
            Name = "drain_source_overrides_shared_gate_pair",
            Level = CascodeLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>(),
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        Id = "M_TOP",
                        DeviceType = "pmos",
                        Primitive = "PMOS_Level1",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "nmid",
                            ["G"] = "vg_shared",
                            ["S"] = "VDD",
                        },
                    },
                    new()
                    {
                        Id = "M_BOT",
                        DeviceType = "nmos",
                        Primitive = "NMOS_Level1",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "nout",
                            ["G"] = "vg_shared",
                            ["S"] = "nmid",
                        },
                    },
                },
                Nets = new List<NetDeclaration>
                {
                    new() { Id = "nmid", Domain = "signal" },
                    new() { Id = "nout", Domain = "signal" },
                    new() { Id = "vg_shared", Domain = "signal" },
                },
            },
        };

    public static Circuit TwoIndependentDevices() =>
        new()
        {
            Name = "two_independent_devices",
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
                        Id = "M_IN",
                        DeviceType = "nmos",
                        Primitive = "NMOS_Level1",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "nint",
                            ["G"] = "IN",
                            ["S"] = "GND",
                        },
                    },
                    new()
                    {
                        Id = "R_LOAD",
                        DeviceType = "resistor",
                        Primitive = "ResistorIdeal",
                        Bindings = new Dictionary<string, string> { ["P"] = "VDD", ["N"] = "OUT" },
                    },
                },
                Nets = new List<NetDeclaration>
                {
                    new() { Id = "nint", Domain = "signal" },
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
                        Primitive = "ResistorIdeal",
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
                        Primitive = "ResistorIdeal",
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
                        Primitive = "ResistorIdeal",
                        Bindings = new Dictionary<string, string> { ["P"] = "IN", ["N"] = "OUT" },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string> { ["R"] = "4.5k" },
                        },
                    },
                    new()
                    {
                        Id = "C1",
                        DeviceType = "capacitor",
                        Primitive = "CapacitorIdeal",
                        Bindings = new Dictionary<string, string> { ["P"] = "OUT", ["N"] = "GND" },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string> { ["C"] = "10n" },
                        },
                    },
                },
            },
        };

    public static Circuit SameFlavorDrainSourceChainWithCompetingGateSides() =>
        new()
        {
            Name = "same_flavor_ds_chain",
            Level = CascodeLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "VBIAS_LEFT",
                    Type = "bias",
                },
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "VBIAS_RIGHT",
                    Type = "bias",
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
                        Id = "M_BOT",
                        DeviceType = "nmos",
                        Primitive = "NMOS_Level1",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "cas",
                            ["G"] = "vg_left",
                            ["S"] = "GND",
                        },
                    },
                    new()
                    {
                        Id = "M_TOP",
                        DeviceType = "nmos",
                        Primitive = "NMOS_Level1",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "OUT",
                            ["G"] = "vg_right",
                            ["S"] = "cas",
                        },
                    },
                    new()
                    {
                        Id = "R_LEFT",
                        DeviceType = "resistor",
                        Primitive = "ResistorIdeal",
                        Bindings = new Dictionary<string, string>
                        {
                            ["P"] = "VBIAS_LEFT",
                            ["N"] = "vg_left",
                        },
                    },
                    new()
                    {
                        Id = "R_RIGHT",
                        DeviceType = "resistor",
                        Primitive = "ResistorIdeal",
                        Bindings = new Dictionary<string, string>
                        {
                            ["P"] = "vg_right",
                            ["N"] = "VBIAS_RIGHT",
                        },
                    },
                },
                Nets = new List<NetDeclaration>
                {
                    new() { Id = "cas", Domain = "signal" },
                    new() { Id = "vg_left", Domain = "signal" },
                    new() { Id = "vg_right", Domain = "signal" },
                },
            },
        };

    public static Circuit DrainSourceNetWithThirdPropagation() =>
        new()
        {
            Name = "drain_source_with_third_propagation",
            Level = CascodeLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "VG_LEFT",
                    Type = "signal",
                },
                new()
                {
                    Direction = PortDirection.Output,
                    Name = "VG_RIGHT",
                    Type = "signal",
                },
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "VG_AUX",
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
                        Id = "M_BOT",
                        DeviceType = "nmos",
                        Primitive = "NMOS_Level1",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "cas",
                            ["G"] = "VG_LEFT",
                            ["S"] = "GND",
                        },
                    },
                    new()
                    {
                        Id = "M_TOP",
                        DeviceType = "nmos",
                        Primitive = "NMOS_Level1",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "OUT",
                            ["G"] = "VG_RIGHT",
                            ["S"] = "cas",
                        },
                    },
                    new()
                    {
                        Id = "M_AUX",
                        DeviceType = "nmos",
                        Primitive = "NMOS_Level1",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "cas",
                            ["G"] = "VG_AUX",
                            ["S"] = "GND",
                        },
                    },
                },
                Nets = new List<NetDeclaration>
                {
                    new() { Id = "cas", Domain = "signal" },
                },
            },
        };

    public static Circuit SameFlavorPmosDrainSourceChainWithCompetingGateSides() =>
        new()
        {
            Name = "same_flavor_pmos_ds_chain",
            Level = CascodeLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "VBIAS_LEFT",
                    Type = "bias",
                },
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "VBIAS_RIGHT",
                    Type = "bias",
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
                        Id = "M_TOP",
                        DeviceType = "pmos",
                        Primitive = "PMOS_Level1",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "cas",
                            ["G"] = "vg_right",
                            ["S"] = "VDD",
                        },
                    },
                    new()
                    {
                        Id = "M_BOT",
                        DeviceType = "pmos",
                        Primitive = "PMOS_Level1",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "OUT",
                            ["G"] = "vg_left",
                            ["S"] = "cas",
                        },
                    },
                    new()
                    {
                        Id = "R_LEFT",
                        DeviceType = "resistor",
                        Primitive = "ResistorIdeal",
                        Bindings = new Dictionary<string, string>
                        {
                            ["P"] = "VBIAS_LEFT",
                            ["N"] = "vg_left",
                        },
                    },
                    new()
                    {
                        Id = "R_RIGHT",
                        DeviceType = "resistor",
                        Primitive = "ResistorIdeal",
                        Bindings = new Dictionary<string, string>
                        {
                            ["P"] = "vg_right",
                            ["N"] = "VBIAS_RIGHT",
                        },
                    },
                },
                Nets = new List<NetDeclaration>
                {
                    new() { Id = "cas", Domain = "signal" },
                    new() { Id = "vg_left", Domain = "signal" },
                    new() { Id = "vg_right", Domain = "signal" },
                },
            },
        };
}
