using System;
using System.IO;
using System.Linq;
using Cascode.Bench;
using Cascode.Language;
using Cascode.Language.BenchRuntime;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Language.Tests;

public sealed class StdlibBenchInputAmplitudeTests
{
    [Fact]
    public void EmitAll_DiffToDiffPss_SplitsInputPowerAcrossLegs()
    {
        var instanceName = BenchInvocationName.Compute(
            "pss_bench",
            new[] { new MetricCallArg("guess_freq", "1MHz") }
        );
        var tb = EmitLinkedTestbench(
            $$"""
            VERSION {{CascodeVersion.Current}}

            include lib.std

            circuit Top {
              level EL
              input IN : Diff
              output OUT : Diff
              ground GND

              env {
                InputPower = 1mW
                InputCommonModeRange = 0V
                SourceImpedance = 100Ohm
                LoadImpedance = 1GOhm
              }

              constraints {
                numeric {
                  c_pin = pss_bench(guess_freq=1MHz)::InputPower >= 0W
                }
              }

              benches {
                bind DiffToDiffPSS as pss_bench {
                  bench.IN--dut.IN
                  bench.OUT--dut.OUT
                }
              }

              harness {
                ground GND = 0V
              }

              fill {
                Resistor R_P = new ResistorIdeal(size(R=1k)) {
                  .P--IN.P
                  .N--OUT.P
                }

                Resistor R_N = new ResistorIdeal(size(R=1k)) {
                  .P--IN.N
                  .N--OUT.N
                }
              }
            }
            """,
            instanceName
        );

        Assert.Contains("VinP", tb, StringComparison.Ordinal);
        Assert.Contains("VinN", tb, StringComparison.Ordinal);
        Assert.Contains("sin(0 447.214m 1MEG 0 0 0)", tb, StringComparison.Ordinal);
        Assert.Contains("sin(0 447.214m 1MEG 0 0 180)", tb, StringComparison.Ordinal);
        Assert.DoesNotContain("894.427m", tb, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitAll_DiffToDiffTran_SplitsInputPowerAcrossLegs()
    {
        var instanceName = BenchInvocationName.Compute(
            "tran_bench",
            new[] { new MetricCallArg("stim_freq", "1kHz") }
        );
        var tb = EmitLinkedTestbench(
            $$"""
            VERSION {{CascodeVersion.Current}}

            include lib.std

            circuit Top {
              level EL
              input IN : Diff
              output OUT : Diff
              ground GND

              env {
                InputPower = 1mW
                InputCommonModeRange = 0V
                SourceImpedance = 100Ohm
                LoadImpedance = 1GOhm
              }

              constraints {
                numeric {
                  c_swing = tran_bench(stim_freq=1kHz)::OutputSwing() at net::OUT >= 0V
                }
              }

              benches {
                bind DiffToDiffTran as tran_bench {
                  bench.IN--dut.IN
                  bench.OUT--dut.OUT
                }
              }

              harness {
                ground GND = 0V
              }

              fill {
                Resistor R_P = new ResistorIdeal(size(R=1k)) {
                  .P--IN.P
                  .N--OUT.P
                }

                Resistor R_N = new ResistorIdeal(size(R=1k)) {
                  .P--IN.N
                  .N--OUT.N
                }
              }
            }
            """,
            instanceName
        );

        Assert.Contains("VinP", tb, StringComparison.Ordinal);
        Assert.Contains("VinN", tb, StringComparison.Ordinal);
        Assert.Contains("sin(0 447.214m 1K 0 0 0)", tb, StringComparison.Ordinal);
        Assert.Contains("sin(0 447.214m 1K 0 0 180)", tb, StringComparison.Ordinal);
        Assert.DoesNotContain("894.427m", tb, StringComparison.Ordinal);
    }

    private static string EmitLinkedTestbench(string cascode, string instanceName)
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        using var cascodeHome = CascodeHome.CreateInTemp("bench-input-amplitude");
        var entryPath = Path.Combine(cascodeHome.Path, "entry.cas");
        var linkOutDir = Path.Combine(cascodeHome.Path, "linked");
        var emitOutDir = Path.Combine(cascodeHome.Path, "emit");
        File.WriteAllText(entryPath, cascode);

        var link = CascodeLinker.LinkFile(entryPath, linkOutDir, repoRoot);
        Assert.True(
            link.Success,
            string.Join(Environment.NewLine, link.Diagnostics.Select(d => d.Message))
        );
        Assert.NotNull(link.LinkedCasPath);

        using var reader = File.OpenText(link.LinkedCasPath!);
        var linked = CascodeReader.Read(reader, link.LinkedCasPath!);
        var designPath = Path.Combine(emitOutDir, "Top.sp");
        Directory.CreateDirectory(emitOutDir);
        File.WriteAllText(designPath, "* dummy design deck");

        BenchTestbenchEmitter.EmitAll(
            linked,
            emitOutDir,
            BenchBackendType.Ngspice,
            designPaths: new[] { designPath }
        );

        var tbPath = Path.Combine(emitOutDir, $"Top_{instanceName}.sp");
        Assert.True(File.Exists(tbPath), $"Expected testbench '{tbPath}' to be written.");
        return File.ReadAllText(tbPath);
    }
}
