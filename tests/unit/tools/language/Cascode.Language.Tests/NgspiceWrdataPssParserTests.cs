using System;
using System.IO;
using Cascode.Language.BenchRuntime;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Language.Tests;

public sealed class NgspiceWrdataPssParserTests
{
    [Fact]
    public void Parse_ReadsStandardWrdataColumns()
    {
        using var tmp = new TemporaryDirectory();
        var path = Path.Combine(tmp.Path, "pss.wrdata");
        File.WriteAllText(
            path,
            """
            0.0  1.0  2.0
            1e-9  1.5  2.5
            """
        );

        var ds = NgspiceWrdataPssParser.Parse(path, new[] { "OUT", "IN" });

        Assert.Equal(new[] { 0.0, 1e-9 }, ds.TimePoints);
        Assert.Equal(new[] { 1.0, 1.5 }, ds.NodeVoltages["OUT"]);
        Assert.Equal(new[] { 2.0, 2.5 }, ds.NodeVoltages["IN"]);
    }

    [Fact]
    public void Parse_ReadsRepeatedXAxisWrdataColumns()
    {
        using var tmp = new TemporaryDirectory();
        var path = Path.Combine(tmp.Path, "pss_repeated_x.wrdata");
        File.WriteAllText(
            path,
            """
            0.0  1.0  0.0  2.0
            1e-9  1.5  1e-9  2.5
            """
        );

        var ds = NgspiceWrdataPssParser.Parse(path, new[] { "OUT", "IN" });

        Assert.Equal(new[] { 0.0, 1e-9 }, ds.TimePoints);
        Assert.Equal(new[] { 1.0, 1.5 }, ds.NodeVoltages["OUT"]);
        Assert.Equal(new[] { 2.0, 2.5 }, ds.NodeVoltages["IN"]);
    }

    [Fact]
    public void Parse_ThrowsOnInconsistentXAxisInRepeatedXFormat()
    {
        using var tmp = new TemporaryDirectory();
        var path = Path.Combine(tmp.Path, "pss_inconsistent_x.wrdata");
        File.WriteAllText(
            path,
            """
            0.0  1.0  0.1  2.0
            """
        );

        var ex = Assert.Throws<InvalidOperationException>(() =>
            NgspiceWrdataPssParser.Parse(path, new[] { "OUT", "IN" })
        );
        Assert.Contains("Inconsistent wrdata X axis", ex.Message);
        Assert.Contains("expected 0, got 0.1", ex.Message);
    }
}
