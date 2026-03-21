using System;
using System.Threading.Tasks;
using Cascode.Cli.IntegrationTests.Infrastructure;

namespace Cascode.Cli.IntegrationTests;

public sealed class HelpCommandIntegrationTests
{
    [Fact]
    public async Task RootHelp_ShowsStructuredSectionsAndHidesInternalCommands()
    {
        var repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        using var cascodeHome = CliIntegrationTestHelper.CreateCascodeHome(repoRoot, "root-help");

        var help = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(10),
            cascodeHome,
            "--help"
        );

        Assert.Equal(0, help.ExitCode);
        Assert.Contains("Cascode CLI", help.Stdout);
        Assert.Contains("Usage: cascode [--workspace <path>] <command> [options]", help.Stdout);
        Assert.Contains("Shell:", help.Stdout);
        Assert.Contains("Design Flow:", help.Stdout);
        Assert.Contains("PDK Workspace:", help.Stdout);
        Assert.Contains("help", help.Stdout);
        Assert.Contains("pdk scan", help.Stdout);
        Assert.DoesNotContain("version", help.Stdout, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("log", help.Stdout, StringComparison.OrdinalIgnoreCase);
    }
}
