using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Cli.IntegrationTests;

public sealed class PdkCharRunContextSelectionTests
{
    [Fact]
    [Trait("Category", "Simulation")]
    public async Task PdkCharRun_UsesDbContexts_ForSectionSelection()
    {
        var repoRoot = Infrastructure.CliIntegrationTestHelper.GetRepositoryRoot();
        using var cascodeHome = Infrastructure.CliIntegrationTestHelper.CreateCascodeHome(
            repoRoot,
            nameof(PdkCharRunContextSelectionTests)
        );

        // 1) Scan fixture PDK (sky130) to build DB
        var scan = await Infrastructure.CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(2),
            cascodeHome,
            "pdk",
            "scan",
            "tests/fixtures/pdk/sky130"
        );
        Infrastructure.CliIntegrationTestHelper.AssertSuccess(scan);

        var emitPrimitives = await Infrastructure.CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(2),
            cascodeHome,
            "pdk",
            "emit",
            "primitives",
            "--workspace",
            "tests/fixtures/pdk/sky130"
        );
        Infrastructure.CliIntegrationTestHelper.AssertSuccess(emitPrimitives);

        // 2) Run a single-device char for 'tt' and verify the generated spec/netlist references a valid include
        var deviceNeedle = "nfet_01v8";
        var run = await Infrastructure.CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(2),
            cascodeHome,
            "pdk",
            "char",
            "run",
            "--backend",
            "ngspice",
            "--corner",
            "tt",
            "--limit",
            "1",
            "--name-contains",
            deviceNeedle,
            "--workspace",
            "tests/fixtures/pdk/sky130"
        );
        Infrastructure.CliIntegrationTestHelper.AssertSuccess(run);

        // 3) Find the most recent job dir and inspect spec.json and netlist
        var workRoot = Path.Combine(cascodeHome.Path, "workspaces");
        var workspaceDirs = Directory.GetDirectories(workRoot);
        Assert.NotEmpty(workspaceDirs);
        var wdir = workspaceDirs.OrderByDescending(Directory.GetLastWriteTimeUtc).First();
        var charRoot = Path.Combine(wdir, "char", "ngspice", "tt");
        var modelDirs = Directory.GetDirectories(charRoot);
        Assert.NotEmpty(modelDirs);
        var modelDir = modelDirs.OrderByDescending(Directory.GetLastWriteTimeUtc).First();
        var jobDirs = Directory.GetDirectories(modelDir);
        Assert.NotEmpty(jobDirs);
        var jobDir = jobDirs.OrderByDescending(Directory.GetLastWriteTimeUtc).First();
        var specPath = Path.Combine(jobDir, "spec.json");
        var specText = File.ReadAllText(specPath);

        using var spec = System.Text.Json.JsonDocument.Parse(specText);
        Assert.True(spec.RootElement.TryGetProperty("device_name", out var deviceNameElem));
        Assert.Contains(
            deviceNeedle,
            deviceNameElem.GetString(),
            StringComparison.OrdinalIgnoreCase
        );

        var netlistFiles = Directory
            .GetFiles(jobDir, "*.cir")
            .Concat(Directory.GetFiles(jobDir, "*.sp"))
            .ToArray();
        Assert.NotEmpty(netlistFiles);
        var netlistPath = netlistFiles.OrderByDescending(File.GetLastWriteTimeUtc).First();
        var netlistText = File.ReadAllText(netlistPath);

        var includeLines = netlistText
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith(".include", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.NotEmpty(includeLines);

        static string? ExtractQuotedPath(string line)
        {
            var first = line.IndexOf('"');
            var last = line.LastIndexOf('"');
            if (first < 0 || last <= first)
                return null;
            return line.Substring(first + 1, last - first - 1);
        }

        var anyExistingInclude = includeLines
            .Select(ExtractQuotedPath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .Any(p => File.Exists(p) || File.Exists(Path.Combine(jobDir, p)));
        Assert.True(anyExistingInclude, "Expected at least one .include path to exist on disk");
    }
}
