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
        using var cascodeHome = Infrastructure.CliIntegrationTestHelper.CreateCascodeHome(repoRoot, nameof(PdkCharRunWithoutSpectreTests));
        var tempPath = Directory.CreateTempSubdirectory();
        try
        {
            var pathValue = BuildSpectreFreePath(tempPath.FullName);

            var scan = await RunCliAsync(
                TimeSpan.FromMinutes(2),
                cascodeHome,
                repoRoot,
                pathValue,
                "pdk", "scan", "tests/fixtures/pdk/sky130");
            AssertSuccess(scan, "PDK scan should succeed");

            var run = await RunCliAsync(
                TimeSpan.FromMinutes(3),
                cascodeHome,
                repoRoot,
                pathValue,
                "pdk", "char", "run",
                "--backend", "spectre",
                "--corner", "tt",
                "--class", "nmos",
                "--limit", "1",
                "--workspace", "tests/fixtures/pdk/sky130");
            AssertSuccess(run, "Characterization run should succeed without Spectre");

            Assert.Contains("SPECTRE_BIN not set or executable not found", run.Stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Characterization batch complete", run.Stdout, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { tempPath.Delete(recursive: true); } catch { }
        }
    }

    private static string BuildSpectreFreePath(string tempDir)
    {
        var dotnetDir = Environment.ProcessPath is string p ? Path.GetDirectoryName(p) : null;
        return string.Join(Path.PathSeparator, new[] { tempDir, dotnetDir }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private static void AssertSuccess(ProcessResult result, string message)
    {
        Assert.True(result.ExitCode == 0, $"{message} (Exit {result.ExitCode})\nStdout: {result.Stdout}\nStderr: {result.Stderr}");
    }

    private static async Task<ProcessResult> RunCliAsync(TimeSpan timeout, CascodeHomeScope cascodeHome, string repoRoot, string pathValue, params string[] args)
    {
        var startInfo = Infrastructure.CliIntegrationTestHelper.CreateCliStartInfo(repoRoot, args, out var commandLine);
        Infrastructure.CliIntegrationTestHelper.ConfigureDeterministicEnvironment(startInfo, repoRoot);
        startInfo.Environment["PATH"] = pathValue;
        startInfo.Environment.Remove("SPECTRE_BIN");
        startInfo.Environment.Remove("SPECTRE_HOME");
        cascodeHome.ApplyTo(startInfo.Environment);

        using var process = new System.Diagnostics.Process { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException("Failed to start CLI");
        var so = process.StandardOutput.ReadToEndAsync();
        var se = process.StandardError.ReadToEndAsync();
        using var cts = new System.Threading.CancellationTokenSource(timeout);
        try { await process.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            Infrastructure.CliIntegrationTestHelper.TryKillProcess(process);
            await process.WaitForExitAsync();
            throw new TimeoutException($"Timed out: {commandLine}\nStdout: {await so}\nStderr: {await se}");
        }
        return new ProcessResult(process.ExitCode, await so, await se, commandLine);
    }

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr, string CommandLine);
}
