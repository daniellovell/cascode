using System.IO;
using System.Linq;
using Cascode.Language;
using Cascode.TestSupport;

namespace Cascode.Language.Tests;

public sealed class PcbSyntaxParsingTests
{
    [Fact]
    public void SyntaxOnlyParse_PcbInterfacesFile_RetainsMetricsBlocks()
    {
        var result = ParseFixture("SensorFrontendPCB.Interfaces.cas");

        Assert.True(
            result.Success,
            string.Join("\n", result.Diagnostics.Select(d => $"{d.Line}:{d.Column}: {d.Message}"))
        );

        using var writer = new StringWriter();
        CascodeWriter.Write(result.Document!, writer);
        var rendered = writer.ToString();

        Assert.Contains("metrics {", rendered, StringComparison.Ordinal);
        Assert.Contains(
            "PassbandGain = transfer_bench::PassbandGain",
            rendered,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void SyntaxOnlyParse_PcbPartsFile_RetainsPartCatalogSyntax()
    {
        var result = ParseFixture("SensorFrontendPCB.Parts.cas");

        Assert.True(
            result.Success,
            string.Join("\n", result.Diagnostics.Select(d => $"{d.Line}:{d.Column}: {d.Message}"))
        );

        using var writer = new StringWriter();
        CascodeWriter.Write(result.Document!, writer);
        var rendered = writer.ToString();

        Assert.Contains("part OPA2376 implements DualOpAmp", rendered, StringComparison.Ordinal);
        Assert.Contains("variant flash {", rendered, StringComparison.Ordinal);
        Assert.Contains("P6:P21 = PA[0:15]", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void SyntaxOnlyParse_PcbComponentsFile_RetainsSelectionsAndForwardedMetrics()
    {
        var result = ParseFixture("SensorFrontendPCB.Components.el.cas");

        Assert.True(
            result.Success,
            string.Join("\n", result.Diagnostics.Select(d => $"{d.Line}:{d.Column}: {d.Message}"))
        );

        using var writer = new StringWriter();
        CascodeWriter.Write(result.Document!, writer);
        var rendered = writer.ToString();

        Assert.Contains(
            "new YageoRC[body=_0402, grade=F](R=100k)",
            rendered,
            StringComparison.Ordinal
        );
        Assert.Contains("FlashSize = uMcu.FlashSize", rendered, StringComparison.Ordinal);
        Assert.Contains("c_bw = LowpassBandwidth >= 10kHz", rendered, StringComparison.Ordinal);
    }

    private static CascodeReadResult ParseFixture(string fileName)
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var path = Path.Combine(repoRoot, "tests", "golden", "cas", "pcb", fileName);
        var text = File.ReadAllText(path);
        return CascodeParserFacade.Parse(path, text, CascodeParseOptions.SyntaxOnly);
    }
}
