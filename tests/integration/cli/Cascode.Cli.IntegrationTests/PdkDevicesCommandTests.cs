using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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

    private static string GetRepositoryRoot()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var directory = new DirectoryInfo(baseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Cascode.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Unable to locate repository root starting from '{baseDirectory}'.");
    }

    private static async Task<ProcessResult> RunCliAsync(TimeSpan timeout, params string[] args)
    {
        var repoRoot = GetRepositoryRoot();
        var startInfo = CreateCliStartInfo(repoRoot, args, out var commandLine);
        startInfo.Environment["HOME"] = repoRoot;
        startInfo.Environment["DOTNET_CLI_HOME"] = repoRoot;
        startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        var dotnetRoot = Environment.ProcessPath is string processPath
            ? Path.GetDirectoryName(processPath)
            : null;
        if (!string.IsNullOrEmpty(dotnetRoot))
        {
            startInfo.Environment["DOTNET_ROOT"] = dotnetRoot!;
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            startInfo.Environment["USERPROFILE"] = repoRoot;
        }

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
            TryKillProcess(process);
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
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // If kill fails we still want to surface the timeout.
        }
    }

    private static ProcessStartInfo CreateCliStartInfo(string repoRoot, IReadOnlyList<string> args, out string commandLine)
    {
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var executablePath = TryGetCliExecutablePath(repoRoot);
        if (executablePath is not null)
        {
            // Use 'dotnet exec' to run the DLL instead of the executable directly
            // This ensures the .NET runtime is properly resolved on all platforms
            var dllPath = Path.ChangeExtension(executablePath, ".dll");
            startInfo.FileName = "dotnet";
            startInfo.ArgumentList.Add("exec");
            startInfo.ArgumentList.Add(dllPath);
            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }
        }
        else
        {
            startInfo.FileName = "dotnet";
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("--project");
            startInfo.ArgumentList.Add("tools/cli/Cascode.Cli.csproj");
            startInfo.ArgumentList.Add("--");
            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }
        }

        commandLine = BuildCommandLine(startInfo.FileName, startInfo.ArgumentList);
        return startInfo;
    }

    private static string? TryGetCliExecutablePath(string repoRoot)
    {
        var configuration = GetBuildConfiguration();
        var cliBinRoot = Path.Combine(repoRoot, "tools", "cli", "bin");
        if (!Directory.Exists(cliBinRoot))
        {
            return null;
        }

        string? configurationPath = Path.Combine(cliBinRoot, configuration);
        if (!Directory.Exists(configurationPath))
        {
            configurationPath = Directory
                .GetDirectories(cliBinRoot)
                .FirstOrDefault();
            if (configurationPath is null)
            {
                return null;
            }
        }

        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Cascode.Cli.exe" : "Cascode.Cli";
        var tfmDirectories = Directory.GetDirectories(configurationPath);
        foreach (var tfmDirectory in tfmDirectories)
        {
            var candidate = Path.Combine(tfmDirectory, exeName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string GetBuildConfiguration()
    {
        var configuration = Environment.GetEnvironmentVariable("DOTNET_CONFIGURATION")
            ?? Environment.GetEnvironmentVariable("CONFIGURATION");
        if (!string.IsNullOrWhiteSpace(configuration))
        {
            return configuration;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (string.Equals(directory.Name, "Debug", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(directory.Name, "Release", StringComparison.OrdinalIgnoreCase))
            {
                return directory.Name;
            }

            directory = directory.Parent;
        }

        return "Debug";
    }

    private static string BuildCommandLine(string executable, IEnumerable<string> arguments)
    {
        static string QuoteIfNeeded(string value)
            => string.IsNullOrEmpty(value) || value.All(ch => !char.IsWhiteSpace(ch))
                ? value
                : $"\"{value}\"";

        var parts = new List<string> { QuoteIfNeeded(executable) };
        parts.AddRange(arguments.Select(QuoteIfNeeded));
        return string.Join(' ', parts);
    }

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr, string CommandLine);
}
