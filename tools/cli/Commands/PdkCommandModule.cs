using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cascode.Cli.Logging;
using Cascode.Cli.Output;
using Cascode.Cli.Services;
using Cascode.Workspace;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace Cascode.Cli.Commands;

internal sealed class PdkCommandModule
    : PdkCommandHandlersSupport,
        ICommandModule,
        IPdkEmitCommandHandlers,
        IPdkCharacterizationCommandHandlers
{
    private readonly WorkspaceScanner _scanner;
    private readonly CliConfig _config;
    private readonly CliConfigStorage _configStorage;
    private readonly string _initialWorkspaceRoot;
    private readonly IPdkEmitCommandHandlers _emitHandlers;
    private readonly IPdkCharacterizationCommandHandlers _characterizationHandlers;
    private CommandRegistry? _registry;
    internal static readonly string[] PdkCommandPrefix = new[] { "pdk" };

    public PdkCommandModule(
        ShellState state,
        WorkspaceScanner scanner,
        CliConfig config,
        CliConfigStorage configStorage,
        string initialWorkspaceRoot,
        Func<bool> isInteractive,
        CliOutputProvider output,
        IPdkEmitCommandHandlers emitHandlers,
        IPdkCharacterizationCommandHandlers characterizationHandlers
    )
        : base(state, isInteractive, output)
    {
        _scanner = scanner;
        _config = config;
        _configStorage = configStorage;
        _initialWorkspaceRoot = initialWorkspaceRoot;
        _emitHandlers = emitHandlers;
        _characterizationHandlers = characterizationHandlers;
    }

    public void Register(CommandRegistry registry)
    {
        _registry = registry;

        registry.Register(
            new DelegateCliCommand(
                "pdk",
                "Manage PDK workspace",
                ShowPdkUsage,
                helpCategory: CommandHelpCategory.Pdk
            )
        );
        registry.Register(
            new DelegateCliCommand(
                "pdk scan",
                "Scan workspace for decks",
                PdkScan,
                helpCategory: CommandHelpCategory.Pdk
            )
        );
        registry.Register(
            new DelegateCliCommand(
                "pdk devices",
                "List discovered devices",
                PdkDevices,
                helpCategory: CommandHelpCategory.Pdk
            )
        );
        registry.Register(
            new DelegateCliCommand(
                "pdk device",
                "Inspect a specific device",
                PdkDevice,
                helpCategory: CommandHelpCategory.Pdk
            )
        );
        registry.Register(
            new DelegateCliCommand(
                "pdk set-dir",
                "Set or clear the default PDK workspace",
                PdkSetDir,
                helpCategory: CommandHelpCategory.Pdk
            )
        );
        registry.Register(
            new DelegateCliCommand(
                "pdk match",
                "Device↔Model coverage and ambiguity summary",
                PdkMatch,
                helpCategory: CommandHelpCategory.Pdk
            )
        );
    }

    CommandResult IPdkEmitCommandHandlers.ShowPdkEmitUsage(string[] args) =>
        _emitHandlers.ShowPdkEmitUsage(args);

    CommandResult IPdkEmitCommandHandlers.PdkEmitPrimitivesCommand(string[] args) =>
        _emitHandlers.PdkEmitPrimitivesCommand(args);

    CommandResult IPdkCharacterizationCommandHandlers.ShowPdkCharUsage(string[] args) =>
        _characterizationHandlers.ShowPdkCharUsage(args);

    CommandResult IPdkCharacterizationCommandHandlers.PdkCharConfigCommand(string[] args) =>
        _characterizationHandlers.PdkCharConfigCommand(args);

    CommandResult IPdkCharacterizationCommandHandlers.PdkCharRunCommand(string[] args) =>
        _characterizationHandlers.PdkCharRunCommand(args);

    CommandResult IPdkCharacterizationCommandHandlers.PdkCharReadCommand(string[] args) =>
        _characterizationHandlers.PdkCharReadCommand(args);

    CommandResult IPdkCharacterizationCommandHandlers.PdkCharStatusCommand(string[] args) =>
        _characterizationHandlers.PdkCharStatusCommand(args);

    private CommandResult ShowPdkUsage(string[] args)
    {
        Output.WriteLine("Usage: pdk <subcommand>");
        var subcommands = _registry!.GetSubcommands(PdkCommandPrefix).ToArray();
        var width = subcommands.Length == 0 ? 0 : subcommands.Max(c => c.DisplayPath.Length);
        foreach (var sub in subcommands)
        {
            var padded = width > 0 ? sub.DisplayPath.PadRight(width) : sub.DisplayPath;
            var description = string.IsNullOrEmpty(sub.Description)
                ? string.Empty
                : $"  {sub.Description}";
            Output.WriteLine($"  {padded}{description}");
        }
        return CommandResult.Success;
    }

    private CommandResult PdkDevices(string[] args)
    {
        try
        {
            var dbPath = Path.Combine(
                WorkspaceState.GetWorkspaceFolder(_state.WorkspaceRoot),
                "pdk.db"
            );
            if (!File.Exists(dbPath))
            {
                Output.WriteLine("No PDK database found. Run 'pdk scan' first.");
                return CommandResult.Failure;
            }

            // Parse filters early to decide fast path
            var (classFilter, vtFilter, vddFilter, infraFilter, matchedFilter, limit) =
                ParseDeviceFilters(args);
            var forceList = args.Any(a =>
                a.Equals("--list", StringComparison.OrdinalIgnoreCase)
                || a.Equals("--detail", StringComparison.OrdinalIgnoreCase)
            );
            var hasFilters =
                classFilter.Count > 0
                || vtFilter.Count > 0
                || vddFilter.Count > 0
                || infraFilter.HasValue
                || matchedFilter.HasValue;
            var filterLabels = new List<string>();
            if (classFilter.Count > 0)
                filterLabels.Add("class=" + string.Join(',', classFilter));
            if (vtFilter.Count > 0)
                filterLabels.Add("vt=" + string.Join(',', vtFilter));
            if (vddFilter.Count > 0)
                filterLabels.Add("vdd=" + string.Join(',', vddFilter));
            if (infraFilter.HasValue)
                filterLabels.Add(infraFilter.Value ? "infra" : "no-infra");
            if (matchedFilter.HasValue)
                filterLabels.Add(matchedFilter.Value ? "matched" : "unmatched");

            if (_isInteractive() && !forceList && !hasFilters)
            {
                // Fast path: use precomputed per-class summary written at 'pdk scan' time
                var summary = Cascode.Workspace.PdkDatabaseReader.LoadDeviceClassSummary(dbPath);
                if (summary.Count == 0)
                {
                    Output.WriteLine("No devices discovered. Run 'pdk scan'.");
                    return CommandResult.Success;
                }
                var classRows = new List<DeviceClassSummaryRow>(summary.Count);
                foreach (var s in summary)
                {
                    var clsEnum = s.DeviceClass;
                    classRows.Add(
                        new DeviceClassSummaryRow(
                            DeviceClass: DeviceSummaryHelpers.FormatDeviceClassName(clsEnum),
                            DeviceCount: s.DeviceCount.ToString(CultureInfo.InvariantCulture),
                            Decks: s.Decks.ToString(CultureInfo.InvariantCulture),
                            VoltageDomains: string.IsNullOrWhiteSpace(s.VoltageDomainsCsv)
                                ? "-"
                                : s.VoltageDomainsCsv,
                            Thresholds: string.IsNullOrWhiteSpace(s.ThresholdsCsv)
                                ? "-"
                                : s.ThresholdsCsv,
                            Corners: string.IsNullOrWhiteSpace(s.CornersCsv) ? "-" : s.CornersCsv,
                            ExampleDevice: string.IsNullOrWhiteSpace(s.ExampleModel)
                                ? "-"
                                : s.ExampleModel,
                            IsUncategorized: s.DeviceClass == DeviceClass.Unknown
                        )
                    );
                }

                var title = "Devices by Class";
                var scope =
                    filterLabels.Count == 0 ? "(all)" : "(" + string.Join(' ', filterLabels) + ")";
                var total = summary.Sum(s => s.DeviceCount);
                var matched = summary.Sum(s => s.Matched);
                var summaryLine =
                    $"Device classes: {classRows.Count} {scope}. Devices: {total}. Matched: {matched}. Use 'pdk devices --list' to list.";
                var view = new DeviceSummaryViewState(
                    title,
                    summaryLine,
                    statsLine: string.Empty,
                    suggestionLine: DeviceSummaryHelpers.BuildSuggestionText(),
                    detailRows: Array.Empty<DeviceSummaryRow>(),
                    classRows: classRows,
                    detailOffset: 0,
                    detailPageSize: 0,
                    detailFilters: Array.Empty<string>()
                );
                _state.ShowDeviceSummary(view);
                Output.WriteLine(summaryLine);
                return CommandResult.Success;
            }
            else
            {
                // Load devices only when we need to render a list or apply filters
                var devicesAll = Cascode.Workspace.PdkDatabaseReader.LoadDevices(dbPath);
                if (devicesAll.Count == 0)
                {
                    Output.WriteLine("No devices discovered. Run 'pdk scan'.");
                    return CommandResult.Success;
                }

                HashSet<string>? matchedKeys = null;
                if (matchedFilter.HasValue)
                    matchedKeys = Cascode.Workspace.PdkDatabaseReader.LoadMatchedDeviceKeys(dbPath);
                var devices = devicesAll
                    .Where(d =>
                        DeviceMatchesFilters(
                            d,
                            classFilter,
                            vtFilter,
                            vddFilter,
                            infraFilter,
                            matchedKeys,
                            matchedFilter
                        )
                    )
                    .ToList();

                var total = devices.Count;
                var matched = matchedKeys is null
                    ? Cascode.Workspace.PdkDatabaseReader.CountMatchedDevices(dbPath)
                    : devices.Count(d => matchedKeys.Contains(d.CanonicalName));

                if (_isInteractive())
                {
                    if (devices.Count == 0)
                    {
                        Output.WriteLine("No devices matched the selected filters.");
                        _state.ShowDeviceSummary(
                            new DeviceSummaryViewState(
                                "Devices",
                                "No devices matched the selected filters.",
                                string.Empty,
                                DeviceSummaryHelpers.BuildSuggestionText(),
                                Array.Empty<DeviceSummaryRow>(),
                                Array.Empty<DeviceClassSummaryRow>(),
                                0,
                                0,
                                filterLabels
                            )
                        );
                        return CommandResult.Success;
                    }

                    var pageSize = Math.Clamp(limit, 1, Math.Max(1, devices.Count));
                    var rows = new List<DeviceSummaryRow>(devices.Count);
                    for (var i = 0; i < devices.Count; i++)
                    {
                        var d = devices[i];
                        var vt = d.VtTags.Count == 0 ? "-" : string.Join('/', d.VtTags);
                        // Values from DB are already numeric CSV; reader maps them to display strings.
                        var vdd = d.VddTags.Count == 0 ? "-" : string.Join('/', d.VddTags);
                        var views = d.Views.Count == 0 ? "-" : string.Join(',', d.Views);
                        rows.Add(
                            new DeviceSummaryRow(
                                i + 1,
                                d.CellName,
                                d.Class.ToString(),
                                vt,
                                vdd,
                                views,
                                string.Empty
                            )
                        );
                    }
                    var title = "Devices";
                    var summaryLine = DeviceSummaryHelpers.BuildDetailSummaryLine(
                        filterLabels,
                        0,
                        pageSize,
                        rows.Count
                    );
                    var statsLine = $"Matched: {matched} of {total}.";
                    var view = new DeviceSummaryViewState(
                        title,
                        summaryLine,
                        statsLine: statsLine,
                        suggestionLine: DeviceSummaryHelpers.BuildSuggestionText(),
                        detailRows: rows,
                        classRows: Array.Empty<DeviceClassSummaryRow>(),
                        detailOffset: 0,
                        detailPageSize: pageSize,
                        detailFilters: filterLabels
                    );
                    _state.ShowDeviceSummary(view);
                    var visible = Math.Min(pageSize, rows.Count);
                    Output.WriteLine(
                        $"Devices: showing {visible} of {rows.Count} (page size {pageSize}, total {total}). Matched: {matched}. Use 'pdk device <name>' for details."
                    );
                }
                else
                {
                    var table = new Table()
                        .Border(TableBorder.Rounded)
                        .AddColumn("Library")
                        .AddColumn("Cell")
                        .AddColumn("Class")
                        .AddColumn("VT")
                        .AddColumn("VDD")
                        .AddColumn("Views");
                    foreach (var d in devices.Take(limit))
                    {
                        var vt = d.VtTags.Count == 0 ? "-" : string.Join('/', d.VtTags);
                        var vdd = d.VddTags.Count == 0 ? "-" : string.Join('/', d.VddTags);
                        var views = d.Views.Count == 0 ? "-" : string.Join(',', d.Views);
                        table.AddRow(d.LibraryName, d.CellName, d.Class.ToString(), vt, vdd, views);
                    }
                    WriteRenderable(table);
                    Output.WriteLine(
                        $"Devices: {total}. Showing first {Math.Min(limit, total)}. Matched: {matched}. Use 'pdk device <name>' for details."
                    );
                }
            }
            return CommandResult.Success;
        }
        catch (Exception ex)
        {
            Output.WriteLine($"Failed to load devices: {ex.Message}");
            return CommandResult.Failure;
        }
    }

    private CommandResult PdkDevice(string[] args)
    {
        if (args.Length == 0)
        {
            Output.WriteLine("Usage: pdk device <name>");
            return CommandResult.Success;
        }

        try
        {
            var dbPath = Path.Combine(
                WorkspaceState.GetWorkspaceFolder(_state.WorkspaceRoot),
                "pdk.db"
            );
            if (!File.Exists(dbPath))
            {
                Output.WriteLine("No PDK database found. Run 'pdk scan' first.");
                return CommandResult.Failure;
            }
            var devices = Cascode.Workspace.PdkDatabaseReader.LoadDevices(dbPath);
            var needle = args[0];
            var d = devices.FirstOrDefault(x =>
                x.CellName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || x.CanonicalName.Contains(needle, StringComparison.OrdinalIgnoreCase)
            );
            if (d is null)
            {
                Output.WriteLine("Device not found.");
                return CommandResult.Failure;
            }

            var detail = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("Field")
                .AddColumn("Value");
            detail.AddRow("Library", d.LibraryName);
            detail.AddRow("Cell", d.CellName);
            detail.AddRow("Class", d.Class.ToString());
            detail.AddRow("VT", d.VtTags.Count == 0 ? "-" : string.Join('/', d.VtTags));
            detail.AddRow("VDD", d.VddTags.Count == 0 ? "-" : string.Join('/', d.VddTags));
            detail.AddRow("Views", d.Views.Count == 0 ? "-" : string.Join(',', d.Views));
            detail.AddRow("Cell Path", d.CellPath);
            var matches = Cascode.Workspace.PdkDatabaseReader.LoadMatchesForDevice(
                dbPath,
                d.CanonicalName
            );
            if (matches.Count > 0)
            {
                var best = matches.OrderBy(m => m.Rank).First();
                detail.AddRow("Match", $"{best.ModelName} ({best.Quality})");
                var geom = Cascode.Workspace.PdkDatabaseReader.LoadGeometryForModel(
                    dbPath,
                    best.ModelName
                );
                if (geom is not null)
                {
                    string fmt(double? v) =>
                        v.HasValue
                            ? v.Value.ToString(
                                "g4",
                                System.Globalization.CultureInfo.InvariantCulture
                            )
                            : "-";
                    var gstr =
                        $"W:[{fmt(geom.WMin)}..{fmt(geom.WMax)}], L:[{fmt(geom.LMin)}..{fmt(geom.LMax)}]; Wdef={fmt(geom.WDefault)}, Ldef={fmt(geom.LDefault)}";
                    detail.AddRow("Geometry", Markup.Escape(gstr));
                }
            }
            WriteRenderable(detail);
            return CommandResult.Success;
        }
        catch (Exception ex)
        {
            Output.WriteLine($"Failed to load device: {ex.Message}");
            return CommandResult.Failure;
        }
    }

    private CommandResult PdkScan(string[] args)
    {
        var targetRoot = args.Length > 0 ? args[0] : _state.WorkspaceRoot;
        targetRoot = Path.GetFullPath(targetRoot);
        _state.SetWorkspace(targetRoot);

        // Create logger for this run
        var level = ParseLogLevelFromEnv();
        using var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(level);
            if (_isInteractive())
            {
                builder.AddProvider(new Cascode.Cli.Logging.ShellLoggerProvider(_state, level));
            }
            else
            {
                builder.AddSimpleConsole(o =>
                {
                    o.SingleLine = true;
                    o.TimestampFormat = "HH:mm:ss ";
                });
                _state.MarkStreamedOutput();
            }
        });
        var logger = loggerFactory.CreateLogger("pdk");

        logger.LogInformation("Scanning workspace {Root}", targetRoot);

        if (_isInteractive())
        {
            using var renderSignal = new System.Threading.AutoResetEvent(false);
            void Handler()
            {
                try
                {
                    renderSignal.Set();
                }
                catch { }
            }
            _state.Changed += Handler;

            using var cancellationTokenSource = new CancellationTokenSource();
            WorkspaceScanResult? result = null;
            Exception? scanError = null;
            bool wasCancelled = false;

            _state.StartBusy("Scanning workspace…");

            var scanTask = Task.Run(() =>
            {
                try
                {
                    result = PerformScanAndUpdateDatabase(
                        targetRoot,
                        logger,
                        cancellationTokenSource.Token
                    );
                }
                catch (OperationCanceledException)
                {
                    wasCancelled = true;
                    scanError = new OperationCanceledException("Scan cancelled by user");
                }
                catch (Exception ex)
                {
                    scanError = ex;
                }
                finally
                {
                    _state.RequestRender();
                }
            });

            // Event-driven live rendering with ESC key monitoring
            try
            {
                var layout = ShellRenderer.Build(_state);
                AnsiConsole
                    .Live(layout)
                    .AutoClear(false)
                    .Start(ctx =>
                    {
                        ctx.Refresh();
                        while (!scanTask.IsCompleted)
                        {
                            // Check for ESC key (non-blocking)
                            try
                            {
                                if (System.Console.KeyAvailable)
                                {
                                    var keyInfo = System.Console.ReadKey(intercept: true);
                                    if (keyInfo.Key == ConsoleKey.Escape)
                                    {
                                        cancellationTokenSource.Cancel();
                                        wasCancelled = true;
                                        Output.WriteLine("Scan aborted by user (ESC)");
                                        break; // Exit immediately
                                    }
                                }
                            }
                            catch (InvalidOperationException)
                            {
                                // Console input not available, continue without key checking
                            }

                            // Refresh on either an event (new logs/state) or timeout tick for spinner
                            renderSignal.WaitOne(System.TimeSpan.FromMilliseconds(50));
                            _state.TickSpinner();

                            // Update panels based on current state
                            try
                            {
                                layout["Content"]
                                    ["Main"]["Log"]
                                    .Update(ShellRenderer.BuildLog(_state));
                                if (_state.Scan is not null)
                                {
                                    layout["Content"]
                                        ["Sidebar"]["Navigator"]
                                        .Update(ShellRenderer.BuildNavigator(_state));
                                    layout["Content"]
                                        ["Sidebar"]["Details"]
                                        .Update(ShellRenderer.BuildDeckDetails(_state));
                                }
                                layout["PromptSpacer"].Update(ShellRenderer.BuildPrompt(_state));
                            }
                            catch (Exception ex)
                            {
                                logger.LogTrace(
                                    ex,
                                    "Ignoring transient error during live UI refresh"
                                );
                            }
                            ctx.Refresh();
                        }

                        // Final update only if not cancelled
                        if (!wasCancelled)
                        {
                            try
                            {
                                layout["Content"]
                                    ["Main"]["Log"]
                                    .Update(ShellRenderer.BuildLog(_state));
                                if (_state.Scan is not null)
                                {
                                    layout["Content"]
                                        ["Sidebar"]["Navigator"]
                                        .Update(ShellRenderer.BuildNavigator(_state));
                                    layout["Content"]
                                        ["Sidebar"]["Details"]
                                        .Update(ShellRenderer.BuildDeckDetails(_state));
                                }
                                layout["PromptSpacer"].Update(ShellRenderer.BuildPrompt(_state));
                            }
                            catch (Exception ex)
                            {
                                logger.LogTrace(
                                    ex,
                                    "Ignoring transient error during live UI refresh"
                                );
                            }
                            ctx.Refresh();
                        }
                    });
            }
            finally
            {
                _state.Changed -= Handler;
                _state.StopBusy();
            }

            if (wasCancelled)
            {
                return CommandResult.Failure;
            }

            if (scanError is not null)
            {
                Output.WriteLine($"Scan failed: {scanError.Message}");
                return CommandResult.Failure;
            }

            if (result is not null)
            {
                logger.LogInformation(
                    "Found {Libraries} libraries, {Decks} model decks.",
                    result.Libraries.Count,
                    result.ModelDecks.Count
                );
                foreach (var warning in result.Warnings)
                    logger.LogWarning("{Warning}", warning);
            }

            return CommandResult.Success;
        }
        else
        {
            var result = PerformScanAndUpdateDatabase(targetRoot, logger);
            logger.LogInformation(
                "Found {Libraries} libraries, {Decks} model decks.",
                result.Libraries.Count,
                result.ModelDecks.Count
            );
            foreach (var warning in result.Warnings)
                logger.LogWarning("{Warning}", warning);
            return CommandResult.Success;
        }
    }

    private WorkspaceScanResult PerformScanAndUpdateDatabase(
        string targetRoot,
        ILogger logger,
        CancellationToken cancellationToken = default
    )
    {
        var overall = System.Diagnostics.Stopwatch.StartNew();

        // Log matching config initialization
        var cfgPath = Cascode.Workspace.PdkMatchingConfigManager.GetConfigFilePath();
        var created = Cascode.Workspace.PdkMatchingConfigManager.EnsureInitialized();
        if (created)
            logger.LogInformation(
                "Initialized default PDK matching patterns → {Path}. Edit this file to customize device↔model matching.",
                cfgPath
            );
        else
            logger.LogInformation(
                "Using PDK matching patterns → {Path}. Edit this file to customize device↔model matching.",
                cfgPath
            );

        // Use PdkScanService for the core scanning workflow
        var scanService = new Cascode.Workspace.PdkScanService(
            _scanner,
            new Cascode.Workspace.PhysicalLibraryScanner()
        );
        var scanResult = scanService.ScanAndPersist(targetRoot, logger, cancellationToken);

        // Update CLI state with scan results
        var previousSelection = _state.SelectedDeckIndex;
        _state.Scan = scanResult.WorkspaceScan;
        if (scanResult.WorkspaceScan.ModelDecks.Count == 0)
            _state.SelectedDeckIndex = null;
        else if (
            previousSelection.HasValue
            && previousSelection.Value >= 0
            && previousSelection.Value < scanResult.WorkspaceScan.ModelDecks.Count
        )
            _state.SelectedDeckIndex = previousSelection;
        else
            _state.SelectedDeckIndex = 0;

        overall.Stop();
        logger.LogInformation("Total scan time: {ElapsedMs} ms.", overall.ElapsedMilliseconds);

        return scanResult.WorkspaceScan;
    }

    // No custom renderable needed; we update the Layout panels in-place.

    private static Microsoft.Extensions.Logging.LogLevel ParseLogLevelFromEnv()
    {
        var v = Environment.GetEnvironmentVariable("CASCODE_LOG_LEVEL");
        return v?.ToLowerInvariant() switch
        {
            "trace" => Microsoft.Extensions.Logging.LogLevel.Trace,
            "debug" => Microsoft.Extensions.Logging.LogLevel.Debug,
            "warn" or "warning" => Microsoft.Extensions.Logging.LogLevel.Warning,
            "error" => Microsoft.Extensions.Logging.LogLevel.Error,
            "critical" => Microsoft.Extensions.Logging.LogLevel.Critical,
            _ => Microsoft.Extensions.Logging.LogLevel.Information,
        };
    }

    private CommandResult PdkSetDir(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("--clear", StringComparison.OrdinalIgnoreCase))
        {
            _config.PdkRoot = null;
            CliConfigStorage.Save(_config);
            _state.UpdatePdkRoot(null);
            _state.SetWorkspace(_initialWorkspaceRoot);
            Output.WriteLine("Cleared default PDK workspace preference.");
            return CommandResult.Success;
        }

        if (args.Length > 0)
        {
            return ApplyPdkDirectory(args[0]);
        }

        var current = _state.PdkRoot ?? _state.WorkspaceRoot;
        var input = AnsiConsole.Ask<string>(
            "Enter PDK workspace directory (leave blank to cancel):",
            current
        );
        if (string.IsNullOrWhiteSpace(input))
        {
            Output.WriteLine("PDK workspace unchanged.");
            return CommandResult.Success;
        }
        return ApplyPdkDirectory(input);
    }

    private CommandResult ApplyPdkDirectory(string path)
    {
        try
        {
            var resolved = PathUtils.NormalizePath(path);
            if (!Directory.Exists(resolved))
            {
                Output.WriteLine($"Directory '{resolved}' not found.");
                return CommandResult.Failure;
            }

            _config.PdkRoot = resolved;
            CliConfigStorage.Save(_config);
            _state.UpdatePdkRoot(resolved);
            _state.SetWorkspace(resolved);
            Output.WriteLine($"PDK workspace set to {resolved}");
            return CommandResult.Success;
        }
        catch (Exception ex)
        {
            Output.WriteLine($"Invalid path: {ex.Message}");
            return CommandResult.Failure;
        }
    }

    /// <summary>
    /// Parse device-related CLI flags and return structured filter criteria.
    /// </summary>
    /// <param name="args">Command-line arguments to parse. Recognized flags:
    /// --class <csv>, --vt <csv>, --vdd <csv>, --infra, --no-infra, --matched, --unmatched, --limit <n>.</param>
    /// <returns>
    /// A tuple containing:
    /// - `classes`: set of class names (case-insensitive),
    /// - `vts`: set of VT tags (uppercased),
    /// - `vdds`: set of normalized VDD display strings (e.g., 1.8V),
    /// - `infra`: `true` to include only infra devices, `false` to exclude infra devices, `null` to include all,
    /// - `matched`: `true` to include only matched devices, `false` to include only unmatched devices, `null` to include all,
    /// - `limit`: maximum number of results to show (minimum 1, defaults to 20).
    /// </returns>
    private static (
        HashSet<string> classes,
        HashSet<string> vts,
        HashSet<string> vdds,
        bool? infra,
        bool? matched,
        int limit
    ) ParseDeviceFilters(string[] args)
    {
        var classes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var vts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var vdds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool? infra = null; // true = only infra, false = exclude infra, null = all
        bool? matched = null; // true = matched only, false = unmatched only, null = all
        int limit = 20;
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a.Equals("--class", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                foreach (var tok in SplitCsv(args[++i]))
                    classes.Add(tok);
            }
            else if (a.Equals("--vt", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                foreach (var tok in SplitCsv(args[++i]))
                    vts.Add(tok.ToUpperInvariant());
            }
            else if (a.Equals("--vdd", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                foreach (var tok in SplitCsv(args[++i]))
                {
                    if (DeviceFilterEvaluator.TryNormalizeVddFilter(tok, out var normalized))
                        vdds.Add(normalized);
                    else
                        vdds.Add(tok.ToLowerInvariant());
                }
            }
            else if (a.Equals("--infra", StringComparison.OrdinalIgnoreCase))
                infra = true;
            else if (a.Equals("--no-infra", StringComparison.OrdinalIgnoreCase))
                infra = false;
            else if (a.Equals("--matched", StringComparison.OrdinalIgnoreCase))
                matched = true;
            else if (a.Equals("--unmatched", StringComparison.OrdinalIgnoreCase))
                matched = false;
            else if (a.Equals("--limit", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out var lim))
                    limit = Math.Max(1, lim);
            }
        }
        return (classes, vts, vdds, infra, matched, limit);
    }

    private static bool DeviceMatchesFilters(
        Cascode.Workspace.Device d,
        HashSet<string> classes,
        HashSet<string> vts,
        HashSet<string> vdds,
        bool? infra,
        HashSet<string>? matchedKeys,
        bool? matched
    )
    {
        var opts = new DeviceFilterOptions(classes, vts, vdds, infra, matched);
        return DeviceFilterEvaluator.Matches(d, opts, matchedKeys);
    }

    private CommandResult PdkMatch(string[] args)
    {
        try
        {
            var dbPath = Path.Combine(
                WorkspaceState.GetWorkspaceFolder(_state.WorkspaceRoot),
                "pdk.db"
            );
            if (!File.Exists(dbPath))
            {
                Output.WriteLine("No PDK database found. Run 'pdk scan' first.");
                return CommandResult.Failure;
            }

            var cov = Cascode.Workspace.PdkDatabaseReader.GetMatchCoverage(dbPath);
            var byClass = Cascode.Workspace.PdkDatabaseReader.GetMatchCoverageByClass(dbPath);

            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("Class")
                .AddColumn("Total")
                .AddColumn("Matched")
                .AddColumn("Ambiguous")
                .AddColumn("Unmatched");
            foreach (var row in byClass)
            {
                table.AddRow(
                    row.Class,
                    row.Total.ToString(CultureInfo.InvariantCulture),
                    row.Matched.ToString(CultureInfo.InvariantCulture),
                    row.Ambiguous.ToString(CultureInfo.InvariantCulture),
                    row.Unmatched.ToString(CultureInfo.InvariantCulture)
                );
            }
            WriteRenderable(table);
            Output.WriteLine(
                $"Coverage: total={cov.Total}, matched={cov.Matched}, ambiguous={cov.Ambiguous}, unmatched={cov.Unmatched}."
            );
            if (cov.SampleAmbiguous.Count > 0)
                Output.WriteLine("Ambiguous examples: " + string.Join(", ", cov.SampleAmbiguous));
            if (cov.SampleUnmatched.Count > 0)
                Output.WriteLine("Unmatched examples: " + string.Join(", ", cov.SampleUnmatched));
            return CommandResult.Success;
        }
        catch (Exception ex)
        {
            Output.WriteLine($"Failed to compute match coverage: {ex.Message}");
            return CommandResult.Failure;
        }
    }
}
