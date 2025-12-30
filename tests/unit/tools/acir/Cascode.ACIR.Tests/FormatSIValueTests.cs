using Cascode.ACIR;
using Xunit;

namespace Cascode.ACIR.Tests
{
    public class FormatSIValueTests
    {
        [Theory]
        [InlineData(0, "0")]
        [InlineData(1.0, "1")]
        [InlineData(1.5, "1.5")]
        [InlineData(1e-15, "1f")]
        [InlineData(500e-15, "500f")]
        [InlineData(1e-12, "1p")]
        [InlineData(10e-12, "10p")]
        [InlineData(1e-9, "1n")]
        [InlineData(10e-9, "10n")]
        [InlineData(1e-6, "1u")]
        [InlineData(10e-6, "10u")]
        [InlineData(1e-3, "1m")]
        [InlineData(10e-3, "10m")]
        [InlineData(1000, "1K")]
        [InlineData(1e6, "1M")]
        [InlineData(10e6, "10M")]
        [InlineData(1e9, "1G")]
        [InlineData(1e12, "1T")]
        [InlineData(2.5e-12, "2.5p")]
        [InlineData(3.3e6, "3.3M")]
        public void FormatSIValue_FormatsCorrectly(double input, string expected)
        {
            var result = ACIRBenchAdapter.FormatSIValue(input);
            Assert.Equal(expected, result);
        }
    }
}
