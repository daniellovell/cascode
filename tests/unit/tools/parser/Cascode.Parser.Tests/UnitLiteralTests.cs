using System.Globalization;
using System.Linq;
using Xunit;

namespace Cascode.Parser.Tests;

/// <summary>
/// Tests for unit literal parsing (e.g., 1.8V, 100MHz, 60deg).
/// </summary>
public class UnitLiteralTests
{
    [Theory]
    [InlineData("1.8V", 1.8, "V")]
    [InlineData("1.2V", 1.2, "V")]
    [InlineData("3.3V", 3.3, "V")]
    [InlineData("100MHz", 100.0, "MHz")]
    [InlineData("60deg", 60.0, "deg")]
    [InlineData("1mW", 1.0, "mW")]
    [InlineData("2pF", 2.0, "pF")]
    [InlineData("180nm", 180.0, "nm")]
    public void Parse_SupplyWithUnitLiteral_ExtractsValueAndUnit(string literal, double expectedValue, string expectedUnit)
    {
        var text = $@"
package test;
motif Test {{
    supply VDD = {literal};
}}";

        var tree = CascodeParserFacade.Parse("test.cas", text);

        ParserTestHelpers.AssertNoParseErrors(tree);

        var motif = tree.Root.Members.OfType<MotifDeclarationSyntax>().Single();
        var supply = motif.Supplies.Single();

        Assert.Equal("VDD", supply.Name);
        Assert.NotNull(supply.Value);
        Assert.Equal(expectedValue, supply.Value!.NumericValue, precision: 10);
        Assert.Equal(expectedUnit, supply.Value.Unit);
    }

    [Theory]
    [InlineData("5", 5.0)]
    [InlineData("1.8", 1.8)]
    [InlineData("0.9", 0.9)]
    public void Parse_SupplyWithBareNumeric_ExtractsValueWithoutUnit(string literal, double expectedValue)
    {
        var text = $@"
package test;
motif Test {{
    supply VDD = {literal};
}}";

        var tree = CascodeParserFacade.Parse("test.cas", text);

        ParserTestHelpers.AssertNoParseErrors(tree);

        var motif = tree.Root.Members.OfType<MotifDeclarationSyntax>().Single();
        var supply = motif.Supplies.Single();

        Assert.Equal("VDD", supply.Name);
        Assert.NotNull(supply.Value);
        Assert.Equal(expectedValue, supply.Value!.NumericValue, precision: 10);
        Assert.Null(supply.Value.Unit);
    }

    [Fact]
    public void Parse_SupplyWithoutValue_HasNullValue()
    {
        const string text = @"
package test;
motif Test {
    supply VDD;
}";

        var tree = CascodeParserFacade.Parse("test.cas", text);

        ParserTestHelpers.AssertNoParseErrors(tree);

        var motif = tree.Root.Members.OfType<MotifDeclarationSyntax>().Single();
        var supply = motif.Supplies.Single();

        Assert.Equal("VDD", supply.Name);
        Assert.Null(supply.Value);
    }

    [Fact]
    public void Parse_OTA5TSingleEnded_ParsesWithoutErrors()
    {
        // This is the actual OTA5TSingleEnded.cas content that was previously failing
        const string text = @"package analog.ota; import lib.std.amp.*; import lib.std.prim.*;

// Five-transistor OTA (single-ended) built from DiffPair + CurrentMirror.
// Synthesizable structural motif (no spec/bench blocks here).
motif OTA5TSingleEnded implements SingleEndedAmplifier {
  supply VDD = 1.8V; ground GND;

  // Public interface (ALL CAPS ports; Diff bundle fields are P/N).
  ports [ IN: Diff, OUT: analog, VTAIL: bias ]

  use {
    // Differential pair with internal tail. Base at ground; bias is VTAIL.
    dp = new DiffPair { p=NMOS; hasTail=true } {
      IN.P -> IN.P; IN.N -> IN.N; BASE -> GND; BIAS -> VTAIL;
    };

    // PMOS current mirror as active load; rails inferred from polarity.
    cm = new CurrentMirror { p=PMOS; taps=1 };
    attach cm to dp;   // Uses CurrentMirrorLike → DiffPairLike connector

    // Single-ended output taken from the sensed branch.
    connect dp.OUT.N -> OUT;
  }
}";

        var tree = CascodeParserFacade.Parse("OTA5TSingleEnded.cas", text);

        ParserTestHelpers.AssertNoParseErrors(tree);

        // Verify supply has the correct value
        var motif = tree.Root.Members.OfType<MotifDeclarationSyntax>().Single();
        var supply = motif.Supplies.Single();

        Assert.Equal("VDD", supply.Name);
        Assert.NotNull(supply.Value);
        Assert.Equal(1.8, supply.Value!.NumericValue, precision: 10);
        Assert.Equal("V", supply.Value.Unit);
    }

    [Theory]
    [InlineData("10")]
    [InlineData("100")]
    [InlineData("1000")]
    public void Parse_SupplyWithIntegerLiteral_ExtractsValue(string literal)
    {
        var text = $@"
package test;
motif Test {{
    supply VDD = {literal};
}}";

        var tree = CascodeParserFacade.Parse("test.cas", text);

        ParserTestHelpers.AssertNoParseErrors(tree);

        var motif = tree.Root.Members.OfType<MotifDeclarationSyntax>().Single();
        var supply = motif.Supplies.Single();

        Assert.NotNull(supply.Value);
        Assert.Equal(double.Parse(literal, CultureInfo.InvariantCulture), supply.Value!.NumericValue);
        Assert.Null(supply.Value.Unit);
    }
}

