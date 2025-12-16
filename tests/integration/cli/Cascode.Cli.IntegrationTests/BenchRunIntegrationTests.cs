using System;
using System.IO;
using System.Linq;
using System.Text.Json;
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

    public BenchRunIntegrationTests()
    {
        _repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        _outputDir = Path.Combine(Path.GetTempPath(), "cascode-bench-run-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_outputDir);
        _cascodeHome = CliIntegrationTestHelper.CreateCascodeHome(_repoRoot, "bench-run");
    }

    public void Dispose()
    {
        _cascodeHome.Dispose();
        if (Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task BenchRun_OTA_DCSwept_WritesTraceAndScalarPower()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/ota/OTA5TSingleEnded_DCSwept.el.cir");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(60),
            _cascodeHome,
            "bench", "run",
            acirPath,
            "-o", _outputDir);

        CliIntegrationTestHelper.AssertSuccess(result, "bench run failed");

        var resultsPath = Path.Combine(_outputDir, "OTA5TSingleEnded_DCSwept_SEOpAmpDCBench_results.json");
        var tracePath = Path.Combine(_outputDir, "OTA5TSingleEnded_DCSwept_SEOpAmpDCBench_trace.jsonl");

        Assert.True(File.Exists(resultsPath), "results.json not found");
        Assert.True(File.Exists(tracePath), "trace.jsonl not found");

        var benchResults = JsonSerializer.Deserialize<BenchResult>(await File.ReadAllTextAsync(resultsPath));
        Assert.NotNull(benchResults);

        var power = benchResults!.Measurements.Values.Where(m => m.Metric == "QuiescentPower").ToList();
        Assert.Single(power);

        var traceText = await File.ReadAllTextAsync(tracePath);
        Assert.Contains("\"type\":\"meta\"", traceText);
        Assert.Contains("\"type\":\"point\"", traceText);
        Assert.Contains("\"type\":\"summary\"", traceText);
        Assert.Contains("QuiescentPower", traceText);

        var verify = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "verify", acirPath, tracePath);
        CliIntegrationTestHelper.AssertSuccess(verify, "verify with positional args failed");
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task BenchRun_MultiBench_DefaultRunsAllAndWritesCombinedResults()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/cs/CommonSourceAmp_MultiBench.el.cir");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(60),
            _cascodeHome,
            "bench", "run",
            acirPath,
            "-o", _outputDir);

        CliIntegrationTestHelper.AssertSuccess(result, "bench run failed");
        Assert.DoesNotContain("info:", result.Stdout, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("> bench run", result.Stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Circuit:", result.Stdout, StringComparison.Ordinal);
        Assert.Contains("Artifacts:", result.Stdout, StringComparison.Ordinal);
        Assert.Contains("Compliance:", result.Stdout, StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(_outputDir, "CommonSourceAmp_MultiBench_SEAmpACBench_results.json")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "CommonSourceAmp_MultiBench_SEAmpACBench_trace.jsonl")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "CommonSourceAmp_MultiBench_SEAmpDCBench_results.json")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "CommonSourceAmp_MultiBench_SEAmpDCBench_trace.jsonl")));

        var combinedResults = Path.Combine(_outputDir, "CommonSourceAmp_MultiBench_results.json");
        Assert.True(File.Exists(combinedResults));

        var verify = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "verify", acirPath, combinedResults);
        CliIntegrationTestHelper.AssertSuccess(verify, "verify with combined results failed");
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task BenchRun_FD_OTA_DCSwept_WritesTraceAndScalarPower()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/ota/OTA5TFullyDiff_DCSwept.el.cir");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(60),
            _cascodeHome,
            "bench", "run",
            acirPath,
            "-o", _outputDir);

        CliIntegrationTestHelper.AssertSuccess(result, "bench run failed");

        var resultsPath = Path.Combine(_outputDir, "OTA5TFullyDiff_DCSwept_FDOpAmpDCBench_results.json");
        var tracePath = Path.Combine(_outputDir, "OTA5TFullyDiff_DCSwept_FDOpAmpDCBench_trace.jsonl");

        Assert.True(File.Exists(resultsPath), "results.json not found");
        Assert.True(File.Exists(tracePath), "trace.jsonl not found");

        var benchResults = JsonSerializer.Deserialize<BenchResult>(await File.ReadAllTextAsync(resultsPath));
        Assert.NotNull(benchResults);

        var power = benchResults!.Measurements.Values.Where(m => m.Metric == "QuiescentPower").ToList();
        Assert.Single(power);

        var traceText = await File.ReadAllTextAsync(tracePath);
        Assert.Contains("\"type\":\"meta\"", traceText);
        Assert.Contains("\"type\":\"point\"", traceText);
        Assert.Contains("\"type\":\"summary\"", traceText);
        Assert.Contains("QuiescentPower", traceText);

        var verify = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "verify", acirPath, tracePath);
        CliIntegrationTestHelper.AssertSuccess(verify, "verify with positional args failed");
    }
}
