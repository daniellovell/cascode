using System.Collections.Generic;
using Cascode.Language;

namespace Cascode.Language.Tests;

public static class TestFixtures
{
    public static CascodeDocument CreateSimpleElCircuit(HarnessBlock? harness = null)
    {
        return new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Primitives = [TestPrimitives.GetLevel1Nmos()],
            Circuits =
            [
                new Circuit
                {
                    Name = "OTA5T",
                    Level = CascodeLevel.EL,
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
                                Primitive = "NMOS_Level1",
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
                                        ["M"] = "1",
                                    },
                                },
                            },
                        ],
                    },
                    Harness = harness,
                },
            ],
        };
    }

    public static CascodeDocument CreateCircuitWithHarness()
    {
        return CreateSimpleElCircuit(
            new HarnessBlock
            {
                Supplies = [new SupplyValue { Net = "VDD", Value = "1.8V" }],
                Loads = [new LoadValue { Net = "OUT", Elements = [new LoadElement("C", "1pF")] }],
            }
        );
    }
}
