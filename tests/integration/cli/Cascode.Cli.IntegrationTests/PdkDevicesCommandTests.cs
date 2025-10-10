using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Cascode.Cli.IntegrationTests;

public sealed class PdkDevicesCommandTests
{
    [Fact]
    public async Task PdkDevicesCommand_WithValidWorkspace_PrintsDeviceSummary()
    {
        var scanResult = await RunCliAsync(
            TimeSpan.FromMinutes(2),
            "pdk",
            "scan",
            "tests/fixtures/pdk/sky130");
        AssertSuccess(scanResult);

        var devicesResult = await RunCliAsync(
            TimeSpan.FromMinutes(2),
            "pdk",
            "devices",
            "--workspace",
            "tests/fixtures/pdk/sky130",
            "--class",
            "nmos");
        AssertSuccess(devicesResult);
        Assert.True(
            devicesResult.Stdout.Contains("nfet_01v8", StringComparison.Ordinal),
            $"Expected device summary to include 'nfet_01v8'. Stdout: {devicesResult.Stdout}{Environment.NewLine}Stderr: {devicesResult.Stderr}");
    }

    private static void AssertSuccess(ProcessResult result)
    {
        Assert.True(
            result.ExitCode == 0,
            $"Command '{result.CommandLine}' exited with {result.ExitCode}. Stdout: {result.Stdout}{Environment.NewLine}Stderr: {result.Stderr}");
    }

    private static async Task<ProcessResult> RunCliAsync(TimeSpan timeout, params string[] args)
    {
        var repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        var startInfo = CliIntegrationTestHelper.CreateCliStartInfo(repoRoot, args, out var commandLine);
        CliIntegrationTestHelper.ConfigureDeterministicEnvironment(startInfo, repoRoot);

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
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            CliIntegrationTestHelper.TryKillProcess(process);
            await process.WaitForExitAsync().ConfigureAwait(false);

            var timedOutStdout = await stdoutTask.ConfigureAwait(false);
            var timedOutStderr = await stderrTask.ConfigureAwait(false);
            throw new TimeoutException(
                $"Command '{commandLine}' timed out after {timeout}. Stdout: {timedOutStdout}{Environment.NewLine}Stderr: {timedOutStderr}");
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        return new ProcessResult(process.ExitCode, stdout, stderr, commandLine);
    }

    private static void TryKillProcess(Process process)
        => CliIntegrationTestHelper.TryKillProcess(process);

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr, string CommandLine);
}
