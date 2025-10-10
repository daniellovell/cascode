using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Rendering;
using Microsoft.Extensions.Logging;
using Cascode.Cli.Logging;
using Cascode.Workspace;
using Cascode.Cli.Services;

namespace Cascode.Cli.Commands;

internal sealed class PdkCommandModule : ICommandModule
{
    private readonly ShellState _state;
    private readonly WorkspaceScanner _scanner;
    private readonly CliConfig _config;
    private readonly CliConfigStorage _configStorage;
    private readonly string _initialWorkspaceRoot;
    private readonly Func<bool> _isInteractive;
    private CommandRegistry? _registry;

    public PdkCommandModule(
        ShellState state,
        WorkspaceScanner scanner,
        CliConfig config,
        CliConfigStorage configStorage,
        string initialWorkspaceRoot,
        Func<bool> isInteractive)
    {
        _state = state;
        _scanner = scanner;
        _config = config;
        _configStorage = configStorage;
        _initialWorkspaceRoot = initialWorkspaceRoot;
        _isInteractive = isInteractive;
    }

    public void Register(CommandRegistry registry)
    {
        _registry = registry;

        registry.Register(new DelegateCliCommand("pdk", "Manage PDK workspace", ShowPdkUsage));
        registry.Register(new DelegateCliCommand("pdk scan", "Scan workspace for decks", PdkScan));
        registry.Register(new DelegateCliCommand("pdk devices", "List discovered devices", PdkDevices));
        registry.Register(new DelegateCliCommand("pdk device", "Inspect a specific device", PdkDevice));
        registry.Register(new DelegateCliCommand("pdk set-dir", "Set or clear the default PDK workspace", PdkSetDir));
        registry.Register(new DelegateCliCommand("pdk match", "Device↔Model coverage and ambiguity summary", PdkMatch));

        // PDK characterization
        registry.Register(new DelegateCliCommand("pdk char", "PDK characterization commands", ShowPdkCharUsage));
        registry.Register(new DelegateCliCommand("pdk char help", "Show PDK characterization help", ShowPdkCharUsage, hidden: true));
        registry.Register(new DelegateCliCommand("pdk char config", "Configure batch characterization", PdkCharConfigCommand));
        registry.Register(new DelegateCliCommand("pdk char run", "Characterize models (Spectre)", PdkCharRunCommand));
        registry.Register(new DelegateCliCommand("pdk char read", "View characterized LUTs", PdkCharReadCommand));
    }

    private CommandResult ShowPdkUsage(string[] args)
    {
        _state.AddMessage("Usage: pdk <subcommand>");
        var subcommands = _registry!.GetSubcommands(new[] { "pdk" }).ToArray();
        var width = subcommands.Length == 0 ? 0 : subcommands.Max(c => c.DisplayPath.Length);
        foreach (var sub in subcommands)
        {
            var padded = width > 0 ? sub.DisplayPath.PadRight(width) : sub.DisplayPath;
            var description = string.IsNullOrEmpty(sub.Description) ? string.Empty : $"  {sub.Description}";
            _state.AddMessage($"  {padded}{description}");
        }
        return CommandResult.Success;
    }

    private CommandResult PdkDevices(string[] args)
    {
        try
        {
            var dbPath = Path.Combine(WorkspaceState.GetWorkspaceFolder(_state.WorkspaceRoot), "pdk.db");
            if (!File.Exists(dbPath)) { _state.AddMessage("No PDK database found. Run 'pdk scan' first."); return CommandResult.Failure; }

            // Parse filters early to decide fast path
            var (classFilter, vtFilter, vddFilter, infraFilter, matchedFilter, limit) = ParseDeviceFilters(args);
            var forceList = args.Any(a => a.Equals("--list", StringComparison.OrdinalIgnoreCase) || a.Equals("--detail", StringComparison.OrdinalIgnoreCase));
            var hasFilters = classFilter.Count > 0 || vtFilter.Count > 0 || vddFilter.Count > 0 || infraFilter.HasValue || matchedFilter.HasValue;
            var filterLabels = new List<string>();
            if (classFilter.Count > 0) filterLabels.Add("class=" + string.Join(',', classFilter));
            if (vtFilter.Count > 0) filterLabels.Add("vt=" + string.Join(',', vtFilter));
            if (vddFilter.Count > 0) filterLabels.Add("vdd=" + string.Join(',', vddFilter));
            if (infraFilter.HasValue) filterLabels.Add(infraFilter.Value ? "infra" : "no-infra");
            if (matchedFilter.HasValue) filterLabels.Add(matchedFilter.Value ? "matched" : "unmatched");

            if (_isInteractive() && !forceList && !hasFilters)
            {
                // Fast path: use precomputed per-class summary written at 'pdk scan' time
                var summary = Cascode.Workspace.PdkDatabaseReader.LoadDeviceClassSummary(dbPath);
                if (summary.Count == 0) { _state.AddMessage("No devices discovered. Run 'pdk scan'."); return CommandResult.Success; }
                var classRows = new List<DeviceClassSummaryRow>(summary.Count);
                foreach (var s in summary)
                {
                    var clsEnum = (DeviceClass)s.DeviceClass;
                    classRows.Add(new DeviceClassSummaryRow(
                        DeviceClass: DeviceSummaryHelpers.FormatDeviceClassName(clsEnum),
                        DeviceCount: s.DeviceCount.ToString(CultureInfo.InvariantCulture),
                        Decks: s.Decks.ToString(CultureInfo.InvariantCulture),
                        VoltageDomains: string.IsNullOrWhiteSpace(s.VoltageDomainsCsv) ? "-" : s.VoltageDomainsCsv,
                        Thresholds: string.IsNullOrWhiteSpace(s.ThresholdsCsv) ? "-" : s.ThresholdsCsv,
                        Corners: string.IsNullOrWhiteSpace(s.CornersCsv) ? "-" : s.CornersCsv,
                        ExampleDevice: string.IsNullOrWhiteSpace(s.ExampleModel) ? "-" : s.ExampleModel,
                        IsUncategorized: s.DeviceClass == (int)DeviceClass.Unknown));
                }

                var title = "Devices by Class";
                var scope = filterLabels.Count == 0 ? "(all)" : "(" + string.Join(' ', filterLabels) + ")";
                var total = summary.Sum(s => s.DeviceCount);
                var matched = summary.Sum(s => s.Matched);
                var summaryLine = $"Device classes: {classRows.Count} {scope}. Devices: {total}. Matched: {matched}. Use 'pdk devices --list' to list.";
                var view = new DeviceSummaryViewState(title, summaryLine, statsLine: string.Empty, suggestionLine: DeviceSummaryHelpers.BuildSuggestionText(), detailRows: Array.Empty<DeviceSummaryRow>(), classRows: classRows, detailOffset: 0, detailPageSize: 0, detailFilters: Array.Empty<string>());
                _state.ShowDeviceSummary(view);
                _state.AddMessage(summaryLine);
                return CommandResult.Success;
            }
            else
            {
                // Load devices only when we need to render a list or apply filters
                var devicesAll = Cascode.Workspace.PdkDatabaseReader.LoadDevices(dbPath);
                if (devicesAll.Count == 0) { _state.AddMessage("No devices discovered. Run 'pdk scan'."); return CommandResult.Success; }

                HashSet<string>? matchedKeys = null;
                if (matchedFilter.HasValue) matchedKeys = Cascode.Workspace.PdkDatabaseReader.LoadMatchedDeviceKeys(dbPath);
                var devices = devicesAll.Where(d => DeviceMatchesFilters(d, classFilter, vtFilter, vddFilter, infraFilter, matchedKeys, matchedFilter)).ToList();

                var total = devices.Count;
                var matched = matchedKeys is null ? Cascode.Workspace.PdkDatabaseReader.CountMatchedDevices(dbPath)
                                                  : devices.Count(d => matchedKeys.Contains(d.CanonicalName));

                if (_isInteractive())
                {
                    if (devices.Count == 0)
                    {
                        _state.AddMessage("No devices matched the selected filters.");
                        _state.ShowDeviceSummary(new DeviceSummaryViewState("Devices", "No devices matched the selected filters.", string.Empty, DeviceSummaryHelpers.BuildSuggestionText(), Array.Empty<DeviceSummaryRow>(), Array.Empty<DeviceClassSummaryRow>(), 0, 0, filterLabels));
                        return CommandResult.Success;
                    }

                    var pageSize = Math.Clamp(limit, 1, Math.Max(1, devices.Count));
                    var rows = new List<DeviceSummaryRow>(devices.Count);
                    for (var i = 0; i < devices.Count; i++)
                    {
                        var d = devices[i];
                        var vt = d.VtTags.Count == 0 ? "-" : string.Join('/', d.VtTags);
                        var vdd = d.VddTags.Count == 0 ? "-" : string.Join('/', d.VddTags);
                        var views = d.Views.Count == 0 ? "-" : string.Join(',', d.Views);
                        rows.Add(new DeviceSummaryRow(i + 1, d.CellName, d.Class.ToString(), vt, vdd, views, string.Empty));
                    }
                    var title = "Devices";
                    var summaryLine = DeviceSummaryHelpers.BuildDetailSummaryLine(filterLabels, 0, pageSize, rows.Count);
                    var statsLine = $"Matched: {matched} of {total}.";
                    var view = new DeviceSummaryViewState(title, summaryLine, statsLine: statsLine, suggestionLine: DeviceSummaryHelpers.BuildSuggestionText(), detailRows: rows, classRows: Array.Empty<DeviceClassSummaryRow>(), detailOffset: 0, detailPageSize: pageSize, detailFilters: filterLabels);
                    _state.ShowDeviceSummary(view);
                    var visible = Math.Min(pageSize, rows.Count);
                    _state.AddMessage($"Devices: showing {visible} of {rows.Count} (page size {pageSize}, total {total}). Matched: {matched}. Use 'pdk device <name>' for details.");
                }
                else
                {
                    var table = new Table().Border(TableBorder.Rounded).AddColumn("Library").AddColumn("Cell").AddColumn("Class").AddColumn("VT").AddColumn("VDD").AddColumn("Views");
                    foreach (var d in devices.Take(limit))
                    {
                        var vt = d.VtTags.Count == 0 ? "-" : string.Join('/', d.VtTags);
                        var vdd = d.VddTags.Count == 0 ? "-" : string.Join('/', d.VddTags);
                        var views = d.Views.Count == 0 ? "-" : string.Join(',', d.Views);
                        table.AddRow(d.LibraryName, d.CellName, d.Class.ToString(), vt, vdd, views);
                    }
                    AnsiConsole.Write(table);
                    _state.AddMessage($"Devices: {total}. Showing first {Math.Min(limit, total)}. Matched: {matched}. Use 'pdk device <name>' for details.");
                }
            }
            return CommandResult.Success;
        }
        catch (Exception ex)
        {
            _state.AddMessage($"Failed to load devices: {ex.Message}");
            return CommandResult.Failure;
        }
    }

    private CommandResult PdkDevice(string[] args)
    {
        if (args.Length == 0)
        {
            _state.AddMessage("Usage: pdk device <name>");
            return CommandResult.Success;
        }

        try
        {
            var dbPath = Path.Combine(WorkspaceState.GetWorkspaceFolder(_state.WorkspaceRoot), "pdk.db");
            if (!File.Exists(dbPath)) { _state.AddMessage("No PDK database found. Run 'pdk scan' first."); return CommandResult.Failure; }
            var devices = Cascode.Workspace.PdkDatabaseReader.LoadDevices(dbPath);
            var needle = args[0];
            var d = devices.FirstOrDefault(x => x.CellName.Contains(needle, StringComparison.OrdinalIgnoreCase) || x.CanonicalName.Contains(needle, StringComparison.OrdinalIgnoreCase));
            if (d is null) { _state.AddMessage("Device not found."); return CommandResult.Failure; }

            var detail = new Table().Border(TableBorder.Rounded).AddColumn("Field").AddColumn("Value");
            detail.AddRow("Library", d.LibraryName);
            detail.AddRow("Cell", d.CellName);
            detail.AddRow("Class", d.Class.ToString());
            detail.AddRow("VT", d.VtTags.Count == 0 ? "-" : string.Join('/', d.VtTags));
            detail.AddRow("VDD", d.VddTags.Count == 0 ? "-" : string.Join('/', d.VddTags));
            detail.AddRow("Views", d.Views.Count == 0 ? "-" : string.Join(',', d.Views));
            detail.AddRow("Cell Path", d.CellPath);
            var matches = Cascode.Workspace.PdkDatabaseReader.LoadMatchesForDevice(dbPath, d.CanonicalName);
            if (matches.Count > 0)
            {
                var best = matches.OrderBy(m => m.Rank).First();
                detail.AddRow("Match", $"{best.ModelName} ({best.Quality})");
                var geom = Cascode.Workspace.PdkDatabaseReader.LoadGeometryForModel(dbPath, best.ModelName);
                if (geom is not null)
                {
                    string fmt(double? v) => v.HasValue ? v.Value.ToString("g4", System.Globalization.CultureInfo.InvariantCulture) : "-";
                    var gstr = $"W:[{fmt(geom.WMin)}..{fmt(geom.WMax)}], L:[{fmt(geom.LMin)}..{fmt(geom.LMax)}]; Wdef={fmt(geom.WDefault)}, Ldef={fmt(geom.LDefault)}";
                    detail.AddRow("Geometry", Markup.Escape(gstr));
                }
            }
            AnsiConsole.Write(detail);
            return CommandResult.Success;
        }
        catch (Exception ex)
        {
            _state.AddMessage($"Failed to load device: {ex.Message}");
            return CommandResult.Failure;
        }
    }

    private CommandResult ShowPdkCharUsage(string[] args)
    {
        // Match legacy interactive log output
        _state.AddMessage("=== PDK Characterization ===");
        _state.AddMessage("");
        _state.AddMessage("Goal: Build device LUTs (gm/Id, etc.) for synthesis and sizing.");
        _state.AddMessage("Outputs: Netlists, results.csv, derived.csv stored in workspace cache.");
        _state.AddMessage("");
        _state.AddMessage("Commands:");
        _state.AddMessage("  pdk char config              Interactive form to set defaults (backend/corner/filters/jobs).");
        _state.AddMessage("  pdk char config --show       Show the saved defaults.");
        _state.AddMessage("  pdk char run                 Run a batch using saved defaults (flags override). Shows progress.");
        _state.AddMessage("  pdk char read <model>        Preview latest LUT for model — table + sparklines.");
        _state.AddMessage("");
        _state.AddMessage("Common Flags:");
        _state.AddMessage("  --backend spectre|ngspice    Pick simulator (Spectre-first; ngspice for CI).");
        _state.AddMessage("  --corner <name>              Model section/corner, e.g., tt/ff/ss.");
        _state.AddMessage("  --limit <n>                  Cap how many models to process (0 = all).");
        _state.AddMessage("  --jobs <n>                   Planned: parallelize bench generation (not Spectre).");
        _state.AddMessage("  --class nmos,pmos            Filter device classes.");
        _state.AddMessage("  --name-contains <csv>        Only names containing any token.");
        _state.AddMessage("  --name-excludes <csv>        Skip names containing any token.");
        _state.AddMessage("  --vt <csv>                   Only VT flavors (e.g., LVT,HVT).");
        _state.AddMessage("");
        _state.AddMessage("Examples:");
        _state.AddMessage("  pdk char config");
        _state.AddMessage("    → Open the defaults form; save corner/backend/filters/jobs to workspace.");
        _state.AddMessage("  pdk char run");
        _state.AddMessage("    → Start a batch with saved defaults; shows a live progress bar chart.");
        _state.AddMessage("  pdk char run --class nmos --limit 5 --name-excludes esd,io --vt LVT");
        _state.AddMessage("    → Quick LVT-only NMOS subset; skips ESD/IO variants.");
        _state.AddMessage("  pdk char run --corner tt --backend spectre --jobs 4");
        _state.AddMessage("    → Spectre-first; prepare to parallelize bench generation with 4 jobs.");
        _state.AddMessage("  pdk char read sky130_fd_pr__nfet_01v8");
        _state.AddMessage("    → Show table and gm/Id sparkline for the latest run of that model.");
        _state.AddMessage("");
        _state.AddMessage("Notes:");
        _state.AddMessage("- If SPECTRE_BIN isn't set, runs are skipped (generation only).");
        _state.AddMessage("- Results live under ~/.cascode/workspaces/<id>/char/<backend>/<corner>/<model>/<ts>/");
        return CommandResult.Success;
    }

    private CommandResult PdkCharConfigCommand(string[] args)
    {
        var cfgPath = WorkspaceState.GetCharConfigPath(_state.WorkspaceRoot);
        var cfg = CharRunConfig.Load(cfgPath);

        if (args.Length > 0 && args[0].Equals("--show", StringComparison.OrdinalIgnoreCase))
        {
            DumpConfig(cfg);
            return CommandResult.Success;
        }

        cfg.Backend = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("Select backend")
            .AddChoices(new[] { "spectre", "ngspice" })
            .HighlightStyle(new Style(Color.Cyan1))
            .MoreChoicesText("[grey](Move up/down to reveal more)[/]")
            .AddChoices(string.IsNullOrWhiteSpace(cfg.Backend) ? Array.Empty<string>() : new[] { cfg.Backend }));

        cfg.Corner = AnsiConsole.Ask<string>("Corner (e.g., tt/ff/ss):", string.IsNullOrWhiteSpace(cfg.Corner) ? "tt" : cfg.Corner);

        var classChoices = new[] { "nmos", "pmos" };
        var classPrompt = new MultiSelectionPrompt<string>()
            .Title("Device classes")
            .NotRequired()
            .InstructionsText("[grey](Space to toggle, Enter to accept)[/]")
            .AddChoices(classChoices);
        foreach (var c in cfg.Classes ?? new()) classPrompt.Select(c);
        var selected = AnsiConsole.Prompt(classPrompt);
        cfg.Classes = selected.ToList();

        cfg.Limit = AnsiConsole.Ask<int>("Limit (0 = all):", Math.Max(0, cfg.Limit));
        cfg.Jobs = AnsiConsole.Ask<int>("Jobs (parallel generation) [1..8]:", Math.Clamp(cfg.Jobs <= 0 ? 1 : cfg.Jobs, 1, 8));

        var nameContains = AnsiConsole.Ask<string>("Name contains (comma-separated, blank=none):", string.Join(',', cfg.NameContains ?? new()));
        cfg.NameContains = string.IsNullOrWhiteSpace(nameContains)
            ? new List<string>()
            : nameContains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        var nameExcludes = AnsiConsole.Ask<string>("Name excludes (comma-separated, blank=esd):", string.Join(',', cfg.NameExcludes ?? new()));
        cfg.NameExcludes = string.IsNullOrWhiteSpace(nameExcludes)
            ? new List<string> { "esd" }
            : nameExcludes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        var vtChoices = new[] { "ULVT", "LLVT", "SLVT", "LVT", "RVT", "SVT", "NVT", "HVT", "MVT" };
        var vtPrompt = new MultiSelectionPrompt<string>()
            .Title("VT flavors (optional)")
            .NotRequired()
            .InstructionsText("[grey](Space to toggle, Enter to accept)[/]")
            .AddChoices(vtChoices);
        foreach (var v in cfg.Vt ?? new()) vtPrompt.Select(v.ToUpperInvariant());
        cfg.Vt = AnsiConsole.Prompt(vtPrompt).ToList();

        cfg.OutRoot = AnsiConsole.Ask<string>("Output root (blank = default)", cfg.OutRoot ?? string.Empty);
        if (string.IsNullOrWhiteSpace(cfg.OutRoot)) cfg.OutRoot = null;

        cfg.Save(cfgPath);
        _state.AddMessage($"Saved characterization config → {cfgPath}");
        _state.AddMessage("Run 'pdk char run' to start a batch using this configuration.");
        return CommandResult.Success;

        void DumpConfig(CharRunConfig show)
        {
            var table = new Table().Border(TableBorder.Rounded).AddColumn("Key").AddColumn("Value");
            table.AddRow("Backend", show.Backend);
            table.AddRow("Corner", show.Corner);
            table.AddRow("Limit", show.Limit.ToString(CultureInfo.InvariantCulture));
            table.AddRow("Jobs", show.Jobs.ToString(CultureInfo.InvariantCulture));
            table.AddRow("Classes", string.Join(',', show.Classes ?? new()));
            table.AddRow("NameContains", string.Join(',', show.NameContains ?? new()));
            table.AddRow("NameExcludes", string.Join(',', show.NameExcludes ?? new()));
            table.AddRow("VT", string.Join(',', show.Vt ?? new()));
            table.AddRow("OutRoot", show.OutRoot ?? "(default)");
            AnsiConsole.Write(table);
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
                builder.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
                _state.MarkStreamedOutput();
            }
        });
        var logger = loggerFactory.CreateLogger("pdk");

        logger.LogInformation("Scanning workspace {Root}", targetRoot);

        if (_isInteractive())
        {
            using var renderSignal = new System.Threading.AutoResetEvent(false);
            void Handler() { try { renderSignal.Set(); } catch { } }
            _state.Changed += Handler;

            WorkspaceScanResult? result = null;
            Exception? scanError = null;

            _state.StartBusy("Scanning workspace…");

            var scanTask = Task.Run(() =>
            {
                try
                {
                    result = PerformScanAndUpdateDatabase(targetRoot, logger);
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

            // Event-driven live rendering
            try
            {
                var layout = ShellRenderer.Build(_state);
                AnsiConsole.Live(layout)
                    .AutoClear(false)
                    .Start(ctx =>
                {
                    // Initial content already present in layout
                    ctx.Refresh();
                    while (!scanTask.IsCompleted)
                    {
                        renderSignal.WaitOne(System.TimeSpan.FromSeconds(2));
                        // Update panels based on current state
                        try
                        {
                            layout["Content"]["Main"]["Log"].Update(ShellRenderer.BuildLog(_state));
                            if (_state.Scan is not null)
                            {
                                layout["Content"]["Sidebar"]["Navigator"].Update(ShellRenderer.BuildNavigator(_state));
                                layout["Content"]["Sidebar"]["Details"].Update(ShellRenderer.BuildDeckDetails(_state));
                            }
                            layout["PromptSpacer"].Update(ShellRenderer.BuildPrompt(_state));
                        }
                        catch { }
                        ctx.Refresh();
                    }
                    // Final update
                    try
                    {
                        layout["Content"]["Main"]["Log"].Update(ShellRenderer.BuildLog(_state));
                        if (_state.Scan is not null)
                        {
                            layout["Content"]["Sidebar"]["Navigator"].Update(ShellRenderer.BuildNavigator(_state));
                            layout["Content"]["Sidebar"]["Details"].Update(ShellRenderer.BuildDeckDetails(_state));
                        }
                        layout["PromptSpacer"].Update(ShellRenderer.BuildPrompt(_state));
                    }
                    catch { }
                    ctx.Refresh();
                });
            }
            finally
            {
                _state.Changed -= Handler;
                _state.StopBusy();
            }

            if (scanError is not null)
            {
                _state.AddMessage($"Scan failed: {scanError.Message}");
                return CommandResult.Failure;
            }

            if (result is not null)
            {
                logger.LogInformation("Found {Libraries} libraries, {Decks} model decks.", result.Libraries.Count, result.ModelDecks.Count);
                foreach (var warning in result.Warnings) logger.LogWarning("{Warning}", warning);
            }

            return CommandResult.Success;
        }
        else
        {
            var result = PerformScanAndUpdateDatabase(targetRoot, logger);
            logger.LogInformation("Found {Libraries} libraries, {Decks} model decks.", result.Libraries.Count, result.ModelDecks.Count);
            foreach (var warning in result.Warnings) logger.LogWarning("{Warning}", warning);
            return CommandResult.Success;
        }
    }

    private WorkspaceScanResult PerformScanAndUpdateDatabase(string targetRoot, ILogger logger)
    {
        var result = _scanner.Scan(targetRoot, logger);
        var previousSelection = _state.SelectedDeckIndex;
        _state.Scan = result;
        if (result.ModelDecks.Count == 0) _state.SelectedDeckIndex = null;
        else if (previousSelection.HasValue && previousSelection.Value >= 0 && previousSelection.Value < result.ModelDecks.Count) _state.SelectedDeckIndex = previousSelection;
        else _state.SelectedDeckIndex = 0;

        var phys = new PhysicalLibraryScanner().Scan(result.Libraries, warnings: null);

        try
        {
            var dbPath = Path.Combine(WorkspaceState.GetWorkspaceFolder(targetRoot), "pdk.db");
            if (File.Exists(dbPath)) File.Delete(dbPath);
            Cascode.Workspace.PdkDatabaseWriter.Write(dbPath, result, phys);
            var matches = Cascode.Workspace.DeviceModelMatcher.Match(phys, result.Models);
            Cascode.Workspace.PdkDatabaseWriter.UpsertMatches(dbPath, matches);
            var geom = Cascode.Workspace.ModelGeometryExtractor.Extract(result.Models);
            Cascode.Workspace.PdkDatabaseWriter.UpsertGeometry(dbPath, geom);
            logger.LogInformation("PDK database updated → {Path}", dbPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update PDK database");
        }

        return result;
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
            _ => Microsoft.Extensions.Logging.LogLevel.Information
        };
    }

    private CommandResult PdkSetDir(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("--clear", StringComparison.OrdinalIgnoreCase))
        {
            _config.PdkRoot = null;
            _configStorage.Save(_config);
            _state.UpdatePdkRoot(null);
            _state.SetWorkspace(_initialWorkspaceRoot);
            _state.AddMessage("Cleared default PDK workspace preference.");
            return CommandResult.Success;
        }

        if (args.Length > 0)
        {
            return ApplyPdkDirectory(args[0]);
        }

        var current = _state.PdkRoot ?? _state.WorkspaceRoot;
        var input = AnsiConsole.Ask<string>("Enter PDK workspace directory (leave blank to cancel):", current);
        if (string.IsNullOrWhiteSpace(input))
        {
            _state.AddMessage("PDK workspace unchanged.");
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
                _state.AddMessage($"Directory '{resolved}' not found.");
                return CommandResult.Failure;
            }

            _config.PdkRoot = resolved;
            _configStorage.Save(_config);
            _state.UpdatePdkRoot(resolved);
            _state.SetWorkspace(resolved);
            _state.AddMessage($"PDK workspace set to {resolved}");
            return CommandResult.Success;
        }
        catch (Exception ex)
        {
            _state.AddMessage($"Invalid path: {ex.Message}");
            return CommandResult.Failure;
        }
    }

    private CommandResult PdkCharRunCommand(string[] args)
    {
        var cfgPath = WorkspaceState.GetCharConfigPath(_state.WorkspaceRoot);
        var cfg = CharRunConfig.Load(cfgPath);

        var backend = cfg.Backend ?? "spectre";
        var corner = cfg.Corner ?? "tt";
        var limit = cfg.Limit;
        var outRoot = cfg.OutRoot ?? WorkspaceState.GetCharacterizationFolder(_state.WorkspaceRoot);
        var jobs = cfg.Jobs <= 0 ? 1 : cfg.Jobs;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--backend", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) backend = args[++i];
            else if (arg.Equals("--corner", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) corner = args[++i];
            else if (arg.Equals("--limit", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLimit)) limit = Math.Max(0, parsedLimit);
            else if (arg.Equals("--jobs", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedJobs)) jobs = Math.Clamp(parsedJobs, 1, 16);
            else if (arg.Equals("--name-contains", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) cfg.NameContains = SplitCsv(args[++i]);
            else if (arg.Equals("--name-excludes", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) cfg.NameExcludes = SplitCsv(args[++i]);
            else if (arg.Equals("--vt", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) cfg.Vt = SplitCsv(args[++i]).Select(s => s.ToUpperInvariant()).ToList();
            else if (arg.Equals("--class", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) cfg.Classes = SplitCsv(args[++i]).Select(s => s.ToLowerInvariant()).ToList();
            else if (arg.Equals("--out-root", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) outRoot = args[++i];
        }

        var models = EnsureModels();
        if (models is null)
        {
            return CommandResult.Failure;
        }
        if (models.Count == 0)
        {
            _state.AddMessage("No models discovered. Run pdk scan.");
            return CommandResult.Failure;
        }

        IEnumerable<SpectreModel> FilterModels()
        {
            var filtered = models.AsEnumerable();
            if (cfg.Classes is { Count: > 0 })
            {
                var set = new HashSet<string>(cfg.Classes.Select(c => c.ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);
                filtered = filtered.Where(m => m.DeviceClass switch
                {
                    DeviceClass.Nmos => set.Contains("nmos") || set.Contains("nfet") || set.Contains("nch"),
                    DeviceClass.Pmos => set.Contains("pmos") || set.Contains("pfet") || set.Contains("pch"),
                    _ => true
                });
            }
            if (cfg.Vt is { Count: > 0 })
            {
                var vt = new HashSet<string>(cfg.Vt.Select(v => v.ToUpperInvariant()), StringComparer.OrdinalIgnoreCase);
                filtered = filtered.Where(m => !string.IsNullOrWhiteSpace(m.ThresholdFlavor) && vt.Contains(m.ThresholdFlavor!.ToUpperInvariant()));
            }
            if (cfg.NameContains is { Count: > 0 })
            {
                filtered = filtered.Where(m => cfg.NameContains!.Any(tok => m.Name.Contains(tok, StringComparison.OrdinalIgnoreCase)));
            }
            if (cfg.NameExcludes is { Count: > 0 })
            {
                filtered = filtered.Where(m => !cfg.NameExcludes!.Any(tok => m.Name.Contains(tok, StringComparison.OrdinalIgnoreCase)));
            }
            return filtered;
        }

        var batch = FilterModels().ToList();
        if (limit > 0 && batch.Count > limit) batch = batch.Take(limit).ToList();
        if (batch.Count == 0)
        {
            _state.AddMessage("No models matched the selection.");
            return CommandResult.Success;
        }

        // Start progress
        _state.StartCharJob(batch.Count, backend, corner);
        _state.AddMessage($"Starting characterization batch → backend={backend}, corner={corner}, models={batch.Count}");

        void RunBatch()
        {
            var executed = 0;
            var exported = 0;
            var skipped = 0;
            var completed = false;

            try
            {
                foreach (var model in batch)
                {
                    _state.UpdateCharProgress(model.Name, generatedDelta: 1);
                    var jobDir = Path.Combine(outRoot, backend.ToLowerInvariant(), string.IsNullOrWhiteSpace(corner) ? "default" : corner, Sanitize(model.Name), DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
                    Directory.CreateDirectory(jobDir);
                    var harnessId = ResolveHarnessForModel(model) ?? "gm_id.v1";
                    var genResult = GenerateBenchForModel(model, harnessId, backend, corner, jobDir);
                    if (!genResult)
                    {
                        skipped++;
                        _state.UpdateCharProgress(model.Name, skippedDelta: 1);
                        continue;
                    }

                    if (TryRunSpectre(jobDir, backend))
                    {
                        executed++;
                        _state.UpdateCharProgress(model.Name, ranDelta: 1);
                    }

                    var exportOk = Services.CharExportService.ExportDerived(jobDir, metricFilter: null, out _, out var exportMsg);
                    _state.AddMessage(exportMsg);
                    if (exportOk)
                    {
                        exported++;
                        _state.UpdateCharProgress(model.Name, exportedDelta: 1);
                    }
                }

                _state.AddMessage($"Characterization batch complete: ran {executed}, exported {exported}, skipped {skipped}.");
                completed = true;
            }
            catch (Exception ex)
            {
                _state.AddMessage($"Characterization batch failed: {ex.Message}");
            }
            finally
            {
                if (!completed)
                {
                    _state.AddMessage("Characterization batch terminated early.");
                }

                _state.CompleteCharJob();
            }
        }

        if (_isInteractive())
        {
            Task.Run(RunBatch);
            _state.AddMessage("Batch running in background; progress will update while the CLI remains responsive.");
            return CommandResult.Success;
        }

        RunBatch();
        return CommandResult.Success;
    }

    private CommandResult PdkCharReadCommand(string[] args)
    {
        if (args.Length == 0)
        {
            _state.AddMessage("Usage: pdk char read <model> [--corner <name>] [--backend spectre] [--head <n>] [--job <path>]");
            return CommandResult.Success;
        }

        var model = args[0];
        var backend = "spectre";
        var corner = "tt";
        int head = 24;
        string? jobOverride = null;
        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--backend", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) backend = args[++i];
            else if (arg.Equals("--corner", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) corner = args[++i];
            else if (arg.Equals("--head", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) head = Math.Max(1, parsed);
            else if (arg.Equals("--job", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) jobOverride = PathUtils.NormalizePath(args[++i]);
        }

        var charRoot = WorkspaceState.GetCharacterizationFolder(_state.WorkspaceRoot);
        if (!Directory.Exists(charRoot))
        {
            _state.AddMessage("No characterization results found. Run 'pdk char run' first.");
            return CommandResult.Failure;
        }

        string jobDir;
        if (!string.IsNullOrEmpty(jobOverride))
        {
            jobDir = jobOverride;
        }
        else
        {
            var backendFolder = Path.Combine(charRoot, backend.ToLowerInvariant());
            if (!Directory.Exists(backendFolder)) { _state.AddMessage($"No runs stored for backend '{backend}'."); return CommandResult.Failure; }
            var cornerFolder = Path.Combine(backendFolder, string.IsNullOrWhiteSpace(corner) ? "default" : corner);
            if (!Directory.Exists(cornerFolder)) { _state.AddMessage($"No runs stored for corner '{corner}'."); return CommandResult.Failure; }
            var sanitizedQuery = Sanitize(model);
            var modelFolder = Path.Combine(cornerFolder, sanitizedQuery);
            if (!Directory.Exists(modelFolder))
            {
                var match = Directory.EnumerateDirectories(cornerFolder).FirstOrDefault(path => Path.GetFileName(path).Contains(sanitizedQuery, StringComparison.OrdinalIgnoreCase));
                if (match is null) { _state.AddMessage($"No characterization recorded for model '{model}'."); return CommandResult.Failure; }
                modelFolder = match;
            }
            var latest = Directory.EnumerateDirectories(modelFolder).Select(path => new DirectoryInfo(path)).OrderByDescending(di => di.LastWriteTimeUtc).FirstOrDefault();
            if (latest is null) { _state.AddMessage($"Model '{model}' has no completed runs."); return CommandResult.Failure; }
            jobDir = latest.FullName;
        }

        var derivedPath = Path.Combine(jobDir, "derived.csv");
        if (!File.Exists(derivedPath)) { _state.AddMessage($"Derived metrics not found at {derivedPath}. Run 'char export {jobDir}' first."); return CommandResult.Failure; }

        var (headers, samples) = Services.CharIoHelpers.LoadDerivedCsv(derivedPath);
        if (headers.Count == 0 || samples.Count == 0) { _state.AddMessage("Derived CSV did not contain numeric samples."); return CommandResult.Failure; }

        var (controlIdx, controlName) = Services.CharIoHelpers.FindColumn(headers, "vgs", "vsg");
        var (idIdx, _) = Services.CharIoHelpers.FindColumn(headers, "id");
        var (gmIdx, _) = Services.CharIoHelpers.FindColumn(headers, "gm");
        var (gmIdIdx, _) = Services.CharIoHelpers.FindColumn(headers, "gm_over_id");
        var (vthIdx, _) = Services.CharIoHelpers.FindColumn(headers, "vth");

        var preview = Math.Min(head, samples.Count);
        var table = new Table().Border(TableBorder.SimpleHeavy).AddColumn("#").AddColumn(controlName.ToUpperInvariant()).AddColumn("Id");
        if (gmIdx >= 0) table.AddColumn("gm");
        if (gmIdIdx >= 0) table.AddColumn("gm/Id");
        if (vthIdx >= 0) table.AddColumn("Vth");

        static double sampleSafe(IReadOnlyList<double> data, int idx) => idx >= 0 && idx < data.Count ? data[idx] : double.NaN;
        for (var i = 0; i < preview; i++)
        {
            var sample = samples[i];
            var row = new List<string>
            {
                (i + 1).ToString(CultureInfo.InvariantCulture),
                Services.CharIoHelpers.FormatNumber(sampleSafe(sample, controlIdx)),
                Services.CharIoHelpers.FormatNumber(sampleSafe(sample, idIdx))
            };
            if (gmIdx >= 0) row.Add(Services.CharIoHelpers.FormatNumber(sampleSafe(sample, gmIdx)));
            if (gmIdIdx >= 0) row.Add(Services.CharIoHelpers.FormatNumber(sampleSafe(sample, gmIdIdx)));
            if (vthIdx >= 0) row.Add(Services.CharIoHelpers.FormatNumber(sampleSafe(sample, vthIdx)));
            table.AddRow(row.ToArray());
        }

        AnsiConsole.Write(new Rule($"[bold]{model}[/] — {backend} / {corner}") { Justification = Justify.Left });
        AnsiConsole.Write(table);
        if (gmIdIdx >= 0) Services.CharIoHelpers.RenderSparkline(samples, gmIdIdx, "gm/Id");
        if (idIdx >= 0) Services.CharIoHelpers.RenderSparkline(samples, idIdx, "Id");
        _state.AddMessage($"Derived source: {derivedPath}");
        return CommandResult.Success;
    }

    private IReadOnlyList<SpectreModel>? EnsureModels()
    {
        try
        {
            var dbPath = Path.Combine(WorkspaceState.GetWorkspaceFolder(_state.WorkspaceRoot), "pdk.db");
            if (!File.Exists(dbPath))
            {
                _state.AddMessage("No PDK database found. Run 'pdk scan' first.");
                return null;
            }
            return Cascode.Workspace.PdkDatabaseReader.LoadModels(dbPath);
        }
        catch (Exception ex)
        {
            _state.AddMessage($"Failed to load models from PDK database: {ex.Message}");
            return null;
        }
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    private static string FormatList(IEnumerable<string> values)
    {
        var distinct = values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (distinct.Length == 0) return "-";
        if (distinct.Length <= 5) return string.Join(", ", distinct);
        return string.Join(", ", distinct.Take(5)) + $" … ({distinct.Length - 5} more)";
    }

    private static List<string> SplitCsv(string value)
        => string.IsNullOrWhiteSpace(value) ? new List<string>() : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private bool GenerateBenchForModel(SpectreModel model, string harnessId, string backend, string? corner, string jobDir)
    {
        try
        {
            // Defaults before geometry override
            double width = 1e-6;
            double length = 0.18e-6;
            int nfVal = 1;
            double vdsVal = 0.9;
            double start = 0.0, stop = 1.2;
            static string? TryNormalizeInclude(string path)
            {
                if (string.IsNullOrWhiteSpace(path)) return null;
                try { return PathUtils.NormalizePath(path); }
                catch { return File.Exists(path) ? Path.GetFullPath(path) : null; }
            }

            var rawDecks = model.Decks ?? Array.Empty<string>();
            var decksWithSection = rawDecks.Select(TryNormalizeInclude).Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p!)).Select(p => p!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var sourceIncludesAll = (model.SourceFiles ?? Array.Empty<string>()).Select(TryNormalizeInclude).Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p!)).Select(p => p!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var extraIncludes = new List<string>();
            if (!string.IsNullOrWhiteSpace(corner))
            {
                var key = corner.Trim();
                sourceIncludesAll = sourceIncludesAll.Where(p => Path.GetFileName(p)!.IndexOf($"_{key}", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            }
            if (decksWithSection.Count == 0) extraIncludes = sourceIncludesAll; else extraIncludes.Clear();

            var resolvedIncludes = new List<string>(decksWithSection.Count + extraIncludes.Count);
            resolvedIncludes.AddRange(decksWithSection);
            resolvedIncludes.AddRange(extraIncludes);

            static string ResolveModelNameForNetlist(SpectreModel m)
            {
                var name = m.Name;
                if (string.IsNullOrWhiteSpace(name)) return name;
                var modelMarker = name.IndexOf("__model", StringComparison.OrdinalIgnoreCase);
                if (modelMarker < 0) return name;
                var basePart = name.Substring(0, modelMarker);
                var lastSeparator = basePart.LastIndexOf("__", StringComparison.Ordinal);
                if (lastSeparator >= 0 && lastSeparator + 2 < basePart.Length) basePart = basePart[(lastSeparator + 2)..];
                return basePart.Replace('.', '_');
            }

            var netlistModelName = ResolveModelNameForNetlist(model);
            if (resolvedIncludes.Count == 0) _state.AddMessage($"[warn] No include decks located for model '{model.Name}'. Spectre run may fail.");

            // Apply geometry constraints from PDK database
            var dbPath = Path.Combine(WorkspaceState.GetWorkspaceFolder(_state.WorkspaceRoot), "pdk.db");
            if (File.Exists(dbPath))
            {
                var geom = Cascode.Workspace.PdkDatabaseReader.LoadGeometryForModel(dbPath, model.Name);
                if (geom is not null)
                {
                    static double Clamp(double val, double? min, double? max)
                    {
                        if (min.HasValue && val < min.Value) val = min.Value;
                        if (max.HasValue && val > max.Value) val = max.Value;
                        return val;
                    }

                    // Use defaults if present; otherwise clamp current
                    if (geom.WDefault.HasValue) width = geom.WDefault.Value;
                    if (geom.LDefault.HasValue) length = geom.LDefault.Value;
                    if (geom.NfDefault.HasValue && geom.NfDefault.Value > 0) nfVal = geom.NfDefault.Value;
                    width = Clamp(width, geom.WMin, geom.WMax);
                    length = Clamp(length, geom.LMin, geom.LMax);
                    if (nfVal <= 0) nfVal = 1;
                    _state.AddMessage($"Geometry for {model.Name}: W={width:g4} m, L={length:g4} m, NF={nfVal}");
                }
            }

            // Adjust Vds/Vgs sweep using model voltage domain when known
            if (!string.IsNullOrWhiteSpace(model.VoltageDomain))
            {
                var vd = model.VoltageDomain.Trim().ToLowerInvariant();
                var m = System.Text.RegularExpressions.Regex.Match(vd, @"(?<n>\d+)(?:\.(?<f>\d+))?v");
                if (m.Success)
                {
                    var nn = int.Parse(m.Groups["n"].Value);
                    var ff = m.Groups["f"].Success ? int.Parse(m.Groups["f"].Value) : 0;
                    var volts = nn + (ff > 0 ? ff / Math.Pow(10, m.Groups["f"].Value.Length) : 0.0);
                    if (volts > 0)
                    {
                        vdsVal = Math.Max(0.1, Math.Min(volts, volts * 0.6));
                        stop = Math.Max(stop, volts);
                    }
                }
            }

            var spec = new Cascode.Bench.TestbenchSpec
            {
                Backend = backend.Equals("spectre", StringComparison.OrdinalIgnoreCase) ? Cascode.Bench.BenchBackendType.Spectre : Cascode.Bench.BenchBackendType.Ngspice,
                Name = harnessId,
                ModelName = netlistModelName,
                IsSubckt = string.Equals(model.ModelType, "subckt", StringComparison.OrdinalIgnoreCase),
                Corner = corner,
                TemperatureC = 27,
                SupplyV = 0,
                W_M = width,
                L_M = length,
                Mult = 1,
                Nfingers = nfVal,
                Vgs = new Cascode.Bench.SweepSpec(start, stop, 0.01),
                Vds = vdsVal,
                Vsb = 0.0,
                Includes = resolvedIncludes,
                Section = corner,
                JobDir = jobDir,
                ResultsCsv = "results.csv"
            };

            var ctx = new Cascode.Bench.TestbenchContext
            {
                Spec = spec,
                WorkspaceRoot = _state.WorkspaceRoot,
                PdkRoot = _state.PdkRoot ?? _state.WorkspaceRoot,
                DeckPaths = resolvedIncludes,
                IncludePathsWithSection = decksWithSection,
                IncludePathsWithoutSection = extraIncludes,
                Section = corner,
                Args = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            };

            var reg = Cascode.Bench.HarnessService.CreateDefault(_state.WorkspaceRoot);
            var gen = new Cascode.Bench.TestbenchGenerator(reg);
            var files = gen.Generate(ctx);
            _state.AddMessage($"Generated testbench: {files.NetlistPath}");
            _state.AddMessage($"Spec: {files.SpecPath}");
            _state.AddMessage("Run your simulator manually and then 'char export' to derive gm/Id.");
            return true;
        }
        catch (Exception ex)
        {
            _state.AddMessage($"Generation failed: {ex.Message}");
            return false;
        }
    }

    private static string? ResolveHarnessForModel(SpectreModel model)
        => model.DeviceClass switch
        {
            DeviceClass.Nmos => "gm_id.v1",
            DeviceClass.Pmos => "gm_id_pmos.v1",
            _ => null
        };

    private bool TryRunSpectre(string jobDir, string backend)
    {
        if (!backend.Equals("spectre", StringComparison.OrdinalIgnoreCase)) return false;
        var binary = Environment.GetEnvironmentVariable("SPECTRE_BIN");
        var exe = ResolveSpectreExecutable(binary);
        if (string.IsNullOrWhiteSpace(exe))
        {
            var home = Environment.GetEnvironmentVariable("SPECTRE_HOME");
            if (!string.IsNullOrWhiteSpace(home))
            {
                exe = ResolveSpectreExecutable(Path.Combine(home, "bin", "spectre"));
            }
        }
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
        {
            _state.AddMessage("SPECTRE_BIN not set or executable not found; skipping runs.");
            return false;
        }
        // Find a netlist to run (prefer .scs)
        var netlist = Directory.EnumerateFiles(jobDir, "*.scs", SearchOption.TopDirectoryOnly)
            .OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc)
            .FirstOrDefault();
        if (netlist is null)
        {
            _state.AddMessage("No Spectre netlist (.scs) found; skipping runs.");
            return false;
        }

        var cmd = $"\"{exe}\" -format nutascii \"{Path.GetFileName(netlist)}\"";
        var sh = OperatingSystem.IsWindows() ? new[] { "cmd", "/c", cmd } : new[] { "/bin/bash", "-lc", cmd };
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = sh[0],
                Arguments = string.Join(' ', sh.Skip(1)),
                WorkingDirectory = jobDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var p = System.Diagnostics.Process.Start(psi)!;
            p.WaitForExit(30_000);
            var rc = p.ExitCode;
            _state.AddMessage(rc == 0 ? "Spectre run completed." : $"Spectre exited with code {rc}.");
            return rc == 0;
        }
        catch (Exception ex)
        {
            _state.AddMessage($"Failed to run Spectre: {ex.Message}");
            return false;
        }
    }

    private static string? ResolveSpectreExecutable(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var candidate = Environment.ExpandEnvironmentVariables(input.Trim());
        if (string.IsNullOrWhiteSpace(candidate)) return null;
        if (File.Exists(candidate)) return candidate;
        if (Directory.Exists(candidate))
        {
            foreach (var guess in EnumerateSpectreGuesses(candidate)) if (File.Exists(guess)) return guess;
        }
        else if (!candidate.Contains(Path.DirectorySeparatorChar) && !candidate.Contains(Path.AltDirectorySeparatorChar))
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var part in pathEnv.Split(Path.PathSeparator))
            {
                var exe = Path.Combine(part, candidate);
                if (File.Exists(exe)) return exe;
            }
        }
        return null;
    }

    private static IEnumerable<string> EnumerateSpectreGuesses(string root)
    {
        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(root, "tools", "bin", "spectre.exe");
            yield return Path.Combine(root, "tools.win64", "bin", "spectre.exe");
        }
        else
        {
            yield return Path.Combine(root, "tools", "bin", "spectre");
            yield return Path.Combine(root, "tools.lnx86", "bin", "spectre");
        }
    }

    // Removed JSON cache loader; the CLI uses the PDK database exclusively.

    /* private static void ParseModelArguments(string[] args, HashSet<DeviceClass> filters, int totalCount, ref int limit)
    {
        if (args.Length == 0) return;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.IsNullOrWhiteSpace(arg)) continue;
            if (arg.Equals("--limit", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLimit))
                {
                    limit = Math.Clamp(parsedLimit, 1, Math.Max(1, totalCount));
                    i++;
                }
                continue;
            }
            foreach (var token in ExpandFilterToken(arg)) if (TryResolveDeviceClass(token, out var dc)) filters.Add(dc);
        }
    } */

    /* private static IEnumerable<string> ExpandFilterToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) yield break;
        var trimmed = token.Trim();
        var segments = trimmed.Split(new[] { '/', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            yield return trimmed.Trim('/');
            yield break;
        }
        foreach (var segment in segments)
        {
            var clean = segment.Trim().Trim('/');
            if (clean.Length == 0) continue;
            yield return clean;
        }
    } */

    /* private static bool TryResolveDeviceClass(string token, out DeviceClass deviceClass)
    {
        deviceClass = DeviceClass.Unknown;
        if (string.IsNullOrWhiteSpace(token)) return false;
        var normalized = token.Trim().Trim('/').ToLowerInvariant();
        switch (normalized)
        {
            case "nmos" or "nfet" or "nch": deviceClass = DeviceClass.Nmos; return true;
            case "pmos" or "pfet" or "pch": deviceClass = DeviceClass.Pmos; return true;
            case "cap" or "caps" or "capacitor" or "capacitors": deviceClass = DeviceClass.Capacitor; return true;
            case "res" or "resistor" or "resistors": deviceClass = DeviceClass.Resistor; return true;
            case "diode" or "diodes": deviceClass = DeviceClass.Diode; return true;
            case "bjt" or "bipolar": deviceClass = DeviceClass.Bipolar; return true;
            case "moscap": deviceClass = DeviceClass.Moscap; return true;
            case "ind" or "inductor" or "inductors": deviceClass = DeviceClass.Inductor; return true;
            case "tline" or "tl" or "transmissionline": deviceClass = DeviceClass.TransmissionLine; return true;
            case "other": deviceClass = DeviceClass.Other; return true;
            case "unknown" or "uncat" or "uncategorized" or "unmatched": deviceClass = DeviceClass.Unknown; return true;
            default: return false;
        }
    } */

    private static (HashSet<string> classes, HashSet<string> vts, HashSet<string> vdds, bool? infra, bool? matched, int limit) ParseDeviceFilters(string[] args)
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
                foreach (var tok in SplitCsv(args[++i])) classes.Add(tok);
            }
            else if (a.Equals("--vt", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                foreach (var tok in SplitCsv(args[++i])) vts.Add(tok.ToUpperInvariant());
            }
            else if (a.Equals("--vdd", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                foreach (var tok in SplitCsv(args[++i])) vdds.Add(tok.ToLowerInvariant());
            }
            else if (a.Equals("--infra", StringComparison.OrdinalIgnoreCase)) infra = true;
            else if (a.Equals("--no-infra", StringComparison.OrdinalIgnoreCase)) infra = false;
            else if (a.Equals("--matched", StringComparison.OrdinalIgnoreCase)) matched = true;
            else if (a.Equals("--unmatched", StringComparison.OrdinalIgnoreCase)) matched = false;
            else if (a.Equals("--limit", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out var lim)) limit = Math.Max(1, lim);
            }
        }
        return (classes, vts, vdds, infra, matched, limit);
    }

    private static bool DeviceMatchesFilters(Cascode.Workspace.Device d, HashSet<string> classes, HashSet<string> vts, HashSet<string> vdds, bool? infra, HashSet<string>? matchedKeys, bool? matched)
    {
        if (classes.Count > 0 && !classes.Contains(d.Class.ToString(), StringComparer.OrdinalIgnoreCase)) return false;
        if (vts.Count > 0 && !d.VtTags.Any(t => vts.Contains(t, StringComparer.OrdinalIgnoreCase))) return false;
        if (vdds.Count > 0 && !d.VddTags.Any(t => vdds.Contains(t, StringComparer.OrdinalIgnoreCase))) return false;
        if (infra.HasValue)
        {
            var isInfra = d.Tags.Any(t => t.Equals("infra", StringComparison.OrdinalIgnoreCase));
            if (infra.Value != isInfra) return false;
        }
        if (matched.HasValue && matchedKeys is not null)
        {
            var isMatched = matchedKeys.Contains(d.CanonicalName);
            if (matched.Value != isMatched) return false;
        }
        return true;
    }

    private CommandResult PdkMatch(string[] args)
    {
        try
        {
            var dbPath = Path.Combine(WorkspaceState.GetWorkspaceFolder(_state.WorkspaceRoot), "pdk.db");
            if (!File.Exists(dbPath)) { _state.AddMessage("No PDK database found. Run 'pdk scan' first."); return CommandResult.Failure; }

            var cov = Cascode.Workspace.PdkDatabaseReader.GetMatchCoverage(dbPath);
            var byClass = Cascode.Workspace.PdkDatabaseReader.GetMatchCoverageByClass(dbPath);

            var table = new Table().Border(TableBorder.Rounded).AddColumn("Class").AddColumn("Total").AddColumn("Matched").AddColumn("Ambiguous").AddColumn("Unmatched");
            foreach (var row in byClass)
            {
                table.AddRow(row.Class, row.Total.ToString(CultureInfo.InvariantCulture), row.Matched.ToString(CultureInfo.InvariantCulture), row.Ambiguous.ToString(CultureInfo.InvariantCulture), row.Unmatched.ToString(CultureInfo.InvariantCulture));
            }
            AnsiConsole.Write(table);
            _state.AddMessage($"Coverage: total={cov.Total}, matched={cov.Matched}, ambiguous={cov.Ambiguous}, unmatched={cov.Unmatched}.");
            if (cov.SampleAmbiguous.Count > 0) _state.AddMessage("Ambiguous examples: " + string.Join(", ", cov.SampleAmbiguous));
            if (cov.SampleUnmatched.Count > 0) _state.AddMessage("Unmatched examples: " + string.Join(", ", cov.SampleUnmatched));
            return CommandResult.Success;
        }
        catch (Exception ex)
        {
            _state.AddMessage($"Failed to compute match coverage: {ex.Message}");
            return CommandResult.Failure;
        }
    }

}
