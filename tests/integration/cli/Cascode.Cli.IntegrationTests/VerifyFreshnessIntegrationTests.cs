using System;
using System.IO;
using System.Threading.Tasks;
using Cascode.Cli.IntegrationTests.Infrastructure;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Cli.IntegrationTests;

public sealed class VerifyFreshnessIntegrationTests : IDisposable
{
    private static readonly TimeSpan s_verifyTimeout = TimeSpan.FromSeconds(30);
    private readonly string _repoRoot;
    private readonly string _tempRoot;
    private readonly CascodeHomeScope _cascodeHome;

    public VerifyFreshnessIntegrationTests()
    {
        _repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        _cascodeHome = CliIntegrationTestHelper.CreateCascodeHome(
            _repoRoot,
            "verify-fresh-results"
        );
        _tempRoot = Path.Combine(_repoRoot, $"verify-fresh-results-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        _cascodeHome.Dispose();
        if (Directory.Exists(_tempRoot))
        {
            try
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
            catch { }
        }
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task Verify_WithFreshBenchResults_ForIncludeBearingSource_DoesNotRerunBenchPipeline()
    {
        var sourceCas = Path.Combine(_repoRoot, "tests/golden/cas/bench/SingleResistor.cas");
        var cascodePath = Path.Combine(_tempRoot, "SingleResistor.cas");
        File.Copy(sourceCas, cascodePath, overwrite: true);

        var run = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(60),
            _cascodeHome,
            "bench",
            "run",
            cascodePath,
            "-o",
            _tempRoot
        );
        CliIntegrationTestHelper.AssertSuccess(run, "bench run failed");

        var resultsPath = Path.Combine(_tempRoot, "SingleResistor_results.json");
        Assert.True(File.Exists(resultsPath), "results.json not found");
        var originalWriteTime = File.GetLastWriteTimeUtc(resultsPath);

        var verify = await CliIntegrationTestHelper.RunCliAsync(
            s_verifyTimeout,
            _cascodeHome,
            "verify",
            cascodePath,
            resultsPath
        );
        CliIntegrationTestHelper.AssertSuccess(verify, "verify should reuse fresh bench results");

        Assert.DoesNotContain("Verification input is missing or stale", verify.Stdout);
        Assert.DoesNotContain("Running bench pipeline", verify.Stdout);
        Assert.Equal(originalWriteTime, File.GetLastWriteTimeUtc(resultsPath));
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task Verify_WithStaleExplicitResults_RerunsAndUsesFreshCanonicalArtifact()
    {
        var fixture = CreateIncludeBearingFixture("stale-explicit");
        await RunBenchAsync(fixture.CascodePath, fixture.OutputDirectory);

        Assert.True(File.Exists(fixture.ResultsPath), "canonical results.json not found");
        File.Copy(fixture.ResultsPath, fixture.ExplicitResultsPath, overwrite: true);

        var staleArtifactTime = DateTime.UtcNow.AddMinutes(-2);
        File.SetLastWriteTimeUtc(fixture.ExplicitResultsPath, staleArtifactTime);
        UpdateHelperResistance(fixture.HelperPath, fixture.LibraryName, fixture.HelperName, 10);
        File.SetLastWriteTimeUtc(fixture.HelperPath, staleArtifactTime.AddMinutes(1));

        var verify = await CliIntegrationTestHelper.RunCliAsync(
            s_verifyTimeout,
            _cascodeHome,
            "verify",
            fixture.CascodePath,
            fixture.ExplicitResultsPath
        );

        Assert.NotEqual(0, verify.ExitCode);
        Assert.Contains("Verification input is missing or stale", verify.Stdout);
        Assert.Contains("Running bench pipeline", verify.Stdout);
        Assert.Contains($"Artifact: {fixture.ResultsPath}", verify.Stdout);
        Assert.DoesNotContain($"Artifact: {fixture.ExplicitResultsPath}", verify.Stdout);
        Assert.Contains("Result: 2/4 constraints satisfied", verify.Stdout);
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task Verify_WithContentChangedSourceAndOlderTimestamp_RerunsFromArtifactProvenance()
    {
        var fixture = CreateIncludeBearingFixture("content-mismatch");
        await RunBenchAsync(fixture.CascodePath, fixture.OutputDirectory);

        Assert.True(File.Exists(fixture.ResultsPath), "canonical results.json not found");
        var originalResultsWriteTime = File.GetLastWriteTimeUtc(fixture.ResultsPath);

        UpdateHelperResistance(fixture.HelperPath, fixture.LibraryName, fixture.HelperName, 10);
        File.SetLastWriteTimeUtc(fixture.HelperPath, originalResultsWriteTime.AddMinutes(-1));

        var verify = await CliIntegrationTestHelper.RunCliAsync(
            s_verifyTimeout,
            _cascodeHome,
            "verify",
            fixture.CascodePath,
            fixture.ResultsPath
        );

        Assert.NotEqual(0, verify.ExitCode);
        Assert.Contains("Verification input is missing or stale", verify.Stdout);
        Assert.Contains("Running bench pipeline", verify.Stdout);
        Assert.Contains("Result: 2/4 constraints satisfied", verify.Stdout);
        Assert.True(
            File.GetLastWriteTimeUtc(fixture.ResultsPath) > originalResultsWriteTime,
            "verify should refresh the canonical results artifact"
        );
    }

    private IncludeBearingFixture CreateIncludeBearingFixture(string prefix)
    {
        var slug = prefix.Replace("-", "_", StringComparison.Ordinal);
        var root = Path.Combine(_tempRoot, prefix);
        Directory.CreateDirectory(root);

        var libraryName = $"test.verifyfresh.{slug}";
        var helperName = $"Helper_{slug}";
        var circuitName = $"VerifyFresh_{slug}";
        var helperPath = Path.Combine(root, $"{helperName}.cas");
        var cascodePath = Path.Combine(root, $"{circuitName}.cas");

        File.WriteAllText(helperPath, BuildHelperSource(libraryName, helperName, 100));
        File.WriteAllText(cascodePath, BuildTopSource(libraryName, helperName, circuitName));

        return new IncludeBearingFixture(
            root,
            libraryName,
            helperName,
            helperPath,
            cascodePath,
            Path.Combine(root, $"{circuitName}_results.json"),
            Path.Combine(root, $"{circuitName}_explicit_results.json")
        );
    }

    private static string BuildHelperSource(
        string libraryName,
        string helperName,
        int resistanceOhms
    )
    {
        return $$"""
            library {{libraryName}}

            include lib.std

            circuit {{helperName}} {
              level EL

              input IN : analog
              ground GND

              fill {
                Resistor R1 = new ResistorIdeal(size(R={{resistanceOhms}})) {
                  .P--IN
                  .N--GND
                }
              }
            }
            """;
    }

    private static string BuildTopSource(string libraryName, string helperName, string circuitName)
    {
        return $$"""
            VERSION 4.0

            include lib.std
            include {{libraryName}}

            circuit {{circuitName}} {
              level EL

              input IN : analog
              ground GND

              env {
                InputCommonModeRange = 0V
                SourceImpedance = 50Ohm
              }

              fill {
                {{helperName}} X1 = new {{helperName}}() {
                  .IN--IN
                  .GND--GND
                }
              }

              benches {
                bind OnePortSParam as sparam_bench {
                  bench.P1--dut.IN
                }
              }

              constraints {
                numeric {
                  c_s11_min = sparam_bench::S11(from=1MHz, to=100MHz) >= -9.543dB
                  c_s11_max = sparam_bench::S11(from=1MHz, to=100MHz) <= -9.542dB
                  c_return_loss_min = sparam_bench::ReturnLoss(from=1MHz, to=100MHz) >= 9.542dB
                  c_return_loss_max = sparam_bench::ReturnLoss(from=1MHz, to=100MHz) <= 9.543dB
                }
              }
            }
            """;
    }

    private static void UpdateHelperResistance(
        string helperPath,
        string libraryName,
        string helperName,
        int resistanceOhms
    )
    {
        File.WriteAllText(helperPath, BuildHelperSource(libraryName, helperName, resistanceOhms));
    }

    private async Task RunBenchAsync(string cascodePath, string outputDirectory)
    {
        var run = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(60),
            _cascodeHome,
            "bench",
            "run",
            cascodePath,
            "-o",
            outputDirectory
        );
        CliIntegrationTestHelper.AssertSuccess(run, "bench run failed");
    }

    private sealed record IncludeBearingFixture(
        string OutputDirectory,
        string LibraryName,
        string HelperName,
        string HelperPath,
        string CascodePath,
        string ResultsPath,
        string ExplicitResultsPath
    );
}
