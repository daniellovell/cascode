using System;
using Cascode.Language;

namespace Cascode.Language.Tests;

public class QuantityLiteralTests
{
    [Theory]
    [InlineData("9n", 9e-9)]
    [InlineData("10u", 10e-6)]
    [InlineData("1m", 1e-3)]
    [InlineData("2k", 2e3)]
    [InlineData("3M", 3e6)]
    [InlineData("4G", 4e9)]
    [InlineData("5T", 5e12)]
    public void ParseMagnitude_SupportedPrefixes_ReturnBaseMagnitude(string value, double expected)
    {
        var magnitude = QuantityLiteral.ParseMagnitude(value);

        Assert.Equal(expected, magnitude, 12);
    }

    [Theory]
    [InlineData("9N")]
    [InlineData("1U")]
    [InlineData("2P")]
    [InlineData("3F")]
    [InlineData("4K")]
    [InlineData("5g")]
    [InlineData("6t")]
    public void ParseMagnitude_LegacyAliases_ThrowsFormatException(string value)
    {
        var ex = Assert.Throws<FormatException>(() => QuantityLiteral.ParseMagnitude(value));

        Assert.Contains("Unrecognized unit suffix", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("20MHz", "20M", "Hz")]
    [InlineData("9nV/rtHz", "9n", "V/rtHz")]
    public void SplitValueAndUnit_SupportedQuantities_ReturnsValueAndUnit(
        string raw,
        string expectedValue,
        string expectedUnit
    )
    {
        var (value, unit) = QuantityLiteral.SplitValueAndUnit(raw);

        Assert.Equal(expectedValue, value);
        Assert.Equal(expectedUnit, unit);
    }
}
