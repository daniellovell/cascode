using System;
using System.IO;
using System.Threading.Tasks;
using Cascode.Cli.IntegrationTests.Infrastructure;
using Xunit;

namespace Cascode.Cli.IntegrationTests;

public sealed class VerifyCommandIntegrationTests
{
    [Fact]
    public async Task Verify_WithSingleBenchResults_FailsMissingDeclaredConstraints()
    {
        var repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        using var cascodeHome = CliIntegrationTestHelper.CreateCascodeHome(repoRoot, "verify");

        var cascodePath = Path.Combine(repoRoot, "tests/golden/cas/ota/OTA5TSingleEnded.el.cai");
        var resultsPath = Path.Combine(
            repoRoot,
            "tests/golden/results/ota/OTA5TSingleEnded_DCSwept_vdd_pwr_results.json"
        );

        var verify = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(10),
            cascodeHome,
            "verify",
            cascodePath,
            resultsPath
        );

        Assert.NotEqual(0, verify.ExitCode);
        Assert.Contains("Result: 1/5 constraints satisfied", verify.Stdout);
        Assert.Contains("c_gbw", verify.Stdout);
        Assert.Contains("FAIL (not measured)", verify.Stdout);
    }
}
