using Cascode.ACIR;

namespace Cascode.ACIR.Tests;

public class ParameterEvaluatorTests
{
    [Fact]
    public void Evaluate_NumericLiteral_ReturnsUnchanged()
    {
        var bindings = new Dictionary<string, string>();

        var result = EvaluateExpression("100", bindings);

        Assert.Equal("100", result);
    }

    [Fact]
    public void Evaluate_NumericWithSIPrefix_ReturnsUnchanged()
    {
        var bindings = new Dictionary<string, string>();

        var result = EvaluateExpression("2u", bindings);

        Assert.Equal("2u", result);
    }

    [Fact]
    public void Evaluate_SymbolicReference_Substitutes()
    {
        var bindings = new Dictionary<string, string> { ["width"] = "1u" };

        var result = EvaluateExpression("width", bindings);

        Assert.Equal("1u", result);
    }

    [Fact]
    public void Evaluate_Multiplication_Computes()
    {
        var bindings = new Dictionary<string, string> { ["W_input"] = "1u" };

        var result = EvaluateExpression("W_input*2", bindings);

        Assert.Equal("2u", result);
    }

    [Fact]
    public void Evaluate_Division_Computes()
    {
        var bindings = new Dictionary<string, string> { ["W_input"] = "4u" };

        var result = EvaluateExpression("W_input/2", bindings);

        Assert.Equal("2u", result);
    }

    [Fact]
    public void Evaluate_Addition_Computes()
    {
        var bindings = new Dictionary<string, string>();

        var result = EvaluateExpression("1u+1u", bindings);

        Assert.Equal("2u", result);
    }

    [Fact]
    public void Evaluate_Subtraction_Computes()
    {
        var bindings = new Dictionary<string, string>();

        var result = EvaluateExpression("3u-1u", bindings);

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

        var result = EvaluateExpression("W", bindings);

        Assert.Equal("1u", result);
    }

    [Fact]
    public void Evaluate_MultipleOperations_RespectsOperatorPrecedence()
    {
        var bindings = new Dictionary<string, string> { ["ratio"] = "2" };

        // ratio*2+1 = (2*2)+1 = 5 (multiplication before addition)
        var result = EvaluateExpression("ratio*2+1", bindings);

        Assert.Equal("5", result);
    }

    [Theory]
    [InlineData("1+2*3", "7")]
    [InlineData("6-4/2", "4")]
    [InlineData("(1+2)*3", "9")]
    [InlineData("2+3*4-1", "13")]
    [InlineData("-2*3", "-6")]
    [InlineData("-(1+2)", "-3")]
    public void Evaluate_OperatorPrecedence_CorrectResult(string expr, string expected)
    {
        var result = ExpressionEvaluator.Evaluate(expr, _ => null);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Evaluate_UndefinedParameter_ThrowsArgumentException()
    {
        var bindings = new Dictionary<string, string>();

        var ex = Assert.Throws<ArgumentException>(() => EvaluateExpression("undefined", bindings));

        Assert.Contains("Undefined parameter reference", ex.Message);
    }

    [Fact]
    public void Evaluate_CircularReference_ThrowsArgumentException()
    {
        var bindings = new Dictionary<string, string> { ["A"] = "B", ["B"] = "A" };

        var ex = Assert.Throws<ArgumentException>(() => EvaluateExpression("A", bindings));

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

        var result = EvaluateExpression("W_input*tail_ratio", bindings);

        Assert.Equal("4u", result);
    }

    [Fact]
    public void Evaluate_EmptyExpression_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            ExpressionEvaluator.Evaluate("", _ => null)
        );

        Assert.Contains("Empty or invalid", ex.Message);
    }

    private static string EvaluateExpression(
        string expression,
        IReadOnlyDictionary<string, string> bindings
    )
    {
        var resolver = CreateResolver(bindings);
        return ExpressionEvaluator.Evaluate(expression, resolver);
    }

    private static Func<string, string?> CreateResolver(
        IReadOnlyDictionary<string, string> bindings
    )
    {
        var cache = new Dictionary<string, string>(StringComparer.Ordinal);
        var resolving = new HashSet<string>(StringComparer.Ordinal);

        string Resolve(string name)
        {
            if (cache.TryGetValue(name, out var cached))
            {
                return cached;
            }

            if (!resolving.Add(name))
            {
                throw new ArgumentException($"Circular parameter reference detected: {name}");
            }

            if (!bindings.TryGetValue(name, out var value) || value is null)
            {
                throw new ArgumentException($"Undefined parameter reference: {name}");
            }

            var resolved = ExpressionEvaluator.Evaluate(value, Resolve);
            cache[name] = resolved;
            resolving.Remove(name);
            return resolved;
        }

        return Resolve;
    }
}
