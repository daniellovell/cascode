using System;
using System.IO;
using System.Threading.Tasks;
using Cascode.Cli.IntegrationTests.Infrastructure;
using Xunit;

namespace Cascode.Cli.IntegrationTests;

public sealed class VerifyFreshnessIntegrationTests
{
    private static readonly TimeSpan s_verifyTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task Verify_WithFreshBenchResults_ForIncludeBearingSource_DoesNotRerunBenchPipeline()
    {
        var repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        using var cascodeHome = CliIntegrationTestHelper.CreateCascodeHome(
            repoRoot,
            "verify-fresh-results"
        );

        var tempRoot = Path.Combine(Path.GetTempPath(), $"verify-fresh-results-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var sourceCas = Path.Combine(repoRoot, "tests/golden/cas/bench/SingleResistor.cas");
            var cascodePath = Path.Combine(tempRoot, "SingleResistor.cas");
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

            var resultsPath = Path.Combine(tempRoot, "SingleResistor_results.json");
            Assert.True(File.Exists(resultsPath), "results.json not found");
            var originalWriteTime = File.GetLastWriteTimeUtc(resultsPath);

            var verify = await CliIntegrationTestHelper.RunCliAsync(
                s_verifyTimeout,
                cascodeHome,
                "verify",
                cascodePath,
                resultsPath
            );
            CliIntegrationTestHelper.AssertSuccess(
                verify,
                "verify should reuse fresh bench results"
            );

            Assert.DoesNotContain("Verification input is missing or stale", verify.Stdout);
            Assert.DoesNotContain("Running bench pipeline", verify.Stdout);
            Assert.Equal(originalWriteTime, File.GetLastWriteTimeUtc(resultsPath));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
