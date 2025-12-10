using System;
using System.IO;

namespace Cascode.Bench;

/// <summary>
/// Discovers bench template files by traversing upward from a starting directory
/// looking for benches/ folders, and also checking lib/std/amp/benches/ relative to workspace root.
/// </summary>
public static class TemplateDiscovery
{
    /// <summary>
    /// Finds a template file for a given bench name and backend.
    /// </summary>
    /// <param name="benchName">Name of the bench (e.g., "SEOpAmpACBench").</param>
    /// <param name="backend">Backend type (ngspice or spectre). If null, auto-detects based on PATH.</param>
    /// <param name="startDir">Starting directory for upward traversal (defaults to current directory).</param>
    /// <param name="workspaceRoot">Optional workspace root to check lib/std/amp/benches/.</param>
    /// <returns>Full path to template file if found, null otherwise.</returns>
    public static string? FindTemplate(string benchName, BenchBackendType? backend = null, string? startDir = null, string? workspaceRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(benchName);

        // Auto-detect backend if not specified
        var effectiveBackend = backend ?? DetectBackend();

        startDir ??= Directory.GetCurrentDirectory();
        var current = Path.GetFullPath(startDir);

        // Determine template filename based on backend
        var templateName = effectiveBackend switch
        {
            BenchBackendType.Ngspice => $"{benchName}.ngspice.tpl",
            BenchBackendType.Spectre => $"{benchName}.spectre.tpl",
            _ => throw new ArgumentOutOfRangeException(nameof(backend), effectiveBackend, $"Unknown backend type: {effectiveBackend}")
        };

        // Traverse upward looking for benches/ folders
        var dir = new DirectoryInfo(current);
        while (dir != null)
        {
            var benchesDir = Path.Combine(dir.FullName, "benches");
            if (Directory.Exists(benchesDir))
            {
                var templatePath = Path.Combine(benchesDir, templateName);
                if (File.Exists(templatePath))
                {
                    return templatePath;
                }
            }

            dir = dir.Parent;
        }

        // Fallback: check lib/std/amp/benches/ relative to workspace root
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            var libBenchesDir = Path.Combine(workspaceRoot, "lib", "std", "amp", "benches");
            if (Directory.Exists(libBenchesDir))
            {
                var templatePath = Path.Combine(libBenchesDir, templateName);
                if (File.Exists(templatePath))
                {
                    return templatePath;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Detects the available backend by checking for executables on the PATH.
    /// Spectre takes precedence over ngspice if both are available.
    /// </summary>
    /// <returns>The detected backend type.</returns>
    /// <exception cref="InvalidOperationException">If neither spectre nor ngspice is found on the PATH.</exception>
    private static BenchBackendType DetectBackend()
    {
        if (IsCommandAvailable("spectre"))
        {
            return BenchBackendType.Spectre;
        }

        if (IsCommandAvailable("ngspice"))
        {
            return BenchBackendType.Ngspice;
        }

        throw new InvalidOperationException(
            "No supported SPICE backend found on PATH. Please install either spectre or ngspice, or explicitly specify a backend.");
    }

    /// <summary>
    /// Checks if a command is available on the PATH.
    /// </summary>
    private static bool IsCommandAvailable(string command)
    {
        try
        {
            var pathVar = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathVar))
            {
                return false;
            }

            var paths = pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            var extensions = OperatingSystem.IsWindows()
                ? Environment.GetEnvironmentVariable("PATHEXT")?.Split(';') ?? new[] { ".exe", ".cmd", ".bat" }
                : new[] { string.Empty };

            foreach (var basePath in paths)
            {
                foreach (var ext in extensions)
                {
                    var fullPath = Path.Combine(basePath, command + ext);
                    if (File.Exists(fullPath))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}

