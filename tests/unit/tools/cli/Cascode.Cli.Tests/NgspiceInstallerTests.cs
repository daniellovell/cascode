using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Cascode.Cli.Services;
using Xunit;

namespace Cascode.Cli.Tests;

public sealed class NgspiceInstallerTests : IDisposable
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
    public void Install_Fails_WhenChecksumMismatch()
    {
        var runtime = CreateRuntime(isWindows: false, isLinux: true, rid: "linux-x64");
        runtime.AvailableTools.UnionWith(
            new[]
            {
                "curl",
                "tar",
                "bison",
                "flex",
                "autoconf",
                "automake",
                "libtool",
                "make",
                "cc",
            }
        );
        runtime.DownloadBytes = Encoding.UTF8.GetBytes("source-archive-bytes");
        WriteManifest(
            runtime.BaseDirectory,
            sourceHash: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            windowsHash: Sha256Hex("windows")
        );

        var result = new NgspiceInstaller(runtime).Install(force: true);

        Assert.False(result.Success);
        Assert.Contains("Checksum verification failed", result.Message);
    }

    [Fact]
    public void Install_Fails_WithLinuxDependencyHints()
    {
        var runtime = CreateRuntime(isWindows: false, isLinux: true, rid: "linux-x64");
        WriteManifest(
            runtime.BaseDirectory,
            sourceHash: Sha256Hex("source"),
            windowsHash: Sha256Hex("windows")
        );

        var result = new NgspiceInstaller(runtime).Install(force: true);

        Assert.False(result.Success);
        Assert.Contains("Missing required build tools", result.Message);
        Assert.Contains("sudo apt-get update", result.Message);
    }

    [Fact]
    public void Install_Fails_OnWindows_WhenSevenZipMissing()
    {
        var runtime = CreateRuntime(isWindows: true, isLinux: false, rid: "win-x64");
        WriteManifest(
            runtime.BaseDirectory,
            sourceHash: Sha256Hex("source"),
            windowsHash: Sha256Hex("windows")
        );

        var result = new NgspiceInstaller(runtime).Install(force: true);

        Assert.False(result.Success);
        Assert.Contains("Missing required tool: 7z", result.Message);
        Assert.Contains("winget install 7zip.7zip", result.Message);
    }

    [Fact]
    public void Install_Succeeds_AndUsesExpectedLayout_OnUnix()
    {
        if (OperatingSystem.IsWindows())
            return;

        var runtime = CreateRuntime(isWindows: false, isLinux: true, rid: "linux-x64");
        runtime.AvailableTools.UnionWith(
            new[]
            {
                "curl",
                "tar",
                "bison",
                "flex",
                "autoconf",
                "automake",
                "libtool",
                "make",
                "cc",
            }
        );
        runtime.DownloadBytes = Encoding.UTF8.GetBytes("source-archive-bytes");
        WriteManifest(
            runtime.BaseDirectory,
            sourceHash: Sha256Hex(runtime.DownloadBytes),
            windowsHash: Sha256Hex("windows")
        );

        var result = new NgspiceInstaller(runtime).Install(force: true);
        var expectedExe = NgspiceInstallLayout.GetExecutablePath(runtime.CascodeHome, "linux-x64");

        Assert.True(result.Success, result.Message);
        Assert.Equal(expectedExe, result.InstallPath);
        Assert.True(File.Exists(expectedExe));
        var (major, minor) = NgspiceLocator.QueryVersionForPath(expectedExe);
        Assert.Equal(45, major);
        Assert.Equal(2, minor);
    }

    private FakeInstallerRuntime CreateRuntime(bool isWindows, bool isLinux, string rid)
    {
        var root = Path.Combine(_tempDir.FullName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new FakeInstallerRuntime
        {
            BaseDirectory = Path.Combine(root, "base"),
            CascodeHome = Path.Combine(root, "home"),
            IsWindows = isWindows,
            IsLinux = isLinux,
            Rid = rid,
        };
    }

    private static void WriteManifest(string baseDir, string sourceHash, string windowsHash)
    {
        var assets = Path.Combine(baseDir, "Assets");
        Directory.CreateDirectory(assets);
        File.WriteAllText(
            Path.Combine(assets, "ngspice-45.2.sha256"),
            $"{sourceHash}  ngspice-45.2.tar.gz\n{windowsHash}  ngspice-45.2_64.7z\n"
        );
    }

    private static string Sha256Hex(string text) => Sha256Hex(Encoding.UTF8.GetBytes(text));

    private static string Sha256Hex(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed class FakeInstallerRuntime : INgspiceInstallerRuntime
    {
        public required string CascodeHome { get; init; }
        public required string BaseDirectory { get; init; }
        public required bool IsWindows { get; init; }
        public required bool IsLinux { get; init; }
        public required string Rid { get; init; }

        public int ProcessorCount => 2;

        public HashSet<string> AvailableTools { get; } = new(StringComparer.OrdinalIgnoreCase);

        public byte[] DownloadBytes { get; set; } = Encoding.UTF8.GetBytes("default-download");

        public string? ConfigurePrefix { get; private set; }

        public string? CurrentRid() => Rid;

        public string? FindTool(string toolName) =>
            AvailableTools.Contains(toolName) ? toolName : null;

        public CommandRunResult RunCommand(
            string fileName,
            IReadOnlyList<string> args,
            string? workingDirectory
        )
        {
            if (fileName == "./configure")
            {
                ConfigurePrefix = args.FirstOrDefault(a =>
                        a.StartsWith("--prefix=", StringComparison.Ordinal)
                    )
                    ?.Substring("--prefix=".Length);
                return new CommandRunResult(0, string.Empty, string.Empty);
            }

            if (fileName == "make" && args.SequenceEqual(new[] { "install" }))
            {
                if (ConfigurePrefix is null)
                {
                    return new CommandRunResult(1, string.Empty, "missing prefix");
                }

                var binDir = Path.Combine(ConfigurePrefix, "bin");
                Directory.CreateDirectory(binDir);
                var ngspicePath = Path.Combine(binDir, "ngspice");
                File.WriteAllText(
                    ngspicePath,
                    "#!/bin/sh\necho '** ngspice-45.2 : Circuit level simulation program'\n"
                );
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(
                        ngspicePath,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    );
                }
            }

            return new CommandRunResult(0, string.Empty, string.Empty);
        }

        public void DownloadFile(string url, string destination)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllBytes(destination, DownloadBytes);
        }

        public string ComputeSha256(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            var hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        public void ExtractTarGz(string archivePath, string destination)
        {
            Directory.CreateDirectory(Path.Combine(destination, "ngspice-45.2"));
        }

        public void EnsureExecutable(string path)
        {
            if (OperatingSystem.IsWindows())
                return;

            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        }
    }
}
