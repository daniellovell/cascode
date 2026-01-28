using Cascode.ACIR;

namespace Cascode.ACIR.Tests;

public class ParameterEvaluatorTests
{
    [Fact]
    public void Evaluate_NumericLiteral_ReturnsUnchanged()
    {
        var bindings = new Dictionary<string, string>();

        var result = ParameterEvaluator.Evaluate("100", bindings);

        Assert.Equal("100", result);
    }

    [Fact]
    public void Evaluate_NumericWithSIPrefix_ReturnsUnchanged()
    {
        var bindings = new Dictionary<string, string>();

        var result = ParameterEvaluator.Evaluate("2u", bindings);

        Assert.Equal("2u", result);
    }

    [Fact]
    public void Evaluate_SymbolicReference_Substitutes()
    {
        var bindings = new Dictionary<string, string> { ["width"] = "1u" };

        var result = ParameterEvaluator.Evaluate("width", bindings);

        Assert.Equal("1u", result);
    }

    [Fact]
    public void Evaluate_Multiplication_Computes()
    {
        var bindings = new Dictionary<string, string> { ["W_input"] = "1u" };

        var result = ParameterEvaluator.Evaluate("W_input*2", bindings);

        Assert.Equal("2u", result);
    }

    [Fact]
    public void Evaluate_Division_Computes()
    {
        var bindings = new Dictionary<string, string> { ["W_input"] = "4u" };

        var result = ParameterEvaluator.Evaluate("W_input/2", bindings);

        Assert.Equal("2u", result);
    }

    [Fact]
    public void Evaluate_Addition_Computes()
    {
        var bindings = new Dictionary<string, string>();

        var result = ParameterEvaluator.Evaluate("1u+1u", bindings);

        Assert.Equal("2u", result);
    }

    [Fact]
    public void Evaluate_Subtraction_Computes()
    {
        var bindings = new Dictionary<string, string>();

        var result = ParameterEvaluator.Evaluate("3u-1u", bindings);

        Assert.Equal("2u", result);
    }

    [Fact]
    public void Evaluate_ChainedReference_ResolvesRecursively()
    {
        var bindings = new Dictionary<string, string>
        {
            ["actual_width"] = "1u",
            ["W"] = "actual_width",
        };

        var result = ParameterEvaluator.Evaluate("W", bindings);

        Assert.Equal("1u", result);
    }

    [Fact]
    public void Evaluate_MultipleOperations_EvaluatesLeftToRight()
    {
        var bindings = new Dictionary<string, string> { ["ratio"] = "2" };

        // ratio*2+1 = 2*2+1 = 5 (left-to-right, no precedence)
        var result = ParameterEvaluator.Evaluate("ratio*2+1", bindings);

        Assert.Equal("5", result);
    }

    [Fact]
    public void Evaluate_UndefinedParameter_ThrowsArgumentException()
    {
        var bindings = new Dictionary<string, string>();

        var ex = Assert.Throws<ArgumentException>(() =>
            ParameterEvaluator.Evaluate("undefined", bindings)
        );

        Assert.Contains("Undefined parameter reference", ex.Message);
    }

    [Fact]
    public void Evaluate_CircularReference_ThrowsArgumentException()
    {
        var bindings = new Dictionary<string, string> { ["A"] = "B", ["B"] = "A" };

        var ex = Assert.Throws<ArgumentException>(() => ParameterEvaluator.Evaluate("A", bindings));

        Assert.Contains("Circular", ex.Message);
    }

    [Theory]
    [InlineData("1f", 1e-15)]
    [InlineData("1p", 1e-12)]
    [InlineData("1n", 1e-9)]
    [InlineData("1u", 1e-6)]
    [InlineData("1m", 1e-3)]
    [InlineData("1k", 1e3)]
    [InlineData("1M", 1e6)]
    [InlineData("1G", 1e9)]
    [InlineData("1T", 1e12)]
    public void ParseNumeric_AllPrefixes_ParsesCorrectly(string input, double expected)
    {
        var result = ParameterEvaluator.ParseNumeric(input);

        Assert.Equal(expected, result, precision: 10);
    }

    [Fact]
    public void ParseNumeric_DecimalWithPrefix_ParsesCorrectly()
    {
        var result = ParameterEvaluator.ParseNumeric("2.5u");

        Assert.Equal(2.5e-6, result, precision: 15);
    }

    [Fact]
    public void ParseNumeric_ScientificNotation_ParsesCorrectly()
    {
        var result = ParameterEvaluator.ParseNumeric("1e-6");

        Assert.Equal(1e-6, result, precision: 15);
    }

    [Fact]
    public void ParseNumeric_NoPrefix_ParsesCorrectly()
    {
        var result = ParameterEvaluator.ParseNumeric("42");

        Assert.Equal(42, result);
    }

    [Theory]
    [InlineData(1e-15, "1f")]
    [InlineData(1e-12, "1p")]
    [InlineData(1e-9, "1n")]
    [InlineData(1e-6, "1u")]
    [InlineData(1e-3, "1m")]
    [InlineData(1e3, "1k")]
    [InlineData(1e6, "1M")]
    [InlineData(1e9, "1G")]
    [InlineData(1e12, "1T")]
    public void FormatNumeric_SelectsAppropriatePrefix(double input, string expected)
    {
        var result = ParameterEvaluator.FormatNumeric(input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void FormatNumeric_Zero_ReturnsZero()
    {
        var result = ParameterEvaluator.FormatNumeric(0);

        Assert.Equal("0", result);
    }

    [Fact]
    public void FormatNumeric_NonPrefixableValue_ReturnsPlainNumber()
    {
        var result = ParameterEvaluator.FormatNumeric(42);

        Assert.Equal("42", result);
    }

    [Fact]
    public void Evaluate_ComplexExpression_ComputesCorrectly()
    {
        var bindings = new Dictionary<string, string> { ["W_input"] = "1u", ["tail_ratio"] = "4" };

        var result = ParameterEvaluator.Evaluate("$W_input*$tail_ratio", bindings);

        Assert.Equal("4u", result);
    }

    [Fact]
    public void Evaluate_EmptyExpression_ThrowsArgumentException()
    {
        var bindings = new Dictionary<string, string>();

        var ex = Assert.Throws<ArgumentException>(() => ParameterEvaluator.Evaluate("", bindings));

        Assert.Contains("Empty or invalid", ex.Message);
    }
}
