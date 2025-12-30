using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cascode.Cli.IntegrationTests;

public sealed class PdkMatchingPatternsInitTests
{
    private static readonly string[] args = new[] { "pdk", "scan", "tests/fixtures/pdk/sky130" };

    [Fact]
    public async Task PdkScan_EnsuresMatchingPatterns_AndLogsPath()
    {
        var repoRoot = Infrastructure.CliIntegrationTestHelper.GetRepositoryRoot();
        var startInfo = Infrastructure.CliIntegrationTestHelper.CreateCliStartInfo(
            repoRoot,
            args,
            out var commandLine
        );
        Infrastructure.CliIntegrationTestHelper.ConfigureDeterministicEnvironment(
            startInfo,
            repoRoot
        );

        using var cascodeHome = Infrastructure.CliIntegrationTestHelper.CreateCascodeHome(
            repoRoot,
            nameof(PdkMatchingPatternsInitTests)
        );
        cascodeHome.ApplyTo(startInfo.Environment);

        var expectedPath = Path.Combine(cascodeHome.Path, "config", "pdk-matching-patterns.yml");
        if (File.Exists(expectedPath))
            File.Delete(expectedPath);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("Failed to start the Cascode CLI process.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await process.WaitForExitAsync(timeout.Token);

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();

        Assert.Equal(0, process.ExitCode);
        Assert.True(
            File.Exists(expectedPath),
            $"Expected patterns file at '{expectedPath}' to be created."
        );
        Assert.Contains("matching patterns", stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pdk-matching-patterns.yml", stdout, StringComparison.OrdinalIgnoreCase);
    }
}
