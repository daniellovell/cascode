using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
    public async Task BenchRun_OTA_DCSwept_WritesTraceAndScalarPower()
    {
        var acirPath = Path.Combine(
            _repoRoot,
            "tests/golden/acir/ota/OTA5TSingleEnded_DCSwept.el.cir"
        );

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(60),
            _cascodeHome,
            "bench",
            "run",
            acirPath,
            "-o",
            _outputDir
        );

        CliIntegrationTestHelper.AssertSuccess(result, "bench run failed");

        var resultsPath = Path.Combine(
            _outputDir,
            "OTA5TSingleEnded_DCSwept_SEOpAmpDCBench_results.json"
        );
        var tracePath = Path.Combine(
            _outputDir,
            "OTA5TSingleEnded_DCSwept_SEOpAmpDCBench_trace.jsonl"
        );

        Assert.True(File.Exists(resultsPath), "results.json not found");
        Assert.True(File.Exists(tracePath), "trace.jsonl not found");

        var benchResults = JsonSerializer.Deserialize<BenchResult>(
            await File.ReadAllTextAsync(resultsPath),
            s_jsonOptions
        );
        Assert.NotNull(benchResults);

        var power = benchResults!
            .Measurements.Values.Where(m => m.Metric == "QuiescentPower")
            .ToList();
        Assert.Single(power);
        Assert.False(
            double.IsNaN(power[0].Value),
            $"QuiescentPower measurement is NaN - simulation likely failed"
        );
        Assert.True(power[0].Value > 0, $"QuiescentPower should be positive, got {power[0].Value}");

        var traceText = await File.ReadAllTextAsync(tracePath);
        Assert.Contains("\"type\":\"meta\"", traceText);
        Assert.Contains("\"type\":\"point\"", traceText);
        Assert.Contains("\"type\":\"summary\"", traceText);
        Assert.Contains("QuiescentPower", traceText);

        var verify = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "verify",
            acirPath,
            tracePath
        );
        CliIntegrationTestHelper.AssertSuccess(verify, "verify with positional args failed");
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task BenchRun_MultiBench_DefaultRunsAllAndWritesCombinedResults()
    {
        var acirPath = Path.Combine(
            _repoRoot,
            "tests/golden/acir/cs/CommonSourceAmp_MultiBench.el.cir"
        );

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(60),
            _cascodeHome,
            "bench",
            "run",
            acirPath,
            "-o",
            _outputDir
        );

        CliIntegrationTestHelper.AssertSuccess(result, "bench run failed");
        Assert.DoesNotContain("info:", result.Stdout, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("> bench run", result.Stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Circuit:", result.Stdout, StringComparison.Ordinal);
        Assert.Contains("Artifacts:", result.Stdout, StringComparison.Ordinal);
        Assert.Contains("Compliance:", result.Stdout, StringComparison.Ordinal);

        Assert.True(
            File.Exists(
                Path.Combine(_outputDir, "CommonSourceAmp_MultiBench_SEAmpACBench_results.json")
            )
        );
        Assert.True(
            File.Exists(
                Path.Combine(_outputDir, "CommonSourceAmp_MultiBench_SEAmpACBench_trace.jsonl")
            )
        );
        Assert.True(
            File.Exists(
                Path.Combine(_outputDir, "CommonSourceAmp_MultiBench_SEAmpDCBench_results.json")
            )
        );
        Assert.True(
            File.Exists(
                Path.Combine(_outputDir, "CommonSourceAmp_MultiBench_SEAmpDCBench_trace.jsonl")
            )
        );

        var combinedResults = Path.Combine(_outputDir, "CommonSourceAmp_MultiBench_results.json");
        Assert.True(File.Exists(combinedResults));

        var combinedBenchResults = JsonSerializer.Deserialize<BenchResult>(
            await File.ReadAllTextAsync(combinedResults),
            s_jsonOptions
        );
        Assert.NotNull(combinedBenchResults);

        // Verify key measurements are not NaN
        foreach (var measurement in combinedBenchResults!.Measurements.Values)
        {
            Assert.False(
                double.IsNaN(measurement.Value),
                $"Measurement '{measurement.Metric}' is NaN - simulation likely failed"
            );
        }

        var verify = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "verify",
            acirPath,
            combinedResults
        );
        CliIntegrationTestHelper.AssertSuccess(verify, "verify with combined results failed");
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task BenchRun_FD_OTA_DCSwept_WritesTraceAndScalarPower()
    {
        var acirPath = Path.Combine(
            _repoRoot,
            "tests/golden/acir/ota/OTA5TFullyDiff_DCSwept.el.cir"
        );

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(60),
            _cascodeHome,
            "bench",
            "run",
            acirPath,
            "-o",
            _outputDir
        );

        CliIntegrationTestHelper.AssertSuccess(result, "bench run failed");

        var resultsPath = Path.Combine(
            _outputDir,
            "OTA5TFullyDiff_DCSwept_FDOpAmpDCBench_results.json"
        );
        var tracePath = Path.Combine(
            _outputDir,
            "OTA5TFullyDiff_DCSwept_FDOpAmpDCBench_trace.jsonl"
        );

        Assert.True(File.Exists(resultsPath), "results.json not found");
        Assert.True(File.Exists(tracePath), "trace.jsonl not found");

        var benchResults = JsonSerializer.Deserialize<BenchResult>(
            await File.ReadAllTextAsync(resultsPath),
            s_jsonOptions
        );
        Assert.NotNull(benchResults);

        var power = benchResults!
            .Measurements.Values.Where(m => m.Metric == "QuiescentPower")
            .ToList();
        Assert.Single(power);
        Assert.False(
            double.IsNaN(power[0].Value),
            $"QuiescentPower measurement is NaN - simulation likely failed"
        );
        Assert.True(power[0].Value > 0, $"QuiescentPower should be positive, got {power[0].Value}");

        var traceText = await File.ReadAllTextAsync(tracePath);
        Assert.Contains("\"type\":\"meta\"", traceText);
        Assert.Contains("\"type\":\"point\"", traceText);
        Assert.Contains("\"type\":\"summary\"", traceText);
        Assert.Contains("QuiescentPower", traceText);

        var verify = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "verify",
            acirPath,
            tracePath
        );
        CliIntegrationTestHelper.AssertSuccess(verify, "verify with positional args failed");
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task BenchRun_PdkSetDir_UsesPdkIncludesAndSimulates()
    {
        var pdkRoot = Path.Combine(_repoRoot, "tests/fixtures/pdk/sky130");
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/ota/OTA5TSingleEnded_Pdk.el.cir");
        var outputDir = Path.Combine(_outputDir, "pdk-setdir-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(outputDir);

        var setDirResult = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "pdk",
            "set-dir",
            pdkRoot
        );
        CliIntegrationTestHelper.AssertSuccess(setDirResult, "pdk set-dir failed");

        var scanResult = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(2),
            _cascodeHome,
            "pdk",
            "scan"
        );
        CliIntegrationTestHelper.AssertSuccess(scanResult, "pdk scan failed");

        var benchResult = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(90),
            _cascodeHome,
            "bench",
            "run",
            acirPath,
            "-o",
            outputDir
        );
        CliIntegrationTestHelper.AssertSuccess(benchResult, "bench run failed");

        var benchPath = Path.Combine(outputDir, "OTA5TSingleEnded_Pdk_SEOpAmpACBench.sp");
        Assert.True(File.Exists(benchPath), "PDK bench netlist not found");

        var content = await File.ReadAllTextAsync(benchPath);
        Assert.Matches(
            new Regex(@"\.lib\s+""[^""]*sky130\.lib\.spice""\s+tt", RegexOptions.IgnoreCase),
            content
        );

        var resultsPath = Path.Combine(
            outputDir,
            "OTA5TSingleEnded_Pdk_SEOpAmpACBench_results.json"
        );
        Assert.True(File.Exists(resultsPath), "PDK results.json not found");
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task BenchRun_PdkWorkspaceFlag_UsesPdkIncludesAndSimulates()
    {
        var pdkRoot = Path.Combine(_repoRoot, "tests/fixtures/pdk/sky130");
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/ota/OTA5TSingleEnded_Pdk.el.cir");
        var outputDir = Path.Combine(
            _outputDir,
            "pdk-workspace-" + Guid.NewGuid().ToString("N")[..8]
        );
        Directory.CreateDirectory(outputDir);

        var scanResult = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(2),
            _cascodeHome,
            "pdk",
            "scan",
            pdkRoot
        );
        CliIntegrationTestHelper.AssertSuccess(scanResult, "pdk scan failed");

        var benchResult = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(90),
            _cascodeHome,
            "--workspace",
            pdkRoot,
            "bench",
            "run",
            acirPath,
            "-o",
            outputDir
        );
        CliIntegrationTestHelper.AssertSuccess(benchResult, "bench run failed");

        var benchPath = Path.Combine(outputDir, "OTA5TSingleEnded_Pdk_SEOpAmpACBench.sp");
        Assert.True(File.Exists(benchPath), "PDK bench netlist not found");

        var content = await File.ReadAllTextAsync(benchPath);
        Assert.Matches(
            new Regex(@"\.lib\s+""[^""]*sky130\.lib\.spice""\s+tt", RegexOptions.IgnoreCase),
            content
        );

        var resultsPath = Path.Combine(
            outputDir,
            "OTA5TSingleEnded_Pdk_SEOpAmpACBench_results.json"
        );
        Assert.True(File.Exists(resultsPath), "PDK results.json not found");
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task BenchRun_FD_OTA_ACBench_ProducesValidMeasurements()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/ota/OTA5TFullyDiff.el.cir");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(60),
            _cascodeHome,
            "bench",
            "run",
            acirPath,
            "-o",
            _outputDir
        );

        CliIntegrationTestHelper.AssertSuccess(result, "bench run failed");

        var resultsPath = Path.Combine(_outputDir, "OTA5TFullyDiff_FDOpAmpACBench_results.json");
        Assert.True(File.Exists(resultsPath), "AC results.json not found");

        var benchResults = JsonSerializer.Deserialize<BenchResult>(
            await File.ReadAllTextAsync(resultsPath),
            s_jsonOptions
        );
        Assert.NotNull(benchResults);

        // Core assertions - these would have caught the vdb(OUT_P, OUT_N) bug
        AssertMeasurementValid(benchResults!, "PassbandGain", minValue: 0, maxValue: 200);
        AssertMeasurementValid(benchResults!, "GainBandwidth", minValue: 1e3, maxValue: 1e12);
        AssertMeasurementValid(benchResults!, "PhaseMargin", minValue: 0, maxValue: 360);
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task BenchRun_SE_OTA_ACBench_ProducesValidMeasurements()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/ota/OTA5TSingleEnded.el.cir");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(60),
            _cascodeHome,
            "bench",
            "run",
            acirPath,
            "-o",
            _outputDir
        );

        CliIntegrationTestHelper.AssertSuccess(result, "bench run failed");

        var resultsPath = Path.Combine(_outputDir, "OTA5TSingleEnded_SEOpAmpACBench_results.json");
        Assert.True(File.Exists(resultsPath), "AC results.json not found");

        var benchResults = JsonSerializer.Deserialize<BenchResult>(
            await File.ReadAllTextAsync(resultsPath),
            s_jsonOptions
        );
        Assert.NotNull(benchResults);

        // Core assertions - validates SE template produces valid measurements
        AssertMeasurementValid(benchResults!, "PassbandGain@OUT", minValue: 0, maxValue: 200);
        AssertMeasurementValid(benchResults!, "GainBandwidth@OUT", minValue: 1e3, maxValue: 1e12);
        AssertMeasurementValid(benchResults!, "PhaseMargin@OUT", minValue: 0, maxValue: 360);
    }

    private static void AssertMeasurementValid(
        BenchResult results,
        string metric,
        double minValue,
        double maxValue
    )
    {
        Assert.True(
            results.Measurements.TryGetValue(metric, out var m),
            $"Measurement '{metric}' not found"
        );
        Assert.False(
            double.IsNaN(m.Value),
            $"Measurement '{metric}' is NaN - simulation measurement failed"
        );
        Assert.True(
            m.Value >= minValue && m.Value <= maxValue,
            $"Measurement '{metric}' = {m.Value} outside expected range [{minValue}, {maxValue}]"
        );
    }
}
