using Cascode.Bench;
using Cascode.Language;
using Xunit;

namespace Cascode.Language.Tests
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
            var result = SiValue.Format(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(1e6, "1MEG")]
        [InlineData(10e6, "10MEG")]
        [InlineData(3.3e6, "3.3MEG")]
        [InlineData(2e6, "2MEG")]
        [InlineData(1e3, "1K")]
        [InlineData(1e-3, "1m")]
        [InlineData(1e-12, "1p")]
        public void FormatSIValueForBackend_NgspiceUsesMEGForMega(double input, string expected)
        {
            var result = SiValue.FormatForBackend(input, BenchBackendType.Ngspice);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(1e6, "1M")]
        [InlineData(10e6, "10M")]
        [InlineData(3.3e6, "3.3M")]
        [InlineData(2e6, "2M")]
        public void FormatSIValueForBackend_SpectreUsesMForMega(double input, string expected)
        {
            var result = SiValue.FormatForBackend(input, BenchBackendType.Spectre);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("2M", "2MEG")]
        [InlineData("10M", "10MEG")]
        [InlineData("3.3M", "3.3MEG")]
        [InlineData("1.5M", "1.5MEG")]
        [InlineData("100M", "100MEG")]
        [InlineData("10MOhm", "10MEGOhm")]
        [InlineData("2.5MOhm", "2.5MEGOhm")]
        [InlineData("1MF", "1MEGF")]
        [InlineData("50MHz", "50MEGHz")]
        public void TransformValueForBackend_NgspiceConvertsMToMEG(string input, string expected)
        {
            var result = SiValue.TransformForBackend(input, BenchBackendType.Ngspice);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("2M", "2M")]
        [InlineData("10M", "10M")]
        [InlineData("3.3M", "3.3M")]
        [InlineData("10MOhm", "10MOhm")]
        [InlineData("50MHz", "50MHz")]
        public void TransformValueForBackend_SpectrePreservesMForMega(string input, string expected)
        {
            var result = SiValue.TransformForBackend(input, BenchBackendType.Spectre);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("1m", "1m")]
        [InlineData("10m", "10m")]
        [InlineData("10mOhm", "10mOhm")]
        [InlineData("5mV", "5mV")]
        [InlineData("1k", "1k")]
        [InlineData("10K", "10K")]
        [InlineData("10KOhm", "10KOhm")]
        [InlineData("1p", "1p")]
        [InlineData("1pF", "1pF")]
        [InlineData("1.8V", "1.8V")]
        [InlineData("M=1", "M=1")]
        [InlineData("abc", "abc")]
        [InlineData("", "")]
        public void TransformValueForBackend_NgspiceDoesNotTransformOtherPrefixes(
            string input,
            string expected
        )
        {
            var result = SiValue.TransformForBackend(input, BenchBackendType.Ngspice);
            Assert.Equal(expected, result);
        }
    }
}
