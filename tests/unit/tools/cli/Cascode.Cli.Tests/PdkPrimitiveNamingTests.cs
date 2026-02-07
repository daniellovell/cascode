using Cascode.Cli.Services;
using Xunit;

namespace Cascode.Cli.Tests;

public sealed class PdkPrimitiveNamingTests
{
    [Theory]
    [InlineData("sky130_fd_pr__nfet_01v8", "nfet_01v8")]
    [InlineData("sky130_fd_pr__nfet_01v8__model.0", "nfet_01v8")]
    [InlineData("sky130_fd_pr__model__parasitic__diode_ps2dn", "diode_ps2dn")]
    [InlineData(
        "sky130_fd_pr__rf_nfet_g5v0d10v5_bM04W5p00L0p50",
        "rf_nfet_g5v0d10v5_bM04W5p00L0p50"
    )]
    public void PrimitiveNameFromModelName_UsesStableSanitizedLeaf(
        string modelName,
        string expected
    )
    {
        var actual = PdkPrimitiveNaming.PrimitiveNameFromModelName(modelName);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("sky130_fd_pr__nfet_01v8", "nfet_01v8")]
    [InlineData("sky130_fd_pr__rf_nfet_g5v0d10v5_bM04", "rf_nfet_g5v0d10v5")]
    [InlineData("sky130_fd_pr__rf_nfet_g5v0d10v5_bM04W5p00L0p50", "rf_nfet_g5v0d10v5")]
    [InlineData("sky130_fd_pr__rf_nfet_01v8_lvt_aF02W0p42L0p15", "rf_nfet_01v8_lvt")]
    [InlineData("res_high_po_0p35", "res_high_po_0p35")]
    [InlineData("cap_vpp_04p4x04p6_m1m2_noshield", "cap_vpp_04p4x04p6_m1m2_noshield")]
    [InlineData("sky130_fd_pr__model__parasitic__diode_ps2dn", "diode_ps2dn")]
    public void PrimitiveFamilyNameFromModelName_CollapsesFixedWrapperSuffixes(
        string modelName,
        string expectedFamily
    )
    {
        var actual = PdkPrimitiveNaming.PrimitiveFamilyNameFromModelName(modelName);

        Assert.Equal(expectedFamily, actual);
    }

    [Fact]
    public void IsFamilyRepresentativeModel_ReturnsFalseForFixedVariants()
    {
        var familyRepresentative = PdkPrimitiveNaming.IsFamilyRepresentativeModel(
            "sky130_fd_pr__rf_nfet_g5v0d10v5"
        );
        var fixedVariant = PdkPrimitiveNaming.IsFamilyRepresentativeModel(
            "sky130_fd_pr__rf_nfet_g5v0d10v5_bM04W5p00L0p50"
        );

        Assert.True(familyRepresentative);
        Assert.False(fixedVariant);
    }
}
