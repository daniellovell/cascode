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
        Assert.Contains("actual missing", verify.Stdout);
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
        using var setup = CreateCanonicalVerifyFixture("verify-autodiscover-default");
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

        CliIntegrationTestHelper.AssertSuccess(verify, "verify should discover default results");
        Assert.Contains("Circuit: RcLowpass", verify.Stdout);
        Assert.Contains("Result: 2/2 constraints satisfied", verify.Stdout);
    }

    [Fact]
    public async Task Verify_WithResultsDirectory_AutoDiscoversResultsFile()
    {
        using var setup = CreateCanonicalVerifyFixture("verify-autodiscover-dir");
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

        CliIntegrationTestHelper.AssertSuccess(
            verify,
            "verify should discover canonical results from the provided directory"
        );
        Assert.Contains("Circuit: RcLowpass", verify.Stdout);
        Assert.Contains("Result: 2/2 constraints satisfied", verify.Stdout);
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task Verify_WithOnlyCascodeAndMissingResults_AutoRunsBenchPipeline_WithoutDuplicateComplianceOutput()
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
            Assert.Equal(1, CountOccurrences(verify.Stdout, "Compliance:"));
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
    [Trait("Category", "Simulation")]
    public async Task Verify_WithMultiCircuitResultsDirectory_VerifiesAllCircuits()
    {
        var repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        using var cascodeHome = CliIntegrationTestHelper.CreateCascodeHome(
            repoRoot,
            "verify-multi-circuit"
        );

        var tempRoot = Path.Combine(Path.GetTempPath(), $"verify-multi-circuit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var sourceCas = Path.Combine(
                repoRoot,
                "tests/golden/cas/bench/RcLowpassMultiCircuit.el.cai"
            );
            var cascodePath = Path.Combine(tempRoot, "RcLowpassMultiCircuit.el.cai");
            File.Copy(sourceCas, cascodePath, overwrite: true);

            var run = await CliIntegrationTestHelper.RunCliAsync(
                TimeSpan.FromSeconds(60),
                cascodeHome,
                "bench",
                "run",
                cascodePath,
                "-o",
                tempRoot
            );
            CliIntegrationTestHelper.AssertSuccess(run, "bench run failed");

            var verify = await CliIntegrationTestHelper.RunCliAsync(
                TimeSpan.FromSeconds(60),
                cascodeHome,
                "verify",
                cascodePath,
                tempRoot
            );
            CliIntegrationTestHelper.AssertSuccess(verify, "verify should pass for both circuits");

            Assert.Contains("Circuits: 2", verify.Stdout);
            Assert.Contains("RcLowpassA: PASS", verify.Stdout);
            Assert.Contains("RcLowpassB: PASS", verify.Stdout);
            Assert.Contains("Global Result: 2/2 circuits compliant", verify.Stdout);
            Assert.Contains("=== RcLowpassA ===", verify.Stdout);
            Assert.Contains("=== RcLowpassB ===", verify.Stdout);
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
    [Trait("Category", "Simulation")]
    public async Task Verify_WithOnlyCascodeOnMultiCircuitSource_AutoDiscoversCanonicalResultsForAllCircuits()
    {
        var repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        using var cascodeHome = CliIntegrationTestHelper.CreateCascodeHome(
            repoRoot,
            "verify-multi-circuit-default"
        );

        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"verify-multi-circuit-default-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(tempRoot);
        try
        {
            var sourceCas = Path.Combine(
                repoRoot,
                "tests/golden/cas/bench/RcLowpassMultiCircuit.el.cai"
            );
            var cascodePath = Path.Combine(tempRoot, "RcLowpassMultiCircuit.el.cai");
            File.Copy(sourceCas, cascodePath, overwrite: true);

            var run = await CliIntegrationTestHelper.RunCliAsync(
                TimeSpan.FromSeconds(60),
                cascodeHome,
                "bench",
                "run",
                cascodePath
            );
            CliIntegrationTestHelper.AssertSuccess(run, "bench run failed");

            var verify = await CliIntegrationTestHelper.RunCliAsync(
                TimeSpan.FromSeconds(60),
                cascodeHome,
                "verify",
                cascodePath
            );
            CliIntegrationTestHelper.AssertSuccess(
                verify,
                "verify should discover canonical results for all circuits"
            );

            Assert.Contains("Circuits: 2", verify.Stdout);
            Assert.Contains("RcLowpassA: PASS", verify.Stdout);
            Assert.Contains("RcLowpassB: PASS", verify.Stdout);
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

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task Verify_WithInlineHelperCircuit_AutoRunIgnoresHelperArtifactTargets()
    {
        using var setup = CreateHierarchicalVerifyFixture("verify-inline-helper-auto-run");
        using var cascodeHome = CliIntegrationTestHelper.CreateCascodeHome(
            setup.RepoRoot,
            "verify-inline-helper-auto-run"
        );

        var verify = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(60),
            cascodeHome,
            "verify",
            setup.CascodePath
        );

        CliIntegrationTestHelper.AssertSuccess(
            verify,
            "verify should auto-run and only require the constrained top-level circuit"
        );
        Assert.Contains("Circuit: RcLowpassWithInlineHelper", verify.Stdout);
        Assert.Contains("Result: 2/2 constraints satisfied", verify.Stdout);
        Assert.DoesNotContain("InlineResistor", verify.Stdout);
        Assert.DoesNotContain("Missing canonical results files", verify.Stderr);
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task Verify_WithInlineHelperCircuit_ResultsDirectoryIgnoresHelperArtifactTargets()
    {
        using var setup = CreateHierarchicalVerifyFixture("verify-inline-helper-dir");
        using var cascodeHome = CliIntegrationTestHelper.CreateCascodeHome(
            setup.RepoRoot,
            "verify-inline-helper-dir"
        );

        var run = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(60),
            cascodeHome,
            "bench",
            "run",
            setup.CascodePath,
            "-o",
            setup.ResultsDir
        );
        CliIntegrationTestHelper.AssertSuccess(run, "bench run failed");

        var verify = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(60),
            cascodeHome,
            "verify",
            setup.CascodePath,
            setup.ResultsDir
        );

        CliIntegrationTestHelper.AssertSuccess(
            verify,
            "verify should discover canonical results only for verifiable circuits"
        );
        Assert.Contains("Circuit: RcLowpassWithInlineHelper", verify.Stdout);
        Assert.DoesNotContain("InlineResistor", verify.Stdout);
        Assert.DoesNotContain("Missing canonical results files", verify.Stderr);
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

    private static VerifyFixture CreateHierarchicalVerifyFixture(string suffix)
    {
        var repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"{suffix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        var sourceCas = Path.Combine(repoRoot, "tests/golden/cas/bench/RcLowpass.el.cai");
        var cascodePath = Path.Combine(tempRoot, "RcLowpassWithInlineHelper.el.cai");
        var sourceText = File.ReadAllText(sourceCas);
        var helperCircuit = """

            circuit InlineResistor {
              level EL
              inline

              input IN : analog
              output OUT : analog

              fill {
                Resistor R1 = new ResistorIdeal(size(R=1k)) {
                  .P--IN
                  .N--OUT
                }
              }
            }

            circuit RcLowpassWithInlineHelper {
            """;
        var resistorBlock = """
                Resistor R1 = new ResistorIdeal(size(R=1k)) {
                  .P--IN.P
                  .N--OUT
                }
            """;
        var helperBlock = """
                InlineResistor helper = new InlineResistor() {
                  .IN--IN.P
                  .OUT--OUT
                }
            """;
        File.WriteAllText(
            cascodePath,
            sourceText
                .Replace("circuit RcLowpass {", helperCircuit, StringComparison.Ordinal)
                .Replace(resistorBlock, helperBlock, StringComparison.Ordinal)
        );

        var resultsDir = Path.Combine(tempRoot, "build", "bench", "RcLowpassWithInlineHelper");
        var resultsPath = Path.Combine(resultsDir, "RcLowpassWithInlineHelper_results.json");
        return new VerifyFixture(tempRoot, repoRoot, cascodePath, resultsPath, resultsDir);
    }

    private static VerifyFixture CreateCanonicalVerifyFixture(string suffix)
    {
        var repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"{suffix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        var sourceCas = Path.Combine(repoRoot, "tests/golden/cas/bench/RcLowpass.el.cai");
        var cascodePath = Path.Combine(tempRoot, "RcLowpass.el.cai");
        File.Copy(sourceCas, cascodePath, overwrite: true);

        var resultsDir = Path.Combine(tempRoot, "build", "bench", "RcLowpass");
        Directory.CreateDirectory(resultsDir);
        var resultsPath = Path.Combine(resultsDir, "RcLowpass_results.json");
        var results = new BenchResult
        {
            Circuit = "RcLowpass",
            Bench = "lp",
            Measurements = new Dictionary<string, MeasurementResult>
            {
                ["LowpassBandwidth"] = new()
                {
                    Metric = "LowpassBandwidth",
                    Value = 159_154_943.1,
                    Unit = "Hz",
                    Bench = "lp",
                },
                ["PassbandGain"] = new()
                {
                    Metric = "PassbandGain",
                    Value = 0.0,
                    Unit = "dB",
                    Bench = "lp",
                },
            },
        };
        File.WriteAllText(resultsPath, JsonSerializer.Serialize(results));
        File.SetLastWriteTimeUtc(resultsPath, DateTime.UtcNow.AddMinutes(1));

        return new VerifyFixture(tempRoot, repoRoot, cascodePath, resultsPath, resultsDir);
    }

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var offset = 0;
        while (offset < text.Length)
        {
            var found = text.IndexOf(needle, offset, StringComparison.Ordinal);
            if (found < 0)
            {
                break;
            }

            count++;
            offset = found + needle.Length;
        }

        return count;
    }
}
