using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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

    private readonly INgspiceInstallerRuntime _runtime;

    public NgspiceInstaller()
        : this(new DefaultNgspiceInstallerRuntime()) { }

    internal NgspiceInstaller(INgspiceInstallerRuntime runtime)
    {
        _runtime = runtime;
    }

    public string Name => "ngspice";

    /// <summary>
    /// Installs ngspice 45.2 for the current RID under CASCODE_HOME.
    /// </summary>
    public SimulatorInstallResult Install(bool force)
    {
        var rid = _runtime.CurrentRid();
        if (rid is null)
            return Fail(
                "Unsupported platform. ngspice installer supports Linux/macOS/Windows x64+arm64."
            );

        var cascodeHome = _runtime.CascodeHome;
        var installBin = NgspiceInstallLayout.GetBinDirectory(cascodeHome, rid);
        var installExe = NgspiceInstallLayout.GetExecutablePath(cascodeHome, rid);

        if (!force && File.Exists(installExe) && TryValidateBinary(installExe, out _))
        {
            return Success(
                $"ngspice {NgspiceInstallLayout.Version} already installed at {installExe}",
                installExe
            );
        }

        var checksums = LoadChecksums();
        if (checksums is null)
            return Fail("Missing checksum manifest. Reinstall Cascode CLI and retry.");

        Directory.CreateDirectory(installBin);
        var tempRoot = Path.Combine(Path.GetTempPath(), $"cascode-ngspice-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            return _runtime.IsWindows
                ? InstallWindows(rid, checksums, tempRoot, installBin, installExe)
                : InstallUnix(rid, checksums, tempRoot, installBin, installExe);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private SimulatorInstallResult InstallWindows(
        string rid,
        IReadOnlyDictionary<string, string> checksums,
        string tempRoot,
        string installBin,
        string installExe
    )
    {
        if (_runtime.FindTool("7z") is not string sevenZip)
            return Fail("Missing required tool: 7z.\nInstall it with: winget install 7zip.7zip");

        var archive = DownloadAndVerifyArchive(
            tempRoot,
            WindowsArchiveName,
            WindowsArchiveUrl,
            checksums
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
            return Fail($"Failed to extract {WindowsArchiveName}: {TrimOutput(extract.Stderr)}");

        var extractedExe = Directory
            .EnumerateFiles(extractDir, "ngspice.exe", SearchOption.AllDirectories)
            .FirstOrDefault();
        if (extractedExe is null)
            return Fail($"Could not find ngspice.exe in extracted archive {WindowsArchiveName}.");

        CopyDirectoryContents(Path.GetDirectoryName(extractedExe)!, installBin);
        if (!TryValidateBinary(installExe, out var validationError))
            return Fail($"Installed binary validation failed: {validationError}");

        var note = rid == "win-arm64" ? " (using win-x64 ngspice binary)" : string.Empty;
        return Success(
            $"Installed ngspice {NgspiceInstallLayout.Version} to {installExe}{note}",
            installExe
        );
    }

    private SimulatorInstallResult InstallUnix(
        string rid,
        IReadOnlyDictionary<string, string> checksums,
        string tempRoot,
        string installBin,
        string installExe
    )
    {
        var missing = MissingUnixBuildDependencies();
        if (missing.Count > 0)
            return Fail(BuildDependencyMessage(missing));

        var archive = DownloadAndVerifyArchive(
            tempRoot,
            SourceArchiveName,
            SourceArchiveUrl,
            checksums
        );
        if (!archive.Success)
            return archive;

        var sourceDir = ExtractSourceDirectory(
            tempRoot,
            archive.InstallPath!,
            out var extractError
        );
        if (sourceDir is null)
            return Fail(extractError ?? "ngspice source extraction produced no source directory.");

        var buildPrefix = Path.Combine(tempRoot, "install");
        Directory.CreateDirectory(buildPrefix);

        var buildError = BuildUnixSource(sourceDir, buildPrefix);
        if (buildError is not null)
            return Fail(buildError);

        var builtExe = Path.Combine(buildPrefix, "bin", "ngspice");
        if (!File.Exists(builtExe))
            return Fail("ngspice build succeeded but binary was not found under install prefix.");

        Directory.CreateDirectory(installBin);
        File.Copy(builtExe, installExe, overwrite: true);
        _runtime.EnsureExecutable(installExe);

        if (!TryValidateBinary(installExe, out var validationError))
            return Fail($"Installed binary validation failed: {validationError}");

        return Success(
            $"Installed ngspice {NgspiceInstallLayout.Version} to {installExe} ({rid})",
            installExe
        );
    }

    /// <summary>
    /// Downloads an archive and verifies its SHA-256 hash against the pinned manifest.
    /// </summary>
    private SimulatorInstallResult DownloadAndVerifyArchive(
        string tempRoot,
        string archiveName,
        string url,
        IReadOnlyDictionary<string, string> checksums
    )
    {
        var archivePath = Path.Combine(tempRoot, archiveName);

        try
        {
            _runtime.DownloadFile(url, archivePath);
        }
        catch (Exception ex)
        {
            return Fail($"Failed to download {archiveName}: {ex.Message}");
        }

        if (!checksums.TryGetValue(archiveName, out var expected))
            return Fail($"Checksum not found for {archiveName}.");

        var actual = _runtime.ComputeSha256(archivePath).ToLowerInvariant();
        if (!string.Equals(actual, expected.ToLowerInvariant(), StringComparison.Ordinal))
            return Fail($"Checksum verification failed for {archiveName}.");

        return Success($"Downloaded {archiveName}", archivePath);
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

        return Directory.GetDirectories(extractDir).FirstOrDefault();
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
                    "--disable-shared",
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
        var required = new[]
        {
            "curl",
            "tar",
            "bison",
            "flex",
            "autoconf",
            "automake",
            "make",
            "cc",
        };
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
            return $"Missing required build tools: {joined}.\nInstall with: sudo apt-get update && sudo apt-get install -y bison flex autoconf automake libtool make gcc curl tar";
        }

        return $"Missing required build tools: {joined}.\nInstall with: brew install bison flex autoconf automake libtool";
    }

    /// <summary>
    /// Loads pinned ngspice archive checksums from bundled CLI assets.
    /// </summary>
    private IReadOnlyDictionary<string, string>? LoadChecksums()
    {
        var manifestPath = Path.Combine(_runtime.BaseDirectory, "Assets", "ngspice-45.2.sha256");
        if (!File.Exists(manifestPath))
            return null;

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

    private static SimulatorInstallResult Success(string message, string path)
    {
        return new SimulatorInstallResult(
            Success: true,
            ExitCode: 0,
            Message: message,
            InstallPath: path
        );
    }

    private static SimulatorInstallResult Fail(string message)
    {
        return new SimulatorInstallResult(Success: false, ExitCode: 1, Message: message);
    }
}
