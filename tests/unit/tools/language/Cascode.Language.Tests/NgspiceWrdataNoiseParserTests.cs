using System;
using System.IO;
using Cascode.Language.BenchRuntime;
using Xunit;

namespace Cascode.Language.Tests;

public sealed class NgspiceWrdataNoiseParserTests
{
    [Fact]
    public void Parse_ReadsTwoColumnNoiseWrdata()
    {
        var tmp = Path.Combine(
            Path.GetTempPath(),
            "cascode-noise-wrdata-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(tmp);

        var path = Path.Combine(tmp, "noise.wrdata");
        File.WriteAllText(
            path,
            """
            1.00000000e+00  2.00000000e-09
            1.00000000e+01  3.00000000e-09
            """
        );

        var ds = NgspiceWrdataNoiseParser.Parse(path);
        Assert.Equal(new[] { 1.0, 10.0 }, ds.FrequenciesHz);
        Assert.Equal(new[] { 2e-9, 3e-9 }, ds.OutputNoiseVPerRtHz);
    }
}
