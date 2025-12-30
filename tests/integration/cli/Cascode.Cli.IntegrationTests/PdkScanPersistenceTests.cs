using System;
using System.IO;
using System.Text.RegularExpressions;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Cli.IntegrationTests;

public sealed partial class PdkScanPersistenceTests
{
    [Fact]
    public async Task PdkScan_WritesDatabaseAndFindsExpectedDeviceCount()
    {
        // Run a full scan of the sky130 fixture
        var repoRoot = Infrastructure.CliIntegrationTestHelper.GetRepositoryRoot();
        using var cascodeHome = Infrastructure.CliIntegrationTestHelper.CreateCascodeHome(
            repoRoot,
            nameof(PdkScanPersistenceTests)
        );

        var scanResult = await Infrastructure.CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(2),
            cascodeHome,
            "pdk",
            "scan",
            "tests/fixtures/pdk/sky130"
        );
        Infrastructure.CliIntegrationTestHelper.AssertSuccess(scanResult);

        // Extract the database path from logs and ensure it was written
        var dbPath = TryExtractDbPath(scanResult.Stdout);
        Assert.True(
            dbPath is not null && File.Exists(dbPath),
            $"Expected scan to write pdk.db, but could not locate it in logs or on disk. Stdout: {scanResult.Stdout}{Environment.NewLine}Stderr: {scanResult.Stderr}"
        );

        // Query devices for the same workspace and verify the total device count
        var devicesResult = await Infrastructure.CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(2),
            cascodeHome,
            "pdk",
            "devices",
            "--workspace",
            "tests/fixtures/pdk/sky130"
        );
        Infrastructure.CliIntegrationTestHelper.AssertSuccess(devicesResult);

        var deviceCount = TryExtractDeviceCount(devicesResult.Stdout);
        Assert.True(
            deviceCount.HasValue,
            $"Unable to parse device count from output. Stdout: {devicesResult.Stdout}{Environment.NewLine}Stderr: {devicesResult.Stderr}"
        );
        Assert.Equal(399, deviceCount.Value);
    }

    private static readonly char[] separator = new[] { '\n', '\r' };

    private static string? TryExtractDbPath(string stdout)
    {
        // Example line: "PDK database updated → /path/to/.cascode/workspaces/<hash>/pdk.db"
        // Be tolerant of separators and Unicode arrow; capture either \ or / based absolute paths ending in pdk.db
        var rx = PdkDatabasePathPattern();
        foreach (var line in stdout.Split(separator, StringSplitOptions.RemoveEmptyEntries))
        {
            var m = rx.Match(line);
            if (m.Success)
            {
                var path = m.Groups["path"].Value.Trim();
                return path;
            }
        }
        return null;
    }

    private static int? TryExtractDeviceCount(string stdout)
    {
        // Non-interactive format includes a summary line:
        // "Devices: 399. Showing first 20. Matched: ..."
        var rx = DeviceCountPattern();
        var m = rx.Match(stdout);
        if (m.Success && int.TryParse(m.Groups["count"].Value, out var value))
            return value;
        return null;
    }

    [GeneratedRegex(
        @"PDK database updated.*?(?<path>(?:[A-Za-z]:)?[\\/].*?pdk\.db)",
        RegexOptions.IgnoreCase,
        "en-US"
    )]
    private static partial Regex PdkDatabasePathPattern();

    [GeneratedRegex(@"Devices:\s*(?<count>\d+)", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex DeviceCountPattern();
}
