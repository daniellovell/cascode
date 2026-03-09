using System;
using System.Collections.Generic;
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

public sealed class BenchRunIntegrationTests : IDisposable
{
    private readonly string _repoRoot;
    private readonly string _outputDir;
    private readonly CascodeHomeScope _cascodeHome;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public BenchRunIntegrationTests()
    {
        _repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        _outputDir = Path.Combine(
            Path.GetTempPath(),
            "cascode-bench-run-test-" + Guid.NewGuid().ToString("N")[..8]
        );
        Directory.CreateDirectory(_outputDir);
        _cascodeHome = CliIntegrationTestHelper.CreateCascodeHome(_repoRoot, "bench-run");
    }

    public void Dispose()
    {
        _cascodeHome.Dispose();
        if (Directory.Exists(_outputDir))
        {
            try
            {
                Directory.Delete(_outputDir, recursive: true);
            }
            catch { }
        }
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task BenchRun_RcLowpass_WritesResultsAndTrace_AndVerifyPasses()
    {
        var cascodePath = Path.Combine(_repoRoot, "tests/golden/cas/bench/RcLowpass.el.cai");

        await RunBenchAsync(cascodePath);

        var resultsPath = Path.Combine(_outputDir, "RcLowpass_lp_results.json");
        var tracePath = Path.Combine(_outputDir, "RcLowpass_lp_trace.jsonl");

        Assert.True(File.Exists(resultsPath), "results.json not found");
        Assert.True(File.Exists(tracePath), "trace.jsonl not found");

        var results = await ReadBenchResultsAsync(resultsPath);
        Assert.Equal("RcLowpass", results.Circuit);
        Assert.Equal("lp", results.Bench);
        Assert.True(results.Measurements.ContainsKey("LowpassBandwidth"));
        Assert.True(results.Measurements["LowpassBandwidth"].Value.HasValue);
        Assert.False(double.IsNaN(results.Measurements["LowpassBandwidth"].Value!.Value));

        await VerifyAsync(cascodePath, resultsPath);
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task BenchRun_MultiCircuit_RunsAllCircuitsWithBenches()
    {
        var cascodePath = Path.Combine(
            _repoRoot,
            "tests/golden/cas/bench/RcLowpassMultiCircuit.el.cai"
        );

        await RunBenchAsync(cascodePath);

        Assert.True(File.Exists(Path.Combine(_outputDir, "RcLowpassA_lp_results.json")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "RcLowpassB_lp_results.json")));
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task BenchRun_CircuitFilter_RunsOnlyRequestedCircuit()
    {
        var cascodePath = Path.Combine(
            _repoRoot,
            "tests/golden/cas/bench/RcLowpassMultiCircuit.el.cai"
        );

        await RunBenchAsync(cascodePath, "--circuit", "RcLowpassB");

        Assert.False(File.Exists(Path.Combine(_outputDir, "RcLowpassA_lp_results.json")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "RcLowpassB_lp_results.json")));
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task BenchRun_TranInternalCurrent_ProbesInternalNodesAndHarnessCurrents()
    {
        var cascodePath = Path.Combine(_repoRoot, "tests/golden/cas/bench/TranInternalCurrent.cas");

        await RunBenchAsync(cascodePath);

        var tbPath = Path.Combine(_outputDir, "TranInternalCurrent_tran.sp");
        Assert.True(File.Exists(tbPath), "tran testbench not found");
        var tbText = await File.ReadAllTextAsync(tbPath);
        Assert.Contains("tran", tbText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("wrdata", tbText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("v(XDUT.n)", tbText, StringComparison.OrdinalIgnoreCase);

        var resultsPath = Path.Combine(_outputDir, "TranInternalCurrent_tran_results.json");
        Assert.True(File.Exists(resultsPath), "results.json not found");

        var results = await ReadBenchResultsAsync(resultsPath);
        Assert.Equal("TranInternalCurrent", results.Circuit);
        Assert.Equal("tran", results.Bench);
        Assert.True(results.Measurements.ContainsKey("InternalNodePeak"));
        Assert.True(results.Measurements.ContainsKey("SupplyCurrentPeak"));
        Assert.True(string.IsNullOrEmpty(results.Measurements["InternalNodePeak"].Error));
        Assert.True(string.IsNullOrEmpty(results.Measurements["SupplyCurrentPeak"].Error));
        Assert.True(results.Measurements["InternalNodePeak"].Value.HasValue);
        Assert.True(results.Measurements["SupplyCurrentPeak"].Value.HasValue);
        Assert.False(double.IsNaN(results.Measurements["InternalNodePeak"].Value!.Value));
        Assert.False(double.IsNaN(results.Measurements["SupplyCurrentPeak"].Value!.Value));
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task BenchRun_DcInternalNode_ProbesInternalNodesInOp()
    {
        var cascodePath = Path.Combine(_repoRoot, "tests/golden/cas/bench/DcInternalNode.cas");

        await RunBenchAsync(cascodePath);

        var tbPath = Path.Combine(_outputDir, "DcInternalNode_dc.sp");
        Assert.True(File.Exists(tbPath), "dc testbench not found");
        var tbText = await File.ReadAllTextAsync(tbPath);
        Assert.Contains("op", tbText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("wrdata", tbText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("setplot op1", tbText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("v(XDUT.mid)", tbText, StringComparison.OrdinalIgnoreCase);

        var resultsPath = Path.Combine(_outputDir, "DcInternalNode_dc_results.json");
        Assert.True(File.Exists(resultsPath), "results.json not found");

        var results = await ReadBenchResultsAsync(resultsPath);
        Assert.Equal("DcInternalNode", results.Circuit);
        Assert.Equal("dc", results.Bench);
        Assert.True(results.Measurements.ContainsKey("MidVoltage"));
        Assert.True(string.IsNullOrEmpty(results.Measurements["MidVoltage"].Error));
        Assert.True(results.Measurements["MidVoltage"].Value.HasValue);
        Assert.False(double.IsNaN(results.Measurements["MidVoltage"].Value!.Value));

        await VerifyAsync(cascodePath, resultsPath);
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task BenchRun_InputDcCharacterization_ReportsNonTrivialUnityGainMetrics()
    {
        var cascodePath = Path.Combine(_outputDir, "PassiveInputDcFixture.cas");
        await File.WriteAllTextAsync(
            cascodePath,
            $$"""
            VERSION {{CascodeVersion.Current}}

            include lib.std

            circuit PassiveInputDcFixture implements SingleEndedOpAmp {
              level EL
              supply VDD
              ground GND
              input IN : Diff
              output OUT : analog

              env {
                InputCommonModeRange = 0.9V
                SourceImpedance = 50Ohm
              }

              constraints {
                numeric {
                  c_vio = input_dc_bench::InputReferredDCOffset >= 100mV
                  c_ip = input_dc_bench::InputCurrentP >= 1nA
                  c_in = input_dc_bench::InputCurrentN >= 500pA
                  c_iib = input_dc_bench::InputBiasCurrent >= 500pA
                  c_iio = input_dc_bench::InputOffsetCurrent >= 500pA
                }
              }

              harness {
                supply VDD = 1.8V
                ground GND = 0V
              }

              fill {
                Resistor R_INP_GND = new ResistorIdeal(size(R=1G)) { .P--IN.P, .N--GND }
                Resistor R_INN_GND = new ResistorIdeal(size(R=300M)) { .P--IN.N, .N--GND }
                Resistor R_FWD = new ResistorIdeal(size(R=200M)) { .P--IN.P, .N--OUT }
                Resistor R_OUT_GND = new ResistorIdeal(size(R=100M)) { .P--OUT, .N--GND }
              }
            }
            """
        );

        await RunBenchAsync(cascodePath, "input_dc_bench");

        var resultsPath = Path.Combine(
            _outputDir,
            "PassiveInputDcFixture_input_dc_bench_results.json"
        );
        Assert.True(File.Exists(resultsPath), "results.json not found");

        var results = await ReadBenchResultsAsync(resultsPath);
        var vio = AssertMeasurement(results, "InputReferredDCOffset");
        var ip = AssertMeasurement(results, "InputCurrentP");
        var inn = AssertMeasurement(results, "InputCurrentN");
        var iib = AssertMeasurement(results, "InputBiasCurrent");
        var iio = AssertMeasurement(results, "InputOffsetCurrent");

        Assert.True(vio > 0.5, $"expected nontrivial offset, got {vio}");
        Assert.True(ip > 3e-9, $"expected input P current above 3 nA, got {ip}");
        Assert.True(inn > 0.8e-9, $"expected input N current above 0.8 nA, got {inn}");
        Assert.True(iib > 2e-9, $"expected bias current above 2 nA, got {iib}");
        Assert.True(iio > 2e-9, $"expected offset current above 2 nA, got {iio}");
        Assert.Equal((ip + inn) / 2, iib, precision: 12);
        Assert.Equal(Math.Abs(ip - inn), iio, precision: 12);
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task BenchRun_DifferentialPassiveFilter_SourceFlow_RunsInheritedTransferBench()
    {
        var cascodePath = Path.Combine(_outputDir, "DiffPassiveRc.cas");
        await File.WriteAllTextAsync(
            cascodePath,
            $$"""
            VERSION {{CascodeVersion.Current}}

            include lib.std

            circuit DiffPassiveRc implements DifferentialPassiveFilter {
              level EL

              input IN : Diff
              output OUT : Diff
              ground GND

              env {
                InputCommonModeRange = 0V
                SourceImpedance = 0Ohm
                LoadImpedance = 1GOhm
              }

              constraints {
                numeric {
                  c_gain = transfer_bench::PassbandGain >= -0.1dB
                }
              }

              fill {
                Capacitor C_DIFF = new CapacitorIdeal(size(C=1p)) {
                  .N--OUT.N
                  .P--OUT.P
                }

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
            """
        );

        await RunBenchAsync(cascodePath, "transfer_bench");

        var resultsPath = Directory
            .GetFiles(
                _outputDir,
                "DiffPassiveRc*transfer_bench*_results.json",
                SearchOption.TopDirectoryOnly
            )
            .SingleOrDefault();
        Assert.False(string.IsNullOrWhiteSpace(resultsPath), "results.json not found");

        var results = await ReadBenchResultsAsync(resultsPath!);
        Assert.Equal("DiffPassiveRc", results.Circuit);
        Assert.Equal("transfer_bench", results.Bench);
        Assert.True(results.Measurements.ContainsKey("PassbandGain"));
        Assert.True(results.Measurements["PassbandGain"].Value.HasValue);

        var combinedResultsPath = Path.Combine(_outputDir, "DiffPassiveRc_results.json");
        Assert.True(File.Exists(combinedResultsPath), "combined results not found");

        await VerifyAsync(cascodePath, combinedResultsPath);
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task BenchRun_CSeries_SParamConstraintsPass()
    {
        var cascodePath = Path.Combine(_repoRoot, "tests/golden/cas/filters/CSeries.cas");

        await RunBenchAsync(cascodePath);

        var resultsPath = Path.Combine(_outputDir, "CSeries_sparam_bench_results.json");
        Assert.True(File.Exists(resultsPath), "results.json not found");

        var results = await ReadBenchResultsAsync(resultsPath);
        Assert.Equal("CSeries", results.Circuit);
        Assert.Equal("sparam_bench", results.Bench);
        Assert.True(results.Measurements.Count > 0, "expected at least one measurement");

        var combinedResultsPath = Path.Combine(_outputDir, "CSeries_results.json");
        Assert.True(File.Exists(combinedResultsPath), "combined results not found");

        await VerifyAsync(cascodePath, combinedResultsPath);
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task BenchRun_RSeries_SParamConstraintsPass()
    {
        var cascodePath = Path.Combine(_repoRoot, "tests/golden/cas/filters/RSeries.cas");

        await RunBenchAsync(cascodePath);

        var resultsPath = Path.Combine(_outputDir, "RSeriesQ_sparam_bench_results.json");
        Assert.True(File.Exists(resultsPath), "results.json not found");

        var results = await ReadBenchResultsAsync(resultsPath);
        Assert.Equal("RSeriesQ", results.Circuit);
        Assert.Equal("sparam_bench", results.Bench);
        Assert.True(results.Measurements.Count > 0, "expected at least one measurement");

        var combinedResultsPath = Path.Combine(_outputDir, "RSeriesQ_results.json");
        Assert.True(File.Exists(combinedResultsPath), "combined results not found");

        await VerifyAsync(cascodePath, combinedResultsPath);
    }

    private async Task RunBenchAsync(string cascodePath, params string[] additionalArgs)
    {
        var args = new List<string> { "bench", "run", cascodePath };
        args.AddRange(additionalArgs);
        args.Add("-o");
        args.Add(_outputDir);

        var run = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            [.. args]
        );
        CliIntegrationTestHelper.AssertSuccess(run, "bench run failed");
    }

    private async Task<BenchResult> ReadBenchResultsAsync(string resultsPath)
    {
        Assert.True(File.Exists(resultsPath), "results.json not found");
        var results = JsonSerializer.Deserialize<BenchResult>(
            await File.ReadAllTextAsync(resultsPath),
            s_jsonOptions
        );
        Assert.NotNull(results);
        return results!;
    }

    private async Task VerifyAsync(string cascodePath, string resultsPath)
    {
        var verify = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(10),
            _cascodeHome,
            "verify",
            cascodePath,
            resultsPath
        );
        CliIntegrationTestHelper.AssertSuccess(verify, "verify failed");
    }

    private static double AssertMeasurement(BenchResult results, string name)
    {
        Assert.True(results.Measurements.ContainsKey(name), $"measurement '{name}' missing");
        var measurement = results.Measurements[name];
        Assert.True(string.IsNullOrEmpty(measurement.Error), measurement.Error);
        Assert.True(measurement.Value.HasValue, $"measurement '{name}' has no value");
        Assert.False(double.IsNaN(measurement.Value!.Value), $"measurement '{name}' is NaN");
        return measurement.Value.Value;
    }
}
