using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Cascode.Cli.IntegrationTests.Infrastructure;

internal static class CliIntegrationTestHelper
{
    internal readonly record struct CliCommandSpec(string FileName, IReadOnlyList<string> Arguments);

    internal static string GetRepositoryRoot()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var directory = new DirectoryInfo(baseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Cascode.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException($"Unable to locate repository root starting from '{baseDirectory}'.");
    }

    internal static CliCommandSpec BuildCliCommand(string repoRoot, IReadOnlyList<string> args)
    {
        var executablePath = TryGetCliExecutablePath(repoRoot);
        if (executablePath is not null)
        {
            // Prefer running the built DLL via 'dotnet <dll>' for cross-platform reliability
            var tfmDirectory = Path.GetDirectoryName(executablePath)!;
            var dllPath = Path.Combine(tfmDirectory, "Cascode.Cli.dll");
            if (File.Exists(dllPath))
            {
                var combined = new List<string> { dllPath };
                combined.AddRange(args);
                return new CliCommandSpec("dotnet", combined);
            }
        }

        // Fallback to 'dotnet run' if we cannot locate the DLL alongside the build output
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

    private static readonly System.Threading.AsyncLocal<string?> s_cascodeHome = new();

    /// <summary>
    /// Populate the process start info with a deterministic set of environment variables and ensure a stable, per-test CASCODE_HOME.
    /// </summary>
    /// <param name="startInfo">The ProcessStartInfo whose Environment will be populated.</param>
    /// <param name="repoRoot">Path to the repository root used to create or locate the per-test CASCODE_HOME under <c>.it</c>.</param>
    internal static void ConfigureDeterministicEnvironment(ProcessStartInfo startInfo, string repoRoot)
    {
        foreach (var kv in BuildDeterministicEnvironment(repoRoot))
        {
            startInfo.Environment[kv.Key] = kv.Value;
        }

        // Assign a stable, per-test CASCODE_HOME (scoped via AsyncLocal) so
        // multiple CLI processes within the same test share the same state,
        // while parallel tests get isolated directories.
        var current = s_cascodeHome.Value;
        if (string.IsNullOrEmpty(current))
        {
            var itRoot = Path.Combine(repoRoot, ".it");
            Directory.CreateDirectory(itRoot);
            current = Path.Combine(itRoot, $"cascode-home-{Guid.NewGuid():N}");
            s_cascodeHome.Value = current;
        }
        startInfo.Environment["CASCODE_HOME"] = current!;
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

    /// <summary>
    /// Gets or creates a per-async-context CASCODE_HOME directory path used by integration tests.
    /// </summary>
    /// <param name="repoRoot">Repository root directory under which a per-test directory (./.it/cascode-home-&lt;guid&gt;) will be created if needed.</param>
    /// <returns>The absolute path to the CASCODE_HOME directory for the current async context; creates and stores a new unique path if one was not already set.</returns>
    internal static string GetOrCreateTestCascodeHome(string repoRoot)
    {
        var current = s_cascodeHome.Value;
        if (string.IsNullOrEmpty(current))
        {
            var itRoot = Path.Combine(repoRoot, ".it");
            Directory.CreateDirectory(itRoot);
            current = Path.Combine(itRoot, $"cascode-home-{Guid.NewGuid():N}");
            s_cascodeHome.Value = current;
        }
        return current!;
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
        // Check common CI environment variables
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