using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Cascode.Bench;
using Cascode.Cli.IntegrationTests.Infrastructure;
using Cascode.Language;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Cli.IntegrationTests;

public sealed class PssCurrentMeasurementIntegrationTests : IDisposable
{
    private readonly string _outputDir;
    private readonly CascodeHomeScope _cascodeHome;
    private readonly string _cascodePath;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public PssCurrentMeasurementIntegrationTests()
    {
        var repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        _outputDir = Path.Combine(
            Path.GetTempPath(),
            "cascode-pss-current-test-" + Guid.NewGuid().ToString("N")[..8]
        );
        Directory.CreateDirectory(_outputDir);
        _cascodeHome = CliIntegrationTestHelper.CreateCascodeHome(repoRoot, "pss-current");
        _cascodePath = Path.Combine(_outputDir, "PssCurrentLoad.cas");
    }

    public void Dispose()
    {
        _cascodeHome.Dispose();
        if (!Directory.Exists(_outputDir))
        {
            return;
        }

        try
        {
            Directory.Delete(_outputDir, recursive: true);
        }
        catch { }
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task BenchRun_PssCurrentMeasurement_WritesCurrentWrdataAndEvaluatesMeasurement()
    {
        await File.WriteAllTextAsync(
            _cascodePath,
            $$"""
            VERSION {{CascodeVersion.Current}}

            include lib.std

            bench PssCurrentBench {
              stim IN : analog
              resp OUT : analog

              fill {
                net gnd : ground
                GND g = new GND() { .GND--gnd }

                VSIN vin = new VSIN(DC=0V, A=100mV, freq=1MHz, phase=0deg) {
                  .P--IN
                  .N--gnd
                }
              }

              analysis {
                PSSAnalysis pss = new PSSAnalysis(fguess=1MHz, tstab=20us, harmonics=5)
              }

              measurements {
                measurement InputCurrentPeak : A {
                  CurrentWaveform iin = current(pss, harness.vin.P)
                  return iin.Max()
                }
              }
            }

            circuit PssCurrentLoad {
              level EL

              input IN : analog
              output OUT : analog
              ground GND

              benches {
                bind PssCurrentBench as pss {
                  bench.IN--dut.IN
                  bench.OUT--dut.OUT
                }
              }

              constraints {
                numeric {
                  c_peak_min = pss::InputCurrentPeak >= 90uA
                  c_peak_max = pss::InputCurrentPeak <= 110uA
                }
              }

              harness {
                ground GND = 0V
              }

              fill {
                Resistor R1 = new ResistorIdeal(size(R=1k)) {
                  .P--IN
                  .N--OUT
                }

                Capacitor C1 = new CapacitorIdeal(size(C=1n)) {
                  .P--OUT
                  .N--GND
                }
              }
            }
            """
        );

        await RunCliAsync("bench", "run", _cascodePath, "-o", _outputDir);

        var testbenchPath = Path.Combine(_outputDir, "PssCurrentLoad_pss.sp");
        Assert.True(File.Exists(testbenchPath), "pss testbench not found");
        var testbench = await File.ReadAllTextAsync(testbenchPath);
        Assert.Contains("setplot pss1", testbench, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "wrdata PssCurrentLoad_pss__pss.pss.currents.wrdata i(Vvin)",
            testbench,
            StringComparison.Ordinal
        );

        var currentWrdataPath = Path.Combine(
            _outputDir,
            "PssCurrentLoad_pss__pss.pss.currents.wrdata"
        );
        Assert.True(File.Exists(currentWrdataPath), "pss current wrdata not found");

        var resultsPath = Path.Combine(_outputDir, "PssCurrentLoad_pss_results.json");
        var results = await ReadBenchResultsAsync(resultsPath);
        Assert.Equal("PssCurrentLoad", results.Circuit);
        Assert.Equal("pss", results.Bench);
        Assert.True(
            results.Measurements.TryGetValue("InputCurrentPeak", out var inputCurrentPeak),
            "InputCurrentPeak measurement missing"
        );
        Assert.True(
            string.IsNullOrEmpty(inputCurrentPeak.Error),
            "InputCurrentPeak had error: " + inputCurrentPeak.Error
        );
        Assert.True(inputCurrentPeak.Value.HasValue, "InputCurrentPeak value missing");
        Assert.InRange(inputCurrentPeak.Value!.Value, 90e-6, 110e-6);

        await RunCliAsync("verify", _cascodePath, resultsPath);
    }

    private async Task RunCliAsync(params string[] args)
    {
        var run = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            args
        );
        CliIntegrationTestHelper.AssertSuccess(run, "CLI command failed");
    }

    private static async Task<BenchResult> ReadBenchResultsAsync(string resultsPath)
    {
        Assert.True(File.Exists(resultsPath), "results.json not found");
        var results = JsonSerializer.Deserialize<BenchResult>(
            await File.ReadAllTextAsync(resultsPath),
            s_jsonOptions
        );
        Assert.NotNull(results);
        return results!;
    }
}
