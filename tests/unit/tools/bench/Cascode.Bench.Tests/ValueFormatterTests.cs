using Cascode.Bench;
using Xunit;

namespace Cascode.Bench.Tests;

public sealed class ValueFormatterTests
{
    [Theory]
    [InlineData(1.0, "V", "1 V")]
    [InlineData(1_000.0, "Hz", "1k Hz")]
    [InlineData(1_000_000.0, "Hz", "1M Hz")]
    [InlineData(1_000_000_000.0, "Hz", "1G Hz")]
    [InlineData(1e-3, "A", "1m A")]
    [InlineData(1e-6, "A", "1u A")]
    [InlineData(1e-9, "A", "1n A")]
    public void FormatValue_UsesExpectedPrefix(double value, string unit, string expected)
    {
        Assert.Equal(expected, ValueFormatter.FormatValue(value, unit));
    }
}
