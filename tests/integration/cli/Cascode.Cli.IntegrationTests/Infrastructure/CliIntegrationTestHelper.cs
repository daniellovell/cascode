using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Cascode.TestSupport;

namespace Cascode.Cli.IntegrationTests.Infrastructure;

internal static class CliIntegrationTestHelper
{
    internal readonly record struct CliCommandSpec(string FileName, IReadOnlyList<string> Arguments);

    internal static string GetRepositoryRoot()
    {
        return TestPathUtilities.GetRepositoryRoot();
    }

    internal static CliCommandSpec BuildCliCommand(string repoRoot, IReadOnlyList<string> args)
    {
        var executablePath = TryGetCliExecutablePath(repoRoot);
        if (executablePath is not null)
        {
            var tfmDirectory = Path.GetDirectoryName(executablePath)!;
            var dllPath = Path.Combine(tfmDirectory, "Cascode.Cli.dll");
            if (File.Exists(dllPath))
            {
                var combined = new List<string> { dllPath };
                combined.AddRange(args);
                return new CliCommandSpec("dotnet", combined);
            }
        }

        var fallbackArgs = new List<string> { "run", "--project", "tools/cli/Cascode.Cli.csproj", "--" };
        fallbackArgs.AddRange(args);
        return new CliCommandSpec("dotnet", fallbackArgs);
    }

    /// <summary>
    /// Create a ProcessStartInfo configured to run the CLI from the repository root.
    /// </summary>
    /// <param name="repoRoot">Repository root directory used as the process working directory and to locate the CLI executable.</param>
    /// <param name="args">Arguments to forward to the CLI process.</param>
    /// <param name="commandLine">The constructed command line string (executable and quoted arguments) produced for diagnostics.</param>
    /// <returns>A ProcessStartInfo configured with the CLI executable, argument list, redirected output/error, no shell execute, and no window.</returns>
    internal static ProcessStartInfo CreateCliStartInfo(string repoRoot, IReadOnlyList<string> args, out string commandLine)
    {
        var command = BuildCliCommand(repoRoot, args);
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = repoRoot,
            FileName = command.FileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in command.Arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        commandLine = BuildCommandLine(startInfo.FileName, startInfo.ArgumentList);
        return startInfo;
    }

    internal static void ConfigureDeterministicEnvironment(ProcessStartInfo startInfo, string repoRoot)
    {
        foreach (var kv in BuildDeterministicEnvironment(repoRoot))
        {
            startInfo.Environment[kv.Key] = kv.Value;
        }
    }

    /// <summary>
    /// Builds a deterministic set of environment variables for running the CLI from the given repository root.
    /// </summary>
    /// <param name="repoRoot">Path to the repository root used to derive deterministic environment values.</param>
    /// <returns>A dictionary of environment variable names and values that force stable CLI behavior (includes DOTNET_CLI_HOME, DOTNET_SKIP_FIRST_TIME_EXPERIENCE, DOTNET_CLI_TELEMETRY_OPTOUT, DOTNET_NOLOGO; sets USERPROFILE on Windows and DOTNET_ROOT when the dotnet executable's directory can be determined).</returns>
    internal static IDictionary<string, string> BuildDeterministicEnvironment(string repoRoot)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DOTNET_CLI_HOME"] = repoRoot,
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_NOLOGO"] = "1",
        };

        var dotnetRoot = Environment.ProcessPath is string processPath ? Path.GetDirectoryName(processPath) : null;
        if (!string.IsNullOrEmpty(dotnetRoot)) env["DOTNET_ROOT"] = dotnetRoot!;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) env["USERPROFILE"] = repoRoot;
        return env;
    }

    internal static CascodeHomeScope CreateCascodeHome(string repoRoot, string prefix)
    {
        var itRoot = Path.Combine(repoRoot, ".it");
        return CascodeHome.CreateUnder(itRoot, prefix, setEnvironmentVariable: false);
    }

    /// <summary>
    /// Attempts to terminate the specified process and its child processes.
    /// </summary>
    /// <param name="process">The process to terminate.</param>
    /// <remarks>Any exceptions thrown while attempting to kill the process are suppressed.</remarks>
    internal static void TryKillProcess(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch { }
    }

    internal static bool IsRunningInCi()
    {
        return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")) ||
               !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")) ||
               !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TF_BUILD")) ||
               !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JENKINS_HOME")) ||
               !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CIRCLECI")) ||
               !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TRAVIS"));
    }

    private static string? TryGetCliExecutablePath(string repoRoot)
    {
        var configuration = GetBuildConfiguration();
        var cliBinRoot = Path.Combine(repoRoot, "tools", "cli", "bin");
        if (!Directory.Exists(cliBinRoot)) return null;
        var configurationPath = Path.Combine(cliBinRoot, configuration);
        if (!Directory.Exists(configurationPath)) configurationPath = Directory.GetDirectories(cliBinRoot).FirstOrDefault() ?? string.Empty;
        if (string.IsNullOrEmpty(configurationPath)) return null;
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Cascode.Cli.exe" : "Cascode.Cli";
        foreach (var tfmDirectory in Directory.GetDirectories(configurationPath))
        {
            var candidate = Path.Combine(tfmDirectory, exeName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string GetBuildConfiguration()
    {
        var configuration = Environment.GetEnvironmentVariable("DOTNET_CONFIGURATION") ?? Environment.GetEnvironmentVariable("CONFIGURATION");
        if (!string.IsNullOrWhiteSpace(configuration)) return configuration;
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (string.Equals(directory.Name, "Debug", StringComparison.OrdinalIgnoreCase) || string.Equals(directory.Name, "Release", StringComparison.OrdinalIgnoreCase)) return directory.Name;
            directory = directory.Parent;
        }
        return "Debug";
    }

    private static string BuildCommandLine(string executable, IEnumerable<string> arguments)
    {
        static string Q(string value) => string.IsNullOrEmpty(value) || value.All(ch => !char.IsWhiteSpace(ch)) ? value : $"\"{value}\"";
        var parts = new List<string> { Q(executable) };
        parts.AddRange(arguments.Select(Q));
        return string.Join(' ', parts);
    }
}