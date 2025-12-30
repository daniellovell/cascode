using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Cli.IntegrationTests;

public sealed class PdkCharRunWithoutSpectreTests
{
    [Fact]
    public async Task PdkCharRun_SpectreMissing_SkipsSimulationGracefully()
    {
        var repoRoot = Infrastructure.CliIntegrationTestHelper.GetRepositoryRoot();
        using var cascodeHome = Infrastructure.CliIntegrationTestHelper.CreateCascodeHome(
            repoRoot,
            nameof(PdkCharRunWithoutSpectreTests)
        );
        var tempPath = Directory.CreateTempSubdirectory();
        try
        {
            var pathValue = BuildSpectreFreePath(tempPath.FullName);

            var scan = await Infrastructure.CliIntegrationTestHelper.RunCliAsync(
                TimeSpan.FromMinutes(2),
                cascodeHome,
                env =>
                {
                    env["PATH"] = pathValue;
                    env.Remove("SPECTRE_BIN");
                    env.Remove("SPECTRE_HOME");
                },
                "pdk",
                "scan",
                "tests/fixtures/pdk/sky130"
            );
            Infrastructure.CliIntegrationTestHelper.AssertSuccess(scan, "PDK scan should succeed");

            var run = await Infrastructure.CliIntegrationTestHelper.RunCliAsync(
                TimeSpan.FromMinutes(3),
                cascodeHome,
                env =>
                {
                    env["PATH"] = pathValue;
                    env.Remove("SPECTRE_BIN");
                    env.Remove("SPECTRE_HOME");
                },
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
                "Characterization run should succeed without Spectre"
            );

            Assert.Contains(
                "SPECTRE_BIN not set or executable not found",
                run.Stdout,
                StringComparison.OrdinalIgnoreCase
            );
            Assert.Contains(
                "Characterization batch complete",
                run.Stdout,
                StringComparison.OrdinalIgnoreCase
            );
        }
        finally
        {
            try
            {
                tempPath.Delete(recursive: true);
            }
            catch { }
        }
    }

    private static string BuildSpectreFreePath(string tempDir)
    {
        var dotnetDir = Environment.ProcessPath is string p ? Path.GetDirectoryName(p) : null;
        return string.Join(
            Path.PathSeparator,
            new[] { tempDir, dotnetDir }.Where(s => !string.IsNullOrWhiteSpace(s))
        );
    }
}
