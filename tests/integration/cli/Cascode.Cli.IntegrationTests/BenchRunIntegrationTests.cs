using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Cascode.Bench;
using Cascode.Cli.IntegrationTests.Infrastructure;
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

        var run = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "bench",
            "run",
            cascodePath,
            "-o",
            _outputDir
        );
        CliIntegrationTestHelper.AssertSuccess(run, "bench run failed");

        var resultsPath = Path.Combine(_outputDir, "RcLowpass_lp_results.json");
        var tracePath = Path.Combine(_outputDir, "RcLowpass_lp_trace.jsonl");

        Assert.True(File.Exists(resultsPath), "results.json not found");
        Assert.True(File.Exists(tracePath), "trace.jsonl not found");

        var results = JsonSerializer.Deserialize<BenchResult>(
            await File.ReadAllTextAsync(resultsPath),
            s_jsonOptions
        );
        Assert.NotNull(results);
        Assert.Equal("RcLowpass", results!.Circuit);
        Assert.Equal("lp", results.Bench);
        Assert.True(results.Measurements.ContainsKey("LowpassBandwidth"));
        Assert.False(double.IsNaN(results.Measurements["LowpassBandwidth"].Value));

        var verify = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(10),
            _cascodeHome,
            "verify",
            cascodePath,
            resultsPath
        );
        CliIntegrationTestHelper.AssertSuccess(verify, "verify failed");
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task BenchRun_MultiCircuit_RunsAllCircuitsWithBenches()
    {
        var cascodePath = Path.Combine(
            _repoRoot,
            "tests/golden/cas/bench/RcLowpassMultiCircuit.el.cai"
        );

        var run = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "bench",
            "run",
            cascodePath,
            "-o",
            _outputDir
        );
        CliIntegrationTestHelper.AssertSuccess(run, "bench run failed");

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

        var run = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "bench",
            "run",
            cascodePath,
            "--circuit",
            "RcLowpassB",
            "-o",
            _outputDir
        );
        CliIntegrationTestHelper.AssertSuccess(run, "bench run failed");

        Assert.False(File.Exists(Path.Combine(_outputDir, "RcLowpassA_lp_results.json")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "RcLowpassB_lp_results.json")));
    }
}
