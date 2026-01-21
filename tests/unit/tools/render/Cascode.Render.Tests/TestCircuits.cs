using Cascode.ACIR;

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
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new() { Name = "IN", Type = "signal" },
                new() { Name = "OUT", Type = "signal" },
            },
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
                            ["D"] = "OUT",
                            ["G"] = "IN",
                            ["S"] = "GND",
                        },
                        Params = new Dictionary<string, string> { ["W"] = "1u", ["L"] = "100n" },
                    },
                },
            },
        };

    public static Circuit BottomDevice() =>
        new()
        {
            Name = "bottom_device",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new() { Name = "BIAS", Type = "bias" },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        Id = "M_TAIL",
                        DeviceType = "nmos",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "tail_node",
                            ["G"] = "BIAS",
                            ["S"] = "GND",
                        },
                        Params = new Dictionary<string, string> { ["W"] = "10u", ["L"] = "500n" },
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
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new() { Name = "OUT", Type = "signal" },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        Id = "M_LOAD",
                        DeviceType = "pmos",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "OUT",
                            ["G"] = "OUT",
                            ["S"] = "VDD",
                        },
                        Params = new Dictionary<string, string> { ["W"] = "5u", ["L"] = "200n" },
                    },
                },
            },
        };

    public static Circuit TwoDevices() =>
        new()
        {
            Name = "two_devices",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new() { Name = "IN_P", Type = "signal" },
                new() { Name = "IN_N", Type = "signal" },
                new() { Name = "OUT", Type = "signal" },
            },
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
                            ["D"] = "OUT",
                            ["G"] = "IN_P",
                            ["S"] = "tail",
                        },
                        Params = new Dictionary<string, string> { ["W"] = "2u", ["L"] = "100n" },
                    },
                    new()
                    {
                        Id = "M2",
                        DeviceType = "nmos",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "OUT",
                            ["G"] = "IN_N",
                            ["S"] = "tail",
                        },
                        Params = new Dictionary<string, string> { ["W"] = "2u", ["L"] = "100n" },
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
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new() { Name = "IN_P", Type = "signal" },
                new() { Name = "IN_N", Type = "signal" },
                new() { Name = "VBIAS1", Type = "bias" },
                new() { Name = "VBIAS2", Type = "bias" },
                new() { Name = "OUT_P", Type = "signal" },
                new() { Name = "OUT_N", Type = "signal" },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        Id = "M_INP",
                        DeviceType = "nmos",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "out_p_int",
                            ["G"] = "IN_P",
                            ["S"] = "tail",
                        },
                        Params = new Dictionary<string, string> { ["W"] = "2u", ["L"] = "180n" },
                    },
                    new()
                    {
                        Id = "M_INN",
                        DeviceType = "nmos",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "out_n_int",
                            ["G"] = "IN_N",
                            ["S"] = "tail",
                        },
                        Params = new Dictionary<string, string> { ["W"] = "2u", ["L"] = "180n" },
                    },
                    new()
                    {
                        Id = "M_TAIL",
                        DeviceType = "nmos",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "tail",
                            ["G"] = "VBIAS2",
                            ["S"] = "GND",
                        },
                        Params = new Dictionary<string, string> { ["W"] = "4u", ["L"] = "180n" },
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
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new() { Name = "OUT_P", Type = "signal" },
                new() { Name = "OUT_N", Type = "signal" },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        Id = "R_CMFB_P",
                        DeviceType = "resistor",
                        Bindings = new Dictionary<string, string>
                        {
                            ["P"] = "OUT_P",
                            ["N"] = "vcm_sense",
                        },
                        Params = new Dictionary<string, string> { ["R"] = "500k" },
                    },
                    new()
                    {
                        Id = "R_CMFB_N",
                        DeviceType = "resistor",
                        Bindings = new Dictionary<string, string>
                        {
                            ["P"] = "OUT_N",
                            ["N"] = "vcm_sense",
                        },
                        Params = new Dictionary<string, string> { ["R"] = "500k" },
                    },
                },
                Nets = new List<NetDeclaration>
                {
                    new() { Id = "vcm_sense", Domain = "signal" },
                },
            },
        };
}
