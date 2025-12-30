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
        var testPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var result = SpectreLocator.FindOnPath(testPath, pathextOverride: null);
        Assert.Null(result);
    }

    [Fact]
    public void FindOnPath_FindsExecutableInPath()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var exeName = OperatingSystem.IsWindows() ? "spectre.exe" : "spectre";
            var exePath = Path.Combine(tempDir.FullName, exeName);
            File.WriteAllText(exePath, "echo stub");

            var dotnetDir = Environment.ProcessPath is string p ? Path.GetDirectoryName(p) : null;
            var original = Environment.GetEnvironmentVariable("PATH");
            var pathValue = string.Join(
                Path.PathSeparator,
                new[] { tempDir.FullName, dotnetDir, original }.Where(s =>
                    !string.IsNullOrWhiteSpace(s)
                )
            );

            var result = SpectreLocator.FindOnPath(pathValue, pathextOverride: null);

            Assert.Equal(exePath, result, ignoreCase: true);
        }
        finally
        {
            try
            {
                tempDir.Delete(recursive: true);
            }
            catch { }
        }
    }

    [Fact]
    public void FindOnPath_UsesPathextOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var exePath = Path.Combine(tempDir.FullName, "spectre.cmd");
            File.WriteAllText(exePath, "@echo off\nexit /b 0");

            var originalPath = Environment.GetEnvironmentVariable("PATH");
            var pathValue = string.Join(
                Path.PathSeparator,
                new[] { tempDir.FullName, originalPath }.Where(s => !string.IsNullOrWhiteSpace(s))
            );

            var result = SpectreLocator.FindOnPath(
                pathValue,
                pathextOverride: ".COM;.EXE;.BAT;.CMD"
            );

            Assert.Equal(exePath, result, ignoreCase: true);
        }
        finally
        {
            try
            {
                tempDir.Delete(recursive: true);
            }
            catch { }
        }
    }
}
