using Xunit;

namespace Cascode.Bench.Tests;

public class BenchTemplateLibraryTests
{
    [Fact]
    public void TryGetTemplate_LoadsEmbeddedTemplate()
    {
        var found = BenchTemplateLibrary.TryGetTemplate(
            "SEOpAmpACBench",
            BenchBackendType.Ngspice,
            out var templateText
        );

        Assert.True(found);
        Assert.False(string.IsNullOrWhiteSpace(templateText));
        Assert.Contains("Generated from ACIR", templateText);
    }

    [Fact]
    public void GetBenchNames_IncludesKnownBench()
    {
        var names = BenchTemplateLibrary.GetBenchNames();

        Assert.Contains("SEOpAmpACBench", names);
    }

    [Fact]
    public void TryGetTemplate_UnknownBench_ReturnsFalse()
    {
        var found = BenchTemplateLibrary.TryGetTemplate(
            "MissingBench",
            BenchBackendType.Ngspice,
            out var templateText
        );

        Assert.False(found);
        Assert.True(string.IsNullOrWhiteSpace(templateText));
    }
}
