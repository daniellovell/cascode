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

public sealed class EmitVerifyFlowTests : IDisposable
{
    private readonly string _repoRoot;
    private readonly string _outputDir;
    private readonly CascodeHomeScope _cascodeHome;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public EmitVerifyFlowTests()
    {
        _repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        _outputDir = Path.Combine(
            Path.GetTempPath(),
            "cascode-emit-verify-test-" + Guid.NewGuid().ToString("N")[..8]
        );
        Directory.CreateDirectory(_outputDir);
        _cascodeHome = CliIntegrationTestHelper.CreateCascodeHome(_repoRoot, "emit-verify");
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
    public async Task Emit_RcLowpass_EmitsDesignAndTestbench()
    {
        var cascodePath = Path.Combine(_repoRoot, "tests/golden/cas/bench/RcLowpass.el.cai");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(10),
            _cascodeHome,
            "emit",
            cascodePath,
            "--out",
            _outputDir,
            "--backend",
            "ngspice"
        );

        CliIntegrationTestHelper.AssertSuccess(result, "emit command failed");
        Assert.True(File.Exists(Path.Combine(_outputDir, "RcLowpass.sp")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "RcLowpass_lp.sp")));
    }

    [Fact]
    public async Task Emit_DesignOnlyCircuit_EmitsNoTestbenches()
    {
        var cascodePath = Path.Combine(_repoRoot, "tests/golden/cas/cs/CSAmpResistive.el.cai");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(10),
            _cascodeHome,
            "emit",
            cascodePath,
            "--out",
            _outputDir,
            "--backend",
            "ngspice"
        );

        CliIntegrationTestHelper.AssertSuccess(result, "emit command failed");
        Assert.True(File.Exists(Path.Combine(_outputDir, "CSAmpResistive.sp")));
        Assert.False(File.Exists(Path.Combine(_outputDir, "CSAmpResistive_lp.sp")));
        Assert.Contains("Emitted 1 design(s) and 0 testbench(es)", result.Stdout);
    }

    [Fact]
    public async Task Emit_JsonOutput_ReturnsValidJson()
    {
        var cascodePath = Path.Combine(_repoRoot, "tests/golden/cas/bench/RcLowpass.el.cai");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(10),
            _cascodeHome,
            "emit",
            cascodePath,
            "--out",
            _outputDir,
            "--backend",
            "ngspice",
            "--json"
        );

        CliIntegrationTestHelper.AssertSuccess(result, "emit --json failed");
        using var parsed = CliIntegrationTestHelper.ParseJsonFromOutput(result.Stdout);
        Assert.True(parsed.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(0, parsed.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal(1, parsed.RootElement.GetProperty("designPaths").GetArrayLength());
        Assert.Equal(1, parsed.RootElement.GetProperty("testbenchPaths").GetArrayLength());
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task Verify_WithFailingResults_ReturnsFailure()
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
        Assert.True(File.Exists(resultsPath));

        var results = JsonSerializer.Deserialize<BenchResult>(
            await File.ReadAllTextAsync(resultsPath),
            s_jsonOptions
        );
        Assert.NotNull(results);

        // Force the constraint to fail by writing a copy with a tiny cutoff.
        var failing = new BenchResult
        {
            Circuit = results!.Circuit,
            Bench = results.Bench,
            Measurements = new(results.Measurements),
        };
        failing.Measurements["LowpassBandwidth"] = new MeasurementResult
        {
            Metric = "LowpassBandwidth",
            Value = 1.0,
            Unit = "Hz",
            Node = null,
        };
        var failingResultsPath = Path.Combine(_outputDir, "RcLowpass_lp_results_failing.json");
        await File.WriteAllTextAsync(
            failingResultsPath,
            JsonSerializer.Serialize(failing, new JsonSerializerOptions { WriteIndented = true })
        );

        var verify = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(10),
            _cascodeHome,
            "verify",
            cascodePath,
            failingResultsPath
        );
        Assert.NotEqual(0, verify.ExitCode);
    }
}
