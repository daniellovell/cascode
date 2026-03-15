using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cascode.Language;

namespace Cascode.Cli.Services;

internal static class BenchRunHelpers
{
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

    public static string ResolveWorkspaceRoot(string cascodePath, string workspaceRoot)
    {
        var resolved = FindWorkspaceRoot(cascodePath) ?? workspaceRoot;
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

    public static string[] GetAvailableBenchNames(CascodeDocument doc, Circuit circuit)
    {
        return ResolveBenchBindings(doc, circuit)
            .Select(b => b.BindingName)
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(b => b, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<BenchBinding> ResolveBenchBindings(
        CascodeDocument doc,
        Circuit circuit
    )
    {
        // Circuit bindings override inherited bindings by binding name.
        var map = new Dictionary<string, BenchBinding>(StringComparer.OrdinalIgnoreCase);

        if (circuit.Traits is { Count: > 0 })
        {
            var interfacesByName = doc.Traits.ToDictionary(
                t => t.Name,
                StringComparer.OrdinalIgnoreCase
            );
            foreach (var iface in circuit.Traits)
            {
                if (!interfacesByName.TryGetValue(iface, out var interfaceDef))
                {
                    continue;
                }

                foreach (var b in interfaceDef.BenchBindings)
                {
                    map.TryAdd(b.BindingName, b);
                }
            }
        }

        foreach (var b in circuit.BenchBindings)
        {
            map[b.BindingName] = b;
        }

        return map.Values.OrderBy(b => b.BindingName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string? GetBundledStdlibRoot()
    {
        var baseDir = AppContext.BaseDirectory;
        var libStdPath = Path.Combine(baseDir, "lib", "std");
        if (Directory.Exists(libStdPath))
            return baseDir;
        return null;
    }

    public static IReadOnlyList<string> BuildSearchRoots(string workspaceRoot)
    {
        var comparer = OperatingSystem.IsLinux()
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;
        var seen = new HashSet<string>(comparer);
        var roots = new List<string>(3);

        var ws = NormalizePath(workspaceRoot);
        if (seen.Add(ws))
            roots.Add(ws);

        var cwd = NormalizePath(Directory.GetCurrentDirectory());
        if (seen.Add(cwd))
            roots.Add(cwd);

        var stdlibRoot = GetBundledStdlibRoot();
        if (stdlibRoot is not null)
        {
            var std = NormalizePath(stdlibRoot);
            if (seen.Add(std))
                roots.Add(std);
        }

        return roots;
    }

    private static string NormalizePath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

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

    public static CascodeDocument ReadCascode(string cascodePath)
    {
        CascodeReadResult readResult;
        using (var reader = File.OpenText(cascodePath))
        {
            readResult = CascodeReader.TryRead(reader, cascodePath);
        }

        if (!readResult.Success)
        {
            var first = readResult.Diagnostics.FirstOrDefault(d =>
                d.Severity == DiagnosticSeverity.Error
            );
            throw new InvalidOperationException(first?.Message ?? "Failed to parse Cascode.");
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
