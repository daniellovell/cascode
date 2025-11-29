using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cascode.TestSupport;

namespace Cascode.Cli.IntegrationTests;

public sealed class PdkCharRunContextSelectionTests
{
    [Fact]
    public async Task PdkCharRun_UsesDbContexts_ForSectionSelection()
    {
        var repoRoot = Infrastructure.CliIntegrationTestHelper.GetRepositoryRoot();
        using var cascodeHome = Infrastructure.CliIntegrationTestHelper.CreateCascodeHome(repoRoot, nameof(PdkCharRunContextSelectionTests));

        // 1) Scan fixture PDK (sky130) to build DB
        var scan = await RunCliAsync(
            TimeSpan.FromMinutes(2),
            cascodeHome,
            "pdk", "scan", "tests/fixtures/pdk/sky130");
        AssertSuccess(scan);

        // 2) Run a single-model char for 'tt' and verify the generated spec/netlist references a valid include+section
        // We choose a model that exists under tt in the fixture example_corner_models.scs
        var modelName = "sky130_fd_pr__nfet_example_tt"; // defined under section tttt_nmos_tn
        var run = await RunCliAsync(
            TimeSpan.FromMinutes(2),
            cascodeHome,
            "pdk", "char", "run",
            "--backend", "spectre",
            "--corner", "tt",
            "--limit", "1",
            "--name-contains", modelName,
            "--workspace", "tests/fixtures/pdk/sky130");
        AssertSuccess(run);

        // 3) Find the most recent job dir and inspect spec.json and netlist
        var workRoot = Path.Combine(cascodeHome.Path, "workspaces");
        var workspaceDirs = Directory.GetDirectories(workRoot);
        Assert.NotEmpty(workspaceDirs);
        var wdir = workspaceDirs.OrderByDescending(Directory.GetLastWriteTimeUtc).First();
        var charRoot = Path.Combine(wdir, "char", "spectre", "tt");
        var modelDirs = Directory.GetDirectories(charRoot);
        Assert.NotEmpty(modelDirs);
        var modelDir = modelDirs.OrderByDescending(Directory.GetLastWriteTimeUtc).First();
        var jobDirs = Directory.GetDirectories(modelDir);
        Assert.NotEmpty(jobDirs);
        var jobDir = jobDirs.OrderByDescending(Directory.GetLastWriteTimeUtc).First();
        var specPath = Path.Combine(jobDir, "spec.json");
        var netlistFiles = Directory.GetFiles(jobDir, "*.scs");
        Assert.NotEmpty(netlistFiles);
        var netlistPath = netlistFiles.OrderByDescending(File.GetLastWriteTimeUtc).First();
        var specText = File.ReadAllText(specPath);
        var netlistText = File.ReadAllText(netlistPath);

        using var spec = System.Text.Json.JsonDocument.Parse(specText);
        Assert.True(spec.RootElement.TryGetProperty("includes", out var includesElem));
        Assert.True(includesElem.GetArrayLength() > 0);
        var includePath = includesElem[0].GetString();
        Assert.False(string.IsNullOrWhiteSpace(includePath));
        Assert.True(File.Exists(includePath!), $"Include path should exist: {includePath}");

        Assert.True(spec.RootElement.TryGetProperty("section", out var sectionElem));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, sectionElem.ValueKind);

        Assert.Contains("include", netlistText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("section=", netlistText, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertSuccess(ProcessResult result)
    {
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}. Stdout: {result.Stdout}\nStderr: {result.Stderr}");
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
