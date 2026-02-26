using System;
using System.IO;
using System.Numerics;
using Cascode.Language.BenchRuntime;
using Xunit;

namespace Cascode.Language.Tests;

public sealed class NgspiceWrdataSpParserTests
{
    [Fact]
    public void Parse_ReadsTwoPortComplexWrdata()
    {
        var tmp = Path.Combine(
            Path.GetTempPath(),
            "cascode-sp-wrdata-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(tmp);

        var path = Path.Combine(tmp, "sp.wrdata");
        File.WriteAllText(
            path,
            """
            1.00000000e+09  1.00000000e-01  2.00000000e-01  1.00000000e+09  3.00000000e-01  4.00000000e-01  1.00000000e+09  5.00000000e-01  6.00000000e-01  1.00000000e+09  7.00000000e-01  8.00000000e-01
            2.00000000e+09  1.10000000e-01  2.10000000e-01  2.00000000e+09  3.10000000e-01  4.10000000e-01  2.00000000e+09  5.10000000e-01  6.10000000e-01  2.00000000e+09  7.10000000e-01  8.10000000e-01
            """
        );

        var ds = NgspiceWrdataSpParser.Parse(path, numPorts: 2);

        Assert.Equal(new[] { 1e9, 2e9 }, ds.FrequenciesHz);
        Assert.Equal(4, ds.Elements.Count);

        Assert.Equal(new Complex(0.1, 0.2), ds.Elements[new BenchPortPair(1, 1)][0]);
        Assert.Equal(new Complex(0.3, 0.4), ds.Elements[new BenchPortPair(1, 2)][0]);
        Assert.Equal(new Complex(0.5, 0.6), ds.Elements[new BenchPortPair(2, 1)][0]);
        Assert.Equal(new Complex(0.7, 0.8), ds.Elements[new BenchPortPair(2, 2)][0]);

        Assert.Equal(new Complex(0.11, 0.21), ds.Elements[new BenchPortPair(1, 1)][1]);
        Assert.Equal(new Complex(0.31, 0.41), ds.Elements[new BenchPortPair(1, 2)][1]);
        Assert.Equal(new Complex(0.51, 0.61), ds.Elements[new BenchPortPair(2, 1)][1]);
        Assert.Equal(new Complex(0.71, 0.81), ds.Elements[new BenchPortPair(2, 2)][1]);
    }

    [Fact]
    public void Parse_ReadsSinglePortComplexWrdata()
    {
        var tmp = Path.Combine(
            Path.GetTempPath(),
            "cascode-sp-wrdata-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(tmp);

        var path = Path.Combine(tmp, "sp1.wrdata");
        File.WriteAllText(path, "1.00000000e+06  2.50000000e-01  -1.25000000e-01");

        var ds = NgspiceWrdataSpParser.Parse(path, numPorts: 1);

        Assert.Equal(new[] { 1e6 }, ds.FrequenciesHz);
        Assert.Single(ds.Elements);
        Assert.Equal(new Complex(0.25, -0.125), ds.Elements[new BenchPortPair(1, 1)][0]);
    }

    [Fact]
    public void Parse_ThrowsWhenColumnCountDoesNotMatchPortCount()
    {
        var tmp = Path.Combine(
            Path.GetTempPath(),
            "cascode-sp-wrdata-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(tmp);

        var path = Path.Combine(tmp, "bad.wrdata");
        File.WriteAllText(
            path,
            "1.00000000e+09  1.0  0.0  1.00000000e+09  2.0  0.0  1.00000000e+09  3.0  0.0"
        );

        var ex = Assert.Throws<InvalidOperationException>(() =>
            NgspiceWrdataSpParser.Parse(path, numPorts: 2)
        );
        Assert.Contains("expected 12, got 9", ex.Message);
    }
}
