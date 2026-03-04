using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private static readonly Regex VersionTokenRegex = new(
        @"\b(\d+)(?:\.(\d+))?\b",
        RegexOptions.Compiled
    );
    private static readonly TimeSpan VersionProbeTimeout = TimeSpan.FromSeconds(15);

    internal static (int Major, int Minor) QueryVersionForPath(string ngspicePath)
    {
        var probeTargets = CandidateProbeTargets(ngspicePath);
        foreach (var target in probeTargets)
        {
            foreach (var arg in ProbeArgs())
            {
                if (!TryRunVersionProbe(target, arg, out var output))
                {
                    continue;
                }

                if (TryParseVersionText(output, out var major, out var minor))
                {
                    return (major, minor);
                }
            }
        }

        if (
            OperatingSystem.IsWindows()
            && TryReadWindowsVersionMetadata(ngspicePath, out var mMajor)
        )
        {
            return (mMajor, 0);
        }

        throw NgspiceNotFoundException.Unparseable();
    }

    internal static bool TryParseVersionText(string output, out int major, out int minor)
    {
        major = 0;
        minor = 0;
        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        var directMatch = VersionRegex.Match(output);
        if (directMatch.Success)
        {
            major = int.Parse(directMatch.Groups[1].Value);
            minor = directMatch.Groups[2].Success ? int.Parse(directMatch.Groups[2].Value) : 0;
            return true;
        }

        // Accept variants like "ngspice version 45.2" from Windows builds.
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (line.IndexOf("ngspice", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            var token = VersionTokenRegex.Match(line);
            if (!token.Success)
            {
                continue;
            }

            major = int.Parse(token.Groups[1].Value);
            minor = token.Groups[2].Success ? int.Parse(token.Groups[2].Value) : 0;
            return true;
        }

        return false;
    }

    private static IEnumerable<string> CandidateProbeTargets(string ngspicePath)
    {
        yield return ngspicePath;
        if (
            OperatingSystem.IsWindows()
            && string.Equals(
                Path.GetFileName(ngspicePath),
                "ngspice.exe",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            var siblingConsole = Path.Combine(
                Path.GetDirectoryName(ngspicePath)!,
                "ngspice_con.exe"
            );
            if (File.Exists(siblingConsole))
            {
                yield return siblingConsole;
            }
        }
    }

    private static IEnumerable<string> ProbeArgs()
    {
        yield return "--version";
        if (OperatingSystem.IsWindows())
        {
            yield return "-v";
            yield return "--help";
        }
    }

    private static bool TryRunVersionProbe(string ngspicePath, string arg, out string output)
    {
        output = string.Empty;
        var startInfo = new ProcessStartInfo
        {
            FileName = ngspicePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(arg);

        try
        {
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit((int)VersionProbeTimeout.TotalMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch { }
                return false;
            }

            Task.WaitAll(stdoutTask, stderrTask);
            output =
                $"{stdoutTask.GetAwaiter().GetResult()}\n{stderrTask.GetAwaiter().GetResult()}";
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadWindowsVersionMetadata(string ngspicePath, out int major)
    {
        major = 0;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(ngspicePath);
            if (info.FileMajorPart > 0)
            {
                major = info.FileMajorPart;
                return true;
            }

            var fields = new[]
            {
                info.ProductVersion,
                info.FileVersion,
                info.ProductName,
                info.FileDescription,
                info.Comments,
            };
            foreach (var field in fields.Where(static f => !string.IsNullOrWhiteSpace(f)))
            {
                if (TryParseVersionText(field!, out major, out _))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
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
