using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cascode.TestSupport;
using Cascode.Workspace;
using Xunit;

namespace Cascode.Cli.IntegrationTests;

public sealed class PdkCharRunWithSpectreTests
{
    [Infrastructure.SpectreAvailableFact]
    public async Task PdkCharRun_WithNmosFilter_RunsSpectreAndStoresLut()
    {
        var repoRoot = Infrastructure.CliIntegrationTestHelper.GetRepositoryRoot();
        using var cascodeHome = Infrastructure.CliIntegrationTestHelper.CreateCascodeHome(repoRoot, nameof(PdkCharRunWithSpectreTests));

        // 1) Scan fixture PDK (sky130) to build DB
        var scan = await RunCliAsync(
            TimeSpan.FromMinutes(2),
            cascodeHome,
            "pdk", "scan", "tests/fixtures/pdk/sky130");
        AssertSuccess(scan, "PDK scan should succeed");

        // 2) Run characterization with NMOS filter, limit to 1 model
        var run = await RunCliAsync(
            TimeSpan.FromMinutes(3),
            cascodeHome,
            "pdk", "char", "run",
            "--backend", "spectre",
            "--corner", "tt",
            "--class", "nmos",
            "--limit", "1",
            "--workspace", "tests/fixtures/pdk/sky130");
        AssertSuccess(run, "Characterization run should succeed");

        // Verify the output mentions characterization completion
        Assert.Contains("Characterization batch complete", run.Stdout, StringComparison.OrdinalIgnoreCase);

        // 3) Find the workspace and verify database has LUT data
        var workRoot = Path.Combine(cascodeHome.Path, "workspaces");
        var workspaceDirs = Directory.GetDirectories(workRoot);
        Assert.NotEmpty(workspaceDirs);
        var wdir = workspaceDirs.OrderByDescending(Directory.GetLastWriteTimeUtc).First();
        var dbPath = Path.Combine(wdir, "pdk.db");
        Assert.True(File.Exists(dbPath), "PDK database should exist");

        // 4) Verify LUT was stored in database
        var coverage = CharLutReader.GetCharacterizationCoverage(dbPath);
        Assert.True(coverage.TotalRuns > 0, $"Expected at least 1 characterization run, got {coverage.TotalRuns}");
        Assert.NotEmpty(coverage.Models);
        Assert.Contains("tt", coverage.Corners);

        // 5) Verify we can load the run and it has data
        var modelName = coverage.Models.First();
        var latestRun = CharLutReader.GetLatestRunForModel(dbPath, modelName, "tt");
        Assert.NotNull(latestRun);
        Assert.Equal("spectre", latestRun.Backend);
        Assert.Equal("tt", latestRun.Corner);
        Assert.Equal("complete", latestRun.Status);

        // 6) Verify LUT points exist
        var points = CharLutReader.LoadLutPoints(dbPath, latestRun.Id);
        Assert.True(points.Count > 0, $"Expected LUT data points, got {points.Count}");

        // 7) Verify summary was computed
        var summary = CharLutReader.LoadRunSummary(dbPath, latestRun.Id);
        Assert.NotNull(summary);
        Assert.True(summary.GmIdPeak.HasValue, "Expected peak gm/Id to be computed");
        Assert.True(summary.VgsAtPeakGmId.HasValue, "Expected Vgs at peak gm/Id to be computed");

        // 8) Verify pdk char status command works
        var status = await RunCliAsync(
            TimeSpan.FromMinutes(1),
            cascodeHome,
            "pdk", "char", "status",
            "--workspace", "tests/fixtures/pdk/sky130");
        AssertSuccess(status, "char status command should succeed");
        Assert.Contains("Coverage:", status.Stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Infrastructure.SpectreAvailableFact]
    public async Task PdkCharRun_WithNmosFilter_ExcludesStdcells()
    {
        var repoRoot = Infrastructure.CliIntegrationTestHelper.GetRepositoryRoot();
        using var cascodeHome = Infrastructure.CliIntegrationTestHelper.CreateCascodeHome(repoRoot, nameof(PdkCharRunWithSpectreTests));

        // Scan fixture
        var scan = await RunCliAsync(
            TimeSpan.FromMinutes(2),
            cascodeHome,
            "pdk", "scan", "tests/fixtures/pdk/sky130");
        AssertSuccess(scan, "PDK scan should succeed");

        // Run with NMOS filter
        var run = await RunCliAsync(
            TimeSpan.FromMinutes(3),
            cascodeHome,
            "pdk", "char", "run",
            "--backend", "spectre",
            "--corner", "tt",
            "--class", "nmos",
            "--limit", "5",
            "--workspace", "tests/fixtures/pdk/sky130");
        AssertSuccess(run, "Characterization run should succeed");

        // Verify stdcells were filtered out
        Assert.DoesNotContain("AND2X2", run.Stdout, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AND3X1", run.Stdout, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INV", run.Stdout, StringComparison.OrdinalIgnoreCase);

        // Should mention filtering non-transistors if any were present
        if (run.Stdout.Contains("Filtered to"))
        {
            Assert.Contains("characterizable models", run.Stdout, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void AssertSuccess(ProcessResult result, string message)
    {
        Assert.True(result.ExitCode == 0, $"{message} (Exit {result.ExitCode})\nStdout: {result.Stdout}\nStderr: {result.Stderr}");
    }

    private static async Task<ProcessResult> RunCliAsync(TimeSpan timeout, CascodeHomeScope cascodeHome, params string[] args)
    {
        var repoRoot = Infrastructure.CliIntegrationTestHelper.GetRepositoryRoot();
        var startInfo = Infrastructure.CliIntegrationTestHelper.CreateCliStartInfo(repoRoot, args, out var commandLine);
        Infrastructure.CliIntegrationTestHelper.ConfigureDeterministicEnvironment(startInfo, repoRoot);
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
