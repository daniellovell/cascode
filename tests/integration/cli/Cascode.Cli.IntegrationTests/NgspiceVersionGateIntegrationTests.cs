using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Cascode.Cli.IntegrationTests.Infrastructure;
using Cascode.Cli.Services;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Cli.IntegrationTests;

public sealed class NgspiceVersionGateIntegrationTests : IDisposable
{
    private readonly string _repoRoot;
    private readonly string _outputDir;
    private readonly string _stubDir;
    private readonly CascodeHomeScope _cascodeHome;

    private static readonly int s_wrongMajor = NgspiceLocator.RequiredMajor - 1;

    public NgspiceVersionGateIntegrationTests()
    {
        _repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        _outputDir = Path.Combine(
            Path.GetTempPath(),
            "cascode-ngspice-gate-" + Guid.NewGuid().ToString("N")[..8]
        );
        _stubDir = Path.Combine(
            Path.GetTempPath(),
            "cascode-ngspice-stub-" + Guid.NewGuid().ToString("N")[..8]
        );
        Directory.CreateDirectory(_outputDir);
        Directory.CreateDirectory(_stubDir);
        _cascodeHome = CliIntegrationTestHelper.CreateCascodeHome(_repoRoot, "ngspice-gate");
    }

    public void Dispose()
    {
        _cascodeHome.Dispose();
        TryDelete(_outputDir);
        TryDelete(_stubDir);
    }

    [Fact]
    public async Task BenchRun_WithWrongNgspiceVersion_ReportsVersionMismatch()
    {
        CreateNgspiceStub(s_wrongMajor);

        var cascodePath = Path.Combine(_repoRoot, "tests/golden/cas/bench/RcLowpass.el.cai");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            env =>
            {
                env["PATH"] = _stubDir + Path.PathSeparator + (env["PATH"] ?? "");
            },
            "bench",
            "run",
            cascodePath,
            "-o",
            _outputDir
        );

        Assert.NotEqual(0, result.ExitCode);

        var combined = result.Stdout + "\n" + result.Stderr;
        Assert.Contains("ngspice", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(s_wrongMajor.ToString(), combined, StringComparison.Ordinal);
        Assert.Contains(
            NgspiceLocator.RequiredMajor.ToString(),
            combined,
            StringComparison.Ordinal
        );
    }

    private void CreateNgspiceStub(int fakeMajor)
    {
        var versionLine = $"** ngspice-{fakeMajor} : Circuit level simulation program";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var batPath = Path.Combine(_stubDir, "ngspice.bat");
            File.WriteAllText(batPath, $"@echo off\r\necho {versionLine}\r\n");
        }
        else
        {
            var shPath = Path.Combine(_stubDir, "ngspice");
            File.WriteAllText(shPath, $"#!/bin/sh\necho '{versionLine}'\n");
            File.SetUnixFileMode(
                shPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch { }
    }
}
