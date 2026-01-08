using Cascode.ACIR;

namespace Cascode.ACIR.Tests;

public class ParamValueParserTests
{
    [Fact]
    public void Parse_SymbolicValue_ReturnsSymbolic()
    {
        var result = ParamValueParser.Parse("$ratio");

        Assert.NotNull(result.Symbolic);
        Assert.Equal("$ratio", result.Symbolic);
        Assert.Null(result.Numeric);
        Assert.Null(result.Literal);
    }

    [Fact]
    public void Parse_SymbolicWithUnderscore_ReturnsSymbolic()
    {
        var result = ParamValueParser.Parse("$my_param");

        Assert.NotNull(result.Symbolic);
        Assert.Equal("$my_param", result.Symbolic);
        Assert.Null(result.Numeric);
        Assert.Null(result.Literal);
    }

    [Theory]
    [InlineData("10k")]
    [InlineData("-1.5M")]
    [InlineData("180n")]
    [InlineData("2u")]
    [InlineData("1.8")]
    [InlineData("100")]
    [InlineData("-5")]
    [InlineData("3.14")]
    [InlineData("1pF")]
    [InlineData("10MOhm")]
    [InlineData("500fF")]
    public void Parse_NumericValues_ReturnsNumeric(string value)
    {
        var result = ParamValueParser.Parse(value);

        Assert.NotNull(result.Numeric);
        Assert.Equal(value, result.Numeric);
        Assert.Null(result.Symbolic);
        Assert.Null(result.Literal);
    }

    [Theory]
    [InlineData("res1stor")]
    [InlineData("nmos2")]
    [InlineData("high_z")]
    [InlineData("sky130_fd_pr__nfet_01v8")]
    [InlineData("auto")]
    [InlineData("MyDevice")]
    public void Parse_LiteralValues_ReturnsLiteral(string value)
    {
        var result = ParamValueParser.Parse(value);

        Assert.NotNull(result.Literal);
        Assert.Equal(value, result.Literal);
        Assert.Null(result.Symbolic);
        Assert.Null(result.Numeric);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsLiteral()
    {
        var result = ParamValueParser.Parse("");

        Assert.NotNull(result.Literal);
        Assert.Equal("", result.Literal);
        Assert.Null(result.Symbolic);
        Assert.Null(result.Numeric);
    }

    [Theory]
    [InlineData("10k", true)]
    [InlineData("-1.5M", true)]
    [InlineData("res1stor", false)]
    [InlineData("nmos2", false)]
    [InlineData("device1", false)]
    public void Parse_CorrectlyDistinguishesNumericFromLiteral(string value, bool shouldBeNumeric)
    {
        var result = ParamValueParser.Parse(value);

        if (shouldBeNumeric)
        {
            Assert.NotNull(result.Numeric);
            Assert.Null(result.Literal);
        }
        else
        {
            Assert.NotNull(result.Literal);
            Assert.Null(result.Numeric);
        }
    }

    [Theory]
    [InlineData("1.5V")]
    [InlineData("100mV")]
    [InlineData("1.8GHz")]
    [InlineData("50Ohm")]
    public void Parse_NumericWithUnits_ReturnsNumeric(string value)
    {
        var result = ParamValueParser.Parse(value);

        Assert.NotNull(result.Numeric);
        Assert.Equal(value, result.Numeric);
        Assert.Null(result.Symbolic);
        Assert.Null(result.Literal);
    }

    [Fact]
    public void Parse_DecimalWithoutInteger_ReturnsLiteral()
    {
        // ".5" should be treated as literal since it doesn't match the pattern
        var result = ParamValueParser.Parse(".5");

        Assert.NotNull(result.Literal);
        Assert.Null(result.Numeric);
        Assert.Null(result.Symbolic);
    }

    [Fact]
    public void Parse_LeadingZeroDecimal_ReturnsNumeric()
    {
        var result = ParamValueParser.Parse("0.5");

        Assert.NotNull(result.Numeric);
        Assert.Null(result.Literal);
        Assert.Null(result.Symbolic);
    }

    [Fact]
    public void Parse_NumberWithUnusualUnit_ReturnsNumeric()
    {
        // "10x" - even with non-standard unit, if it starts with digit it's numeric
        var result = ParamValueParser.Parse("10x");

        Assert.NotNull(result.Numeric);
        Assert.Null(result.Literal);
        Assert.Null(result.Symbolic);
    }
}
