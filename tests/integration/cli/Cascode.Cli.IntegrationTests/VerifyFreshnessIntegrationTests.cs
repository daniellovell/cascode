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
        _tempRoot = Path.Combine(Path.GetTempPath(), $"verify-fresh-results-{Guid.NewGuid():N}");
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
}
