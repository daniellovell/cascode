using System;
using System.IO;
using System.Linq;
using Cascode.Cli.Services;
using Xunit;

namespace Cascode.Cli.Tests;

public sealed class SpectreLocatorTests
{
    [Fact]
    public void FindOnPath_ReturnsNullWhenMissing()
    {
        var original = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

            var result = SpectreLocator.FindOnPath();

            Assert.Null(result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", original);
        }
    }

    [Fact]
    public void FindOnPath_FindsExecutableInPath()
    {
        var original = Environment.GetEnvironmentVariable("PATH");
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var exeName = OperatingSystem.IsWindows() ? "spectre.exe" : "spectre";
            var exePath = Path.Combine(tempDir.FullName, exeName);
            File.WriteAllText(exePath, "echo stub");

            var dotnetDir = Environment.ProcessPath is string p ? Path.GetDirectoryName(p) : null;
            var pathValue = string.Join(Path.PathSeparator, new[] { tempDir.FullName, dotnetDir, original }.Where(s => !string.IsNullOrWhiteSpace(s)));
            Environment.SetEnvironmentVariable("PATH", pathValue);

            var result = SpectreLocator.FindOnPath();

            Assert.Equal(exePath, result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", original);
            try { tempDir.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void FindOnPath_UsesPathextOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var originalPath = Environment.GetEnvironmentVariable("PATH");
        var originalPathext = Environment.GetEnvironmentVariable("PATHEXT");
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var exePath = Path.Combine(tempDir.FullName, "spectre.cmd");
            File.WriteAllText(exePath, "@echo off\nexit /b 0");

            Environment.SetEnvironmentVariable("PATHEXT", ".COM;.EXE;.BAT;.CMD");
            var pathValue = string.Join(Path.PathSeparator, new[] { tempDir.FullName, originalPath }.Where(s => !string.IsNullOrWhiteSpace(s)));
            Environment.SetEnvironmentVariable("PATH", pathValue);

            var result = SpectreLocator.FindOnPath();

            Assert.Equal(exePath, result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Environment.SetEnvironmentVariable("PATHEXT", originalPathext);
            try { tempDir.Delete(recursive: true); } catch { }
        }
    }
}
