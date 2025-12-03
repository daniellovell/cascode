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

        // Scan fixture PDK (sky130) to build DB
        var scan = await Infrastructure.CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(2),
            cascodeHome,
            "pdk", "scan", "tests/fixtures/pdk/sky130");
        Infrastructure.CliIntegrationTestHelper.AssertSuccess(scan, "PDK scan should succeed");

        // Run characterization with NMOS filter
        var run = await Infrastructure.CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(3),
            cascodeHome,
            "pdk", "char", "run",
            "--backend", "spectre",
            "--corner", "tt",
            "--class", "nmos",
            "--workspace", "tests/fixtures/pdk/sky130");
        Infrastructure.CliIntegrationTestHelper.AssertSuccess(run, "Characterization run should succeed");

        // Verify the output mentions characterization completion
        Assert.Contains("Characterization batch complete", run.Stdout, StringComparison.OrdinalIgnoreCase);

        // Find the workspace and verify database has LUT data
        var workRoot = Path.Combine(cascodeHome.Path, "workspaces");
        var workspaceDirs = Directory.GetDirectories(workRoot);
        Assert.NotEmpty(workspaceDirs);
        var wdir = workspaceDirs.OrderByDescending(Directory.GetLastWriteTimeUtc).First();
        var dbPath = Path.Combine(wdir, "pdk.db");
        Assert.True(File.Exists(dbPath), "PDK database should exist");

        // Verify LUTs were stored for each NMOS device
        var devices = PdkDatabaseReader.LoadDevices(dbPath).Where(d => d.Class == DeviceClass.Nmos).ToList();
        Assert.Equal(7, devices.Count);

        foreach (var device in devices)
        {
            var runs = CharLutReader.GetRunsForDevice(dbPath, device.CanonicalName, "tt");
            Assert.NotEmpty(runs);
            var latest = runs.First();
            Assert.Equal(device.CanonicalName, latest.DeviceName);
            Assert.Equal("spectre", latest.Backend);
            Assert.Equal("tt", latest.Corner);
            Assert.Equal("complete", latest.Status);

            var points = CharLutReader.LoadLutPoints(dbPath, latest.Id);
            Assert.True(points.Count > 0, $"Expected LUT data points for {device.CanonicalName}");

            var summary = CharLutReader.LoadRunSummary(dbPath, latest.Id);
            Assert.NotNull(summary);
            Assert.True(summary.GmIdPeak.HasValue, "Expected peak gm/Id to be computed");
            Assert.True(summary.VgsAtPeakGmId.HasValue, "Expected Vgs at peak gm/Id to be computed");
        }

        // Verify pdk char status command works
        var status = await Infrastructure.CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(1),
            cascodeHome,
            "pdk", "char", "status",
            "--workspace", "tests/fixtures/pdk/sky130");
        Infrastructure.CliIntegrationTestHelper.AssertSuccess(status, "char status command should succeed");
        Assert.Contains("Device coverage:", status.Stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Infrastructure.SpectreAvailableFact]
    public async Task PdkCharRun_WithNmosFilter_ExcludesStdcells()
    {
        var repoRoot = Infrastructure.CliIntegrationTestHelper.GetRepositoryRoot();
        using var cascodeHome = Infrastructure.CliIntegrationTestHelper.CreateCascodeHome(repoRoot, nameof(PdkCharRunWithSpectreTests));

        // Scan fixture
        var scan = await Infrastructure.CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(2),
            cascodeHome,
            "pdk", "scan", "tests/fixtures/pdk/sky130");
        Infrastructure.CliIntegrationTestHelper.AssertSuccess(scan, "PDK scan should succeed");

        // Run with NMOS filter
        var run = await Infrastructure.CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(3),
            cascodeHome,
            "pdk", "char", "run",
            "--backend", "spectre",
            "--corner", "tt",
            "--class", "nmos",
            "--limit", "5",
            "--workspace", "tests/fixtures/pdk/sky130");
        Infrastructure.CliIntegrationTestHelper.AssertSuccess(run, "Characterization run should succeed");

        var workRoot = Path.Combine(cascodeHome.Path, "workspaces");
        var workspaceDirs = Directory.GetDirectories(workRoot);
        Assert.NotEmpty(workspaceDirs);
        var wdir = workspaceDirs.OrderByDescending(Directory.GetLastWriteTimeUtc).First();
        var dbPath = Path.Combine(wdir, "pdk.db");
        Assert.True(File.Exists(dbPath), "PDK database should exist");

        var devices = PdkDatabaseReader.LoadDevices(dbPath);
        var stdcells = new HashSet<string>(devices.Where(d => d.Class == DeviceClass.Stdcell).Select(d => d.CanonicalName), StringComparer.OrdinalIgnoreCase);
        var coverage = CharLutReader.GetDeviceCoverage(dbPath);
        var hasStdcellRun = stdcells.Any(name => coverage.Corners.Any(corner => coverage.HasRun(name, corner)));

        Assert.False(hasStdcellRun, "Stdcell devices should not be characterized");
    }
}
