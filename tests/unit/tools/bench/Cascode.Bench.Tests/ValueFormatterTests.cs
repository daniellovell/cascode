using Cascode.Bench;
using Xunit;

namespace Cascode.Bench.Tests;

public sealed class ValueFormatterTests
{
    [Theory]
    [InlineData(1.0, "V", "1 V")]
    [InlineData(1_000.0, "Hz", "1 kHz")]
    [InlineData(1_000_000.0, "Hz", "1 MHz")]
    [InlineData(1_000_000_000.0, "Hz", "1 GHz")]
    [InlineData(1_000_000_000_000.0, "Hz", "1 THz")]
    [InlineData(999.96, "Hz", "1 kHz")]
    [InlineData(0.99996, "A", "1 A")]
    [InlineData(1e-3, "A", "1 mA")]
    [InlineData(1e-6, "A", "1 uA")]
    [InlineData(1e-9, "A", "1 nA")]
    [InlineData(1e-12, "F", "1 pF")]
    [InlineData(1e-15, "F", "1 fF")]
    [InlineData(0.0, "V", "0 V")]
    [InlineData(-2.5e6, "Hz", "-2.5 MHz")]
    [InlineData(double.NaN, "V", "NaN V")]
    [InlineData(double.PositiveInfinity, "V", "Infinity V")]
    [InlineData(double.NegativeInfinity, "V", "-Infinity V")]
    public void FormatValue_UsesExpectedPrefix(double value, string unit, string expected)
    {
        Assert.Equal(expected, ValueFormatter.FormatValue(value, unit));
    }
}
