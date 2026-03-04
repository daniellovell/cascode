using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Cascode.Cli.Services;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Cli.Tests;

public sealed class NgspiceLocatorTests : IDisposable
{
    private readonly DirectoryInfo _tempDir = Directory.CreateTempSubdirectory();
    private readonly List<CascodeHomeScope> _homes = new();

    public void Dispose()
    {
        foreach (var home in _homes)
        {
            home.Dispose();
        }

        try
        {
            _tempDir.Delete(recursive: true);
        }
        catch { }
    }

    [Fact]
    public void Resolve_ReturnsInfo_WhenCorrectVersion()
    {
        var cascodeHome = CreateEmptyHome();
        CreatePathStub(_tempDir.FullName, "** ngspice-45.2 : Circuit level simulation program");
        var info = NgspiceLocator.Resolve(
            _tempDir.FullName,
            pathextOverride: null,
            cascodeHomeOverride: cascodeHome
        );
        Assert.Equal(45, info.Major);
        Assert.Equal(2, info.Minor);
        Assert.Contains("ngspice", info.Path);
    }

    [Fact]
    public void Resolve_AcceptsMinorVariant()
    {
        var cascodeHome = CreateEmptyHome();
        CreatePathStub(_tempDir.FullName, "** ngspice-45.3 : Circuit level simulation program");
        var info = NgspiceLocator.Resolve(
            _tempDir.FullName,
            pathextOverride: null,
            cascodeHomeOverride: cascodeHome
        );
        Assert.Equal(45, info.Major);
        Assert.Equal(3, info.Minor);
    }

    [Fact]
    public void Resolve_Throws_WhenNotOnPath()
    {
        var cascodeHome = CreateEmptyHome();
        var emptyDir = Path.Combine(_tempDir.FullName, "empty");
        Directory.CreateDirectory(emptyDir);

        var ex = Assert.Throws<NgspiceNotFoundException>(() =>
            NgspiceLocator.Resolve(
                emptyDir,
                pathextOverride: null,
                cascodeHomeOverride: cascodeHome
            )
        );
        Assert.Contains("cascode install ngspice", ex.Message);
    }

    [Fact]
    public void Resolve_Throws_WhenVersionTooOld()
    {
        var cascodeHome = CreateEmptyHome();
        CreatePathStub(_tempDir.FullName, "** ngspice-43 : Circuit level simulation program");

        var ex = Assert.Throws<NgspiceNotFoundException>(() =>
            NgspiceLocator.Resolve(
                _tempDir.FullName,
                pathextOverride: null,
                cascodeHomeOverride: cascodeHome
            )
        );
        Assert.Contains("ngspice 43 found", ex.Message);
        Assert.Contains("requires ngspice 45", ex.Message);
        Assert.Contains("cascode install ngspice", ex.Message);
    }

    [Fact]
    public void Resolve_Throws_WhenVersionTooNew()
    {
        var cascodeHome = CreateEmptyHome();
        CreatePathStub(_tempDir.FullName, "** ngspice-46 : Circuit level simulation program");

        var ex = Assert.Throws<NgspiceNotFoundException>(() =>
            NgspiceLocator.Resolve(
                _tempDir.FullName,
                pathextOverride: null,
                cascodeHomeOverride: cascodeHome
            )
        );
        Assert.Contains("ngspice 46 found", ex.Message);
        Assert.Contains("requires ngspice 45", ex.Message);
    }

    [Fact]
    public void Resolve_Throws_WhenVersionUnparseable()
    {
        var cascodeHome = CreateEmptyHome();
        CreatePathStub(_tempDir.FullName, "Some random output with no version");

        var ex = Assert.Throws<NgspiceNotFoundException>(() =>
            NgspiceLocator.Resolve(
                _tempDir.FullName,
                pathextOverride: null,
                cascodeHomeOverride: cascodeHome
            )
        );
        Assert.Contains("Could not determine", ex.Message);
        Assert.Contains("cascode install ngspice", ex.Message);
    }

    [Theory]
    [InlineData("** ngspice-45.2 : Circuit level simulation program", 45, 2)]
    [InlineData("ngspice version 45.2", 45, 2)]
    [InlineData("NgSpIcE version 45", 45, 0)]
    public void TryParseVersionText_AcceptsSupportedFormats(
        string text,
        int expectedMajor,
        int expectedMinor
    )
    {
        var parsed = NgspiceLocator.TryParseVersionText(text, out var major, out var minor);

        Assert.True(parsed);
        Assert.Equal(expectedMajor, major);
        Assert.Equal(expectedMinor, minor);
    }

    [Fact]
    public void Resolve_PrefersCascodeHomeInstall_OverPath()
    {
        var cascodeHome = CreateEmptyHome();
        CreatePathStub(_tempDir.FullName, "** ngspice-44.1 : Circuit level simulation program");
        var installed = CreateCascodeHomeStub(
            cascodeHome,
            "** ngspice-45.2 : Circuit level simulation program"
        );

        var info = NgspiceLocator.Resolve(
            _tempDir.FullName,
            pathextOverride: null,
            cascodeHomeOverride: cascodeHome
        );

        Assert.Equal(installed, info.Path);
        Assert.Equal(45, info.Major);
    }

    [WindowsOnlyFact]
    public void Resolve_UsesPathextOnWindows()
    {
        var batPath = Path.Combine(_tempDir.FullName, "ngspice.cmd");
        File.WriteAllText(
            batPath,
            "@echo off\r\necho ** ngspice-45.2 : Circuit level simulation program\r\n"
        );
        var cascodeHome = CreateEmptyHome();

        var info = NgspiceLocator.Resolve(
            _tempDir.FullName,
            pathextOverride: ".COM;.EXE;.BAT;.CMD",
            cascodeHomeOverride: cascodeHome
        );
        Assert.Equal(45, info.Major);
    }

    private void CreatePathStub(string path, string versionLine)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var batPath = Path.Combine(path, "ngspice.bat");
            File.WriteAllText(batPath, $"@echo off\r\necho {versionLine}\r\n");
        }
        else
        {
            var shPath = Path.Combine(path, "ngspice");
            File.WriteAllText(shPath, $"#!/bin/sh\necho '{versionLine}'\n");
            File.SetUnixFileMode(
                shPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        }
    }

    private string CreateCascodeHomeStub(string cascodeHome, string versionLine)
    {
        var rid = RuntimeIdentifier.CurrentRid() ?? throw new InvalidOperationException("No RID");
        var exe = NgspiceInstallLayout.GetExecutablePath(cascodeHome, rid);
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            File.WriteAllText(exe, $"@echo off\r\necho {versionLine}\r\n");
        }
        else
        {
            File.WriteAllText(exe, $"#!/bin/sh\necho '{versionLine}'\n");
            File.SetUnixFileMode(
                exe,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        }

        return exe;
    }

    private string CreateEmptyHome()
    {
        var home = CascodeHome.CreateUnder(
            _tempDir.FullName,
            "ngspice-locator-home",
            setEnvironmentVariable: false
        );
        _homes.Add(home);
        return home.Path;
    }
}
