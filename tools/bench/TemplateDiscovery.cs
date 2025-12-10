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
    /// <param name="backend">Backend type (ngspice or spectre).</param>
    /// <param name="startDir">Starting directory for upward traversal (defaults to current directory).</param>
    /// <param name="workspaceRoot">Optional workspace root to check lib/std/amp/benches/.</param>
    /// <returns>Full path to template file if found, null otherwise.</returns>
    public static string? FindTemplate(string benchName, BenchBackendType backend, string? startDir = null, string? workspaceRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(benchName);

        startDir ??= Directory.GetCurrentDirectory();
        var current = Path.GetFullPath(startDir);

        // Determine template filename based on backend
        var templateName = backend switch
        {
            BenchBackendType.Ngspice => $"{benchName}.ngspice.tpl",
            BenchBackendType.Spectre => $"{benchName}.spectre.tpl",
            _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, $"Unknown backend type: {backend}")
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
}

