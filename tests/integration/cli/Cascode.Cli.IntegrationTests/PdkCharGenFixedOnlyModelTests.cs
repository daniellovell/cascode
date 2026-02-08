using System;
using System.Threading.Tasks;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Cli.IntegrationTests;

public sealed class PdkCharGenFixedOnlyModelTests
{
    [Fact]
    public async Task CharGen_FixedOnlyModel_FailsWithParametricPrimitiveGuidance()
    {
        var repoRoot = Infrastructure.CliIntegrationTestHelper.GetRepositoryRoot();
        using var cascodeHome = Infrastructure.CliIntegrationTestHelper.CreateCascodeHome(
            repoRoot,
            nameof(PdkCharGenFixedOnlyModelTests)
        );

        var scan = await Infrastructure.CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(2),
            cascodeHome,
            "pdk",
            "scan",
            "tests/fixtures/pdk/sky130"
        );
        Infrastructure.CliIntegrationTestHelper.AssertSuccess(scan, "PDK scan should succeed");

        var emitPrimitives = await Infrastructure.CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(2),
            cascodeHome,
            "pdk",
            "emit",
            "primitives",
            "--workspace",
            "tests/fixtures/pdk/sky130"
        );
        Infrastructure.CliIntegrationTestHelper.AssertSuccess(
            emitPrimitives,
            "PDK emit primitives should succeed"
        );

        var fixedOnlyModel = "sky130_fd_pr__rf_nfet_01v8_lvt_aF02W0p42L0p15";
        var charGen = await Infrastructure.CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(2),
            cascodeHome,
            "char",
            "gen",
            fixedOnlyModel,
            "--workspace",
            "tests/fixtures/pdk/sky130"
        );

        Assert.NotEqual(0, charGen.ExitCode);
        var output = charGen.Stdout + "\n" + charGen.Stderr;
        Assert.Contains(
            "No parametric primitive is available",
            output,
            StringComparison.OrdinalIgnoreCase
        );
    }
}
