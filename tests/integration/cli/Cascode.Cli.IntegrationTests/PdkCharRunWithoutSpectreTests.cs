using System;
using System.Threading.Tasks;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Cli.IntegrationTests;

public sealed class PdkCharRunWithoutSpectreTests
{
    [Fact]
    public async Task PdkCharRun_SpectreRequested_FallsBackToNgspiceWithWarning()
    {
        var repoRoot = Infrastructure.CliIntegrationTestHelper.GetRepositoryRoot();
        using var cascodeHome = Infrastructure.CliIntegrationTestHelper.CreateCascodeHome(
            repoRoot,
            nameof(PdkCharRunWithoutSpectreTests)
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

        var run = await Infrastructure.CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(3),
            cascodeHome,
            "pdk",
            "char",
            "run",
            "--backend",
            "spectre",
            "--corner",
            "tt",
            "--class",
            "nmos",
            "--limit",
            "1",
            "--workspace",
            "tests/fixtures/pdk/sky130"
        );
        Infrastructure.CliIntegrationTestHelper.AssertSuccess(
            run,
            "Characterization run should succeed"
        );

        Assert.Contains(
            "not supported by the declarative characterization flow",
            run.Stdout,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains(
            "Characterization batch complete",
            run.Stdout,
            StringComparison.OrdinalIgnoreCase
        );
    }
}
