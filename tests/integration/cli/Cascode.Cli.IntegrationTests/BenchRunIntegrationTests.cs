using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Cascode.Bench;
using Cascode.Cli.IntegrationTests.Infrastructure;
using Cascode.Language;
using Cascode.Language.BenchRuntime;
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

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task BenchRun_SingleResistor_OnePortSParamConstraintsPass()
    {
        var cascodePath = Path.Combine(_repoRoot, "tests/golden/cas/bench/SingleResistor.cas");

        await RunBenchAsync(cascodePath);

        var resultsPath = Path.Combine(_outputDir, "SingleResistor_sparam_bench_results.json");
        Assert.True(File.Exists(resultsPath), "results.json not found");

        var results = await ReadBenchResultsAsync(resultsPath);
        Assert.Equal("SingleResistor", results.Circuit);
        Assert.Equal("sparam_bench", results.Bench);
        Assert.True(
            results.Measurements.ContainsKey("S11(from=1MHz, to=100MHz)"),
            "expected S11(from=1MHz, to=100MHz) measurement in bench results"
        );
        Assert.True(
            results.Measurements.ContainsKey("ReturnLoss(from=1MHz, to=100MHz)"),
            "expected ReturnLoss(from=1MHz, to=100MHz) measurement in bench results"
        );
        Assert.True(string.IsNullOrEmpty(results.Measurements["S11(from=1MHz, to=100MHz)"].Error));
        Assert.True(
            string.IsNullOrEmpty(results.Measurements["ReturnLoss(from=1MHz, to=100MHz)"].Error)
        );
        Assert.NotNull(results.Measurements["S11(from=1MHz, to=100MHz)"].Values);
        Assert.NotNull(results.Measurements["ReturnLoss(from=1MHz, to=100MHz)"].Values);
        Assert.NotEmpty(results.Measurements["S11(from=1MHz, to=100MHz)"].Values!);
        Assert.NotEmpty(results.Measurements["ReturnLoss(from=1MHz, to=100MHz)"].Values!);

        var combinedResultsPath = Path.Combine(_outputDir, "SingleResistor_results.json");
        Assert.True(File.Exists(combinedResultsPath), "combined results not found");

        await VerifyAsync(cascodePath, combinedResultsPath);
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task BenchRun_LCSeries_PSSConstraintsPass()
    {
        var cascodePath = Path.Combine(_repoRoot, "tests/golden/cas/bench/LCSeries.cas");

        await RunBenchAsync(cascodePath, TimeSpan.FromSeconds(60));

        var instanceName = BenchInvocationName.Compute(
            "pss_bench",
            new[] { new MetricCallArg("guess_frequency", "100MHz") }
        );
        var resultsPath = Path.Combine(_outputDir, $"LCSeries_{instanceName}_results.json");
        Assert.True(File.Exists(resultsPath), "results.json not found");

        var results = await ReadBenchResultsAsync(resultsPath);
        Assert.Equal("LCSeries", results.Circuit);
        Assert.Equal(instanceName, results.Bench);
        Assert.True(
            results.Measurements.ContainsKey("FundamentalFrequency"),
            "FundamentalFrequency measurement missing"
        );
        Assert.True(
            string.IsNullOrEmpty(results.Measurements["FundamentalFrequency"].Error),
            "FundamentalFrequency had error: " + results.Measurements["FundamentalFrequency"].Error
        );
        Assert.True(results.Measurements["FundamentalFrequency"].Value.HasValue);
        Assert.False(double.IsNaN(results.Measurements["FundamentalFrequency"].Value!.Value));

        var combinedResultsPath = Path.Combine(_outputDir, "LCSeries_results.json");
        Assert.True(File.Exists(combinedResultsPath), "combined results not found");

        await VerifyAsync(cascodePath, combinedResultsPath);
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task BenchRun_LCTank_PSSConstraintsPass()
    {
        var cascodePath = Path.Combine(_repoRoot, "tests/golden/cas/osc/LCTank.cas");

        await RunBenchAsync(cascodePath, TimeSpan.FromSeconds(60));

        var instanceName = BenchInvocationName.Compute(
            "pss_bench",
            new[] { new MetricCallArg("guess_frequency", "2.8GHz") }
        );
        var resultsPath = Path.Combine(_outputDir, $"LCTank_{instanceName}_results.json");
        Assert.True(File.Exists(resultsPath), "results.json not found");

        var results = await ReadBenchResultsAsync(resultsPath);
        Assert.Equal("LCTank", results.Circuit);
        Assert.Equal(instanceName, results.Bench);
        Assert.True(
            results.Measurements.ContainsKey("FundamentalFrequency"),
            "FundamentalFrequency measurement missing"
        );
        Assert.True(
            string.IsNullOrEmpty(results.Measurements["FundamentalFrequency"].Error),
            "FundamentalFrequency had error: " + results.Measurements["FundamentalFrequency"].Error
        );
        Assert.True(results.Measurements["FundamentalFrequency"].Value.HasValue);
        Assert.False(double.IsNaN(results.Measurements["FundamentalFrequency"].Value!.Value));

        var combinedResultsPath = Path.Combine(_outputDir, "LCTank_results.json");
        Assert.True(File.Exists(combinedResultsPath), "combined results not found");

        await VerifyAsync(cascodePath, combinedResultsPath);
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task BenchRun_LCOscSky130_PSSConstraintsPass()
    {
        var cascodePath = Path.Combine(_repoRoot, "tests/golden/cas/osc/LCOsc_Sky130.cas");
        var pdkRoot = Path.Combine(_repoRoot, "tests/fixtures/pdk/sky130");

        await SetupPdkAsync(pdkRoot);
        await RunBenchAsync(cascodePath, TimeSpan.FromSeconds(60));

        var instanceName = BenchInvocationName.Compute(
            "pss_bench",
            new[] { new MetricCallArg("guess_frequency", "2.7GHz") }
        );
        var resultsPath = Path.Combine(_outputDir, $"LCOsc_Sky130_{instanceName}_results.json");
        Assert.True(File.Exists(resultsPath), "results.json not found");

        var results = await ReadBenchResultsAsync(resultsPath);
        Assert.Equal("LCOsc_Sky130", results.Circuit);
        Assert.Equal(instanceName, results.Bench);
        Assert.True(
            results.Measurements.ContainsKey("FundamentalFrequency"),
            "FundamentalFrequency measurement missing"
        );
        Assert.True(
            string.IsNullOrEmpty(results.Measurements["FundamentalFrequency"].Error),
            "FundamentalFrequency had error: " + results.Measurements["FundamentalFrequency"].Error
        );
        Assert.True(results.Measurements["FundamentalFrequency"].Value.HasValue);
        Assert.False(double.IsNaN(results.Measurements["FundamentalFrequency"].Value!.Value));

        var combinedResultsPath = Path.Combine(_outputDir, "LCOsc_Sky130_results.json");
        Assert.True(File.Exists(combinedResultsPath), "combined results not found");

        await VerifyAsync(cascodePath, combinedResultsPath);
    }

    private async Task RunBenchAsync(string cascodePath, params string[] additionalArgs)
    {
        await RunBenchAsync(cascodePath, TimeSpan.FromSeconds(30), additionalArgs);
    }

    private async Task RunBenchAsync(
        string cascodePath,
        TimeSpan timeout,
        params string[] additionalArgs
    )
    {
        var args = new List<string> { "bench", "run", cascodePath };
        args.AddRange(additionalArgs);
        args.Add("-o");
        args.Add(_outputDir);

        var run = await CliIntegrationTestHelper.RunCliAsync(timeout, _cascodeHome, [.. args]);
        CliIntegrationTestHelper.AssertSuccess(run, "bench run failed");
    }

    private async Task SetupPdkAsync(string pdkRoot)
    {
        var pdkSet = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(10),
            _cascodeHome,
            "pdk",
            "set-dir",
            pdkRoot
        );
        CliIntegrationTestHelper.AssertSuccess(pdkSet, "pdk set-dir failed");

        var scan = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(3),
            _cascodeHome,
            "pdk",
            "scan",
            pdkRoot
        );
        CliIntegrationTestHelper.AssertSuccess(scan, "pdk scan failed");
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
}
