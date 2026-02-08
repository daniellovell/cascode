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
using Spectre.Console.Rendering;

namespace Cascode.Cli.Commands;

internal sealed class PdkCommandModule : ICommandModule
{
    private readonly ShellState _state;
    private readonly WorkspaceScanner _scanner;
    private readonly CliConfig _config;
    private readonly CliConfigStorage _configStorage;
    private readonly string _initialWorkspaceRoot;
    private readonly Func<bool> _isInteractive;
    private readonly CliOutputProvider _outputProvider;
    private readonly ICliOutput _shellOutput;
    private ICliOutput? _nonInteractiveOutput;
    private CommandRegistry? _registry;
    internal static readonly string[] PdkCommandPrefix = new[] { "pdk" };
    internal static readonly string[] SimulatorBackends = new[] { "spectre", "ngspice" };
    internal static readonly string[] InfraFilterOptions = new[]
    {
        "all",
        "infra-only",
        "exclude-infra",
    };

    public PdkCommandModule(
        ShellState state,
        WorkspaceScanner scanner,
        CliConfig config,
        CliConfigStorage configStorage,
        string initialWorkspaceRoot,
        Func<bool> isInteractive,
        CliOutputProvider output
    )
    {
        _state = state;
        _scanner = scanner;
        _config = config;
        _configStorage = configStorage;
        _initialWorkspaceRoot = initialWorkspaceRoot;
        _isInteractive = isInteractive;
        _outputProvider = output;
        _shellOutput = new ShellStateCliOutput(_state);
    }

    private ICliOutput Output =>
        _isInteractive() ? _shellOutput : _nonInteractiveOutput ??= _outputProvider.Get();

    private void WriteRenderable(IRenderable renderable)
    {
        if (Output.Out is not null)
        {
            Output.Out.Write(renderable);
            return;
        }

        AnsiConsole.Write(renderable);
    }

    public void Register(CommandRegistry registry)
    {
        _registry = registry;

        registry.Register(new DelegateCliCommand("pdk", "Manage PDK workspace", ShowPdkUsage));
        registry.Register(new DelegateCliCommand("pdk scan", "Scan workspace for decks", PdkScan));
        registry.Register(
            new DelegateCliCommand("pdk devices", "List discovered devices", PdkDevices)
        );
        registry.Register(
            new DelegateCliCommand("pdk device", "Inspect a specific device", PdkDevice)
        );
        registry.Register(
            new DelegateCliCommand(
                "pdk set-dir",
                "Set or clear the default PDK workspace",
                PdkSetDir
            )
        );
        registry.Register(
            new DelegateCliCommand(
                "pdk match",
                "Device↔Model coverage and ambiguity summary",
                PdkMatch
            )
        );

        // PDK emit
        registry.Register(
            new DelegateCliCommand("pdk emit", "Emit derived PDK artifacts", ShowPdkEmitUsage)
        );
        registry.Register(
            new DelegateCliCommand(
                "pdk emit primitives",
                "Generate a Cascode primitive library from pdk.db",
                PdkEmitPrimitivesCommand
            )
        );

        // PDK characterization
        registry.Register(
            new DelegateCliCommand("pdk char", "PDK characterization commands", ShowPdkCharUsage)
        );
        registry.Register(
            new DelegateCliCommand(
                "pdk char help",
                "Show PDK characterization help",
                ShowPdkCharUsage,
                hidden: true
            )
        );
        registry.Register(
            new DelegateCliCommand(
                "pdk char config",
                "Configure batch characterization",
                PdkCharConfigCommand
            )
        );
        registry.Register(
            new DelegateCliCommand("pdk char run", "Characterize devices", PdkCharRunCommand)
        );
        registry.Register(
            new DelegateCliCommand("pdk char read", "View characterized LUTs", PdkCharReadCommand)
        );
        registry.Register(
            new DelegateCliCommand(
                "pdk char status",
                "Show characterization coverage",
                PdkCharStatusCommand
            )
        );
    }

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

    private CommandResult ShowPdkEmitUsage(string[] args)
    {
        Output.WriteLine("Usage: pdk emit <subcommand>");
        Output.WriteLine(
            "  pdk emit primitives  Generate lib/pdk/<pdk>/{devices,resistors,capacitors,diodes}.cas"
        );
        return CommandResult.Success;
    }

    private CommandResult PdkEmitPrimitivesCommand(string[] args)
    {
        string? pdkName = null;
        string? outDirectory = null;
        var includeFixed = false;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--pdk", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                pdkName = args[++i];
            }
            else if (
                args[i].Equals("--out", StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length
            )
            {
                outDirectory = args[++i];
            }
            else if (args[i].Equals("--include-fixed", StringComparison.OrdinalIgnoreCase))
            {
                includeFixed = true;
            }
            else if (args[i].Equals("--help", StringComparison.OrdinalIgnoreCase))
            {
                Output.WriteLine(
                    "Usage: pdk emit primitives [--pdk <name>] [--out <dir>] [--include-fixed]\n\nDefaults:\n  --pdk            (derived from current PDK root directory name)\n  --out            lib/pdk/<pdk>\n  --include-fixed  disabled (emit only parametric primitive families)"
                );
                return CommandResult.Success;
            }
            else
            {
                Output.WriteLine($"Unknown option: {args[i]}");
                return CommandResult.Failure;
            }
        }

        pdkName ??= Path.GetFileName(Path.GetFullPath(_state.PdkRoot ?? _state.WorkspaceRoot));
        if (string.IsNullOrWhiteSpace(pdkName))
        {
            Output.Error("Unable to determine PDK name. Provide --pdk <name>.");
            return CommandResult.Failure;
        }

        outDirectory ??= PdkPrimitiveLibraryLayout.GetDefaultOutputDirectory(pdkName);

        var dbPath = Path.Combine(
            WorkspaceState.GetWorkspaceFolder(_state.WorkspaceRoot),
            "pdk.db"
        );
        var result = PdkEmitPrimitivesService.Emit(
            new PdkEmitPrimitivesService.EmitArgs(
                PdkName: pdkName,
                DbPath: dbPath,
                OutputDirectory: outDirectory,
                IncludeFixed: includeFixed
            )
        );

        if (!result.Succeeded)
        {
            Output.Error(result.Message);
            return CommandResult.Failure;
        }

        Output.WriteLine(result.Message);
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

    private CommandResult ShowPdkCharUsage(string[] args)
    {
        // Match legacy interactive log output
        Output.WriteLine("=== PDK Characterization ===");
        Output.WriteLine("");
        Output.WriteLine("Goal: Build device LUTs (gm/Id, etc.) for synthesis and sizing.");
        Output.WriteLine("Outputs: Netlists, results.csv, derived.csv stored in workspace cache.");
        Output.WriteLine("");
        Output.WriteLine("Commands:");
        Output.WriteLine(
            "  pdk char config              Interactive form to set defaults (backend/corner/filters/jobs)."
        );
        Output.WriteLine("  pdk char config --show       Show the saved defaults.");
        Output.WriteLine(
            "  pdk char run                 Run a batch using saved defaults (flags override). Shows progress."
        );
        Output.WriteLine(
            "  pdk char status              Show characterization coverage matrix (devices × corners)."
        );
        Output.WriteLine("  pdk char read <device/model> Preview latest LUT — table + sparklines.");
        Output.WriteLine("");
        Output.WriteLine("Common Flags:");
        Output.WriteLine("  --backend ngspice           Pick simulator (ngspice only for now).");
        Output.WriteLine("  --corner <name>              Model section/corner, e.g., tt/ff/ss.");
        Output.WriteLine(
            "  --limit <n>                  Cap how many devices to process (0 = all)."
        );
        Output.WriteLine("  --jobs <n>                   Ignored (legacy flag).");
        Output.WriteLine("  --class nmos,pmos            Filter device classes.");
        Output.WriteLine("  --name-contains <csv>        Only names containing any token.");
        Output.WriteLine("  --name-excludes <csv>        Skip names containing any token.");
        Output.WriteLine("  --vt <csv>                   Only VT flavors (e.g., LVT,HVT).");
        Output.WriteLine("  --vdd <csv>                  Only VDD tags (e.g., 1.8V,01v8).");
        Output.WriteLine(
            "  --infra / --no-infra         Include only infra devices, or exclude them."
        );
        Output.WriteLine("");
        Output.WriteLine("Examples:");
        Output.WriteLine("  pdk char config");
        Output.WriteLine(
            "    → Open the defaults form; save corner/backend/filters/jobs to workspace."
        );
        Output.WriteLine("  pdk char run");
        Output.WriteLine(
            "    → Start a batch with saved defaults; shows a live progress bar chart."
        );
        Output.WriteLine("  pdk char run --class nmos --limit 5 --name-excludes esd,io --vt LVT");
        Output.WriteLine("    → Quick LVT-only NMOS subset; skips ESD/IO variants.");
        Output.WriteLine("  pdk char run --corner tt --backend ngspice");
        Output.WriteLine("    → Run characterization using ngspice.");
        Output.WriteLine("  pdk char read sky130_fd_pr__nfet_01v8");
        Output.WriteLine("    → Show table and gm/Id sparkline for the latest run of that device.");
        Output.WriteLine("");
        Output.WriteLine("Notes:");
        Output.WriteLine("- Requires 'pdk scan' and 'pdk emit primitives' before running.");
        Output.WriteLine(
            "- Results live under ~/.cascode/workspaces/<id>/char/<backend>/<corner>/<device>/<ts>/"
        );
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

        cfg.Backend = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select backend")
                .AddChoices(SimulatorBackends)
                .HighlightStyle(new Style(Color.Cyan1))
                .MoreChoicesText("[grey](Move up/down to reveal more)[/]")
                .AddChoices(
                    string.IsNullOrWhiteSpace(cfg.Backend)
                        ? Array.Empty<string>()
                        : new[] { cfg.Backend }
                )
        );

        cfg.Corner = AnsiConsole.Ask<string>(
            "Corner (e.g., tt/ff/ss):",
            string.IsNullOrWhiteSpace(cfg.Corner) ? "tt" : cfg.Corner
        );

        var classChoices = new[] { "nmos", "pmos" };
        var classPrompt = new MultiSelectionPrompt<string>()
            .Title("Device classes")
            .NotRequired()
            .InstructionsText("[grey](Space to toggle, Enter to accept)[/]")
            .AddChoices(classChoices);
        foreach (var c in cfg.Classes ?? new())
            classPrompt.Select(c);
        var selected = AnsiConsole.Prompt(classPrompt);
        cfg.Classes = selected.ToList();

        cfg.Limit = AnsiConsole.Ask<int>("Limit (0 = all):", Math.Max(0, cfg.Limit));
        cfg.Jobs = AnsiConsole.Ask<int>(
            "Jobs (parallel generation) [1..8]:",
            Math.Clamp(cfg.Jobs <= 0 ? 1 : cfg.Jobs, 1, 8)
        );

        var nameContains = AnsiConsole.Ask<string>(
            "Name contains (comma-separated, blank=none):",
            string.Join(',', cfg.NameContains ?? new())
        );
        cfg.NameContains = string.IsNullOrWhiteSpace(nameContains)
            ? new List<string>()
            : nameContains
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

        var nameExcludes = AnsiConsole.Ask<string>(
            "Name excludes (comma-separated, blank=esd):",
            string.Join(',', cfg.NameExcludes ?? new())
        );
        cfg.NameExcludes = string.IsNullOrWhiteSpace(nameExcludes)
            ? new List<string> { "esd" }
            : nameExcludes
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

        var vtChoices = new[] { "ULVT", "LLVT", "SLVT", "LVT", "RVT", "SVT", "NVT", "HVT", "MVT" };
        var vtPrompt = new MultiSelectionPrompt<string>()
            .Title("VT flavors (optional)")
            .NotRequired()
            .InstructionsText("[grey](Space to toggle, Enter to accept)[/]")
            .AddChoices(vtChoices);
        foreach (var v in cfg.Vt ?? new())
            vtPrompt.Select(v.ToUpperInvariant());
        cfg.Vt = AnsiConsole.Prompt(vtPrompt).ToList();

        var vddInput = AnsiConsole.Ask<string>(
            "VDD filter (comma-separated, blank=all):",
            string.Join(',', cfg.Vdd ?? new())
        );
        cfg.Vdd = string.IsNullOrWhiteSpace(vddInput) ? new List<string>() : SplitCsv(vddInput);

        var infraPrompt = new SelectionPrompt<string>()
            .Title("Infra devices")
            .AddChoices(InfraFilterOptions)
            .HighlightStyle(new Style(Color.Cyan1))
            .MoreChoicesText("[grey](Use arrows to change selection)[/]");
        var infraChoice = AnsiConsole.Prompt(infraPrompt);
        cfg.Infra = infraChoice switch
        {
            "infra-only" => true,
            "exclude-infra" => false,
            _ => null,
        };

        cfg.OutRoot = AnsiConsole.Ask<string>(
            "Output root (blank = default)",
            cfg.OutRoot ?? string.Empty
        );
        if (string.IsNullOrWhiteSpace(cfg.OutRoot))
            cfg.OutRoot = null;

        cfg.Save(cfgPath);
        Output.WriteLine($"Saved characterization config → {cfgPath}");
        Output.WriteLine("Run 'pdk char run' to start a batch using this configuration.");
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
            table.AddRow("VDD", string.Join(',', show.Vdd ?? new()));
            table.AddRow(
                "Infra",
                show.Infra.HasValue ? (show.Infra.Value ? "infra-only" : "exclude-infra") : "all"
            );
            table.AddRow("OutRoot", show.OutRoot ?? "(default)");
            WriteRenderable(table);
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

    private CommandResult PdkCharRunCommand(string[] args)
    {
        var cfgPath = WorkspaceState.GetCharConfigPath(_state.WorkspaceRoot);
        var cfg = CharRunConfig.Load(cfgPath);

        var backend = cfg.Backend ?? "ngspice";
        var corner = cfg.Corner ?? "tt";
        var limit = cfg.Limit;
        var outRoot = cfg.OutRoot ?? WorkspaceState.GetCharacterizationFolder(_state.WorkspaceRoot);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--backend", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                backend = args[++i];
            else if (
                arg.Equals("--corner", StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length
            )
                corner = args[++i];
            else if (
                arg.Equals("--limit", StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length
                && int.TryParse(
                    args[++i],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsedLimit
                )
            )
                limit = Math.Max(0, parsedLimit);
            else if (
                arg.Equals("--jobs", StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length
                && int.TryParse(
                    args[++i],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsedJobs
                )
            )
            {
                _ = parsedJobs; // legacy option: parallelism was for old harness generation
            }
            else if (
                arg.Equals("--name-contains", StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length
            )
                cfg.NameContains = SplitCsv(args[++i]);
            else if (
                arg.Equals("--name-excludes", StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length
            )
                cfg.NameExcludes = SplitCsv(args[++i]);
            else if (arg.Equals("--vt", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                cfg.Vt = SplitCsv(args[++i]).Select(s => s.ToUpperInvariant()).ToList();
            else if (
                arg.Equals("--class", StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length
            )
                cfg.Classes = SplitCsv(args[++i]).Select(s => s.ToLowerInvariant()).ToList();
            else if (arg.Equals("--vdd", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                cfg.Vdd = SplitCsv(args[++i]);
            else if (arg.Equals("--infra", StringComparison.OrdinalIgnoreCase))
                cfg.Infra = true;
            else if (arg.Equals("--no-infra", StringComparison.OrdinalIgnoreCase))
                cfg.Infra = false;
            else if (
                arg.Equals("--out-root", StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length
            )
                outRoot = args[++i];
        }

        var dbPath = Path.Combine(
            WorkspaceState.GetWorkspaceFolder(_state.WorkspaceRoot),
            "pdk.db"
        );
        if (!File.Exists(dbPath))
        {
            Output.WriteLine("No PDK database found. Run 'pdk scan' first.");
            return CommandResult.Failure;
        }

        if (!backend.Equals("ngspice", StringComparison.OrdinalIgnoreCase))
        {
            Output.WriteLine(
                $"[warn] Backend '{backend}' is not supported by the declarative characterization flow yet; using ngspice."
            );
            backend = "ngspice";
        }

        var pdkNameSource = _state.PdkRoot ?? _state.WorkspaceRoot;
        var pdkName = Path.GetFileName(Path.GetFullPath(pdkNameSource));
        if (string.IsNullOrWhiteSpace(pdkName))
        {
            Output.WriteLine("Unable to determine active PDK name.");
            return CommandResult.Failure;
        }

        if (
            !PdkPrimitiveLibraryLayout.TryValidateLibrary(
                Directory.GetCurrentDirectory(),
                pdkName,
                out _,
                out var libraryError
            )
        )
        {
            Output.WriteLine(libraryError);
            return CommandResult.Failure;
        }

        var filters = BuildDeviceFilterOptions(cfg);
        var options = DeviceCharPlannerOptions.Create(backend, corner, limit, filters);

        IReadOnlyList<DeviceCharPlan> plans;
        try
        {
            plans = Cascode.Workspace.DeviceCharPlanner.Plan(dbPath, options);
        }
        catch (Exception ex)
        {
            Output.WriteLine($"Failed to build characterization plan: {ex.Message}");
            return CommandResult.Failure;
        }

        if (plans.Count == 0)
        {
            // Provide diagnostics to help users recover
            var allDevices = Cascode.Workspace.PdkDatabaseReader.LoadDevices(dbPath);
            if (allDevices.Count == 0)
            {
                Output.WriteLine("No devices discovered. Run 'pdk scan' first.");
                return CommandResult.Failure;
            }

            HashSet<string>? matchedKeys = null;
            if (filters.Matched.HasValue)
            {
                matchedKeys = Cascode.Workspace.PdkDatabaseReader.LoadMatchedDeviceKeys(dbPath);
            }

            var filteredDevices = allDevices
                .Where(d => DeviceFilterEvaluator.Matches(d, filters, matchedKeys))
                .ToList();
            if (filteredDevices.Count == 0)
            {
                Output.WriteLine("No devices matched the selected filters.");
                return CommandResult.Failure;
            }

            var bestMatches = Cascode.Workspace.PdkDatabaseReader.LoadBestMatchByDevice(dbPath);
            var matchedFiltered = filteredDevices
                .Where(d => bestMatches.ContainsKey(d.CanonicalName))
                .ToList();
            if (matchedFiltered.Count == 0)
            {
                Output.WriteLine(
                    $"Filtered devices: {filteredDevices.Count}. None have matched models; rerun 'pdk scan' or adjust matching."
                );
                return CommandResult.Failure;
            }

            Output.WriteLine("No devices matched the selection.");
            return CommandResult.Success;
        }

        var modelCount = plans
            .Select(p => p.ModelName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        // Start progress
        _state.StartCharJob(plans.Count, backend, corner);
        Output.WriteLine(
            $"Starting characterization batch → backend={backend}, corner={corner}, devices={plans.Count}, models={modelCount}"
        );

        bool RunBatch()
        {
            var oldCorner = Environment.GetEnvironmentVariable("CASCODE_PDK_CORNER");
            Environment.SetEnvironmentVariable("CASCODE_PDK_CORNER", corner);

            ILoggerFactory? localFactory = null;
            var loggerFactory =
                _state.LoggerFactory
                ?? (
                    localFactory = LoggerFactory.Create(builder =>
                    {
                        builder.SetMinimumLevel(LogLevel.Warning);
                        builder.AddSimpleConsole(o =>
                        {
                            o.SingleLine = true;
                        });
                    })
                );

            var ran = 0;
            var exported = 0;
            var skipped = 0;
            var completed = false;
            var fatalFailure = false;

            try
            {
                foreach (var plan in plans)
                {
                    _state.UpdateCharProgress(plan.DeviceName);
                    var jobDir = Path.Combine(
                        outRoot,
                        backend.ToLowerInvariant(),
                        string.IsNullOrWhiteSpace(corner) ? "default" : corner,
                        Sanitize(plan.DeviceName),
                        DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture)
                    );
                    Directory.CreateDirectory(jobDir);

                    var gen = CharGenService.GenerateAndRun(
                        Directory.GetCurrentDirectory(),
                        _state.PdkRoot ?? _state.WorkspaceRoot,
                        new CharGenService.CharGenArgs(
                            ModelQuery: plan.ModelName,
                            OutputDir: jobDir,
                            Corner: corner,
                            Backend: backend,
                            DeviceName: plan.DeviceName,
                            WidthM: plan.Width,
                            LengthM: plan.Length,
                            Mult: 1,
                            Nf: plan.Nf,
                            VdsV: plan.Vds,
                            VsbV: plan.Vsb,
                            VgsStartV: 0.0,
                            VgsStopV: plan.VgsStop,
                            VgsStepV: 0.01
                        ),
                        loggerFactory,
                        Output
                    );
                    if (!gen.Succeeded)
                    {
                        if (
                            gen.Message.Contains(
                                "No parametric primitive is available",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            Output.WriteLine($"[error] {plan.DeviceName}: {gen.Message}");
                            fatalFailure = true;
                            break;
                        }

                        Output.WriteLine($"[warn] {plan.DeviceName}: {gen.Message}");
                        skipped++;
                        _state.UpdateCharProgress(plan.DeviceName, skippedDelta: 1);
                        continue;
                    }

                    ran++;
                    _state.UpdateCharProgress(plan.DeviceName, generatedDelta: 1, ranDelta: 1);

                    var exportOk = Services.CharExportService.ExportDerived(
                        jobDir,
                        metricFilter: null,
                        out _,
                        out var exportMsg
                    );
                    Output.WriteLine(exportMsg);
                    if (!exportOk)
                    {
                        skipped++;
                        _state.UpdateCharProgress(plan.DeviceName, skippedDelta: 1);
                        continue;
                    }

                    exported++;
                    _state.UpdateCharProgress(plan.DeviceName, exportedDelta: 1);

                    try
                    {
                        Cascode.Workspace.CharLutWriter.ImportFromJobDir(dbPath, jobDir);
                        Output.WriteLine($"LUT stored in database for {plan.DeviceName}.");
                    }
                    catch (Exception lutEx)
                    {
                        Output.WriteLine($"[warn] Failed to store LUT: {lutEx.Message}");
                    }
                }

                Output.WriteLine(
                    $"Characterization batch complete: ran {ran}, exported {exported}, skipped {skipped}."
                );
                completed = !fatalFailure;
            }
            catch (Exception ex)
            {
                Output.WriteLine($"Characterization batch failed: {ex.Message}");
            }
            finally
            {
                if (!completed)
                {
                    Output.WriteLine("Characterization batch terminated early.");
                }

                _state.CompleteCharJob();
                Environment.SetEnvironmentVariable("CASCODE_PDK_CORNER", oldCorner);
                localFactory?.Dispose();
            }

            return completed;
        }

        if (_isInteractive())
        {
            Task.Run(RunBatch);
            Output.WriteLine(
                "Batch running in background; progress will update while the CLI remains responsive."
            );
            return CommandResult.Success;
        }

        var batchSucceeded = RunBatch();
        return batchSucceeded ? CommandResult.Success : CommandResult.Failure;
    }

    private static DeviceFilterOptions BuildDeviceFilterOptions(CharRunConfig cfg)
    {
        var vddNormalized = new List<string>();
        foreach (var tok in cfg.Vdd ?? new List<string>())
        {
            if (DeviceFilterEvaluator.TryNormalizeVddFilter(tok, out var normalized))
                vddNormalized.Add(normalized);
            else
                vddNormalized.Add(tok.ToLowerInvariant());
        }

        return new DeviceFilterOptions(
            cfg.Classes ?? new List<string>(),
            cfg.Vt ?? new List<string>(),
            vddNormalized,
            cfg.Infra,
            matched: null,
            cfg.NameContains ?? new List<string>(),
            cfg.NameExcludes ?? new List<string>()
        );
    }

    private CommandResult PdkCharReadCommand(string[] args)
    {
        if (args.Length == 0)
        {
            Output.WriteLine(
                "Usage: pdk char read <model> [--corner <name>] [--backend ngspice] [--head <n>] [--job <path>]"
            );
            return CommandResult.Success;
        }

        var query = args[0];
        var backend = "ngspice";
        var corner = "tt";
        int head = 24;
        string? jobOverride = null;
        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--backend", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                backend = args[++i];
            else if (
                arg.Equals("--corner", StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length
            )
                corner = args[++i];
            else if (
                arg.Equals("--head", StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length
                && int.TryParse(
                    args[++i],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsed
                )
            )
                head = Math.Max(1, parsed);
            else if (arg.Equals("--job", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                jobOverride = PathUtils.NormalizePath(args[++i]);
        }

        var dbPath = Path.Combine(
            WorkspaceState.GetWorkspaceFolder(_state.WorkspaceRoot),
            "pdk.db"
        );
        if (!File.Exists(dbPath))
        {
            Output.WriteLine("No PDK database found. Run 'pdk scan' first.");
            return CommandResult.Failure;
        }

        string jobDir;
        if (!string.IsNullOrEmpty(jobOverride))
        {
            jobDir = jobOverride;
        }
        else
        {
            CharRunRecord? run = null;
            var deviceRuns = Cascode.Workspace.CharLutReader.GetRunsForDevice(
                dbPath,
                query,
                corner
            );
            if (deviceRuns.Count > 0)
                run = deviceRuns[0];
            if (run is null)
            {
                var modelRuns = Cascode.Workspace.CharLutReader.GetRunsForModel(
                    dbPath,
                    query,
                    corner
                );
                if (modelRuns.Count > 0)
                    run = modelRuns[0];
            }

            if (run is null)
            {
                Output.WriteLine($"No characterization recorded for '{query}'.");
                return CommandResult.Failure;
            }

            jobDir = run.JobDir;
        }

        if (!Directory.Exists(jobDir))
        {
            Output.WriteLine($"Job directory not found: {jobDir}");
            return CommandResult.Failure;
        }

        var derivedPath = Path.Combine(jobDir, "derived.csv");
        if (!File.Exists(derivedPath))
        {
            Output.WriteLine(
                $"Derived metrics not found at {derivedPath}. Run 'char export {jobDir}' first."
            );
            return CommandResult.Failure;
        }

        var (headers, samples) = Services.CharIoHelpers.LoadDerivedCsv(derivedPath);
        if (headers.Count == 0 || samples.Count == 0)
        {
            Output.WriteLine("Derived CSV did not contain numeric samples.");
            return CommandResult.Failure;
        }

        var (controlIdx, controlName) = Services.CharIoHelpers.FindColumn(headers, "vgs", "vsg");
        var (idIdx, _) = Services.CharIoHelpers.FindColumn(headers, "id");
        var (gmIdx, _) = Services.CharIoHelpers.FindColumn(headers, "gm");
        var (gmIdIdx, _) = Services.CharIoHelpers.FindColumn(headers, "gm_over_id");
        var (vthIdx, _) = Services.CharIoHelpers.FindColumn(headers, "vth");
        var (gmPerWIdx, _) = Services.CharIoHelpers.FindColumn(headers, "gm_per_w");
        var (idPerWIdx, _) = Services.CharIoHelpers.FindColumn(headers, "id_per_w");
        var (vstarIdx, _) = Services.CharIoHelpers.FindColumn(headers, "vstar");
        var (roIdx, _) = Services.CharIoHelpers.FindColumn(headers, "ro");
        var (gmRoIdx, _) = Services.CharIoHelpers.FindColumn(headers, "gm_ro");
        var (ftIdx, _) = Services.CharIoHelpers.FindColumn(headers, "ft");

        var preview = Math.Min(head, samples.Count);

        // Build headers
        var displayHeaders = new List<string> { "#", controlName.ToUpperInvariant(), "Id" };
        if (gmIdx >= 0)
            displayHeaders.Add("gm");
        if (gmIdIdx >= 0)
            displayHeaders.Add("gm/Id");
        if (gmPerWIdx >= 0)
            displayHeaders.Add("gm/W");
        if (idPerWIdx >= 0)
            displayHeaders.Add("Id/W");
        if (vstarIdx >= 0)
            displayHeaders.Add("Vov");
        if (roIdx >= 0)
            displayHeaders.Add("ro");
        if (gmRoIdx >= 0)
            displayHeaders.Add("gm·ro");
        if (ftIdx >= 0)
            displayHeaders.Add("fT");
        if (vthIdx >= 0)
            displayHeaders.Add("Vth");

        // Build rows
        static double sampleSafe(IReadOnlyList<double> data, int idx) =>
            idx >= 0 && idx < data.Count ? data[idx] : double.NaN;
        var displayRows = new List<List<string>>();

        for (var i = 0; i < preview; i++)
        {
            var sample = samples[i];
            var row = new List<string>
            {
                (i + 1).ToString(CultureInfo.InvariantCulture),
                Services.CharIoHelpers.FormatNumber(sampleSafe(sample, controlIdx)),
                Services.CharIoHelpers.FormatNumber(sampleSafe(sample, idIdx)),
            };
            if (gmIdx >= 0)
                row.Add(Services.CharIoHelpers.FormatNumber(sampleSafe(sample, gmIdx)));
            if (gmIdIdx >= 0)
                row.Add(Services.CharIoHelpers.FormatNumber(sampleSafe(sample, gmIdIdx)));
            if (gmPerWIdx >= 0)
                row.Add(Services.CharIoHelpers.FormatNumber(sampleSafe(sample, gmPerWIdx)));
            if (idPerWIdx >= 0)
                row.Add(Services.CharIoHelpers.FormatNumber(sampleSafe(sample, idPerWIdx)));
            if (vstarIdx >= 0)
                row.Add(Services.CharIoHelpers.FormatNumber(sampleSafe(sample, vstarIdx)));
            if (roIdx >= 0)
                row.Add(Services.CharIoHelpers.FormatNumber(sampleSafe(sample, roIdx)));
            if (gmRoIdx >= 0)
                row.Add(Services.CharIoHelpers.FormatNumber(sampleSafe(sample, gmRoIdx)));
            if (ftIdx >= 0)
                row.Add(Services.CharIoHelpers.FormatNumber(sampleSafe(sample, ftIdx)));
            if (vthIdx >= 0)
                row.Add(Services.CharIoHelpers.FormatNumber(sampleSafe(sample, vthIdx)));
            displayRows.Add(row);
        }

        // Build sparklines
        var sparklines = new Dictionary<string, IReadOnlyList<double>>();
        List<double> ExtractCol(int idx) => samples.Select(s => sampleSafe(s, idx)).ToList();

        if (gmIdIdx >= 0)
            sparklines["gm/Id"] = ExtractCol(gmIdIdx);
        if (idIdx >= 0)
            sparklines["Id"] = ExtractCol(idIdx);
        if (gmPerWIdx >= 0)
            sparklines["gm/W"] = ExtractCol(gmPerWIdx);
        if (idPerWIdx >= 0)
            sparklines["Id/W"] = ExtractCol(idPerWIdx);
        if (vstarIdx >= 0)
            sparklines["Vov"] = ExtractCol(vstarIdx);
        if (roIdx >= 0)
            sparklines["ro"] = ExtractCol(roIdx);
        if (gmRoIdx >= 0)
            sparklines["gm·ro"] = ExtractCol(gmRoIdx);
        if (ftIdx >= 0)
            sparklines["fT"] = ExtractCol(ftIdx);

        if (controlIdx >= 0 && vthIdx >= 0)
        {
            sparklines["Vov (VGS-Vth)"] = samples
                .Select(s => sampleSafe(s, controlIdx) - sampleSafe(s, vthIdx))
                .ToList();
        }

        if (_isInteractive())
        {
            var rowsReadOnly = displayRows.Select(r => (IReadOnlyList<string>)r).ToList();
            var view = new CharReadViewState(
                query,
                $"{backend} / {corner}",
                displayHeaders,
                rowsReadOnly,
                sparklines,
                derivedPath
            );
            _state.ShowCharRead(view);
            Output.WriteLine($"Showing characterization for {query}");
            return CommandResult.Success;
        }

        // Non-interactive fallthrough
        var table = new Table().Border(TableBorder.SimpleHeavy);
        foreach (var h in displayHeaders)
            table.AddColumn(h);
        foreach (var r in displayRows)
            table.AddRow(r.ToArray());

        WriteRenderable(
            new Rule($"[bold]{query}[/] — {backend} / {corner}") { Justification = Justify.Left }
        );
        WriteRenderable(table);

        foreach (var kvp in sparklines)
        {
            WriteRenderable(ShellRenderer.BuildSparkline(kvp.Key, kvp.Value));
            Output.WriteLine(string.Empty);
        }

        Output.WriteLine($"Derived source: {derivedPath}");
        return CommandResult.Success;
    }

    private CommandResult PdkCharStatusCommand(string[] args)
    {
        var dbPath = Path.Combine(
            WorkspaceState.GetWorkspaceFolder(_state.WorkspaceRoot),
            "pdk.db"
        );
        if (!File.Exists(dbPath))
        {
            Output.WriteLine("PDK database not found. Run 'pdk scan' first.");
            return CommandResult.Failure;
        }

        try
        {
            var coverage = Cascode.Workspace.CharLutReader.GetDeviceCoverage(dbPath);
            if (coverage.Devices.Count == 0)
            {
                Output.WriteLine("No devices discovered. Run 'pdk scan' first.");
                return CommandResult.Success;
            }

            if (coverage.TotalRuns == 0)
            {
                Output.WriteLine(
                    "No characterization runs found. Use 'pdk char run' to characterize devices."
                );
                return CommandResult.Success;
            }

            var classes = coverage
                .DeviceClasses.Values.Distinct()
                .OrderBy(c => c.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();
            var totalsByClass = coverage
                .DeviceClasses.GroupBy(kvp => kvp.Value)
                .ToDictionary(g => g.Key, g => g.Count());

            var table = new Table()
                .Border(TableBorder.Rounded)
                .Title("[bold]Device Characterization Coverage[/]");
            table.AddColumn("Class");
            table.AddColumn("Total");
            foreach (var corner in coverage.Corners)
            {
                table.AddColumn(new TableColumn(corner.ToUpperInvariant()).Centered());
            }

            foreach (var cls in classes)
            {
                var total = totalsByClass.TryGetValue(cls, out var t) ? t : 0;
                var row = new List<string>
                {
                    cls.ToString(),
                    total.ToString(CultureInfo.InvariantCulture),
                };
                foreach (var corner in coverage.Corners)
                {
                    var covered = coverage.Devices.Count(d =>
                        coverage.GetDeviceClass(d) == cls && coverage.HasRun(d, corner)
                    );
                    row.Add($"{covered}/{total}");
                }
                table.AddRow(row.ToArray());
            }

            WriteRenderable(table);

            var totalPossible = coverage.Devices.Count * Math.Max(1, coverage.Corners.Count);
            var percentage = totalPossible > 0 ? (coverage.TotalRuns * 100.0 / totalPossible) : 0.0;
            Output.WriteLine(
                $"Device coverage: {coverage.TotalRuns}/{totalPossible} ({percentage:F1}%)"
            );
            return CommandResult.Success;
        }
        catch (Exception ex)
        {
            Output.WriteLine($"Failed to load characterization status: {ex.Message}");
            return CommandResult.Failure;
        }
    }

    /// <summary>
    /// Normalize a string for use as a filename by replacing all characters invalid in file names with underscores.
    /// </summary>
    /// <param name="name">The input string to sanitize.</param>
    /// <returns>The input string with every character that is invalid in file names replaced by '_' (underscore).</returns>
    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    /// <summary>
    /// Split a comma-separated string into trimmed, non-empty tokens.
    /// </summary>
    /// <param name="value">The comma-separated input string to split.</param>
    /// <returns>A list of trimmed tokens; an empty list if <paramref name="value"/> is null, empty, or consists only of whitespace.</returns>
    private static List<string> SplitCsv(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? new List<string>()
            : value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

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
