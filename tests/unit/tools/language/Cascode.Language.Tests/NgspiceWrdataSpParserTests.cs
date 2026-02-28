using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Cascode.Language;
using Cascode.Language.BenchRuntime;
using Cascode.TestSupport;
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

    [Fact]
    public void Parse_SpVectorOrder_ConsistentWithEmitterWrdataOrder()
    {
        const string cascode = """
            VERSION 4.0

            bench SBench {
              resp P1 : analog
              resp P2 : analog

              fill {
                net gnd : ground
                GND g = new GND() { .GND--gnd }
                Port p1 = new Port(N=1, Z=50Ohm, V=0V) {
                  .P--P1
                  .N--gnd
                }
                Port p2 = new Port(N=2, Z=50Ohm, V=0V) {
                  .P--P2
                  .N--gnd
                }
              }

              analysis {
                SPAnalysis sp = new SPAnalysis(space=Log, samples=1, start=1GHz, stop=1GHz)
              }

              measurements {
                measurement Dummy : V {
                  return 1V
                }
              }
            }

            circuit Top {
              level EL
              input P1 : analog
              input P2 : analog

              constraints {
                numeric {
                  c_dummy = sp::Dummy >= 0V
                }
              }

              benches {
                bind SBench as sp {
                  bench.P1--dut.P1
                  bench.P2--dut.P2
                }
              }

              fill { }
            }
            """;

        var parsed = CascodeReader.TryParse(cascode, "sp_order_contract.cas");
        Assert.True(parsed.Success, parsed.Diagnostics.ToString());
        var doc = parsed.Document!;

        using var tmpDir = new TemporaryDirectory();
        var designPath = Path.Combine(tmpDir.Path, "Top.sp");
        File.WriteAllText(designPath, "* dummy design deck");
        BenchTestbenchEmitter.EmitAll(
            doc,
            tmpDir.Path,
            Cascode.Bench.BenchBackendType.Ngspice,
            designPaths: new[] { designPath }
        );

        var tbPath = Path.Combine(tmpDir.Path, "Top_sp.sp");
        Assert.True(File.Exists(tbPath), "Expected SP testbench to be written.");
        var wrdataLine = File.ReadAllLines(tbPath)
            .Single(line =>
                line.Contains("wrdata", StringComparison.OrdinalIgnoreCase)
                && line.Contains("S_1_1", StringComparison.OrdinalIgnoreCase)
            );

        var s11 = wrdataLine.IndexOf("S_1_1", StringComparison.OrdinalIgnoreCase);
        var s12 = wrdataLine.IndexOf("S_1_2", StringComparison.OrdinalIgnoreCase);
        var s21 = wrdataLine.IndexOf("S_2_1", StringComparison.OrdinalIgnoreCase);
        var s22 = wrdataLine.IndexOf("S_2_2", StringComparison.OrdinalIgnoreCase);
        Assert.True(s11 >= 0 && s12 > s11 && s21 > s12 && s22 > s21);

        var wrdataPath = Path.Combine(tmpDir.Path, "sp.wrdata");
        File.WriteAllText(
            wrdataPath,
            "1.00000000e+09  1.10000000e+01  -1.10000000e+01  1.00000000e+09  1.20000000e+01  -1.20000000e+01  1.00000000e+09  2.10000000e+01  -2.10000000e+01  1.00000000e+09  2.20000000e+01  -2.20000000e+01"
        );
        var ds = NgspiceWrdataSpParser.Parse(wrdataPath, numPorts: 2);

        Assert.Equal(new Complex(11.0, -11.0), ds.Elements[new BenchPortPair(1, 1)][0]);
        Assert.Equal(new Complex(12.0, -12.0), ds.Elements[new BenchPortPair(1, 2)][0]);
        Assert.Equal(new Complex(21.0, -21.0), ds.Elements[new BenchPortPair(2, 1)][0]);
        Assert.Equal(new Complex(22.0, -22.0), ds.Elements[new BenchPortPair(2, 2)][0]);
    }

    [Fact]
    public void ParseNoiseFactor_ReadsNoiseVectors()
    {
        using var tmpDir = new TemporaryDirectory();
        var path = Path.Combine(tmpDir.Path, "sp.nf.wrdata");
        File.WriteAllText(
            path,
            """
            1.00000000e+09  2.00000000e+00  0.00000000e+00  1.00000000e+09  1.20000000e+00  0.00000000e+00  1.00000000e+09  3.00000000e+01  0.00000000e+00
            2.00000000e+09  1.50000000e+00  0.00000000e+00  2.00000000e+09  1.10000000e+00  0.00000000e+00  2.00000000e+09  4.00000000e+01  0.00000000e+00
            """
        );

        var ds = NgspiceWrdataSpParser.ParseNoiseFactor(path);
        Assert.Equal(new[] { 1e9, 2e9 }, ds.FrequenciesHz);
        Assert.Equal(new[] { 2.0, 1.5 }, ds.NoiseFactor);
        Assert.Equal(new[] { 1.2, 1.1 }, ds.MinNoiseFactor);
        Assert.Equal(new[] { 30.0, 40.0 }, ds.NoiseResistance);
    }

    [Fact]
    public void ParseNoiseFactor_ThrowsWhenColumnCountIsInvalid()
    {
        using var tmpDir = new TemporaryDirectory();
        var path = Path.Combine(tmpDir.Path, "bad.nf.wrdata");
        File.WriteAllText(path, "1.00000000e+09  2.0");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            NgspiceWrdataSpParser.ParseNoiseFactor(path)
        );
        Assert.Contains("expected 9, got 2", ex.Message);
    }
}
