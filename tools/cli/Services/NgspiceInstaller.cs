using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Cascode.Cli.Services;

/// <summary>
/// Installs and validates ngspice 45.2 under CASCODE_HOME.
/// </summary>
internal sealed class NgspiceInstaller : ISimulatorInstaller
{
    private const string SourceArchiveName = "ngspice-45.2.tar.gz";
    private const string WindowsArchiveName = "ngspice-45.2_64.7z";
    private const string SourceArchiveUrl =
        "https://sourceforge.net/projects/ngspice/files/ng-spice-rework/45.2/ngspice-45.2.tar.gz/download";
    private const string WindowsArchiveUrl =
        "https://sourceforge.net/projects/ngspice/files/ng-spice-rework/45.2/ngspice-45.2_64.7z/download";

    private static readonly Regex ReleaseVersionPattern = new(
        @"^\d+\.\d+\.\d+(?:-[0-9A-Za-z\.-]+)?$",
        RegexOptions.Compiled
    );

    private readonly INgspiceInstallerRuntime _runtime;
    private readonly IGitHubReleaseClient _releaseClient;
    private readonly Func<string?> _rawVersionProvider;

    public NgspiceInstaller()
        : this(new DefaultNgspiceInstallerRuntime(), new GitHubReleaseClient(), GetRawCliVersion)
    { }

    internal NgspiceInstaller(
        INgspiceInstallerRuntime runtime,
        IGitHubReleaseClient releaseClient,
        Func<string?> rawVersionProvider
    )
    {
        _runtime = runtime;
        _releaseClient = releaseClient;
        _rawVersionProvider = rawVersionProvider;
    }

    internal NgspiceInstaller(INgspiceInstallerRuntime runtime)
        : this(runtime, new GitHubReleaseClient(), GetRawCliVersion) { }

    public string Name => "ngspice";

    /// <summary>
    /// Installs ngspice 45.2 for the current RID under CASCODE_HOME.
    /// </summary>
    public SimulatorInstallResult Install(SimulatorInstallOptions options)
    {
        var installMode = options.FromSource
            ? SimulatorInstallModes.SourceBuild
            : SimulatorInstallModes.ReleaseBinary;
        var rid = _runtime.CurrentRid();
        if (rid is null)
            return Fail(
                "Unsupported platform. ngspice installer supports Linux/macOS/Windows x64+arm64.",
                installMode
            );

        var cascodeHome = _runtime.CascodeHome;
        var installBin = NgspiceInstallLayout.GetBinDirectory(cascodeHome, rid);
        var installExe = NgspiceInstallLayout.GetExecutablePath(cascodeHome, rid);

        if (!options.Force && File.Exists(installExe) && TryValidateBinary(installExe, out _))
        {
            return Success(
                $"ngspice {NgspiceInstallLayout.Version} already installed at {installExe}",
                installExe,
                installMode
            );
        }

        Directory.CreateDirectory(installBin);
        var tempRoot = Path.Combine(Path.GetTempPath(), $"cascode-ngspice-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            if (options.FromSource)
            {
                return InstallFromSource(rid, tempRoot, installBin, installExe);
            }

            return InstallFromReleaseBinary(rid, tempRoot, installBin, installExe);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private SimulatorInstallResult InstallFromReleaseBinary(
        string rid,
        string tempRoot,
        string installBin,
        string installExe
    )
    {
        if (
            !TryResolveReleaseBinaryInputs(
                rid,
                tempRoot,
                out var releaseTag,
                out var archiveName,
                out var archiveUrl,
                out var checksums,
                out var failure
            )
        )
        {
            return failure!;
        }

        var archive = DownloadAndVerifyArchive(
            tempRoot,
            archiveName,
            archiveUrl,
            checksums,
            SimulatorInstallModes.ReleaseBinary
        );
        if (!archive.Success)
            return archive;

        var extractedExe = ExtractReleaseExecutable(
            tempRoot,
            archive.InstallPath!,
            archiveName,
            out var extractFailure
        );
        if (extractFailure is not null)
        {
            return extractFailure;
        }
        if (extractedExe is null)
        {
            return FailBinary($"Could not locate ngspice in extracted archive '{archiveName}'.");
        }

        CopyDirectoryContents(Path.GetDirectoryName(extractedExe)!, installBin);
        if (!TryValidateBinary(installExe, out var validationError))
            return FailBinary($"Installed binary validation failed: {validationError}");

        return Success(
            $"Installed ngspice {NgspiceInstallLayout.Version} to {installExe} from release {releaseTag}",
            installExe,
            SimulatorInstallModes.ReleaseBinary
        );
    }

    private bool TryResolveReleaseBinaryInputs(
        string rid,
        string tempRoot,
        out string releaseTag,
        out string archiveName,
        out string archiveUrl,
        out IReadOnlyDictionary<string, string> checksums,
        out SimulatorInstallResult? failure
    )
    {
        releaseTag = string.Empty;
        archiveName = string.Empty;
        archiveUrl = string.Empty;
        checksums = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        failure = null;

        if (!TryResolveReleaseTag(out var resolvedTag, out var tagError))
        {
            failure = FailBinary(tagError);
            return false;
        }

        releaseTag = resolvedTag!;
        var release = _releaseClient.FetchReleaseByTag(releaseTag);
        if (release is null)
        {
            failure = FailBinary(
                $"No GitHub release was found for tag '{releaseTag}'."
                    + " Default install works only for published release tags."
            );
            return false;
        }

        var requestedArchiveName = ReleaseArchiveNameForRid(rid);
        archiveName = requestedArchiveName;
        var archiveAsset = release.Assets.FirstOrDefault(a =>
            string.Equals(a.Name, requestedArchiveName, StringComparison.OrdinalIgnoreCase)
        );
        if (archiveAsset is null)
        {
            failure = FailBinary(
                $"Release '{releaseTag}' is missing ngspice asset '{archiveName}'."
            );
            return false;
        }

        archiveUrl = archiveAsset.BrowserDownloadUrl;
        checksums = LoadReleaseChecksums(tempRoot, release, releaseTag, archiveName, out failure);
        return failure is null;
    }

    private IReadOnlyDictionary<string, string> LoadReleaseChecksums(
        string tempRoot,
        GitHubRelease release,
        string releaseTag,
        string archiveName,
        out SimulatorInstallResult? failure
    )
    {
        failure = null;
        var checksumName = $"cascode-ngspice-{NgspiceInstallLayout.Version}-sha256.txt";
        var checksumAsset = release.Assets.FirstOrDefault(a =>
            string.Equals(a.Name, checksumName, StringComparison.OrdinalIgnoreCase)
        );
        if (checksumAsset is null)
        {
            failure = FailBinary(
                $"Release '{releaseTag}' is missing checksum asset '{checksumName}'."
            );
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var checksumPath = Path.Combine(tempRoot, checksumName);
        try
        {
            _runtime.DownloadFile(checksumAsset.BrowserDownloadUrl, checksumPath);
        }
        catch (Exception ex)
        {
            failure = FailBinary(
                $"Failed to download checksum asset '{checksumName}': {ex.Message}"
            );
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var checksums = LoadChecksumsFromFile(checksumPath);
        if (!checksums.TryGetValue(archiveName, out _))
        {
            failure = FailBinary(
                $"Checksum manifest '{checksumName}' does not include '{archiveName}'."
            );
        }

        return checksums;
    }

    private string? ExtractReleaseExecutable(
        string tempRoot,
        string archivePath,
        string archiveName,
        out SimulatorInstallResult? failure
    )
    {
        failure = null;
        var extractDir = Path.Combine(tempRoot, "extract");
        Directory.CreateDirectory(extractDir);

        try
        {
            if (_runtime.IsWindows)
            {
                _runtime.ExtractZip(archivePath, extractDir);
            }
            else
            {
                _runtime.ExtractTarGz(archivePath, extractDir);
            }
        }
        catch (Exception ex)
        {
            failure = FailBinary($"Failed to extract {archiveName}: {ex.Message}");
            return null;
        }

        var executableName = _runtime.IsWindows ? "ngspice.exe" : "ngspice";
        var extractedExe = Directory
            .EnumerateFiles(extractDir, executableName, SearchOption.AllDirectories)
            .FirstOrDefault();
        if (extractedExe is null)
        {
            failure = FailBinary(
                $"Could not find {executableName} in extracted archive '{archiveName}'."
            );
        }

        return extractedExe;
    }

    private SimulatorInstallResult InstallFromSource(
        string rid,
        string tempRoot,
        string installBin,
        string installExe
    )
    {
        var checksums = LoadBundledSourceChecksums();
        if (checksums is null)
        {
            return Fail(
                "Missing checksum manifest. Reinstall Cascode CLI and retry.",
                SimulatorInstallModes.SourceBuild
            );
        }

        return _runtime.IsWindows
            ? InstallWindowsFromSource(rid, checksums, tempRoot, installBin, installExe)
            : InstallUnixFromSource(rid, checksums, tempRoot, installBin, installExe);
    }

    private SimulatorInstallResult InstallWindowsFromSource(
        string rid,
        IReadOnlyDictionary<string, string> checksums,
        string tempRoot,
        string installBin,
        string installExe
    )
    {
        if (_runtime.FindTool("7z") is not string sevenZip)
        {
            return Fail(
                "Missing required tool: 7z.\nInstall it with: winget install 7zip.7zip",
                SimulatorInstallModes.SourceBuild
            );
        }

        var archive = DownloadAndVerifyArchive(
            tempRoot,
            WindowsArchiveName,
            WindowsArchiveUrl,
            checksums,
            SimulatorInstallModes.SourceBuild
        );
        if (!archive.Success)
            return archive;

        var extractDir = Path.Combine(tempRoot, "extract");
        Directory.CreateDirectory(extractDir);

        var extract = _runtime.RunCommand(
            sevenZip,
            new[] { "x", archive.InstallPath!, $"-o{extractDir}", "-y" },
            workingDirectory: null
        );
        if (extract.ExitCode != 0)
        {
            return Fail(
                $"Failed to extract {WindowsArchiveName}: {TrimOutput(extract.Stderr)}",
                SimulatorInstallModes.SourceBuild
            );
        }

        var extractedExe = Directory
            .EnumerateFiles(extractDir, "ngspice.exe", SearchOption.AllDirectories)
            .FirstOrDefault();
        if (extractedExe is null)
        {
            return Fail(
                $"Could not find ngspice.exe in extracted archive {WindowsArchiveName}.",
                SimulatorInstallModes.SourceBuild
            );
        }

        CopyDirectoryContents(Path.GetDirectoryName(extractedExe)!, installBin);
        if (!TryValidateBinary(installExe, out var validationError))
        {
            return Fail(
                $"Installed binary validation failed: {validationError}",
                SimulatorInstallModes.SourceBuild
            );
        }

        var note = rid == "win-arm64" ? " (using win-x64 ngspice binary)" : string.Empty;
        return Success(
            $"Installed ngspice {NgspiceInstallLayout.Version} to {installExe}{note}",
            installExe,
            SimulatorInstallModes.SourceBuild
        );
    }

    private SimulatorInstallResult InstallUnixFromSource(
        string rid,
        IReadOnlyDictionary<string, string> checksums,
        string tempRoot,
        string installBin,
        string installExe
    )
    {
        var missing = MissingUnixBuildDependencies();
        if (missing.Count > 0)
            return Fail(BuildDependencyMessage(missing), SimulatorInstallModes.SourceBuild);

        var archive = DownloadAndVerifyArchive(
            tempRoot,
            SourceArchiveName,
            SourceArchiveUrl,
            checksums,
            SimulatorInstallModes.SourceBuild
        );
        if (!archive.Success)
            return archive;

        var sourceDir = ExtractSourceDirectory(
            tempRoot,
            archive.InstallPath!,
            out var extractError
        );
        if (sourceDir is null)
        {
            return Fail(
                extractError ?? "ngspice source extraction produced no source directory.",
                SimulatorInstallModes.SourceBuild
            );
        }

        var buildPrefix = Path.Combine(tempRoot, "install");
        Directory.CreateDirectory(buildPrefix);

        var buildError = BuildUnixSource(sourceDir, buildPrefix);
        if (buildError is not null)
            return Fail(buildError, SimulatorInstallModes.SourceBuild);

        var builtExe = Path.Combine(buildPrefix, "bin", "ngspice");
        if (!File.Exists(builtExe))
        {
            return Fail(
                "ngspice build succeeded but binary was not found under install prefix.",
                SimulatorInstallModes.SourceBuild
            );
        }

        Directory.CreateDirectory(installBin);
        File.Copy(builtExe, installExe, overwrite: true);
        _runtime.EnsureExecutable(installExe);

        if (!TryValidateBinary(installExe, out var validationError))
        {
            return Fail(
                $"Installed binary validation failed: {validationError}",
                SimulatorInstallModes.SourceBuild
            );
        }

        return Success(
            $"Installed ngspice {NgspiceInstallLayout.Version} to {installExe} ({rid})",
            installExe,
            SimulatorInstallModes.SourceBuild
        );
    }

    /// <summary>
    /// Downloads an archive and verifies its SHA-256 hash against the pinned manifest.
    /// </summary>
    private SimulatorInstallResult DownloadAndVerifyArchive(
        string tempRoot,
        string archiveName,
        string url,
        IReadOnlyDictionary<string, string> checksums,
        string installMode
    )
    {
        var archivePath = Path.Combine(tempRoot, archiveName);

        try
        {
            _runtime.DownloadFile(url, archivePath);
        }
        catch (Exception ex)
        {
            return installMode == SimulatorInstallModes.ReleaseBinary
                ? FailBinary($"Failed to download {archiveName}: {ex.Message}")
                : Fail($"Failed to download {archiveName}: {ex.Message}", installMode);
        }

        if (!checksums.TryGetValue(archiveName, out var expected))
        {
            return installMode == SimulatorInstallModes.ReleaseBinary
                ? FailBinary($"Checksum not found for {archiveName}.")
                : Fail($"Checksum not found for {archiveName}.", installMode);
        }

        var actual = _runtime.ComputeSha256(archivePath).ToLowerInvariant();
        if (!string.Equals(actual, expected.ToLowerInvariant(), StringComparison.Ordinal))
        {
            return installMode == SimulatorInstallModes.ReleaseBinary
                ? FailBinary($"Checksum verification failed for {archiveName}.")
                : Fail($"Checksum verification failed for {archiveName}.", installMode);
        }

        return Success($"Downloaded {archiveName}", archivePath, installMode);
    }

    /// <summary>
    /// Extracts the source archive and returns the source directory.
    /// </summary>
    private string? ExtractSourceDirectory(string tempRoot, string archivePath, out string? error)
    {
        error = null;
        var extractDir = Path.Combine(tempRoot, "extract");
        Directory.CreateDirectory(extractDir);

        try
        {
            _runtime.ExtractTarGz(archivePath, extractDir);
        }
        catch (Exception ex)
        {
            error = $"Failed to extract {SourceArchiveName}: {ex.Message}";
            return null;
        }

        var expected = Path.Combine(extractDir, $"ngspice-{NgspiceInstallLayout.Version}");
        if (Directory.Exists(expected))
            return expected;

        var candidates = Directory
            .GetDirectories(extractDir)
            .Select(Path.GetFileName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        var listed = candidates.Length == 0 ? "<none>" : string.Join(", ", candidates);
        error =
            $"Expected extracted source directory 'ngspice-{NgspiceInstallLayout.Version}' under '{extractDir}', "
            + $"found: {listed}.";
        return null;
    }

    /// <summary>
    /// Configures and builds ngspice from source.
    /// </summary>
    private string? BuildUnixSource(string sourceDir, string prefix)
    {
        var configurePath = Path.Combine(sourceDir, "configure");
        if (!File.Exists(configurePath))
            return $"ngspice configure script was not found at '{configurePath}'.";

        _runtime.EnsureExecutable(configurePath);

        CommandRunResult configure;
        try
        {
            configure = _runtime.RunCommand(
                configurePath,
                new[]
                {
                    $"--prefix={prefix}",
                    "--without-x",
                    "--without-readline",
                    "--enable-xspice",
                    "CFLAGS=-O2",
                },
                sourceDir
            );
        }
        catch (Exception ex)
        {
            return $"ngspice configure launch failed: {ex.Message}";
        }

        if (configure.ExitCode != 0)
            return $"ngspice configure failed: {TrimOutput(configure.Stderr)}";

        var jobs = Math.Max(1, _runtime.ProcessorCount);
        CommandRunResult makeBuild;
        try
        {
            makeBuild = _runtime.RunCommand("make", new[] { $"-j{jobs}" }, sourceDir);
        }
        catch (Exception ex)
        {
            return $"ngspice build launch failed: {ex.Message}";
        }

        if (makeBuild.ExitCode != 0)
            return $"ngspice build failed: {TrimOutput(makeBuild.Stderr)}";

        CommandRunResult makeInstall;
        try
        {
            makeInstall = _runtime.RunCommand("make", new[] { "install" }, sourceDir);
        }
        catch (Exception ex)
        {
            return $"ngspice install launch failed: {ex.Message}";
        }

        if (makeInstall.ExitCode != 0)
            return $"ngspice install failed: {TrimOutput(makeInstall.Stderr)}";

        return null;
    }

    /// <summary>
    /// Detects missing toolchain dependencies for Unix source builds.
    /// </summary>
    private List<string> MissingUnixBuildDependencies()
    {
        var required = new[] { "bison", "flex", "autoconf", "automake", "make", "cc" };
        var missing = new List<string>();
        foreach (var tool in required)
        {
            if (_runtime.FindTool(tool) is null)
                missing.Add(tool);
        }

        // Ubuntu commonly exposes libtool functionality as libtoolize.
        if (_runtime.FindTool("libtool") is null && _runtime.FindTool("libtoolize") is null)
            missing.Add("libtool");

        return missing;
    }

    /// <summary>
    /// Builds a user-actionable dependency installation hint.
    /// </summary>
    private string BuildDependencyMessage(IReadOnlyList<string> missing)
    {
        var joined = string.Join(", ", missing);
        if (_runtime.IsLinux)
        {
            return $"Missing required build tools: {joined}.\nInstall with: sudo apt-get update && sudo apt-get install -y bison flex autoconf automake libtool make gcc";
        }

        return $"Missing required build tools: {joined}.\nInstall with: brew install bison flex autoconf automake libtool coreutils gnu-sed findutils gawk";
    }

    /// <summary>
    /// Loads pinned ngspice archive checksums from bundled CLI assets.
    /// </summary>
    private IReadOnlyDictionary<string, string>? LoadBundledSourceChecksums()
    {
        var manifestPath = Path.Combine(_runtime.BaseDirectory, "Assets", "ngspice-45.2.sha256");
        if (!File.Exists(manifestPath))
            return null;

        return LoadChecksumsFromFile(manifestPath);
    }

    private static IReadOnlyDictionary<string, string> LoadChecksumsFromFile(string manifestPath)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(manifestPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            var pieces = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (pieces.Length >= 2)
                map[pieces[^1]] = pieces[0];
        }

        return map;
    }

    private bool TryResolveReleaseTag(out string? releaseTag, out string error)
    {
        releaseTag = null;
        var rawVersion = _rawVersionProvider();
        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            error = "Could not determine this Cascode CLI version for release asset lookup.";
            return false;
        }

        if (string.Equals(rawVersion, "dev", StringComparison.OrdinalIgnoreCase))
        {
            error = "Release-binary install is unavailable for dev CLI builds.";
            return false;
        }

        if (!ReleaseVersionPattern.IsMatch(rawVersion))
        {
            error = $"CLI version '{rawVersion}' does not map to a release tag.";
            return false;
        }

        releaseTag = $"v{rawVersion}";
        error = string.Empty;
        return true;
    }

    private static string ReleaseArchiveNameForRid(string rid)
    {
        var extension = rid.StartsWith("win-", StringComparison.OrdinalIgnoreCase)
            ? "zip"
            : "tar.gz";
        return $"cascode-ngspice-{NgspiceInstallLayout.Version}-{rid}.{extension}";
    }

    private static string? GetRawCliVersion()
    {
        var asm = typeof(NgspiceInstaller).Assembly;
        var info =
            asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(info))
            return asm.GetName().Version?.ToString();
        return info.Split('+', 2)[0];
    }

    private static bool TryValidateBinary(string binaryPath, out string error)
    {
        if (!File.Exists(binaryPath))
        {
            error = $"binary not found at '{binaryPath}'";
            return false;
        }

        try
        {
            var (major, _) = NgspiceLocator.QueryVersionForPath(binaryPath);
            if (major != NgspiceLocator.RequiredMajor)
            {
                error =
                    $"binary reports ngspice {major}, but Cascode requires {NgspiceLocator.RequiredMajor}.";
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void CopyDirectoryContents(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (
            var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.TopDirectoryOnly)
        )
        {
            var destination = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static string TrimOutput(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "no stderr output";

        var trimmed = text.Trim();
        return trimmed.Length <= 500 ? trimmed : trimmed[..500];
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch { }
    }

    private static SimulatorInstallResult Success(string message, string path, string installMode)
    {
        return new SimulatorInstallResult(
            Success: true,
            ExitCode: 0,
            Message: message,
            InstallPath: path,
            InstallMode: installMode
        );
    }

    private static SimulatorInstallResult Fail(string message, string installMode)
    {
        return new SimulatorInstallResult(
            Success: false,
            ExitCode: 1,
            Message: message,
            InstallMode: installMode
        );
    }

    private static SimulatorInstallResult FailBinary(string message)
    {
        return Fail(
            $"{message}\nRun: cascode install ngspice --from-source",
            SimulatorInstallModes.ReleaseBinary
        );
    }
}
