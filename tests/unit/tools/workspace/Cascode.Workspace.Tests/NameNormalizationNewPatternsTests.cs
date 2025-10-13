using Cascode.TestSupport;

namespace Cascode.Workspace.Tests;

public sealed class NameNormalizationNewPatternsTests
{
    [Fact]
    public void ClassifyByName_Nch_IsNmos()
    {
        using var cascodeHome = CascodeHome.CreateInTemp("cascode-name-nch");
        Assert.Equal(DeviceClass.Nmos, NameNormalization.ClassifyByName("mycell_nch_01v8"));
    }

    [Fact]
    public void ClassifyByName_Pch_IsPmos()
    {
        using var cascodeHome = CascodeHome.CreateInTemp("cascode-name-pch");
        Assert.Equal(DeviceClass.Pmos, NameNormalization.ClassifyByName("foo_pch_01v8"));
    }

    [Fact]
    public void Subclass_DeepNwell_OnNmos_Detected()
    {
        using var cascodeHome = CascodeHome.CreateInTemp("cascode-name-deepnwell");
        Assert.Equal(DeviceClass.Nmos, NameNormalization.ClassifyByName("nch_dnw_01v8"));
        Assert.Equal(DeviceSubclass.DeepNwell, NameNormalization.ClassifySubclass("nch_dnw_01v8"));
    }

    [Fact]
    public void Subclass_RF_OnNmos_AndPmos_Detected()
    {
        using var cascodeHome = CascodeHome.CreateInTemp("cascode-name-rf");
        Assert.Equal(DeviceSubclass.RF, NameNormalization.ClassifySubclass("nch_rf_01v8"));
        Assert.Equal(DeviceSubclass.RF, NameNormalization.ClassifySubclass("pch_rf_01v8"));
    }

    [Theory]
    [InlineData("nmoscap")]
    [InlineData("nmoscap_25")]
    [InlineData("pmoscap")]
    [InlineData("pmoscap_50")]
    public void ClassifyByName_NmoscapPmoscap_AreCapacitor_MoscapSubclass(string name)
    {
        using var cascodeHome = CascodeHome.CreateInTemp("cascode-name-moscap");
        Assert.Equal(DeviceClass.Capacitor, NameNormalization.ClassifyByName(name));
        Assert.Equal(DeviceSubclass.MOSCAP, NameNormalization.ClassifySubclass(name));
    }
}
