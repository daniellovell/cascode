using System.Collections.Generic;
using System.Text.RegularExpressions;
using Cascode.ACIR;
using Xunit;

namespace Cascode.ACIR.Tests;

public class VariantNamingTests
{
    [Fact]
    public void VariantNaming_BuildsCanonicalName_AlphabeticalOrder()
    {
        var name = VariantNaming.BuildCanonicalName(
            "Circuit",
            new Dictionary<string, string> { ["b"] = "1", ["a"] = "2" },
            new Dictionary<string, SizePack>
            {
                ["Size"] = new SizePack
                {
                    Entries = new Dictionary<string, string>
                    {
                        ["W"] = "2u",
                        ["L"] = "180n",
                        ["M"] = "1",
                    },
                },
            }
        );

        Assert.Equal("Circuit_a_2_b_1_Size_L_180n_Size_M_1_Size_W_2u", name);
    }

    [Fact]
    public void VariantNaming_UsesSIPrefixes_ForReals()
    {
        var circuit = new Circuit
        {
            Name = "RealParams",
            Level = ACIRLevel.EL,
            Parameters =
            [
                new CircuitParameter
                {
                    Name = "gain",
                    Type = "real",
                    Default = new ParamValue { Numeric = "2e-6" },
                },
            ],
        };

        var name = SpiceEmitter.GetDefaultVariantName(circuit);

        Assert.Contains("gain_2u", name);
        Assert.DoesNotContain("2e-6", name);
    }

    [Fact]
    public void VariantNaming_HashFallback_WhenExceeds64Chars()
    {
        var baseName = new string('A', 60);
        var name = VariantNaming.BuildCanonicalName(
            baseName,
            new Dictionary<string, string> { ["param"] = "1" },
            new Dictionary<string, SizePack>()
        );

        Assert.True(name.Length <= 64);
        Assert.Matches(new Regex(@"_[0-9a-f]{8}$"), name);
    }
}
