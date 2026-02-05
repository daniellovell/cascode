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
    [Fact]
    public async Task PdkCharRun_WithNmosFilter_RunsNgspiceAndStoresLut()
    {
        var repoRoot = Infrastructure.CliIntegrationTestHelper.GetRepositoryRoot();
        using var cascodeHome = Infrastructure.CliIntegrationTestHelper.CreateCascodeHome(
            repoRoot,
            nameof(PdkCharRunWithSpectreTests)
        );

        // Scan fixture PDK (sky130) to build DB
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

        // Run characterization with NMOS filter
        var run = await Infrastructure.CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(3),
            cascodeHome,
            "pdk",
            "char",
            "run",
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

        // Verify the output mentions characterization completion
        Assert.Contains(
            "Characterization batch complete",
            run.Stdout,
            StringComparison.OrdinalIgnoreCase
        );

        // Find the workspace and verify database has LUT data
        var workRoot = Path.Combine(cascodeHome.Path, "workspaces");
        var workspaceDirs = Directory.GetDirectories(workRoot);
        Assert.NotEmpty(workspaceDirs);
        var wdir = workspaceDirs.OrderByDescending(Directory.GetLastWriteTimeUtc).First();
        var dbPath = Path.Combine(wdir, "pdk.db");
        Assert.True(File.Exists(dbPath), "PDK database should exist");

        // Verify at least one NMOS device run was stored
        var devices = PdkDatabaseReader
            .LoadDevices(dbPath)
            .Where(d => d.Class == DeviceClass.Nmos)
            .ToList();
        Assert.NotEmpty(devices);

        var anyRun = devices
            .SelectMany(d => CharLutReader.GetRunsForDevice(dbPath, d.CanonicalName, "tt"))
            .FirstOrDefault();
        Assert.NotNull(anyRun);
        Assert.Equal("ngspice", anyRun!.Backend);
        Assert.Equal("tt", anyRun.Corner);
        Assert.Equal("complete", anyRun.Status);

        var points = CharLutReader.LoadLutPoints(dbPath, anyRun.Id);
        Assert.True(points.Count > 0, "Expected LUT data points");

        var summary = CharLutReader.LoadRunSummary(dbPath, anyRun.Id);
        Assert.NotNull(summary);
        Assert.True(summary.GmIdPeak.HasValue, "Expected peak gm/Id to be computed");
        Assert.True(summary.VgsAtPeakGmId.HasValue, "Expected Vgs at peak gm/Id to be computed");

        // Verify pdk char status command works
        var status = await Infrastructure.CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(1),
            cascodeHome,
            "pdk",
            "char",
            "status",
            "--workspace",
            "tests/fixtures/pdk/sky130"
        );
        Infrastructure.CliIntegrationTestHelper.AssertSuccess(
            status,
            "char status command should succeed"
        );
        Assert.Contains("Device coverage:", status.Stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PdkCharRun_WithNmosFilter_ExcludesStdcells()
    {
        var repoRoot = Infrastructure.CliIntegrationTestHelper.GetRepositoryRoot();
        using var cascodeHome = Infrastructure.CliIntegrationTestHelper.CreateCascodeHome(
            repoRoot,
            nameof(PdkCharRunWithSpectreTests)
        );

        // Scan fixture
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

        // Run with NMOS filter
        var run = await Infrastructure.CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(3),
            cascodeHome,
            "pdk",
            "char",
            "run",
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

        var workRoot = Path.Combine(cascodeHome.Path, "workspaces");
        var workspaceDirs = Directory.GetDirectories(workRoot);
        Assert.NotEmpty(workspaceDirs);
        var wdir = workspaceDirs.OrderByDescending(Directory.GetLastWriteTimeUtc).First();
        var dbPath = Path.Combine(wdir, "pdk.db");
        Assert.True(File.Exists(dbPath), "PDK database should exist");

        var devices = PdkDatabaseReader.LoadDevices(dbPath);
        var stdcells = new HashSet<string>(
            devices.Where(d => d.Class == DeviceClass.Stdcell).Select(d => d.CanonicalName),
            StringComparer.OrdinalIgnoreCase
        );
        var coverage = CharLutReader.GetDeviceCoverage(dbPath);
        var hasStdcellRun = stdcells.Any(name =>
            coverage.Corners.Any(corner => coverage.HasRun(name, corner))
        );

        Assert.False(hasStdcellRun, "Stdcell devices should not be characterized");
    }
}
