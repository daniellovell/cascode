using Cascode.Workspace;

namespace Cascode.Workspace.Tests;

public class NameNormalizationTests
{
    [Fact]
    public void NameNormalizationInfersVtTags()
    {
        var lvtTags = NameNormalization.ExtractVtTags("pfet_01v8_lvt");
        Assert.Contains("LVT", lvtTags);

        var hvtTags = NameNormalization.ExtractVtTags("nfet_03v3_hvt");
        Assert.Contains("HVT", hvtTags);

        var defaultTags = NameNormalization.ExtractVtTags("pfet_01v8");
        Assert.Contains("SVT", defaultTags);
    }

    // Stdcell class detection tests
    public static TheoryData<string, DeviceClass> ClassByNameCases =>
        new()
        {
            // Inverters
            { "INVD8LVT", DeviceClass.Stdcell },
            { "INV_X1", DeviceClass.Stdcell },
            { "INVX2", DeviceClass.Stdcell },
            // Multiplexers
            { "MUX3D1LVT", DeviceClass.Stdcell },
            { "MUX2_X1", DeviceClass.Stdcell },
            { "MUX4X2", DeviceClass.Stdcell },
            // Buffers
            { "BUFF_X1", DeviceClass.Stdcell },
            { "BUFX2", DeviceClass.Stdcell },
            { "BUF_LVT", DeviceClass.Stdcell },
            // NAND gates
            { "NAND2_X1", DeviceClass.Stdcell },
            { "ND3D1LVT", DeviceClass.Stdcell },
            { "NAND4X2", DeviceClass.Stdcell },
        };

    [Theory]
    [MemberData(nameof(ClassByNameCases))]
    public void ClassifyByName_ReturnsExpectedClass(string cellName, DeviceClass expected)
    {
        var result = NameNormalization.ClassifyByName(cellName);
        Assert.Equal(expected, result);
    }

    // Device subclass detection tests
    public static TheoryData<string, DeviceSubclass> SubclassCases =>
        new()
        {
            // Stdcell subtypes
            { "INVD8LVT", DeviceSubclass.Inverter },
            { "INV_X1", DeviceSubclass.Inverter },
            { "INVX2", DeviceSubclass.Inverter },
            { "MUX3D1LVT", DeviceSubclass.Multiplexer },
            { "MUX2_X1", DeviceSubclass.Multiplexer },
            { "MUXD4", DeviceSubclass.Multiplexer },
            { "BUFF_X1", DeviceSubclass.Buffer },
            { "BUFX2", DeviceSubclass.Buffer },
            { "BUF_LVT", DeviceSubclass.Buffer },
            { "NAND2_X1", DeviceSubclass.Nand },
            { "ND3D1LVT", DeviceSubclass.Nand },
            { "NAND4X2", DeviceSubclass.Nand },
            { "NOR2_X1", DeviceSubclass.Nor },
            { "NR3D1LVT", DeviceSubclass.Nor },
            // Capacitors
            { "MIMCAP_2p0", DeviceSubclass.MIMCAP },
            { "mimcap_1p5fF", DeviceSubclass.MIMCAP },
            { "MOMCAP_2p0", DeviceSubclass.MOMCAP },
            { "momcap_1p5fF", DeviceSubclass.MOMCAP },
            // Resistors
            { "TFR_1k", DeviceSubclass.TFR },
            { "tfr_res", DeviceSubclass.TFR },
            { "RMetal_1k", DeviceSubclass.RMetal },
            { "rmetal", DeviceSubclass.RMetal },
            { "RPoly_1k", DeviceSubclass.RPoly },
            { "rpoly", DeviceSubclass.RPoly },
        };

    [Theory]
    [MemberData(nameof(SubclassCases))]
    public void ClassifySubclass_ReturnsExpectedSubclass(string cellName, DeviceSubclass expected)
    {
        var result = NameNormalization.ClassifySubclass(cellName);
        Assert.Equal(expected, result);
    }
}
