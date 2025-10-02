using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Rendering;
using Cascode.Workspace;
using Cascode.Cli.Services;

namespace Cascode.Cli.Commands;

internal sealed class PdkCommandModule : ICommandModule
{
    private readonly ShellState _state;
    private readonly WorkspaceScanner _scanner;
    private readonly WorkspaceScanStorage _storage;
    private readonly CliConfig _config;
    private readonly CliConfigStorage _configStorage;
    private readonly string _initialWorkspaceRoot;
    private readonly Func<bool> _isInteractive;
    private CommandRegistry? _registry;

    public PdkCommandModule(
        ShellState state,
        WorkspaceScanner scanner,
        WorkspaceScanStorage storage,
        CliConfig config,
        CliConfigStorage configStorage,
        string initialWorkspaceRoot,
        Func<bool> isInteractive)
    {
        _state = state;
        _scanner = scanner;
        _storage = storage;
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
        registry.Register(new DelegateCliCommand("pdk models", "List discovered decks", PdkModels));
        registry.Register(new DelegateCliCommand("pdk model", "Inspect a specific deck", PdkModel));
        registry.Register(new DelegateCliCommand("pdk set-dir", "Set or clear the default PDK workspace", PdkSetDir));

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
        _state.AddMessage($"Scanning workspace {targetRoot}");

        var result = _scanner.Scan(targetRoot);
        _state.Scan = result;
        _state.SelectedDeckIndex = result.ModelDecks.Count > 0 ? 0 : null;

        var scanPath = WorkspaceState.GetScanPath(targetRoot);
        _storage.Save(result, scanPath);

        _state.AddMessage($"Found {result.Libraries.Count} libraries, {result.ModelDecks.Count} model decks.");
        foreach (var warning in result.Warnings) _state.AddMessage($"Warning: {warning}");
        return CommandResult.Success;
    }

    private CommandResult PdkModels(string[] args)
    {
        var scan = EnsureScan();
        if (scan is null) return CommandResult.Failure;

        var models = scan.Models;
        if (models.Count == 0)
        {
            var emptyLine = "No models discovered. Run pdk scan.";
            _state.AddMessage(emptyLine);

            if (_isInteractive())
            {
                var emptyView = new ModelSummaryViewState(
                    "Model Catalog",
                    emptyLine,
                    string.Empty,
                    ModelSummaryHelpers.BuildModelSuggestionText(),
                    Array.Empty<ModelSummaryRow>(),
                    Array.Empty<ModelClassSummaryRow>());
                _state.ShowModelSummary(emptyView);
            }
            return CommandResult.Success;
        }

        var filters = new HashSet<SpectreModelDeviceClass>();
        var limit = 0;
        ParseModelArguments(args, filters, Math.Max(1, models.Count), ref limit);

        var categorizedClassCount = models
            .Where(model => model.DeviceClass != SpectreModelDeviceClass.Unknown)
            .Select(model => model.DeviceClass)
            .Distinct()
            .Count();

        if (filters.Count == 0) return RenderClassSummary(models, categorizedClassCount, filters, limit);
        return RenderDetailSummary(models, filters, limit);
    }

    private CommandResult RenderClassSummary(
        IReadOnlyList<SpectreModel> models,
        int categorizedClassCount,
        HashSet<SpectreModelDeviceClass> filters,
        int parsedLimit)
    {
        var maxClassCount = Math.Max(1, categorizedClassCount);

        var limit = parsedLimit;
        if (limit <= 0) limit = _isInteractive() ? Math.Min(8, maxClassCount) : maxClassCount;
        limit = Math.Clamp(limit, 1, maxClassCount);

        var uncategorizedList = models.Where(m => m.DeviceClass == SpectreModelDeviceClass.Unknown).ToList();
        var includeUncategorized = uncategorizedList.Count > 0;

        var categorizedGroups = models
            .Where(m => m.DeviceClass != SpectreModelDeviceClass.Unknown)
            .GroupBy(m => m.DeviceClass)
            .Select(g => (Class: g.Key, Models: g.ToList()))
            .OrderByDescending(e => e.Models.Count)
            .ThenBy(e => ModelSummaryHelpers.FormatDeviceClassName(e.Class), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var matchingClassCount = categorizedGroups.Count + (includeUncategorized ? 1 : 0);
        var matchingModelCount = categorizedGroups.Sum(e => e.Models.Count) + uncategorizedList.Count;

        var limitedGroups = categorizedGroups.Take(limit).ToList();
        var displayedClassCount = limitedGroups.Count + (includeUncategorized ? 1 : 0);
        var displayedModelCount = limitedGroups.Sum(e => e.Models.Count) + (includeUncategorized ? uncategorizedList.Count : 0);

        var classRows = new List<ModelClassSummaryRow>(displayedClassCount);
        foreach (var entry in limitedGroups)
        {
            classRows.Add(ModelSummaryHelpers.CreateClassSummaryRow(entry.Class, entry.Models, isUncategorized: false));
        }
        if (includeUncategorized) classRows.Add(ModelSummaryHelpers.CreateClassSummaryRow(SpectreModelDeviceClass.Unknown, uncategorizedList, isUncategorized: true));

        var title = ModelSummaryHelpers.BuildModelSummaryTitle(filters);
        var summaryLine = ModelSummaryHelpers.BuildClassSummaryLine(
            displayedClassCount,
            scopedClassCount: matchingClassCount,
            displayedModelCount,
            scopedModelCount: matchingModelCount,
            totalModelCount: models.Count,
            filters,
            limited: limitedGroups.Count < categorizedGroups.Count,
            includeUncategorized: includeUncategorized);
        var statsLine = ModelSummaryHelpers.BuildClassStatsLine(categorizedGroups.Select(e => (e.Class, e.Models.Count)), uncategorizedList);
        var suggestionLine = ModelSummaryHelpers.BuildModelSuggestionText();

        var view = new ModelSummaryViewState(title, summaryLine, statsLine, suggestionLine, Array.Empty<ModelSummaryRow>(), classRows, detailOffset: 0, detailPageSize: 0, detailFilters: Array.Empty<string>());

        if (_isInteractive())
        {
            _state.ShowModelSummary(view);
        }
        else
        {
            var table = ShellRenderer.CreateModelClassSummaryTable(classRows);
            AnsiConsole.Write(table);
            if (!string.IsNullOrWhiteSpace(summaryLine)) AnsiConsole.MarkupLine(Markup.Escape(summaryLine));
            if (!string.IsNullOrWhiteSpace(statsLine)) AnsiConsole.MarkupLine(Markup.Escape(statsLine));
            if (!string.IsNullOrWhiteSpace(suggestionLine)) AnsiConsole.MarkupLine(Markup.Escape(suggestionLine));
        }

        _state.AddMessage(summaryLine);
        if (!string.IsNullOrWhiteSpace(statsLine)) _state.AddMessage(statsLine);
        if (!string.IsNullOrWhiteSpace(suggestionLine)) _state.AddMessage(suggestionLine);

        if (categorizedGroups.Count == 0)
        {
            _state.AddMessage(includeUncategorized ? "No categorized classes to display. Showing uncategorized models for review." : "No categorized classes to display. Adjust filters or scan again.");
        }
        else if (limitedGroups.Count < categorizedGroups.Count)
        {
            _state.AddMessage($"Showing top {limitedGroups.Count} of {categorizedGroups.Count} categorized classes. Use --limit for more.");
        }
        else
        {
            _state.AddMessage("Displayed all categorized classes in scope.");
        }
        if (includeUncategorized && uncategorizedList.Count > 0) _state.AddMessage($"Uncategorized models: {uncategorizedList.Count}. Run 'pdk match' to classify them.");
        return CommandResult.Success;
    }

    private CommandResult RenderDetailSummary(IReadOnlyList<SpectreModel> models, HashSet<SpectreModelDeviceClass> filters, int parsedLimit)
    {
        var filtered = models.Where(m => filters.Contains(m.DeviceClass)).ToList();
        if (filtered.Count == 0)
        {
            var title = ModelSummaryHelpers.BuildModelSummaryTitle(filters);
            var message = "No models matched the selected device filters.";
            _state.ShowModelSummary(new ModelSummaryViewState(title, message, string.Empty, ModelSummaryHelpers.BuildModelSuggestionText(), Array.Empty<ModelSummaryRow>(), Array.Empty<ModelClassSummaryRow>()));
            _state.AddMessage(message);
            return CommandResult.Success;
        }

        var filterLabels = filters.Select(ModelSummaryHelpers.FormatDeviceClassName).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
        var pageSize = parsedLimit > 0 ? Math.Clamp(parsedLimit, 1, filtered.Count) : (_isInteractive() ? Math.Min(20, filtered.Count) : filtered.Count);
        var rows = new List<ModelSummaryRow>(filtered.Count);
        for (var i = 0; i < filtered.Count; i++) rows.Add(ModelSummaryHelpers.CreateModelSummaryRow(filtered[i], i + 1));

        var offset = 0;
        var summaryLine = ModelSummaryHelpers.BuildDetailSummaryLine(filterLabels, offset, pageSize, filtered.Count);
        var statsLine = ModelSummaryHelpers.BuildDetailStatsLine(filtered);
        var suggestionLine = ModelSummaryHelpers.BuildModelSuggestionText();
        var viewTitle = ModelSummaryHelpers.BuildModelSummaryTitle(filters);
        var view = new ModelSummaryViewState(viewTitle, summaryLine, statsLine, suggestionLine, rows, Array.Empty<ModelClassSummaryRow>(), detailOffset: offset, detailPageSize: pageSize, detailFilters: filterLabels);

        if (_isInteractive())
        {
            _state.ShowModelSummary(view);
        }
        else
        {
            var table = ShellRenderer.CreateModelDetailTable(view);
            AnsiConsole.Write(table);
            if (!string.IsNullOrWhiteSpace(summaryLine)) AnsiConsole.MarkupLine(Markup.Escape(summaryLine));
            if (!string.IsNullOrWhiteSpace(statsLine)) AnsiConsole.MarkupLine(Markup.Escape(statsLine));
            if (!string.IsNullOrWhiteSpace(suggestionLine)) AnsiConsole.MarkupLine(Markup.Escape(suggestionLine));
        }

        _state.AddMessage(summaryLine);
        if (!string.IsNullOrWhiteSpace(statsLine)) _state.AddMessage(statsLine);
        if (!string.IsNullOrWhiteSpace(suggestionLine)) _state.AddMessage(suggestionLine);
        return CommandResult.Success;
    }

    private CommandResult PdkModel(string[] args)
    {
        var scan = EnsureScan();
        if (scan is null) return CommandResult.Failure;
        if (scan.Models.Count == 0)
        {
            _state.AddMessage("No models discovered. Run pdk scan.");
            return CommandResult.Success;
        }
        if (args.Length == 0)
        {
            _state.AddMessage("Usage: pdk model <index|name>");
            return CommandResult.Success;
        }

        SpectreModel? model = null;
        var models = scan.Models;
        if (int.TryParse(args[0], out var parsedIndex))
        {
            parsedIndex -= 1;
            if (parsedIndex >= 0 && parsedIndex < models.Count) model = models[parsedIndex];
        }
        else
        {
            model = models.FirstOrDefault(m => m.Name.Contains(args[0], StringComparison.OrdinalIgnoreCase));
        }
        if (model is null)
        {
            _state.AddMessage("Model not found.");
            return CommandResult.Failure;
        }

        var detail = new Table().AddColumn("Field").AddColumn("Value").Border(TableBorder.Rounded);
        detail.AddRow("Name", model.Name);
        detail.AddRow("Model Type", string.IsNullOrWhiteSpace(model.ModelType) ? "-" : model.ModelType);
        detail.AddRow("Class", model.DeviceClass == SpectreModelDeviceClass.Unknown ? "Unknown" : model.DeviceClass.ToString());
        detail.AddRow("Threshold", string.IsNullOrWhiteSpace(model.ThresholdFlavor) ? "-" : model.ThresholdFlavor!);
        detail.AddRow("Voltage", string.IsNullOrWhiteSpace(model.VoltageDomain) ? "-" : model.VoltageDomain!);
        detail.AddRow("Corners", FormatList(model.Corners));
        detail.AddRow("Corner Details", FormatList(model.CornerDetails));
        detail.AddRow("Sections", FormatList(model.Sections));
        detail.AddRow("Decks", FormatList(model.Decks.Select(d => Path.GetFileName(d) ?? d)));
        detail.AddRow("Sources", FormatList(model.SourceFiles));
        AnsiConsole.Write(detail);
        return CommandResult.Success;
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
            TryLoadCachedScan(resolved, logFailure: true);
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

        var scan = EnsureScan();
        if (scan is null) return CommandResult.Failure;
        var models = scan.Models;
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
                    SpectreModelDeviceClass.Nmos => set.Contains("nmos") || set.Contains("nfet") || set.Contains("nch"),
                    SpectreModelDeviceClass.Pmos => set.Contains("pmos") || set.Contains("pfet") || set.Contains("pch"),
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

    private WorkspaceScanResult? EnsureScan()
    {
        if (_state.Scan is not null) return _state.Scan;
        var scanPath = WorkspaceState.GetScanPath(_state.WorkspaceRoot);
        if (File.Exists(scanPath))
        {
            try
            {
                _state.Scan = _storage.Load(scanPath);
                _state.SelectedDeckIndex = _state.Scan.ModelDecks.Count > 0 ? 0 : null;
                return _state.Scan;
            }
            catch (Exception ex)
            {
                _state.AddMessage($"Failed to load cached scan: {ex.Message}");
            }
        }
        _state.AddMessage("No workspace scan available. Run pdk scan.");
        return null;
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

            var spec = new Cascode.Bench.TestbenchSpec
            {
                Backend = backend.Equals("spectre", StringComparison.OrdinalIgnoreCase) ? Cascode.Bench.BenchBackendType.Spectre : Cascode.Bench.BenchBackendType.Ngspice,
                Name = harnessId,
                ModelName = netlistModelName,
                IsSubckt = string.Equals(model.ModelType, "subckt", StringComparison.OrdinalIgnoreCase),
                Corner = corner,
                TemperatureC = 27,
                SupplyV = 0,
                W_M = 1e-6,
                L_M = 0.18e-6,
                Mult = 1,
                Nfingers = 1,
                Vgs = new Cascode.Bench.SweepSpec(0, 1.2, 0.01),
                Vds = 0.9,
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
            SpectreModelDeviceClass.Nmos => "gm_id.v1",
            SpectreModelDeviceClass.Pmos => "gm_id_pmos.v1",
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
        var cmd = $"\"{exe}\" -format nutascii results.sp\n";
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

    private void TryLoadCachedScan(string workspaceRoot, bool logFailure)
    {
        var scanPath = WorkspaceState.GetScanPath(workspaceRoot);
        if (!File.Exists(scanPath)) return;
        try
        {
            var scan = _storage.Load(scanPath);
            _state.Scan = scan;
            _state.SelectedDeckIndex = scan.ModelDecks.Count > 0 ? 0 : null;
        }
        catch (Exception ex)
        {
            if (logFailure) _state.AddMessage($"Failed to load cached scan: {ex.Message}");
        }
    }

    private static void ParseModelArguments(string[] args, HashSet<SpectreModelDeviceClass> filters, int totalCount, ref int limit)
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
    }

    private static IEnumerable<string> ExpandFilterToken(string token)
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
    }

    private static bool TryResolveDeviceClass(string token, out SpectreModelDeviceClass deviceClass)
    {
        deviceClass = SpectreModelDeviceClass.Unknown;
        if (string.IsNullOrWhiteSpace(token)) return false;
        var normalized = token.Trim().Trim('/').ToLowerInvariant();
        switch (normalized)
        {
            case "nmos" or "nfet" or "nch": deviceClass = SpectreModelDeviceClass.Nmos; return true;
            case "pmos" or "pfet" or "pch": deviceClass = SpectreModelDeviceClass.Pmos; return true;
            case "cap" or "caps" or "capacitor" or "capacitors": deviceClass = SpectreModelDeviceClass.Capacitor; return true;
            case "res" or "resistor" or "resistors": deviceClass = SpectreModelDeviceClass.Resistor; return true;
            case "diode" or "diodes": deviceClass = SpectreModelDeviceClass.Diode; return true;
            case "bjt" or "bipolar": deviceClass = SpectreModelDeviceClass.Bipolar; return true;
            case "moscap": deviceClass = SpectreModelDeviceClass.Moscap; return true;
            case "ind" or "inductor" or "inductors": deviceClass = SpectreModelDeviceClass.Inductor; return true;
            case "tline" or "tl" or "transmissionline": deviceClass = SpectreModelDeviceClass.TransmissionLine; return true;
            case "other": deviceClass = SpectreModelDeviceClass.Other; return true;
            case "unknown" or "uncat" or "uncategorized" or "unmatched": deviceClass = SpectreModelDeviceClass.Unknown; return true;
            default: return false;
        }
    }
}
