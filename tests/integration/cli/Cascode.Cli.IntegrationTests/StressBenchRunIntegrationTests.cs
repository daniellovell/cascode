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

public sealed class StressBenchRunIntegrationTests : IDisposable
{
    private readonly string _repoRoot;
    private readonly string _outputDir;
    private readonly CascodeHomeScope _cascodeHome;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public StressBenchRunIntegrationTests()
    {
        _repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        _outputDir = Path.Combine(
            Path.GetTempPath(),
            "cascode-stress-bench-run-test-" + Guid.NewGuid().ToString("N")[..8]
        );
        Directory.CreateDirectory(_outputDir);
        _cascodeHome = CliIntegrationTestHelper.CreateCascodeHome(_repoRoot, "stress-bench-run");
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
    public async Task BenchRun_OTA5T_Sky130_CompletesSimulationsAndEmitsPdkIncludes()
    {
        var cascodePath = Path.Combine(_repoRoot, "tests/golden/cas/stress/OTA5T_Sky130.cas");
        var pdkRoot = Path.Combine(_repoRoot, "tests/fixtures/pdk/sky130");

        // Persist a PDK workspace root so `emit`/`bench run` can resolve model includes.
        var pdkSet = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(10),
            _cascodeHome,
            "pdk",
            "set-dir",
            pdkRoot
        );
        CliIntegrationTestHelper.AssertSuccess(pdkSet, "pdk set-dir failed");

        // Build the PDK DB used by .include/.lib resolution during emission.
        var scan = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(90),
            _cascodeHome,
            "pdk",
            "scan",
            pdkRoot
        );
        CliIntegrationTestHelper.AssertSuccess(scan, "pdk scan failed");

        var run = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(90),
            _cascodeHome,
            "bench",
            "run",
            cascodePath,
            "-o",
            _outputDir
        );
        CliIntegrationTestHelper.AssertSuccess(run, "bench run failed");

        var transferTb = Path.Combine(
            _outputDir,
            "OTA5T_Hierarchical_Attach_Pdk_transfer_bench.sp"
        );
        var noiseTb = Path.Combine(_outputDir, "OTA5T_Hierarchical_Attach_Pdk_noise_bench.sp");
        Assert.True(File.Exists(transferTb), "transfer testbench not found");
        Assert.True(File.Exists(noiseTb), "noise testbench not found");

        var transferText = await File.ReadAllTextAsync(transferTb);
        var noiseText = await File.ReadAllTextAsync(noiseTb);
        Assert.Contains("sky130.lib.spice", transferText);
        Assert.Contains("sky130.lib.spice", noiseText);

        var transferResultsPath = Path.Combine(
            _outputDir,
            "OTA5T_Hierarchical_Attach_Pdk_transfer_bench_results.json"
        );
        var noiseResultsPath = Path.Combine(
            _outputDir,
            "OTA5T_Hierarchical_Attach_Pdk_noise_bench_results.json"
        );
        Assert.True(File.Exists(transferResultsPath), "transfer results.json not found");
        Assert.True(File.Exists(noiseResultsPath), "noise results.json not found");

        var transferResults = JsonSerializer.Deserialize<BenchResult>(
            await File.ReadAllTextAsync(transferResultsPath),
            s_jsonOptions
        );
        Assert.NotNull(transferResults);
        Assert.Equal("OTA5T_Hierarchical_Attach_Pdk", transferResults!.Circuit);
        Assert.True(transferResults.Measurements.Count > 0);
        Assert.All(transferResults.Measurements.Values, m => Assert.False(double.IsNaN(m.Value)));

        var noiseResults = JsonSerializer.Deserialize<BenchResult>(
            await File.ReadAllTextAsync(noiseResultsPath),
            s_jsonOptions
        );
        Assert.NotNull(noiseResults);
        Assert.Equal("OTA5T_Hierarchical_Attach_Pdk", noiseResults!.Circuit);
        Assert.True(noiseResults.Measurements.Count > 0);
        Assert.All(noiseResults.Measurements.Values, m => Assert.False(double.IsNaN(m.Value)));
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task BenchRun_OTA5TFullyDiff_Ideal_CompletesAndSplitsDiffLoadImpedance()
    {
        var cascodePath = Path.Combine(
            _repoRoot,
            "tests/golden/cas/stress/OTA5TFullyDiff_Ideal.cas"
        );

        var run = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(60),
            _cascodeHome,
            "bench",
            "run",
            cascodePath,
            "-o",
            _outputDir
        );
        CliIntegrationTestHelper.AssertSuccess(run, "bench run failed");

        var transferTb = Path.Combine(_outputDir, "OTA5TFullyDiff_Ideal_transfer_bench.sp");
        Assert.True(File.Exists(transferTb), "transfer testbench not found");

        var transferText = await File.ReadAllTextAsync(transferTb);

        // The Diff->Diff transfer bench loads each output leg to AC ground using:
        //   Impedor loadP = new Impedor(Z=env.LoadImpedance.DiffToShunt()) ...
        // For LoadImpedance=(1GOhm||15pF), DiffToShunt() => Z/2 => (500MEG || 30pF) per side.
        Assert.Contains("500MEG", transferText);
        Assert.Contains("30p", transferText);

        // Ensure both halves exist (two shunts).
        Assert.True(
            transferText.Split("500MEG", StringSplitOptions.None).Length - 1 >= 2,
            "expected two 500MEG load resistors"
        );
        Assert.True(
            transferText.Split("30p", StringSplitOptions.None).Length - 1 >= 2,
            "expected two 30p load capacitors"
        );
    }
}
