using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Cascode.Bench;
using Cascode.Cli.IntegrationTests.Infrastructure;
using Xunit;

namespace Cascode.Cli.IntegrationTests;

public sealed class VerifyCommandIntegrationTests
{
    [Fact]
    public async Task Verify_WithSingleBenchResults_FailsMissingDeclaredConstraints()
    {
        using var setup = CreateVerifyFixture("verify-single-results");
        using var cascodeHome = CliIntegrationTestHelper.CreateCascodeHome(
            setup.RepoRoot,
            "verify-single-results"
        );

        var verify = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(10),
            cascodeHome,
            "verify",
            setup.CascodePath,
            setup.ResultsPath
        );

        Assert.NotEqual(0, verify.ExitCode);
        Assert.Contains("Result: 1/5 constraints satisfied", verify.Stdout);
        Assert.Contains("c_gbw", verify.Stdout);
        Assert.Contains("FAIL (not measured)", verify.Stdout);
    }

    [Fact]
    public async Task Verify_HelpFlag_ShowsUsage()
    {
        var repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        using var cascodeHome = CliIntegrationTestHelper.CreateCascodeHome(repoRoot, "verify-help");

        var verify = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(10),
            cascodeHome,
            "verify",
            "--help"
        );

        Assert.Equal(0, verify.ExitCode);
        Assert.Contains("Usage: verify", verify.Stdout);
    }

    [Fact]
    public async Task Verify_HelpFlagShort_ShowsUsage()
    {
        var repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        using var cascodeHome = CliIntegrationTestHelper.CreateCascodeHome(
            repoRoot,
            "verify-help-short"
        );

        var verify = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(10),
            cascodeHome,
            "verify",
            "-h"
        );

        Assert.Equal(0, verify.ExitCode);
        Assert.Contains("Usage: verify", verify.Stdout);
    }

    [Fact]
    public async Task Verify_SingleResultsFile_ShowsActionableCascodeError()
    {
        using var setup = CreateVerifyFixture("verify-only-results");
        using var cascodeHome = CliIntegrationTestHelper.CreateCascodeHome(
            setup.RepoRoot,
            "verify-only-results"
        );

        var verify = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(10),
            cascodeHome,
            "verify",
            setup.ResultsPath
        );

        Assert.NotEqual(0, verify.ExitCode);
        Assert.Contains("Cascode source file is required", verify.Stderr);
        Assert.Contains("Usage: verify", verify.Stdout);
    }

    [Fact]
    public async Task Verify_WithOnlyCascode_AutoDiscoversDefaultResultsPath()
    {
        using var setup = CreateVerifyFixture("verify-autodiscover-default");
        using var cascodeHome = CliIntegrationTestHelper.CreateCascodeHome(
            setup.RepoRoot,
            "verify-autodiscover-default"
        );

        var verify = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(10),
            cascodeHome,
            "verify",
            setup.CascodePath
        );

        Assert.NotEqual(0, verify.ExitCode);
        Assert.Contains("Result: 1/5 constraints satisfied", verify.Stdout);
    }

    [Fact]
    public async Task Verify_WithResultsDirectory_AutoDiscoversResultsFile()
    {
        using var setup = CreateVerifyFixture("verify-autodiscover-dir");
        using var cascodeHome = CliIntegrationTestHelper.CreateCascodeHome(
            setup.RepoRoot,
            "verify-autodiscover-dir"
        );

        var verify = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(10),
            cascodeHome,
            "verify",
            setup.CascodePath,
            setup.ResultsDir
        );

        Assert.NotEqual(0, verify.ExitCode);
        Assert.Contains("Result: 1/5 constraints satisfied", verify.Stdout);
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task Verify_WithOnlyCascodeAndMissingResults_AutoRunsBenchPipeline()
    {
        var repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        using var cascodeHome = CliIntegrationTestHelper.CreateCascodeHome(
            repoRoot,
            "verify-auto-run"
        );

        var tempRoot = Path.Combine(Path.GetTempPath(), $"verify-auto-run-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var sourceCas = Path.Combine(repoRoot, "tests/golden/cas/bench/RcLowpass.el.cai");
            var cascodePath = Path.Combine(tempRoot, "RcLowpass.el.cai");
            File.Copy(sourceCas, cascodePath, overwrite: true);

            var verify = await CliIntegrationTestHelper.RunCliAsync(
                TimeSpan.FromSeconds(60),
                cascodeHome,
                "verify",
                cascodePath
            );

            CliIntegrationTestHelper.AssertSuccess(verify, "verify should auto-run bench pipeline");
            Assert.Contains("Circuit: RcLowpass", verify.Stdout);
            Assert.Contains("Result: 2/2 constraints satisfied", verify.Stdout);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Verify_ResultsCircuitNameMismatch_FailsWithHelpfulError()
    {
        using var setup = CreateVerifyFixture("verify-circuit-mismatch");
        using var cascodeHome = CliIntegrationTestHelper.CreateCascodeHome(
            setup.RepoRoot,
            "verify-circuit-mismatch"
        );

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

        var original = JsonSerializer.Deserialize<BenchResult>(File.ReadAllText(setup.ResultsPath));
        Assert.NotNull(original);
        var mismatched = new BenchResult
        {
            Circuit = "DoesNotExist",
            Bench = original!.Bench,
            Measurements = new Dictionary<string, MeasurementResult>(original.Measurements),
        };
        File.WriteAllText(setup.ResultsPath, JsonSerializer.Serialize(mismatched, jsonOptions));

        var verify = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(10),
            cascodeHome,
            "verify",
            setup.CascodePath,
            setup.ResultsPath
        );

        Assert.NotEqual(0, verify.ExitCode);
        Assert.Contains("DoesNotExist", verify.Stderr);
        Assert.Contains("Available EL circuits", verify.Stderr);
        Assert.Contains("OTA5TSingleEnded", verify.Stderr);
    }

    private sealed class VerifyFixture : IDisposable
    {
        public VerifyFixture(
            string tempRoot,
            string repoRoot,
            string cascodePath,
            string resultsPath,
            string resultsDir
        )
        {
            TempRoot = tempRoot;
            RepoRoot = repoRoot;
            CascodePath = cascodePath;
            ResultsPath = resultsPath;
            ResultsDir = resultsDir;
        }

        public string TempRoot { get; }
        public string RepoRoot { get; }
        public string CascodePath { get; }
        public string ResultsPath { get; }
        public string ResultsDir { get; }

        public void Dispose()
        {
            if (Directory.Exists(TempRoot))
            {
                Directory.Delete(TempRoot, recursive: true);
            }
        }
    }

    private static VerifyFixture CreateVerifyFixture(string suffix)
    {
        var repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"{suffix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        var sourceCas = Path.Combine(repoRoot, "tests/golden/cas/ota/OTA5TSingleEnded.el.cai");
        var cascodePath = Path.Combine(tempRoot, "OTA5TSingleEnded.el.cai");
        File.Copy(sourceCas, cascodePath, overwrite: true);

        var resultsDir = Path.Combine(tempRoot, "build", "bench", "OTA5TSingleEnded");
        Directory.CreateDirectory(resultsDir);
        var sourceResults = Path.Combine(
            repoRoot,
            "tests/golden/results/ota/OTA5TSingleEnded_DCSwept_vdd_pwr_results.json"
        );
        var resultsPath = Path.Combine(resultsDir, "OTA5TSingleEnded_results.json");
        File.Copy(sourceResults, resultsPath, overwrite: true);
        var copied = JsonSerializer.Deserialize<BenchResult>(File.ReadAllText(resultsPath));
        Assert.NotNull(copied);
        var normalized = new BenchResult
        {
            Circuit = "OTA5TSingleEnded",
            Bench = copied!.Bench,
            Measurements = new Dictionary<string, MeasurementResult>(copied.Measurements),
        };
        File.WriteAllText(resultsPath, JsonSerializer.Serialize(normalized));

        // Keep results fresher than the Cascode source for tests that should not trigger auto-run.
        File.SetLastWriteTimeUtc(resultsPath, DateTime.UtcNow.AddMinutes(1));

        return new VerifyFixture(tempRoot, repoRoot, cascodePath, resultsPath, resultsDir);
    }
}
