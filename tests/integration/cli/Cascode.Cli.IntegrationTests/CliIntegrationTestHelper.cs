using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Cascode.Cli.IntegrationTests;

internal static class CliIntegrationTestHelper
{
    internal static string GetRepositoryRoot()
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

    internal static ProcessStartInfo CreateCliStartInfo(string repoRoot, IReadOnlyList<string> args, out string commandLine)
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
            startInfo.FileName = executablePath;
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

    internal static void ConfigureDeterministicEnvironment(ProcessStartInfo startInfo, string repoRoot)
    {
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
    }

    internal static void TryKillProcess(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // ignored
        }
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
}

