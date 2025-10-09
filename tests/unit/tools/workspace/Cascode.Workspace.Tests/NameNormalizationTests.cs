using Cascode.Workspace;

namespace Cascode.Workspace.Tests;

public class NameNormalizationTests
{
    [Fact]
    public void NameNormalizationInfersVtTags()
    {
        var lvtTags = NameNormalization.ExtractVtTags("pfet_01v8_lvt");
        Assert.Contains("LVT", lvtTags);

        var hvtTags = NameNormalization.ExtractVtTags("nfet_03v3_hvt");
        Assert.Contains("HVT", hvtTags);

        var defaultTags = NameNormalization.ExtractVtTags("pfet_01v8");
        Assert.Contains("SVT", defaultTags);
    }
}
