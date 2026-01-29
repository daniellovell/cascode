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

public sealed partial class BenchRunIntegrationTests : IDisposable
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
        var cascodePath = Path.Combine(
            _repoRoot,
            "tests/golden/cascode/ota/OTA5TSingleEnded_DCSwept.el.cas"
        );

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(60),
            _cascodeHome,
            "bench",
            "run",
            cascodePath,
            "-o",
            _outputDir
        );

        CliIntegrationTestHelper.AssertSuccess(result, "bench run failed");

        var resultsPath = Path.Combine(_outputDir, "OTA5TSingleEnded_DCSwept_DCBench_results.json");
        var tracePath = Path.Combine(_outputDir, "OTA5TSingleEnded_DCSwept_DCBench_trace.jsonl");

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
            cascodePath,
            tracePath
        );
        CliIntegrationTestHelper.AssertSuccess(verify, "verify with positional args failed");
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task BenchRun_MultiBench_DefaultRunsAllAndWritesCombinedResults()
    {
        var cascodePath = Path.Combine(
            _repoRoot,
            "tests/golden/cascode/cs/CommonSourceAmp_MultiBench.el.cas"
        );

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(60),
            _cascodeHome,
            "bench",
            "run",
            cascodePath,
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
            File.Exists(Path.Combine(_outputDir, "CommonSourceAmp_MultiBench_ACBench_results.json"))
        );
        Assert.True(
            File.Exists(Path.Combine(_outputDir, "CommonSourceAmp_MultiBench_ACBench_trace.jsonl"))
        );
        Assert.True(
            File.Exists(Path.Combine(_outputDir, "CommonSourceAmp_MultiBench_DCBench_results.json"))
        );
        Assert.True(
            File.Exists(Path.Combine(_outputDir, "CommonSourceAmp_MultiBench_DCBench_trace.jsonl"))
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
            cascodePath,
            combinedResults
        );
        CliIntegrationTestHelper.AssertSuccess(verify, "verify with combined results failed");
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task BenchRun_FD_OTA_DCSwept_WritesTraceAndScalarPower()
    {
        var cascodePath = Path.Combine(
            _repoRoot,
            "tests/golden/cascode/ota/OTA5TFullyDiff_DCSwept.el.cas"
        );

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(60),
            _cascodeHome,
            "bench",
            "run",
            cascodePath,
            "-o",
            _outputDir
        );

        CliIntegrationTestHelper.AssertSuccess(result, "bench run failed");

        var resultsPath = Path.Combine(_outputDir, "OTA5TFullyDiff_DCSwept_DCBench_results.json");
        var tracePath = Path.Combine(_outputDir, "OTA5TFullyDiff_DCSwept_DCBench_trace.jsonl");

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
            cascodePath,
            tracePath
        );
        CliIntegrationTestHelper.AssertSuccess(verify, "verify with positional args failed");
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task BenchRun_PdkSetDir_UsesPdkIncludesAndSimulates()
    {
        var pdkRoot = Path.Combine(_repoRoot, "tests/fixtures/pdk/sky130");
        var cascodePath = Path.Combine(
            _repoRoot,
            "tests/golden/cascode/ota/OTA5TSingleEnded_Pdk.el.cas"
        );
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
            cascodePath,
            "-o",
            outputDir
        );
        CliIntegrationTestHelper.AssertSuccess(benchResult, "bench run failed");

        var benchPath = Path.Combine(outputDir, "OTA5TSingleEnded_Pdk_ACBench.sp");
        Assert.True(File.Exists(benchPath), "PDK bench netlist not found");

        var content = await File.ReadAllTextAsync(benchPath);
        Assert.Matches(Sky130LibIncludePattern(), content);

        var resultsPath = Path.Combine(outputDir, "OTA5TSingleEnded_Pdk_ACBench_results.json");
        Assert.True(File.Exists(resultsPath), "PDK results.json not found");
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task BenchRun_PdkWorkspaceFlag_UsesPdkIncludesAndSimulates()
    {
        var pdkRoot = Path.Combine(_repoRoot, "tests/fixtures/pdk/sky130");
        var cascodePath = Path.Combine(
            _repoRoot,
            "tests/golden/cascode/ota/OTA5TSingleEnded_Pdk.el.cas"
        );
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
            cascodePath,
            "-o",
            outputDir
        );
        CliIntegrationTestHelper.AssertSuccess(benchResult, "bench run failed");

        var benchPath = Path.Combine(outputDir, "OTA5TSingleEnded_Pdk_ACBench.sp");
        Assert.True(File.Exists(benchPath), "PDK bench netlist not found");

        var content = await File.ReadAllTextAsync(benchPath);
        Assert.Matches(Sky130LibIncludePattern(), content);

        var resultsPath = Path.Combine(outputDir, "OTA5TSingleEnded_Pdk_ACBench_results.json");
        Assert.True(File.Exists(resultsPath), "PDK results.json not found");
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task BenchRun_FD_OTA_ACBench_ProducesValidMeasurements()
    {
        var cascodePath = Path.Combine(_repoRoot, "tests/golden/cascode/ota/OTA5TFullyDiff.el.cas");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(60),
            _cascodeHome,
            "bench",
            "run",
            cascodePath,
            "-o",
            _outputDir
        );

        CliIntegrationTestHelper.AssertSuccess(result, "bench run failed");

        var resultsPath = Path.Combine(_outputDir, "OTA5TFullyDiff_ACBench_results.json");
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

        var tranResultsPath = Path.Combine(_outputDir, "OTA5TFullyDiff_TranBench_results.json");
        Assert.True(File.Exists(tranResultsPath), "Tran results.json not found");

        var tranResults = JsonSerializer.Deserialize<BenchResult>(
            await File.ReadAllTextAsync(tranResultsPath),
            s_jsonOptions
        );
        Assert.NotNull(tranResults);

        const double vdd = 1.8;
        AssertMeasurementValid(
            tranResults!,
            "DifferentialOutputSwing",
            minValue: 0,
            maxValue: 2 * vdd
        );
        AssertMeasurementValid(tranResults!, "SingleEndedOutputSwing", minValue: 0, maxValue: vdd);
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task BenchRun_SE_OTA_ACBench_ProducesValidMeasurements()
    {
        var cascodePath = Path.Combine(
            _repoRoot,
            "tests/golden/cascode/ota/OTA5TSingleEnded.el.cas"
        );

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(60),
            _cascodeHome,
            "bench",
            "run",
            cascodePath,
            "-o",
            _outputDir
        );

        CliIntegrationTestHelper.AssertSuccess(result, "bench run failed");

        var resultsPath = Path.Combine(_outputDir, "OTA5TSingleEnded_ACBench_results.json");
        Assert.True(File.Exists(resultsPath), "AC results.json not found");

        var benchResults = JsonSerializer.Deserialize<BenchResult>(
            await File.ReadAllTextAsync(resultsPath),
            s_jsonOptions
        );
        Assert.NotNull(benchResults);

        // Core assertions - validates SE template produces valid measurements
        AssertMeasurementValid(benchResults!, "PassbandGain@net::OUT", minValue: 0, maxValue: 200);
        AssertMeasurementValid(
            benchResults!,
            "GainBandwidth@net::OUT",
            minValue: 1e3,
            maxValue: 1e12
        );
        AssertMeasurementValid(benchResults!, "PhaseMargin@net::OUT", minValue: 0, maxValue: 360);

        var tranResultsPath = Path.Combine(_outputDir, "OTA5TSingleEnded_TranBench_results.json");
        Assert.True(File.Exists(tranResultsPath), "Tran results.json not found");

        var tranResults = JsonSerializer.Deserialize<BenchResult>(
            await File.ReadAllTextAsync(tranResultsPath),
            s_jsonOptions
        );
        Assert.NotNull(tranResults);

        const double vdd = 1.8;
        AssertMeasurementValid(
            tranResults!,
            "SingleEndedOutputSwing@net::OUT",
            minValue: 0,
            maxValue: vdd
        );
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task BenchRun_OTA5T_Hierarchical_MatchesFlatVersion()
    {
        // Run bench on flat OTA5TSingleEnded
        var flatCascodePath = Path.Combine(
            _repoRoot,
            "tests/golden/cascode/ota/OTA5TSingleEnded.el.cas"
        );
        var flatOutputDir = Path.Combine(_outputDir, "flat");
        Directory.CreateDirectory(flatOutputDir);

        var flatResult = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(60),
            _cascodeHome,
            "bench",
            "run",
            flatCascodePath,
            "-o",
            flatOutputDir
        );

        CliIntegrationTestHelper.AssertSuccess(flatResult, "bench run failed for flat OTA5T");

        // Run bench on hierarchical OTA5T_Hierarchical
        var hierarchicalCascodePath = Path.Combine(
            _repoRoot,
            "tests/golden/cascode/hierarchy/OTA5T_Hierarchical.el.cas"
        );
        var hierarchicalOutputDir = Path.Combine(_outputDir, "hierarchical");
        Directory.CreateDirectory(hierarchicalOutputDir);

        var hierarchicalResult = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(60),
            _cascodeHome,
            "bench",
            "run",
            hierarchicalCascodePath,
            "-o",
            hierarchicalOutputDir
        );

        CliIntegrationTestHelper.AssertSuccess(
            hierarchicalResult,
            "bench run failed for hierarchical OTA5T"
        );

        // Load AC bench results for both
        var flatAcResultsPath = Path.Combine(
            flatOutputDir,
            "OTA5TSingleEnded_ACBench_results.json"
        );
        var hierarchicalAcResultsPath = Path.Combine(
            hierarchicalOutputDir,
            "OTA5T_Hierarchical_ACBench_results.json"
        );

        Assert.True(File.Exists(flatAcResultsPath), "Flat AC results not found");
        Assert.True(File.Exists(hierarchicalAcResultsPath), "Hierarchical AC results not found");

        var flatAcResults = JsonSerializer.Deserialize<BenchResult>(
            await File.ReadAllTextAsync(flatAcResultsPath),
            s_jsonOptions
        );
        var hierarchicalAcResults = JsonSerializer.Deserialize<BenchResult>(
            await File.ReadAllTextAsync(hierarchicalAcResultsPath),
            s_jsonOptions
        );

        Assert.NotNull(flatAcResults);
        Assert.NotNull(hierarchicalAcResults);

        // Compare AC measurements (5% tolerance)
        AssertMeasurementsMatch(
            flatAcResults!,
            hierarchicalAcResults!,
            "PassbandGain@net::OUT",
            tolerancePercent: 5.0
        );
        AssertMeasurementsMatch(
            flatAcResults!,
            hierarchicalAcResults!,
            "GainBandwidth@net::OUT",
            tolerancePercent: 5.0
        );
        AssertMeasurementsMatch(
            flatAcResults!,
            hierarchicalAcResults!,
            "PhaseMargin@net::OUT",
            tolerancePercent: 5.0
        );

        // Load DC bench results for both
        var flatDcResultsPath = Path.Combine(
            flatOutputDir,
            "OTA5TSingleEnded_DCBench_results.json"
        );
        var hierarchicalDcResultsPath = Path.Combine(
            hierarchicalOutputDir,
            "OTA5T_Hierarchical_DCBench_results.json"
        );

        Assert.True(File.Exists(flatDcResultsPath), "Flat DC results not found");
        Assert.True(File.Exists(hierarchicalDcResultsPath), "Hierarchical DC results not found");

        var flatDcResults = JsonSerializer.Deserialize<BenchResult>(
            await File.ReadAllTextAsync(flatDcResultsPath),
            s_jsonOptions
        );
        var hierarchicalDcResults = JsonSerializer.Deserialize<BenchResult>(
            await File.ReadAllTextAsync(hierarchicalDcResultsPath),
            s_jsonOptions
        );

        Assert.NotNull(flatDcResults);
        Assert.NotNull(hierarchicalDcResults);

        // Compare DC measurements (5% tolerance)
        AssertMeasurementsMatch(
            flatDcResults!,
            hierarchicalDcResults!,
            "QuiescentPower",
            tolerancePercent: 5.0
        );

        // Verify both pass constraints
        var flatVerify = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "verify",
            flatCascodePath,
            Path.Combine(flatOutputDir, "OTA5TSingleEnded_results.json")
        );
        CliIntegrationTestHelper.AssertSuccess(flatVerify, "verify failed for flat OTA5T");

        var hierarchicalVerify = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "verify",
            hierarchicalCascodePath,
            Path.Combine(hierarchicalOutputDir, "OTA5T_Hierarchical_results.json")
        );
        CliIntegrationTestHelper.AssertSuccess(
            hierarchicalVerify,
            "verify failed for hierarchical OTA5T"
        );
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task BenchRun_OTA5T_Hierarchical_Attach_MatchesFlatVersion()
    {
        // Run bench on flat OTA5TSingleEnded
        var flatCascodePath = Path.Combine(
            _repoRoot,
            "tests/golden/cascode/ota/OTA5TSingleEnded.el.cas"
        );
        var flatOutputDir = Path.Combine(_outputDir, "flat");
        Directory.CreateDirectory(flatOutputDir);

        var flatResult = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(60),
            _cascodeHome,
            "bench",
            "run",
            flatCascodePath,
            "-o",
            flatOutputDir
        );

        CliIntegrationTestHelper.AssertSuccess(flatResult, "bench run failed for flat OTA5T");

        // Run bench on hierarchical-attach OTA5T_Hierarchical_Attach
        var attachCascodePath = Path.Combine(
            _repoRoot,
            "tests/golden/cascode/hierarchy/OTA5T_Hierarchical_Attach.el.cas"
        );
        var attachOutputDir = Path.Combine(_outputDir, "attach");
        Directory.CreateDirectory(attachOutputDir);

        var attachResult = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(60),
            _cascodeHome,
            "bench",
            "run",
            attachCascodePath,
            "-o",
            attachOutputDir
        );

        CliIntegrationTestHelper.AssertSuccess(
            attachResult,
            "bench run failed for hierarchical-attach OTA5T"
        );

        // Load AC bench results for both
        var flatAcResultsPath = Path.Combine(
            flatOutputDir,
            "OTA5TSingleEnded_ACBench_results.json"
        );
        var attachAcResultsPath = Path.Combine(
            attachOutputDir,
            "OTA5T_Hierarchical_Attach_ACBench_results.json"
        );

        Assert.True(File.Exists(flatAcResultsPath), "Flat AC results not found");
        Assert.True(File.Exists(attachAcResultsPath), "Attach AC results not found");

        var flatAcResults = JsonSerializer.Deserialize<BenchResult>(
            await File.ReadAllTextAsync(flatAcResultsPath),
            s_jsonOptions
        );
        var attachAcResults = JsonSerializer.Deserialize<BenchResult>(
            await File.ReadAllTextAsync(attachAcResultsPath),
            s_jsonOptions
        );

        Assert.NotNull(flatAcResults);
        Assert.NotNull(attachAcResults);

        // Compare AC measurements (5% tolerance)
        AssertMeasurementsMatch(
            flatAcResults!,
            attachAcResults!,
            "PassbandGain@net::OUT",
            tolerancePercent: 5.0
        );
        AssertMeasurementsMatch(
            flatAcResults!,
            attachAcResults!,
            "GainBandwidth@net::OUT",
            tolerancePercent: 5.0
        );
        AssertMeasurementsMatch(
            flatAcResults!,
            attachAcResults!,
            "PhaseMargin@net::OUT",
            tolerancePercent: 5.0
        );

        // Load DC bench results for both
        var flatDcResultsPath = Path.Combine(
            flatOutputDir,
            "OTA5TSingleEnded_DCBench_results.json"
        );
        var attachDcResultsPath = Path.Combine(
            attachOutputDir,
            "OTA5T_Hierarchical_Attach_DCBench_results.json"
        );

        Assert.True(File.Exists(flatDcResultsPath), "Flat DC results not found");
        Assert.True(File.Exists(attachDcResultsPath), "Attach DC results not found");

        var flatDcResults = JsonSerializer.Deserialize<BenchResult>(
            await File.ReadAllTextAsync(flatDcResultsPath),
            s_jsonOptions
        );
        var attachDcResults = JsonSerializer.Deserialize<BenchResult>(
            await File.ReadAllTextAsync(attachDcResultsPath),
            s_jsonOptions
        );

        Assert.NotNull(flatDcResults);
        Assert.NotNull(attachDcResults);

        // Compare DC measurements (5% tolerance)
        AssertMeasurementsMatch(
            flatDcResults!,
            attachDcResults!,
            "QuiescentPower",
            tolerancePercent: 5.0
        );

        // Verify both pass constraints
        var flatVerify = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "verify",
            flatCascodePath,
            Path.Combine(flatOutputDir, "OTA5TSingleEnded_results.json")
        );
        CliIntegrationTestHelper.AssertSuccess(flatVerify, "verify failed for flat OTA5T");

        var attachVerify = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "verify",
            attachCascodePath,
            Path.Combine(attachOutputDir, "OTA5T_Hierarchical_Attach_results.json")
        );
        CliIntegrationTestHelper.AssertSuccess(
            attachVerify,
            "verify failed for hierarchical-attach OTA5T"
        );
    }

    private static void AssertMeasurementsMatch(
        BenchResult expected,
        BenchResult actual,
        string metric,
        double tolerancePercent
    )
    {
        Assert.True(
            expected.Measurements.TryGetValue(metric, out var expectedMeasurement),
            $"Expected measurement '{metric}' not found in flat results"
        );
        Assert.True(
            actual.Measurements.TryGetValue(metric, out var actualMeasurement),
            $"Actual measurement '{metric}' not found in hierarchical results"
        );

        Assert.False(double.IsNaN(expectedMeasurement.Value), $"Expected '{metric}' is NaN");
        Assert.False(double.IsNaN(actualMeasurement.Value), $"Actual '{metric}' is NaN");

        var difference = Math.Abs(expectedMeasurement.Value - actualMeasurement.Value);
        var toleranceValue = Math.Abs(expectedMeasurement.Value) * (tolerancePercent / 100.0);

        Assert.True(
            difference <= toleranceValue,
            $"Measurement '{metric}' mismatch: flat={expectedMeasurement.Value}, hierarchical={actualMeasurement.Value}, "
                + $"difference={difference}, tolerance={toleranceValue} ({tolerancePercent}%)"
        );
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

    [GeneratedRegex(@"\.lib\s+""[^""]*sky130\.lib\.spice""\s+tt", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex Sky130LibIncludePattern();
}
