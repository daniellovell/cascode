using System;

namespace Cascode.Workspace.Tests;

public sealed class NameNormalizationNewPatternsTests
{
    [Fact]
    public void ClassifyByName_Nch_IsNmos()
    {
        Environment.SetEnvironmentVariable("CASCODE_HOME", null);
        Assert.Equal(DeviceClass.Nmos, NameNormalization.ClassifyByName("mycell_nch_01v8"));
    }

    [Fact]
    public void ClassifyByName_Pch_IsPmos()
    {
        Environment.SetEnvironmentVariable("CASCODE_HOME", null);
        Assert.Equal(DeviceClass.Pmos, NameNormalization.ClassifyByName("foo_pch_01v8"));
    }

    [Fact]
    public void Subclass_DeepNwell_OnNmos_Detected()
    {
        Environment.SetEnvironmentVariable("CASCODE_HOME", null);
        // Ensure parent class is NMOS (contains nch) and includes dnw token
        Assert.Equal(DeviceClass.Nmos, NameNormalization.ClassifyByName("nch_dnw_01v8"));
        Assert.Equal(DeviceSubclass.DeepNwell, NameNormalization.ClassifySubclass("nch_dnw_01v8"));
    }

    [Fact]
    public void Subclass_RF_OnNmos_AndPmos_Detected()
    {
        Environment.SetEnvironmentVariable("CASCODE_HOME", null);
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
        Environment.SetEnvironmentVariable("CASCODE_HOME", null);
        Assert.Equal(DeviceClass.Capacitor, NameNormalization.ClassifyByName(name));
        Assert.Equal(DeviceSubclass.MOSCAP, NameNormalization.ClassifySubclass(name));
    }
}
