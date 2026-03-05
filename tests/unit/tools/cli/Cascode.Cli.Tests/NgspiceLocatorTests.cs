using System;
using System.IO;
using System.Runtime.InteropServices;
using Cascode.Cli.Services;
using Xunit;

namespace Cascode.Cli.Tests;

public sealed class NgspiceLocatorTests : IDisposable
{
    private readonly DirectoryInfo _tempDir = Directory.CreateTempSubdirectory();

    public void Dispose()
    {
        try
        {
            _tempDir.Delete(recursive: true);
        }
        catch { }
    }

    [Fact]
    public void Resolve_ReturnsInfo_WhenCorrectVersion()
    {
        CreateStub("** ngspice-45.2 : Circuit level simulation program");
        var info = NgspiceLocator.Resolve(_tempDir.FullName, pathextOverride: null);
        Assert.Equal(45, info.Major);
        Assert.Equal(2, info.Minor);
        Assert.Contains("ngspice", info.Path);
    }

    [Fact]
    public void Resolve_AcceptsMinorVariant()
    {
        CreateStub("** ngspice-45.3 : Circuit level simulation program");
        var info = NgspiceLocator.Resolve(_tempDir.FullName, pathextOverride: null);
        Assert.Equal(45, info.Major);
        Assert.Equal(3, info.Minor);
    }

    [Fact]
    public void Resolve_Throws_WhenNotOnPath()
    {
        var emptyDir = Path.Combine(_tempDir.FullName, "empty");
        Directory.CreateDirectory(emptyDir);

        var ex = Assert.Throws<NgspiceNotFoundException>(() =>
            NgspiceLocator.Resolve(emptyDir, pathextOverride: null)
        );
        Assert.Contains("not installed", ex.Message);
        Assert.Contains("Install ngspice", ex.Message);
    }

    [Fact]
    public void Resolve_Throws_WhenVersionTooOld()
    {
        CreateStub("** ngspice-43 : Circuit level simulation program");

        var ex = Assert.Throws<NgspiceNotFoundException>(() =>
            NgspiceLocator.Resolve(_tempDir.FullName, pathextOverride: null)
        );
        Assert.Contains("ngspice 43 found", ex.Message);
        Assert.Contains("requires ngspice 45", ex.Message);
    }

    [Fact]
    public void Resolve_Throws_WhenVersionTooNew()
    {
        CreateStub("** ngspice-46 : Circuit level simulation program");

        var ex = Assert.Throws<NgspiceNotFoundException>(() =>
            NgspiceLocator.Resolve(_tempDir.FullName, pathextOverride: null)
        );
        Assert.Contains("ngspice 46 found", ex.Message);
        Assert.Contains("requires ngspice 45", ex.Message);
    }

    [Fact]
    public void Resolve_Throws_WhenVersionUnparseable()
    {
        CreateStub("Some random output with no version");

        var ex = Assert.Throws<NgspiceNotFoundException>(() =>
            NgspiceLocator.Resolve(_tempDir.FullName, pathextOverride: null)
        );
        Assert.Contains("Could not determine", ex.Message);
    }

    [Fact]
    public void Resolve_UsesPathextOnWindows()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var batPath = Path.Combine(_tempDir.FullName, "ngspice.cmd");
        File.WriteAllText(
            batPath,
            "@echo off\r\necho ** ngspice-45.2 : Circuit level simulation program\r\n"
        );

        var info = NgspiceLocator.Resolve(
            _tempDir.FullName,
            pathextOverride: ".COM;.EXE;.BAT;.CMD"
        );
        Assert.Equal(45, info.Major);
    }

    private void CreateStub(string versionLine)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var batPath = Path.Combine(_tempDir.FullName, "ngspice.bat");
            File.WriteAllText(batPath, $"@echo off\r\necho {versionLine}\r\n");
        }
        else
        {
            var shPath = Path.Combine(_tempDir.FullName, "ngspice");
            File.WriteAllText(shPath, $"#!/bin/sh\necho '{versionLine}'\n");
            File.SetUnixFileMode(
                shPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        }
    }
}
