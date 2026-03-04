using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Cascode.Cli.Services;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Cli.Tests;

public sealed class NgspiceInstallerTests : IDisposable
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

    [UnixOnlyFact]
    public void Install_DefaultMode_UsesReleaseBinary()
    {
        var runtime = CreateRuntime(isWindows: false, isLinux: true, rid: "linux-x64");
        runtime.DownloadBytesByUrl["https://example.invalid/ngspice.tar.gz"] =
            Encoding.UTF8.GetBytes("release-archive");
        runtime.DownloadBytesByUrl["https://example.invalid/checksums.txt"] =
            Encoding.UTF8.GetBytes(
                $"{Sha256Hex("release-archive")}  cascode-ngspice-45.2-linux-x64.tar.gz\n"
            );

        var releaseClient = new FakeGitHubReleaseClient
        {
            ReleaseByTag =
            {
                ["v1.2.3"] = new GitHubRelease(
                    "v1.2.3",
                    new[]
                    {
                        new GitHubReleaseAsset(
                            "cascode-ngspice-45.2-linux-x64.tar.gz",
                            "https://example.invalid/ngspice.tar.gz"
                        ),
                        new GitHubReleaseAsset(
                            "cascode-ngspice-45.2-sha256.txt",
                            "https://example.invalid/checksums.txt"
                        ),
                    }
                ),
            },
        };

        var result = new NgspiceInstaller(runtime, releaseClient, () => "1.2.3").Install(
            new SimulatorInstallOptions(Force: true)
        );

        var expectedExe = NgspiceInstallLayout.GetExecutablePath(runtime.CascodeHome, "linux-x64");
        Assert.True(result.Success, result.Message);
        Assert.Equal(SimulatorInstallModes.ReleaseBinary, result.InstallMode);
        Assert.Equal(expectedExe, result.InstallPath);
        Assert.Equal("v1.2.3", releaseClient.LastReleaseTagRequested);
    }

    [Fact]
    public void Install_UsesSourceMode_WhenRequested()
    {
        var runtime = CreateRuntime(isWindows: false, isLinux: true, rid: "linux-x64");
        WriteSourceManifest(
            runtime.BaseDirectory,
            sourceHash: Sha256Hex("source"),
            windowsHash: Sha256Hex("windows")
        );

        var result = new NgspiceInstaller(runtime).Install(
            new SimulatorInstallOptions(Force: true, FromSource: true)
        );

        Assert.False(result.Success);
        Assert.Equal(SimulatorInstallModes.SourceBuild, result.InstallMode);
        Assert.Contains("Missing required build tools", result.Message);
    }

    [Fact]
    public void Install_ReleaseMode_UsesPrereleaseTagFromVersion()
    {
        var runtime = CreateRuntime(isWindows: false, isLinux: true, rid: "linux-x64");
        var releaseClient = new FakeGitHubReleaseClient();

        var result = new NgspiceInstaller(runtime, releaseClient, () => "1.4.0-rc.1").Install(
            new SimulatorInstallOptions(Force: true)
        );

        Assert.False(result.Success);
        Assert.Equal("v1.4.0-rc.1", releaseClient.LastReleaseTagRequested);
    }

    [Fact]
    public void Install_ReleaseMode_FailsForDevBuildsWithSourceFallback()
    {
        var runtime = CreateRuntime(isWindows: false, isLinux: true, rid: "linux-x64");

        var result = new NgspiceInstaller(
            runtime,
            new FakeGitHubReleaseClient(),
            () => "dev"
        ).Install(new SimulatorInstallOptions(Force: true));

        Assert.False(result.Success);
        Assert.Equal(SimulatorInstallModes.ReleaseBinary, result.InstallMode);
        Assert.Contains("--from-source", result.Message);
    }

    [Fact]
    public void Install_ReleaseMode_FailsWhenReleaseMissing()
    {
        var runtime = CreateRuntime(isWindows: false, isLinux: true, rid: "linux-x64");

        var result = new NgspiceInstaller(
            runtime,
            new FakeGitHubReleaseClient(),
            () => "1.2.3"
        ).Install(new SimulatorInstallOptions(Force: true));

        Assert.False(result.Success);
        Assert.Contains("No GitHub release", result.Message);
        Assert.Contains("--from-source", result.Message);
    }

    [Fact]
    public void Install_ReleaseMode_FailsWhenAssetMissing()
    {
        var runtime = CreateRuntime(isWindows: false, isLinux: true, rid: "linux-x64");
        var releaseClient = new FakeGitHubReleaseClient
        {
            ReleaseByTag =
            {
                ["v1.2.3"] = new GitHubRelease(
                    "v1.2.3",
                    new[]
                    {
                        new GitHubReleaseAsset(
                            "cascode-ngspice-45.2-sha256.txt",
                            "https://example.invalid/checksums.txt"
                        ),
                    }
                ),
            },
        };

        var result = new NgspiceInstaller(runtime, releaseClient, () => "1.2.3").Install(
            new SimulatorInstallOptions(Force: true)
        );

        Assert.False(result.Success);
        Assert.Contains("missing ngspice asset", result.Message);
    }

    [Fact]
    public void Install_ReleaseMode_FailsWhenChecksumAssetMissing()
    {
        var runtime = CreateRuntime(isWindows: false, isLinux: true, rid: "linux-x64");
        var releaseClient = new FakeGitHubReleaseClient
        {
            ReleaseByTag =
            {
                ["v1.2.3"] = new GitHubRelease(
                    "v1.2.3",
                    new[]
                    {
                        new GitHubReleaseAsset(
                            "cascode-ngspice-45.2-linux-x64.tar.gz",
                            "https://example.invalid/ngspice.tar.gz"
                        ),
                    }
                ),
            },
        };

        var result = new NgspiceInstaller(runtime, releaseClient, () => "1.2.3").Install(
            new SimulatorInstallOptions(Force: true)
        );

        Assert.False(result.Success);
        Assert.Contains("missing checksum asset", result.Message);
    }

    [Fact]
    public void Install_ReleaseMode_FailsWhenChecksumMismatch()
    {
        var runtime = CreateRuntime(isWindows: false, isLinux: true, rid: "linux-x64");
        runtime.DownloadBytesByUrl["https://example.invalid/ngspice.tar.gz"] =
            Encoding.UTF8.GetBytes("actual-release-archive");
        runtime.DownloadBytesByUrl["https://example.invalid/checksums.txt"] =
            Encoding.UTF8.GetBytes(
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  cascode-ngspice-45.2-linux-x64.tar.gz\n"
            );

        var releaseClient = new FakeGitHubReleaseClient
        {
            ReleaseByTag =
            {
                ["v1.2.3"] = new GitHubRelease(
                    "v1.2.3",
                    new[]
                    {
                        new GitHubReleaseAsset(
                            "cascode-ngspice-45.2-linux-x64.tar.gz",
                            "https://example.invalid/ngspice.tar.gz"
                        ),
                        new GitHubReleaseAsset(
                            "cascode-ngspice-45.2-sha256.txt",
                            "https://example.invalid/checksums.txt"
                        ),
                    }
                ),
            },
        };

        var result = new NgspiceInstaller(runtime, releaseClient, () => "1.2.3").Install(
            new SimulatorInstallOptions(Force: true)
        );

        Assert.False(result.Success);
        Assert.Contains("Checksum verification failed", result.Message);
        Assert.Contains("--from-source", result.Message);
    }

    [Fact]
    public void Install_FromSource_Fails_WhenChecksumMismatch()
    {
        var runtime = CreateRuntime(isWindows: false, isLinux: true, rid: "linux-x64");
        runtime.AvailableTools.UnionWith(
            new[] { "bison", "flex", "autoconf", "automake", "libtoolize", "make", "cc" }
        );
        runtime.DownloadBytes = Encoding.UTF8.GetBytes("source-archive-bytes");
        WriteSourceManifest(
            runtime.BaseDirectory,
            sourceHash: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            windowsHash: Sha256Hex("windows")
        );

        var result = new NgspiceInstaller(runtime).Install(
            new SimulatorInstallOptions(Force: true, FromSource: true)
        );

        Assert.False(result.Success);
        Assert.Equal(SimulatorInstallModes.SourceBuild, result.InstallMode);
        Assert.Contains("Checksum verification failed", result.Message);
    }

    [Fact]
    public void Install_FromSource_Fails_WhenSourceDirectoryIsUnexpected()
    {
        var runtime = CreateRuntime(isWindows: false, isLinux: true, rid: "linux-x64");
        runtime.AvailableTools.UnionWith(
            new[] { "bison", "flex", "autoconf", "automake", "libtool", "make", "cc" }
        );
        runtime.DownloadBytes = Encoding.UTF8.GetBytes("source-archive-bytes");
        runtime.SourceExtractDirectoryNames = new[] { "a-dir", "z-dir" };
        WriteSourceManifest(
            runtime.BaseDirectory,
            sourceHash: Sha256Hex(runtime.DownloadBytes),
            windowsHash: Sha256Hex("windows")
        );

        var result = new NgspiceInstaller(runtime).Install(
            new SimulatorInstallOptions(Force: true, FromSource: true)
        );

        Assert.False(result.Success);
        Assert.Equal(SimulatorInstallModes.SourceBuild, result.InstallMode);
        Assert.Contains("Expected extracted source directory", result.Message);
        Assert.Contains("a-dir, z-dir", result.Message);
    }

    [Fact]
    public void Install_FromSource_Fails_WithLinuxDependencyHints()
    {
        var runtime = CreateRuntime(isWindows: false, isLinux: true, rid: "linux-x64");
        WriteSourceManifest(
            runtime.BaseDirectory,
            sourceHash: Sha256Hex("source"),
            windowsHash: Sha256Hex("windows")
        );

        var result = new NgspiceInstaller(runtime).Install(
            new SimulatorInstallOptions(Force: true, FromSource: true)
        );

        Assert.False(result.Success);
        Assert.Equal(SimulatorInstallModes.SourceBuild, result.InstallMode);
        Assert.Contains("Missing required build tools", result.Message);
        Assert.Contains("sudo apt-get update", result.Message);
    }

    [Fact]
    public void Install_FromSource_Fails_OnWindows_WhenSevenZipMissing()
    {
        var runtime = CreateRuntime(isWindows: true, isLinux: false, rid: "win-x64");
        WriteSourceManifest(
            runtime.BaseDirectory,
            sourceHash: Sha256Hex("source"),
            windowsHash: Sha256Hex("windows")
        );

        var result = new NgspiceInstaller(runtime).Install(
            new SimulatorInstallOptions(Force: true, FromSource: true)
        );

        Assert.False(result.Success);
        Assert.Equal(SimulatorInstallModes.SourceBuild, result.InstallMode);
        Assert.Contains("Missing required tool: 7z", result.Message);
        Assert.Contains("winget install 7zip.7zip", result.Message);
    }

    [UnixOnlyFact]
    public void Install_FromSource_Succeeds_AndUsesExpectedLayout_OnUnix()
    {
        var runtime = CreateRuntime(isWindows: false, isLinux: true, rid: "linux-x64");
        runtime.AvailableTools.UnionWith(
            new[] { "bison", "flex", "autoconf", "automake", "libtool", "make", "cc" }
        );
        runtime.DownloadBytes = Encoding.UTF8.GetBytes("source-archive-bytes");
        WriteSourceManifest(
            runtime.BaseDirectory,
            sourceHash: Sha256Hex(runtime.DownloadBytes),
            windowsHash: Sha256Hex("windows")
        );

        var result = new NgspiceInstaller(runtime).Install(
            new SimulatorInstallOptions(Force: true, FromSource: true)
        );
        var expectedExe = NgspiceInstallLayout.GetExecutablePath(runtime.CascodeHome, "linux-x64");

        Assert.True(result.Success, result.Message);
        Assert.Equal(SimulatorInstallModes.SourceBuild, result.InstallMode);
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
        var home = CascodeHome.CreateUnder(
            root,
            "ngspice-installer-home",
            setEnvironmentVariable: false
        );
        _homes.Add(home);
        return new FakeInstallerRuntime
        {
            BaseDirectory = Path.Combine(root, "base"),
            CascodeHome = home.Path,
            IsWindows = isWindows,
            IsLinux = isLinux,
            Rid = rid,
        };
    }

    private static void WriteSourceManifest(string baseDir, string sourceHash, string windowsHash)
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

    private sealed class FakeGitHubReleaseClient : IGitHubReleaseClient
    {
        public Dictionary<string, GitHubRelease> ReleaseByTag { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public string? LastReleaseTagRequested { get; private set; }

        public GitHubRelease? FetchLatestRelease() => null;

        public GitHubRelease? FetchReleaseByTag(string tagName)
        {
            LastReleaseTagRequested = tagName;
            return ReleaseByTag.GetValueOrDefault(tagName);
        }
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

        public Dictionary<string, byte[]> DownloadBytesByUrl { get; } = new(StringComparer.Ordinal);

        public string? ConfigurePrefix { get; private set; }
        public IReadOnlyList<string> SourceExtractDirectoryNames { get; set; } =
            new[] { "ngspice-45.2" };

        public string? CurrentRid() => Rid;

        public string? FindTool(string toolName) =>
            AvailableTools.Contains(toolName) ? toolName : null;

        public CommandRunResult RunCommand(
            string fileName,
            IReadOnlyList<string> args,
            string? workingDirectory
        )
        {
            if (string.Equals(Path.GetFileName(fileName), "configure", StringComparison.Ordinal))
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
            if (DownloadBytesByUrl.TryGetValue(url, out var bytes))
            {
                File.WriteAllBytes(destination, bytes);
                return;
            }

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
            if (
                Path.GetFileName(archivePath)
                    .StartsWith("cascode-ngspice", StringComparison.Ordinal)
            )
            {
                WriteReleaseArchiveContents(destination, executableName: "ngspice");
                return;
            }

            foreach (var sourceDirName in SourceExtractDirectoryNames)
            {
                var sourceDir = Path.Combine(destination, sourceDirName);
                Directory.CreateDirectory(sourceDir);
                var configurePath = Path.Combine(sourceDir, "configure");
                File.WriteAllText(configurePath, "#!/bin/sh\nexit 0\n");
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(
                        configurePath,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    );
                }
            }
        }

        public void ExtractZip(string archivePath, string destination)
        {
            WriteReleaseArchiveContents(destination, executableName: "ngspice.exe");
        }

        private static void WriteReleaseArchiveContents(string destination, string executableName)
        {
            var bin = Path.Combine(destination, "cascode-ngspice-45.2", "bin");
            Directory.CreateDirectory(bin);
            var binaryPath = Path.Combine(bin, executableName);
            if (executableName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(binaryPath, "stub");
                return;
            }

            File.WriteAllText(
                binaryPath,
                "#!/bin/sh\necho '** ngspice-45.2 : Circuit level simulation program'\n"
            );
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    binaryPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                );
            }
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
