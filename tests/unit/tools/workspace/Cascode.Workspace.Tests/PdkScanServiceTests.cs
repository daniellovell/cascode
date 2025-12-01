using System;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using Microsoft.Extensions.Logging;

namespace Cascode.Workspace.Tests;

/// <summary>
/// Unit tests for PdkScanService that test PDK scanning without CLI invocation.
/// These tests use the sky130 fixture directly.
/// </summary>
public class PdkScanServiceTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _cascodeHome;
    private readonly string? _originalCascodeHome;

    public PdkScanServiceTests(ITestOutputHelper output)
    {
        _output = output;

        // Create isolated CASCODE_HOME for test
        _cascodeHome = Path.Combine(Path.GetTempPath(), $"cascode-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_cascodeHome);

        _originalCascodeHome = Environment.GetEnvironmentVariable("CASCODE_HOME");
        Environment.SetEnvironmentVariable("CASCODE_HOME", _cascodeHome);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CASCODE_HOME", _originalCascodeHome);
        try { Directory.Delete(_cascodeHome, recursive: true); } catch { }
    }

    private static string GetFixturePath()
    {
        // Navigate from test bin directory to fixture
        var baseDir = AppContext.BaseDirectory;
        var repoRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "tests", "fixtures", "pdk", "sky130");
    }

    [Fact]
    public void ScanAndPersist_Sky130Fixture_FindsDevicesAndModels()
    {
        var fixturePath = GetFixturePath();
        if (!Directory.Exists(fixturePath))
        {
            _output.WriteLine($"Skipping: fixture not found at {fixturePath}");
            return;
        }

        var service = new PdkScanService();
        var logger = new TestLogger(_output);

        var result = service.ScanAndPersist(fixturePath, logger);

        _output.WriteLine($"Libraries: {result.WorkspaceScan.Libraries.Count}");
        _output.WriteLine($"Model Decks: {result.WorkspaceScan.ModelDecks.Count}");
        _output.WriteLine($"Models: {result.WorkspaceScan.Models.Count}");
        _output.WriteLine($"Devices: {result.Devices.Count}");
        _output.WriteLine($"Matches: {result.Matches.Count}");
        _output.WriteLine($"Geometry entries: {result.ModelGeometry.Count}");
        _output.WriteLine($"Database: {result.DatabasePath}");

        // Verify we found the expected structure
        Assert.True(result.WorkspaceScan.Libraries.Count > 0, "Should find libraries");
        Assert.True(result.WorkspaceScan.Models.Count > 0, "Should find models");
        Assert.True(result.Devices.Count > 0, "Should find devices");
        Assert.True(File.Exists(result.DatabasePath), "Database should exist");
    }

    [Fact]
    public void ScanAndPersist_Sky130Fixture_FindsNmosDevices()
    {
        var fixturePath = GetFixturePath();
        if (!Directory.Exists(fixturePath))
        {
            _output.WriteLine($"Skipping: fixture not found at {fixturePath}");
            return;
        }

        var service = new PdkScanService();
        var result = service.ScanAndPersist(fixturePath);

        var nmosDevices = result.Devices
            .Where(d => d.Class == DeviceClass.Nmos)
            .ToList();

        _output.WriteLine($"Found {nmosDevices.Count} NMOS devices:");
        foreach (var dev in nmosDevices)
        {
            _output.WriteLine($"  - {dev.CanonicalName}");
        }

        // Sky130 fixture should have NMOS devices
        Assert.True(nmosDevices.Count >= 7, $"Expected at least 7 NMOS devices, found {nmosDevices.Count}");

        // Check for specific expected devices
        var deviceNames = nmosDevices.Select(d => d.CanonicalName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.True(deviceNames.Any(n => n.Contains("nfet_01v8", StringComparison.OrdinalIgnoreCase)), "Should find nfet_01v8");
        Assert.True(deviceNames.Any(n => n.Contains("nfet_01v8_lvt", StringComparison.OrdinalIgnoreCase)), "Should find nfet_01v8_lvt");
    }

    [Fact]
    public void ScanAndPersist_Sky130Fixture_MatchesNmosDevicesToModels()
    {
        var fixturePath = GetFixturePath();
        if (!Directory.Exists(fixturePath))
        {
            _output.WriteLine($"Skipping: fixture not found at {fixturePath}");
            return;
        }

        var service = new PdkScanService();
        var result = service.ScanAndPersist(fixturePath);

        // Find matches for NMOS devices
        var nmosDeviceNames = result.Devices
            .Where(d => d.Class == DeviceClass.Nmos)
            .Select(d => d.CanonicalName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var nmosMatches = result.Matches
            .Where(m => nmosDeviceNames.Contains(m.DeviceCanonicalName))
            .ToList();

        _output.WriteLine($"Found {nmosMatches.Count} matches for {nmosDeviceNames.Count} NMOS devices:");
        foreach (var match in nmosMatches.OrderBy(m => m.DeviceCanonicalName))
        {
            _output.WriteLine($"  {match.DeviceCanonicalName} → {match.ModelName} (rank={match.Rank}, quality={match.Quality})");
        }

        // Each NMOS device should have at least one match
        var matchedDevices = nmosMatches.Select(m => m.DeviceCanonicalName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        Assert.True(matchedDevices >= 7, $"Expected at least 7 matched NMOS devices, found {matchedDevices}");
    }

    [Fact]
    public void ScanAndPersist_Sky130Fixture_ExtractsGeometryForBinnedModels()
    {
        var fixturePath = GetFixturePath();
        if (!Directory.Exists(fixturePath))
        {
            _output.WriteLine($"Skipping: fixture not found at {fixturePath}");
            return;
        }

        var service = new PdkScanService();
        var result = service.ScanAndPersist(fixturePath);

        // Find geometry for nfet_03v3_nvt (a binned model with lmin/lmax constraints)
        var nvtGeometry = result.ModelGeometry
            .FirstOrDefault(g => g.ModelName.Contains("nfet_03v3_nvt", StringComparison.OrdinalIgnoreCase));

        _output.WriteLine($"Geometry for nfet_03v3_nvt:");
        if (nvtGeometry != null)
        {
            _output.WriteLine($"  LMin: {nvtGeometry.LMin}");
            _output.WriteLine($"  LMax: {nvtGeometry.LMax}");
            _output.WriteLine($"  WMin: {nvtGeometry.WMin}");
            _output.WriteLine($"  WMax: {nvtGeometry.WMax}");
            _output.WriteLine($"  Source: {nvtGeometry.Source}");
        }
        else
        {
            _output.WriteLine("  Not found!");
        }

        Assert.NotNull(nvtGeometry);
        Assert.NotNull(nvtGeometry.LMin);
        Assert.NotNull(nvtGeometry.LMax);
        Assert.NotNull(nvtGeometry.WMin);
        Assert.NotNull(nvtGeometry.WMax);

        // The nfet_03v3_nvt has specific geometry constraints from binned models
        // LMin should be around 0.495um (4.95e-7)
        // LMax should be around 0.805um (8.05e-7)
        Assert.True(nvtGeometry.LMin >= 4e-7 && nvtGeometry.LMin <= 6e-7,
            $"LMin should be around 0.5um, got {nvtGeometry.LMin}");
        Assert.True(nvtGeometry.LMax >= 7e-7 && nvtGeometry.LMax <= 9e-7,
            $"LMax should be around 0.8um, got {nvtGeometry.LMax}");
    }

    [Fact]
    public void ScanAndPersist_Sky130Fixture_DatabaseContainsExpectedTables()
    {
        var fixturePath = GetFixturePath();
        if (!Directory.Exists(fixturePath))
        {
            _output.WriteLine($"Skipping: fixture not found at {fixturePath}");
            return;
        }

        var service = new PdkScanService();
        var result = service.ScanAndPersist(fixturePath);

        // Verify database structure by reading back
        var devices = PdkDatabaseReader.LoadDevices(result.DatabasePath);
        var models = PdkDatabaseReader.LoadModels(result.DatabasePath);

        _output.WriteLine($"Database contains {devices.Count} devices and {models.Count} models");

        Assert.True(devices.Count > 0, "Database should contain devices");
        Assert.True(models.Count > 0, "Database should contain models");

        // Verify we can read geometry
        var nfet01v8 = devices.FirstOrDefault(d => d.CanonicalName.Contains("nfet_01v8", StringComparison.OrdinalIgnoreCase));
        if (nfet01v8 != null)
        {
            var geom = PdkDatabaseReader.LoadGeometryForDevice(result.DatabasePath, nfet01v8.CanonicalName);
            _output.WriteLine($"Geometry for {nfet01v8.CanonicalName}: W={geom?.WDefault}, L={geom?.LDefault}");
        }
    }

    /// <summary>
    /// Simple test logger that writes to xUnit output.
    /// </summary>
    private sealed class TestLogger : ILogger
    {
        private readonly ITestOutputHelper _output;

        public TestLogger(ITestOutputHelper output) => _output = output;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                _output.WriteLine($"[{logLevel}] {formatter(state, exception)}");
            }
        }
    }
}

