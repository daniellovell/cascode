using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Cascode.Workspace;

namespace Cascode.Cli.Services;

internal static class NgspiceLocator
{
    internal const int RequiredMajor = 45;

    internal sealed record NgspiceInfo(string Path, int Major, int Minor);

    private static readonly Lazy<NgspiceInfo> _cached = new(() =>
        Resolve(pathOverride: null, pathextOverride: null, cascodeHomeOverride: null)
    );

    public static NgspiceInfo Resolve() => _cached.Value;

    internal static NgspiceInfo Resolve(string? pathOverride, string? pathextOverride)
    {
        return Resolve(pathOverride, pathextOverride, cascodeHomeOverride: null);
    }

    internal static NgspiceInfo Resolve(
        string? pathOverride,
        string? pathextOverride,
        string? cascodeHomeOverride
    )
    {
        var path =
            FindInstalled(cascodeHomeOverride)
            ?? ToolLocator.FindOnPath("ngspice", pathOverride, pathextOverride);
        if (path is null)
            throw NgspiceNotFoundException.NotFound();

        var (major, minor) = QueryVersionForPath(path);
        if (major != RequiredMajor)
            throw NgspiceNotFoundException.WrongVersion(major);

        return new NgspiceInfo(path, major, minor);
    }

    private static readonly Regex VersionRegex = new(
        @"ngspice-(\d+)(?:\.(\d+))?",
        RegexOptions.Compiled
    );

    internal static (int Major, int Minor) QueryVersionForPath(string ngspicePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ngspicePath,
            ArgumentList = { "--version" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        var match = VersionRegex.Match(stdout);
        if (!match.Success)
            throw NgspiceNotFoundException.Unparseable();

        var major = int.Parse(match.Groups[1].Value);
        var minor = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
        return (major, minor);
    }

    private static string? FindInstalled(string? cascodeHomeOverride)
    {
        var rid = RuntimeIdentifier.CurrentRid();
        if (rid is null)
            return null;

        var cascodeHome = cascodeHomeOverride ?? WorkspacePaths.GetCascodeHome();
        var path = NgspiceInstallLayout.GetExecutablePath(cascodeHome, rid);
        return File.Exists(path) ? path : null;
    }
}
