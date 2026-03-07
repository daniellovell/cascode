using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Cascode.TestSupport;

namespace Cascode.Cli.IntegrationTests;

public sealed class PdkScanStreamingTests
{
    private static readonly string[] args = new[] { "pdk", "scan", "tests/fixtures/pdk/sky130" };

    [Infrastructure.LinuxOnlyFact]
    public async Task PdkScan_RunOnce_StreamsLogsImmediately()
    {
        // Arrange: start CLI in run-once mode so logging goes to console immediately
        var repoRoot = Infrastructure.CliIntegrationTestHelper.GetRepositoryRoot();
        var startInfo = Infrastructure.CliIntegrationTestHelper.CreateCliStartInfo(
            repoRoot,
            args,
            out var commandLine
        );
        Infrastructure.CliIntegrationTestHelper.ConfigureDeterministicEnvironment(
            startInfo,
            repoRoot
        );
        using var cascodeHome = Infrastructure.CliIntegrationTestHelper.CreateCascodeHome(
            repoRoot,
            nameof(PdkScanStreamingTests)
        );
        cascodeHome.ApplyTo(startInfo.Environment);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        var firstProgressSignal = new AsyncSignal<string>();
        var allStdout = new List<string>();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            lock (allStdout)
            {
                allStdout.Add(e.Data);
            }

            // Accept any of several progress anchors that the scanner emits early
            if (
                e.Data.Contains("Scanning workspace", StringComparison.OrdinalIgnoreCase)
                || e.Data.Contains("Workspace root resolved", StringComparison.OrdinalIgnoreCase)
                || e.Data.Contains("Inspecting cdsinit", StringComparison.OrdinalIgnoreCase)
                || e.Data.Contains("Inspecting libInit", StringComparison.OrdinalIgnoreCase)
            )
            {
                firstProgressSignal.TrySet(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) => {
            // stderr is not required for the assertion, but capturing reduces risk of buffer deadlocks
        };

        // Act
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start the Cascode CLI process.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Assert: a progress line must appear quickly, proving streaming behavior
        try
        {
            await AsyncTest.WaitAsync(
                firstProgressSignal.Task,
                TimeSpan.FromSeconds(5),
                "Timed out waiting for streaming progress."
            );
        }
        catch (TimeoutException)
        {
            Infrastructure.CliIntegrationTestHelper.TryKillProcess(process);
            await process.WaitForExitAsync();

            var combined = string.Join(Environment.NewLine, allStdout);
            throw new TimeoutException(
                $"No streaming progress detected within timeout. Command: {commandLine}{Environment.NewLine}Stdout so far:{Environment.NewLine}{combined}"
            );
        }

        // Clean up: allow the scan to finish, but bound total time to prevent hanging CI
        using var overallTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            await process.WaitForExitAsync(overallTimeout.Token);
        }
        catch (OperationCanceledException)
        {
            Infrastructure.CliIntegrationTestHelper.TryKillProcess(process);
            await process.WaitForExitAsync();
            var combined = string.Join(Environment.NewLine, allStdout);
            throw new TimeoutException(
                $"Scan did not complete in time. Command: {commandLine}{Environment.NewLine}Stdout so far:{Environment.NewLine}{combined}"
            );
        }

        Assert.Equal(0, process.ExitCode);
    }
}
