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
        var scan = await Infrastructure.CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(2),
            cascodeHome,
            "pdk", "scan", "tests/fixtures/pdk/sky130");
        Infrastructure.CliIntegrationTestHelper.AssertSuccess(scan);

        // 2) Run a single-device char for 'tt' and verify the generated spec/netlist references a valid include
        var deviceNeedle = "nfet_01v8";
        var run = await Infrastructure.CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(2),
            cascodeHome,
            "pdk", "char", "run",
            "--backend", "spectre",
            "--corner", "tt",
            "--limit", "1",
            "--name-contains", deviceNeedle,
            "--workspace", "tests/fixtures/pdk/sky130");
        Infrastructure.CliIntegrationTestHelper.AssertSuccess(run);

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

        Assert.True(spec.RootElement.TryGetProperty("device_name", out var deviceNameElem));
        Assert.Contains(deviceNeedle, deviceNameElem.GetString(), StringComparison.OrdinalIgnoreCase);

        Assert.Contains("include", netlistText, StringComparison.OrdinalIgnoreCase);
    }
}
