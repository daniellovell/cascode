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
    [Theory]
    [InlineData("INVD8LVT")]
    [InlineData("INV_X1")]
    [InlineData("INVX2")]
    public void ClassifyByName_Inverter_ReturnsStdcell(string cellName)
    {
        var result = NameNormalization.ClassifyByName(cellName);
        Assert.Equal(DeviceClass.Stdcell, result);
    }

    [Theory]
    [InlineData("MUX3D1LVT")]
    [InlineData("MUX2_X1")]
    [InlineData("MUX4X2")]
    public void ClassifyByName_Multiplexer_ReturnsStdcell(string cellName)
    {
        var result = NameNormalization.ClassifyByName(cellName);
        Assert.Equal(DeviceClass.Stdcell, result);
    }

    [Theory]
    [InlineData("BUFF_X1")]
    [InlineData("BUFX2")]
    [InlineData("BUF_LVT")]
    public void ClassifyByName_Buffer_ReturnsStdcell(string cellName)
    {
        var result = NameNormalization.ClassifyByName(cellName);
        Assert.Equal(DeviceClass.Stdcell, result);
    }

    [Theory]
    [InlineData("NAND2_X1")]
    [InlineData("ND3D1LVT")]
    [InlineData("NAND4X2")]
    public void ClassifyByName_Nand_ReturnsStdcell(string cellName)
    {
        var result = NameNormalization.ClassifyByName(cellName);
        Assert.Equal(DeviceClass.Stdcell, result);
    }

    // Stdcell subclass detection tests
    [Theory]
    [InlineData("INVD8LVT", DeviceSubclass.Inverter)]
    [InlineData("INV_X1", DeviceSubclass.Inverter)]
    [InlineData("INVX2", DeviceSubclass.Inverter)]
    public void ClassifySubclass_Inverter_ReturnsInverter(string cellName, DeviceSubclass expected)
    {
        var result = NameNormalization.ClassifySubclass(cellName);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("MUX3D1LVT", DeviceSubclass.Multiplexer)]
    [InlineData("MUX2_X1", DeviceSubclass.Multiplexer)]
    [InlineData("MUXD4", DeviceSubclass.Multiplexer)]
    public void ClassifySubclass_Multiplexer_ReturnsMultiplexer(string cellName, DeviceSubclass expected)
    {
        var result = NameNormalization.ClassifySubclass(cellName);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("BUFF_X1", DeviceSubclass.Buffer)]
    [InlineData("BUFX2", DeviceSubclass.Buffer)]
    [InlineData("BUF_LVT", DeviceSubclass.Buffer)]
    public void ClassifySubclass_Buffer_ReturnsBuffer(string cellName, DeviceSubclass expected)
    {
        var result = NameNormalization.ClassifySubclass(cellName);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("NAND2_X1", DeviceSubclass.Nand)]
    [InlineData("ND3D1LVT", DeviceSubclass.Nand)]
    [InlineData("NAND4X2", DeviceSubclass.Nand)]
    public void ClassifySubclass_Nand_ReturnsNand(string cellName, DeviceSubclass expected)
    {
        var result = NameNormalization.ClassifySubclass(cellName);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("NOR2_X1", DeviceSubclass.Nor)]
    [InlineData("NR3D1LVT", DeviceSubclass.Nor)]
    public void ClassifySubclass_Nor_ReturnsNor(string cellName, DeviceSubclass expected)
    {
        var result = NameNormalization.ClassifySubclass(cellName);
        Assert.Equal(expected, result);
    }

    // Capacitor subclass detection tests
    [Theory]
    [InlineData("MIMCAP_2p0", DeviceSubclass.MIMCAP)]
    [InlineData("mimcap_1p5fF", DeviceSubclass.MIMCAP)]
    public void ClassifySubclass_MIMCAP_ReturnsMIMCAP(string cellName, DeviceSubclass expected)
    {
        var result = NameNormalization.ClassifySubclass(cellName);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("MOMCAP_2p0", DeviceSubclass.MOMCAP)]
    [InlineData("momcap_1p5fF", DeviceSubclass.MOMCAP)]
    public void ClassifySubclass_MOMCAP_ReturnsMOMCAP(string cellName, DeviceSubclass expected)
    {
        var result = NameNormalization.ClassifySubclass(cellName);
        Assert.Equal(expected, result);
    }

    // Resistor subclass detection tests
    [Theory]
    [InlineData("TFR_1k", DeviceSubclass.TFR)]
    [InlineData("tfr_res", DeviceSubclass.TFR)]
    public void ClassifySubclass_TFR_ReturnsTFR(string cellName, DeviceSubclass expected)
    {
        var result = NameNormalization.ClassifySubclass(cellName);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("RMetal_1k", DeviceSubclass.RMetal)]
    [InlineData("rmetal", DeviceSubclass.RMetal)]
    public void ClassifySubclass_RMetal_ReturnsRMetal(string cellName, DeviceSubclass expected)
    {
        var result = NameNormalization.ClassifySubclass(cellName);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("RPoly_1k", DeviceSubclass.RPoly)]
    [InlineData("rpoly", DeviceSubclass.RPoly)]
    public void ClassifySubclass_RPoly_ReturnsRPoly(string cellName, DeviceSubclass expected)
    {
        var result = NameNormalization.ClassifySubclass(cellName);
        Assert.Equal(expected, result);
    }
}
