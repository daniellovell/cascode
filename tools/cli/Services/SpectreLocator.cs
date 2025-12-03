using System;
using System.Collections.Generic;
using System.IO;

namespace Cascode.Cli.Services;

public static class SpectreLocator
{
    public static string? FindOnPath()
    {
        return FindOnPath(pathOverride: null, pathextOverride: null);
    }

    internal static string? FindOnPath(string? pathOverride, string? pathextOverride)
    {
        var pathEnv = pathOverride ?? Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv)) return null;

        var names = BuildCandidateNames("spectre", pathextOverride);
        foreach (var rawSegment in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var segment = Environment.ExpandEnvironmentVariables(rawSegment);
            foreach (var name in names)
            {
                var candidate = Path.Combine(segment, name);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> BuildCandidateNames(string baseName, string? pathextOverride)
    {
        if (OperatingSystem.IsWindows())
        {
            yield return baseName;
            var pathext = pathextOverride ?? Environment.GetEnvironmentVariable("PATHEXT");
            var exts = string.IsNullOrWhiteSpace(pathext)
                ? new[] { ".exe", ".bat", ".cmd", ".com" }
                : pathext.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var ext in exts)
            {
                var normalized = ext.StartsWith('.') ? ext : "." + ext;
                yield return baseName + normalized;
            }
        }
        else
        {
            yield return baseName;
        }
    }
}
