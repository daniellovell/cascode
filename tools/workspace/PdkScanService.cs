using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cascode.Workspace;

/// <summary>
/// Encapsulates the PDK scanning workflow: workspace scan, physical library scan,
/// device-model matching, geometry extraction, and database persistence.
/// </summary>
public sealed class PdkScanService
{
    private readonly WorkspaceScanner _workspaceScanner;
    private readonly PhysicalLibraryScanner _physicalLibraryScanner;

    public PdkScanService()
        : this(new WorkspaceScanner(), new PhysicalLibraryScanner()) { }

    public PdkScanService(
        WorkspaceScanner workspaceScanner,
        PhysicalLibraryScanner physicalLibraryScanner
    )
    {
        _workspaceScanner =
            workspaceScanner ?? throw new ArgumentNullException(nameof(workspaceScanner));
        _physicalLibraryScanner =
            physicalLibraryScanner
            ?? throw new ArgumentNullException(nameof(physicalLibraryScanner));
    }

    /// <summary>
    /// Result of a PDK scan operation.
    /// </summary>
    public sealed record PdkScanResult(
        WorkspaceScanResult WorkspaceScan,
        IReadOnlyList<Device> Devices,
        IReadOnlyList<DeviceModelMatchRecord> Matches,
        IReadOnlyList<ModelGeometry> ModelGeometry,
        string DatabasePath
    );

    /// <summary>
    /// Performs a full PDK scan and writes results to the workspace database.
    /// </summary>
    /// <param name="targetRoot">Root directory of the PDK to scan.</param>
    /// <param name="logger">Logger for progress and diagnostic messages.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Scan results including workspace info, devices, matches, and geometry.</returns>
    public PdkScanResult ScanAndPersist(
        string targetRoot,
        ILogger? logger = null,
        CancellationToken cancellationToken = default
    )
    {
        logger ??= NullLogger.Instance;

        cancellationToken.ThrowIfCancellationRequested();

        // Stage 1: Workspace scan (model decks, libraries)
        logger.LogInformation("Scanning workspace: {Root}", targetRoot);
        var workspaceScan = _workspaceScanner.Scan(targetRoot, logger, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Stage 2: Physical library scan (devices)
        logger.LogInformation(
            "Scanning physical libraries for devices (libraries={Libraries})…",
            workspaceScan.Libraries.Count
        );
        var devices = _physicalLibraryScanner.Scan(
            workspaceScan.Libraries,
            warnings: null,
            cancellationToken
        );
        logger.LogInformation("Physical scan complete: {Devices} devices", devices.Count);
        cancellationToken.ThrowIfCancellationRequested();

        // Stage 3: Prepare database
        var dbPath = WorkspacePaths.GetDatabasePath(targetRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        if (File.Exists(dbPath))
            File.Delete(dbPath);

        logger.LogInformation("Writing PDK database → {Path}", dbPath);
        PdkDatabaseWriter.Write(dbPath, workspaceScan, devices, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Stage 4: Device↔Model matching
        PdkMatchingConfigManager.EnsureInitialized();
        PdkMatchingConfigManager.Load(logger);

        logger.LogInformation(
            "Matching devices to models ({Devices} × {Models})…",
            devices.Count,
            workspaceScan.Models.Count
        );
        var matches = DeviceModelMatcher.Match(devices, workspaceScan.Models);
        PdkDatabaseWriter.UpsertMatches(dbPath, matches, cancellationToken);
        logger.LogInformation("Matching complete: {Matches} associations", matches.Count);
        cancellationToken.ThrowIfCancellationRequested();

        // Stage 5: Geometry extraction
        logger.LogInformation(
            "Extracting model geometry from sources ({Models})…",
            workspaceScan.Models.Count
        );
        var geometry = ModelGeometryExtractor.Extract(workspaceScan.Models, logger);
        cancellationToken.ThrowIfCancellationRequested();
        PdkDatabaseWriter.UpsertGeometry(dbPath, geometry, cancellationToken);
        logger.LogInformation("Geometry extraction complete for {Count} models", geometry.Count);

        // Stage 6: Project geometry to devices
        logger.LogInformation("Projecting geometry onto devices ({Devices})…", devices.Count);
        PdkDatabaseWriter.UpsertDeviceGeometry(
            dbPath,
            devices,
            matches,
            geometry,
            cancellationToken
        );
        cancellationToken.ThrowIfCancellationRequested();

        logger.LogInformation("PDK database updated → {Path}", dbPath);

        return new PdkScanResult(workspaceScan, devices, matches, geometry, dbPath);
    }
}
