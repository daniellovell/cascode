using System;
using System.IO;
using System.Linq;
using Cascode.Cli.Services;
using Xunit;

namespace Cascode.Cli.Tests;

public sealed class ToolLocatorTests
{
    [Fact]
    public void FindOnPath_ReturnsNull_WhenToolMissing()
    {
        var emptyDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var result = ToolLocator.FindOnPath("nonexistent", emptyDir, pathextOverride: null);
        Assert.Null(result);
    }

    [Fact]
    public void FindOnPath_ReturnsNull_WhenPathEmpty()
    {
        var result = ToolLocator.FindOnPath("anything", "", pathextOverride: null);
        Assert.Null(result);
    }

    [Fact]
    public void FindOnPath_FindsExecutable()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var name = OperatingSystem.IsWindows() ? "mytool.exe" : "mytool";
            var toolPath = Path.Combine(tempDir.FullName, name);
            File.WriteAllText(toolPath, "stub");

            var result = ToolLocator.FindOnPath("mytool", tempDir.FullName, pathextOverride: null);
            Assert.Equal(toolPath, result, ignoreCase: true);
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
    public void FindOnPath_SearchesMultipleDirectories()
    {
        var dir1 = Directory.CreateTempSubdirectory();
        var dir2 = Directory.CreateTempSubdirectory();
        try
        {
            var name = OperatingSystem.IsWindows() ? "mytool.exe" : "mytool";
            var toolPath = Path.Combine(dir2.FullName, name);
            File.WriteAllText(toolPath, "stub");

            var pathValue = string.Join(Path.PathSeparator, dir1.FullName, dir2.FullName);
            var result = ToolLocator.FindOnPath("mytool", pathValue, pathextOverride: null);
            Assert.Equal(toolPath, result, ignoreCase: true);
        }
        finally
        {
            try
            {
                dir1.Delete(recursive: true);
            }
            catch { }
            try
            {
                dir2.Delete(recursive: true);
            }
            catch { }
        }
    }

    [Fact]
    public void FindOnPath_UsesPathextOnWindows()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var batPath = Path.Combine(tempDir.FullName, "mytool.cmd");
            File.WriteAllText(batPath, "@echo off");

            var result = ToolLocator.FindOnPath(
                "mytool",
                tempDir.FullName,
                pathextOverride: ".COM;.EXE;.BAT;.CMD"
            );
            Assert.Equal(batPath, result, ignoreCase: true);
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
