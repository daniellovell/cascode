using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Cascode.Cli.IntegrationTests;

public sealed class PdkScanStreamingTests
{
    [Fact]
    public async Task PdkScan_RunOnce_StreamsLogsImmediately()
    {
        // Arrange: start CLI in run-once mode so logging goes to console immediately
        var repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        var startInfo = CliIntegrationTestHelper.CreateCliStartInfo(repoRoot, new[] { "pdk", "scan", "tests/fixtures/pdk/sky130" }, out var commandLine);
        CliIntegrationTestHelper.ConfigureDeterministicEnvironment(startInfo, repoRoot);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        var firstProgressTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
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
            if (e.Data.Contains("Scanning workspace", StringComparison.OrdinalIgnoreCase) ||
                e.Data.Contains("Workspace root resolved", StringComparison.OrdinalIgnoreCase) ||
                e.Data.Contains("Inspecting cdsinit", StringComparison.OrdinalIgnoreCase) ||
                e.Data.Contains("Inspecting libInit", StringComparison.OrdinalIgnoreCase))
            {
                firstProgressTcs.TrySetResult(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
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
        using var streamingTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var completed = await Task.WhenAny(firstProgressTcs.Task, Task.Delay(Timeout.Infinite, streamingTimeout.Token));
        if (completed != firstProgressTcs.Task)
        {
            CliIntegrationTestHelper.TryKillProcess(process);
            await process.WaitForExitAsync().ConfigureAwait(false);

            var combined = string.Join(Environment.NewLine, allStdout);
            throw new TimeoutException($"No streaming progress detected within timeout. Command: {commandLine}{Environment.NewLine}Stdout so far:{Environment.NewLine}{combined}");
        }

        // Clean up: allow the scan to finish, but bound total time to prevent hanging CI
        using var overallTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            await process.WaitForExitAsync(overallTimeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            CliIntegrationTestHelper.TryKillProcess(process);
            await process.WaitForExitAsync().ConfigureAwait(false);
            var combined = string.Join(Environment.NewLine, allStdout);
            throw new TimeoutException($"Scan did not complete in time. Command: {commandLine}{Environment.NewLine}Stdout so far:{Environment.NewLine}{combined}");
        }

        Assert.Equal(0, process.ExitCode);
    }
}
