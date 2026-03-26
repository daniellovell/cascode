using Cascode.Language;

namespace Cascode.Render.Tests;

internal static class BiasTestCircuits
{
    public static Circuit BiasFilterChain() =>
        new()
        {
            Name = "bias_filter_chain",
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
                        Id = "R_TOP",
                        DeviceType = "resistor",
                        Primitive = "ResistorIdeal",
                        Bindings = new Dictionary<string, string> { ["P"] = "VDD", ["N"] = "vref" },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string> { ["R"] = "220k" },
                        },
                    },
                    new()
                    {
                        Id = "R_BOT",
                        DeviceType = "resistor",
                        Primitive = "ResistorIdeal",
                        Bindings = new Dictionary<string, string> { ["P"] = "vref", ["N"] = "GND" },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string> { ["R"] = "470k" },
                        },
                    },
                    new()
                    {
                        Id = "R_FILTER",
                        DeviceType = "resistor",
                        Primitive = "ResistorIdeal",
                        Bindings = new Dictionary<string, string>
                        {
                            ["P"] = "vref",
                            ["N"] = "vbias",
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string> { ["R"] = "68k" },
                        },
                    },
                    new()
                    {
                        Id = "C_FILTER",
                        DeviceType = "capacitor",
                        Primitive = "CapacitorIdeal",
                        Bindings = new Dictionary<string, string>
                        {
                            ["P"] = "vbias",
                            ["N"] = "GND",
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string> { ["C"] = "10p" },
                        },
                    },
                    new()
                    {
                        Id = "M_GAIN",
                        DeviceType = "nmos",
                        Primitive = "NMOS_Level1",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "OUT",
                            ["G"] = "vbias",
                            ["S"] = "source",
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string>
                            {
                                ["W"] = "8u",
                                ["L"] = "180n",
                            },
                        },
                    },
                    new()
                    {
                        Id = "M_INPUT",
                        DeviceType = "nmos",
                        Primitive = "NMOS_Level1",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "source",
                            ["G"] = "IN",
                            ["S"] = "GND",
                        },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string>
                            {
                                ["W"] = "12u",
                                ["L"] = "180n",
                            },
                        },
                    },
                    new()
                    {
                        Id = "R_LOAD",
                        DeviceType = "resistor",
                        Primitive = "ResistorIdeal",
                        Bindings = new Dictionary<string, string> { ["P"] = "VDD", ["N"] = "OUT" },
                        Size = new SizePack
                        {
                            Entries = new Dictionary<string, string> { ["R"] = "4k" },
                        },
                    },
                },
                Nets = new List<NetDeclaration>
                {
                    new() { Id = "vref", Domain = "signal" },
                    new() { Id = "vbias", Domain = "signal" },
                    new() { Id = "source", Domain = "signal" },
                },
            },
        };
}
