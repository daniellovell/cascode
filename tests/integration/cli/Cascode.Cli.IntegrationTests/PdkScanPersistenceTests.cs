using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cascode.TestSupport;

namespace Cascode.Cli.IntegrationTests;

public sealed class PdkScanPersistenceTests
{
    [Fact]
    public async Task PdkScan_WritesDatabaseAndFindsExpectedDeviceCount()
    {
        // Run a full scan of the sky130 fixture
        var repoRoot = Infrastructure.CliIntegrationTestHelper.GetRepositoryRoot();
        using var cascodeHome = Infrastructure.CliIntegrationTestHelper.CreateCascodeHome(repoRoot, nameof(PdkScanPersistenceTests));

        var scanResult = await RunCliAsync(
            TimeSpan.FromMinutes(2),
            cascodeHome,
            "pdk",
            "scan",
            "tests/fixtures/pdk/sky130");
        AssertSuccess(scanResult);

        // Extract the database path from logs and ensure it was written
        var dbPath = TryExtractDbPath(scanResult.Stdout);
        Assert.True(
            dbPath is not null && File.Exists(dbPath),
            $"Expected scan to write pdk.db, but could not locate it in logs or on disk. Stdout: {scanResult.Stdout}{Environment.NewLine}Stderr: {scanResult.Stderr}");

        // Query devices for the same workspace and verify the total device count
        var devicesResult = await RunCliAsync(
            TimeSpan.FromMinutes(2),
            cascodeHome,
            "pdk",
            "devices",
            "--workspace",
            "tests/fixtures/pdk/sky130");
        AssertSuccess(devicesResult);

        var deviceCount = TryExtractDeviceCount(devicesResult.Stdout);
        Assert.True(
            deviceCount.HasValue,
            $"Unable to parse device count from output. Stdout: {devicesResult.Stdout}{Environment.NewLine}Stderr: {devicesResult.Stderr}");
        Assert.Equal(399, deviceCount.Value);
    }

    private static string? TryExtractDbPath(string stdout)
    {
        // Example line: "PDK database updated → /path/to/.cascode/workspaces/<hash>/pdk.db"
        // Be tolerant of separators and Unicode arrow; capture either \ or / based absolute paths ending in pdk.db
        var rx = new Regex(@"PDK database updated.*?(?<path>(?:[A-Za-z]:)?[\\/].*?pdk\.db)", RegexOptions.IgnoreCase);
        foreach (var line in stdout.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
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
        var rx = new Regex(@"Devices:\s*(?<count>\d+)", RegexOptions.IgnoreCase);
        var m = rx.Match(stdout);
        if (m.Success && int.TryParse(m.Groups["count"].Value, out var value)) return value;
        return null;
    }

    private static void AssertSuccess(ProcessResult result)
    {
        Assert.True(
            result.ExitCode == 0,
            $"Command '{result.CommandLine}' exited with {result.ExitCode}. Stdout: {result.Stdout}{Environment.NewLine}Stderr: {result.Stderr}");
    }

    private static async Task<ProcessResult> RunCliAsync(TimeSpan timeout, CascodeHomeScope cascodeHome, params string[] args)
    {
        var repoRoot = Infrastructure.CliIntegrationTestHelper.GetRepositoryRoot();
        var startInfo = Infrastructure.CliIntegrationTestHelper.CreateCliStartInfo(repoRoot, args, out var commandLine);
        Infrastructure.CliIntegrationTestHelper.ConfigureDeterministicEnvironment(startInfo, repoRoot);
        cascodeHome.ApplyTo(startInfo.Environment);

        using var process = new Process { StartInfo = startInfo };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start the Cascode CLI process.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Infrastructure.CliIntegrationTestHelper.TryKillProcess(process);
            await process.WaitForExitAsync();

            var timedOutStdout = await stdoutTask;
            var timedOutStderr = await stderrTask;
            throw new TimeoutException(
                $"Command '{commandLine}' timed out after {timeout}. Stdout: {timedOutStdout}{Environment.NewLine}Stderr: {timedOutStderr}");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return new ProcessResult(process.ExitCode, stdout, stderr, commandLine);
    }

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr, string CommandLine);
}
