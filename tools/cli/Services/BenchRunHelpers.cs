using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cascode.ACIR;
using Cascode.Parser;

namespace Cascode.Cli.Services;

internal static class BenchRunHelpers
{
    public static Circuit GetSingleElCircuit(ACIRDocument doc)
    {
        return doc.Circuits.FirstOrDefault(c => c.Level == ACIRLevel.EL)
            ?? throw new InvalidOperationException("No EL-level circuits found in ACIR document.");
    }

    public static string ResolveOutputDir(
        string? outputDir,
        string circuitName,
        IReadOnlyList<string> benchesToRun
    )
    {
        if (!string.IsNullOrWhiteSpace(outputDir))
        {
            return Path.GetFullPath(outputDir);
        }

        var leaf = benchesToRun.Count == 1 ? $"{circuitName}_{benchesToRun[0]}" : circuitName;
        return Path.Combine(Directory.GetCurrentDirectory(), "build", "bench", leaf);
    }

    public static string ResolveWorkspaceRoot(string acirPath, string workspaceRoot)
    {
        var resolved = FindWorkspaceRoot(acirPath) ?? workspaceRoot;
        return string.IsNullOrWhiteSpace(resolved) ? Directory.GetCurrentDirectory() : resolved;
    }

    public static HashSet<string> GetSweepNames(Circuit circuit)
    {
        return circuit
                .Harness?.Sweeps?.Select(s => s.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string>? ResolveBenchesToRun(
        string[] availableBenches,
        string? explicitBench
    )
    {
        if (!string.IsNullOrWhiteSpace(explicitBench))
        {
            var match = availableBenches.FirstOrDefault(b =>
                b.Equals(explicitBench, StringComparison.OrdinalIgnoreCase)
            );
            return match == null ? null : new[] { match };
        }

        return availableBenches;
    }

    public static string[] GetAvailableBenchNames(Circuit circuit)
    {
        return circuit
                .Benches?.Benches.Select(b => b.Name)
                .Where(b => !string.IsNullOrWhiteSpace(b))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(b => b, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            ?? Array.Empty<string>();
    }

    public static string? FindWorkspaceRoot(string inputPath)
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? Directory.GetCurrentDirectory()
        );
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Cascode.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        return null;
    }

    public static ACIRDocument ReadAcir(string acirPath)
    {
        ACIRReadResult readResult;
        using (var reader = File.OpenText(acirPath))
        {
            readResult = ACIRReader.TryRead(reader, acirPath);
        }

        if (!readResult.Success)
        {
            var first = readResult.Diagnostics.FirstOrDefault(d =>
                d.Severity == DiagnosticSeverity.Error
            );
            throw new InvalidOperationException(first?.Message ?? "Failed to parse ACIR.");
        }

        return readResult.Document!;
    }

    public static string FindTestbenchPath(
        IReadOnlyList<string> testbenches,
        string circuitName,
        string benchName
    )
    {
        foreach (var path in testbenches)
        {
            var file = Path.GetFileNameWithoutExtension(path);
            var prefix = circuitName + "_";
            if (!file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fileBench = file.Substring(prefix.Length);
            if (fileBench.Equals(benchName, StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
        }

        throw new InvalidOperationException($"Testbench for '{benchName}' not emitted.");
    }
}
