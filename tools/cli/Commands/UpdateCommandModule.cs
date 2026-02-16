using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cascode.Cli.Output;

namespace Cascode.Cli.Commands;

internal sealed class UpdateCommandModule : ICommandModule
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly CliOutputProvider _output;

    public UpdateCommandModule(CliOutputProvider output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    public void Register(CommandRegistry registry)
    {
        registry.Register(
            new DelegateCliCommand(
                path: "update",
                description: "Check for and install CLI updates",
                handler: RunUpdate
            )
        );
    }

    private CommandResult RunUpdate(string[] args)
    {
        var output = _output.Get();

        var rawVersion = GetRawVersion();
        if (string.Equals(rawVersion, "dev", StringComparison.OrdinalIgnoreCase))
        {
            output.Error(
                "Self-update is not available for dev builds. "
                    + "Rebuild from source with ./scripts/install-dev-tool.sh to pick up changes."
            );
            return CommandResult.Failure;
        }

        var currentVersion = ParseVersion(rawVersion);
        if (currentVersion is null)
        {
            output.Error("Could not determine current version.");
            return CommandResult.Failure;
        }

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath))
        {
            output.Error(
                "Could not determine binary path. Update is not supported in this environment."
            );
            return CommandResult.Failure;
        }

        return output.RunWithProgress(
            "Checking for updates...",
            updateStatus =>
            {
                var target = ResolveUpdateTarget(output, currentVersion);
                if (target is null)
                    return CommandResult.Failure;

                if (target.Value.AlreadyCurrent)
                {
                    output.Success($"Already up to date ({currentVersion}).");
                    return CommandResult.Success;
                }

                return DownloadAndInstall(
                    output,
                    updateStatus,
                    target.Value,
                    processPath!,
                    currentVersion
                );
            }
        );
    }

    private record struct UpdateTarget(
        GitHubAsset Asset,
        string AssetName,
        Version LatestVersion,
        bool IsWindows,
        bool AlreadyCurrent
    );

    private static UpdateTarget? ResolveUpdateTarget(ICliOutput output, Version currentVersion)
    {
        var release = FetchLatestRelease();
        if (release is null)
        {
            output.Error("Failed to fetch latest release from GitHub.");
            return null;
        }

        var latestVersion = ParseVersion(release.TagName.TrimStart('v'));
        if (latestVersion is null)
        {
            output.Error($"Could not parse release version '{release.TagName}'.");
            return null;
        }

        if (currentVersion >= latestVersion)
            return new UpdateTarget(default!, "", latestVersion, false, AlreadyCurrent: true);

        var rid = GetRuntimeIdentifier();
        if (rid is null)
        {
            output.Error("Unsupported platform. Cannot determine download target.");
            return null;
        }

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var suffix = isWindows ? ".zip" : ".tar.gz";
        var assetName = $"cascode-{rid}{suffix}";
        var asset = release.Assets?.FirstOrDefault(a =>
            string.Equals(a.Name, assetName, StringComparison.OrdinalIgnoreCase)
        );

        if (asset is null)
        {
            output.Error($"No release asset found for '{assetName}'.");
            return null;
        }

        return new UpdateTarget(asset, assetName, latestVersion, isWindows, AlreadyCurrent: false);
    }

    private static CommandResult DownloadAndInstall(
        ICliOutput output,
        Action<string> updateStatus,
        UpdateTarget target,
        string processPath,
        Version currentVersion
    )
    {
        updateStatus($"Downloading {target.AssetName}...");
        var tempDir = Path.Combine(Path.GetTempPath(), $"cascode-update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var archivePath = Path.Combine(tempDir, target.AssetName);
            DownloadFile(target.Asset.BrowserDownloadUrl, archivePath);

            updateStatus("Extracting...");
            var extractDir = Path.Combine(tempDir, "extract");
            Directory.CreateDirectory(extractDir);

            if (target.IsWindows)
                ZipFile.ExtractToDirectory(archivePath, extractDir);
            else
                ExtractTarGz(archivePath, extractDir);

            var binaryName = target.IsWindows ? "cascode.exe" : "cascode";
            var newBinary = FindBinary(extractDir, binaryName);
            if (newBinary is null)
            {
                output.Error("Could not find binary in downloaded archive.");
                return CommandResult.Failure;
            }

            updateStatus("Installing...");
            ReplaceBinary(processPath, newBinary, target.IsWindows);

            output.Success($"Updated {currentVersion} → {target.LatestVersion}");
            return CommandResult.Success;
        }
        catch (Exception ex)
        {
            output.Error($"Update failed: {ex.Message}");
            return CommandResult.Failure;
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch { }
        }
    }

    private static string? GetRawVersion()
    {
        var asm = typeof(UpdateCommandModule).Assembly;
        var info =
            asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(info))
            return asm.GetName().Version?.ToString();
        return info.Split('+', 2)[0];
    }

    private static Version? ParseVersion(string? raw)
    {
        if (raw is null)
            return null;
        var numeric = raw.Split('-', 2)[0];
        return Version.TryParse(numeric, out var v) ? v : null;
    }

    private static string? GetRuntimeIdentifier()
    {
        string os;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            os = "win";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            os = "linux";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            os = "osx";
        else
            return null;

        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => null,
        };

        return arch is null ? null : $"{os}-{arch}";
    }

    private static GitHubRelease? FetchLatestRelease()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://api.github.com/repos/daniellovell/cascode/releases/latest"
        );
        request.Headers.Add("User-Agent", "cascode-cli");
        request.Headers.Add("Accept", "application/vnd.github+json");

        using var response = Http.Send(request);
        if (!response.IsSuccessStatusCode)
            return null;

        using var stream = response.Content.ReadAsStream();
        return JsonSerializer.Deserialize<GitHubRelease>(stream);
    }

    private static void DownloadFile(string url, string destination)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "cascode-cli");
        using var response = Http.Send(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        using var stream = response.Content.ReadAsStream();
        using var file = File.Create(destination);
        stream.CopyTo(file);
    }

    private static void ExtractTarGz(string archivePath, string destination)
    {
        using var fileStream = File.OpenRead(archivePath);
        using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        TarFile.ExtractToDirectory(gzipStream, destination, overwriteFiles: true);
    }

    private static string? FindBinary(string directory, string binaryName)
    {
        foreach (
            var file in Directory.EnumerateFiles(directory, binaryName, SearchOption.AllDirectories)
        )
            return file;
        return null;
    }

    private static void ReplaceBinary(string currentPath, string newBinaryPath, bool isWindows)
    {
        if (isWindows)
        {
            var oldPath = currentPath + ".old";
            if (File.Exists(oldPath))
                File.Delete(oldPath);
            File.Move(currentPath, oldPath);
            try
            {
                File.Copy(newBinaryPath, currentPath);
            }
            catch
            {
                File.Move(oldPath, currentPath);
                throw;
            }
        }
        else
        {
            File.Copy(newBinaryPath, currentPath, overwrite: true);
            if (!OperatingSystem.IsWindows())
                SetExecutablePermission(currentPath);
        }
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static void SetExecutablePermission(string path)
    {
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead
                | UnixFileMode.OtherExecute
        );
    }

    private sealed record GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = "";

        [JsonPropertyName("assets")]
        public GitHubAsset[]? Assets { get; init; }
    }

    private sealed record GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; init; } = "";
    }
}
