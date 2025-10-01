using Cascode.Workspace;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Spectre.Console.Rendering;
using System.IO;
using System.Linq;
using System.Text;

namespace Cascode.Cli;

internal sealed class CascodeShell
{
    private readonly WorkspaceScanner _scanner = new();
    private readonly WorkspaceScanStorage _storage = new();
    private readonly CliConfigStorage _configStorage = new();
    private readonly CommandRegistry _commands = new();
    private readonly CliConfig _config;
    private readonly string _initialWorkspaceRoot;
    private readonly ShellState _state;
    private bool _isInteractive;

    public CascodeShell(string workspaceRoot)
    {
        _initialWorkspaceRoot = Path.GetFullPath(workspaceRoot);
        _config = _configStorage.Load();

        var startingRoot = _config.PdkRoot ?? _initialWorkspaceRoot;
        _state = new ShellState(Path.GetFullPath(startingRoot));
        if (_config.PdkRoot is not null)
        {
            _state.UpdatePdkRoot(_config.PdkRoot);
        }

        RegisterCommands();
        TryLoadCachedScan(_state.WorkspaceRoot, logFailure: false);
    }

    private void RegisterCommands()
    {
        _commands.Register("help", "Show this message", ShowHelp, aliases: new[] { "-h", "--help" });
        _commands.Register("version", "Show CLI version", ShowVersion, hidden: true, aliases: new[] { "--version", "-v" });

        _commands.Register("pdk", "Manage PDK workspace", ShowPdkUsage);
        _commands.Register("pdk scan", "Scan workspace for decks", PdkScan);
        _commands.Register("pdk models", "List discovered decks", PdkModels);
        _commands.Register("pdk model", "Inspect a specific deck", PdkModel);
        _commands.Register("pdk set-dir", "Set or clear the default PDK workspace", PdkSetDir);
        _commands.Register("pdk char", "PDK characterization commands", ShowPdkCharUsage);
        _commands.Register("pdk char help", "Show PDK characterization help", ShowPdkCharUsage, hidden: true);
        _commands.Register("pdk char run", "Characterize models (Spectre)", PdkCharRunCommand);
        _commands.Register("pdk char read", "View characterized LUTs", PdkCharReadCommand);
        _commands.Register("pdk char config", "Configure batch characterization", PdkCharConfigCommand);

        _commands.Register("char", "Characterization commands", ShowCharUsage);
        _commands.Register("char gen", "Generate characterization testbench", CharacterizationGenerateCommand);
        _commands.Register("char read", "Read characterization results", CharacterizationReadCommand);
        _commands.Register("char export", "Export derived metrics (e.g., gm/Id)", CharacterizationExportCommand);

        _commands.Register("bench", "Bench and harness commands", ShowBenchUsage);
        _commands.Register("bench harness", "Harness helpers", ShowBenchHarnessUsage);
        _commands.Register("bench harness list", "List available harnesses", BenchHarnessListCommand);
        _commands.Register("bench harness show", "Show harness details", BenchHarnessShowCommand);

        _commands.Register("build", "Compile ADL (preview)", BuildCommand);
        _commands.Register("log", "Scroll the log history", HandleLog, hidden: true);

        _commands.Register("home", "Return to dashboard layout", HomeCommand);

        _commands.Register("quit", "Exit the CLI", Quit, aliases: new[] { "exit" });
    }

    public int RunInteractive()
    {
        _isInteractive = true;
        while (true)
        {
            Render();
            var input = Prompt();
            if (input is null)
            {
                return 0;
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            _state.RecordCommand(input);
            var tokens = Tokenize(input);
            if (tokens.Length == 0)
            {
                continue;
            }

            var result = Execute(tokens);
            if (!result.ExitImmediate && !tokens[0].Equals("log", StringComparison.OrdinalIgnoreCase))
            {
                _state.PinLog();
            }

            if (result.ExitImmediate)
            {
                return result.ExitCode;
            }
        }
    }

    public int RunOnce(string[] tokens)
    {
        _isInteractive = false;
        if (tokens.Length == 0)
        {
            return 0;
        }

        var raw = string.Join(' ', tokens);
        _state.RecordCommand(raw);
        var result = Execute(tokens);
        if (!tokens[0].Equals("log", StringComparison.OrdinalIgnoreCase))
        {
            _state.PinLog();
        }
        FlushLogToConsole();
        return result.ExitCode;
    }

    private CommandResult Execute(string[] tokens)
    {
        if (tokens.Length == 0)
        {
            return CommandResult.Success;
        }

        if (_commands.TryResolve(tokens, out var descriptor, out var args, out var matchedLength) && descriptor is not null)
        {
            return descriptor.Handler(args);
        }

        return UnknownCommand(tokens, matchedLength);
    }

    private CommandResult ShowHelp(string[] args)
    {
        _state.AddMessage("Commands:");

        var commands = _commands.GetCanonicalCommands().ToArray();
        var width = commands.Length == 0 ? 0 : commands.Max(c => c.DisplayPath.Length);

        foreach (var command in commands)
        {
            var padded = width > 0 ? command.DisplayPath.PadRight(width) : command.DisplayPath;
            var description = string.IsNullOrEmpty(command.Description) ? string.Empty : $"  {command.Description}";
            _state.AddMessage($"  {padded}{description}");
        }

        return CommandResult.Success;
    }

    private void ParseModelArguments(string[] args, HashSet<SpectreModelDeviceClass> filters, int totalCount, ref int limit)
    {
        if (args.Length == 0)
        {
            return;
        }

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.IsNullOrWhiteSpace(arg))
            {
                continue;
            }

            if (arg.Equals("--limit", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length &&
                    int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLimit))
                {
                    limit = Math.Clamp(parsedLimit, 1, Math.Max(1, totalCount));
                    i++;
                }
                else
                {
                    _state.AddMessage("Expected integer value after --limit.");
                }

                continue;
            }

            foreach (var token in ExpandFilterToken(arg))
            {
                if (TryResolveDeviceClass(token, out var deviceClass))
                {
                    filters.Add(deviceClass);
                }
                else if (!string.IsNullOrWhiteSpace(token))
                {
                    _state.AddMessage($"Unknown device filter '{token}'.");
                }
            }
        }
    }

    private static IEnumerable<string> ExpandFilterToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            yield break;
        }

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
            if (clean.Length == 0)
            {
                continue;
            }

            yield return clean;
        }
    }

    private static bool TryResolveDeviceClass(string token, out SpectreModelDeviceClass deviceClass)
    {
        deviceClass = SpectreModelDeviceClass.Unknown;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var normalized = token.Trim().Trim('/').ToLowerInvariant();
        switch (normalized)
        {
            case "nmos" or "nfet" or "nch":
                deviceClass = SpectreModelDeviceClass.Nmos;
                return true;
            case "pmos" or "pfet" or "pch":
                deviceClass = SpectreModelDeviceClass.Pmos;
                return true;
            case "cap" or "caps" or "capacitor" or "capacitors":
                deviceClass = SpectreModelDeviceClass.Capacitor;
                return true;
            case "res" or "resistor" or "resistors":
                deviceClass = SpectreModelDeviceClass.Resistor;
                return true;
            case "diode" or "diodes":
                deviceClass = SpectreModelDeviceClass.Diode;
                return true;
            case "bjt" or "bipolar":
                deviceClass = SpectreModelDeviceClass.Bipolar;
                return true;
            case "moscap":
                deviceClass = SpectreModelDeviceClass.Moscap;
                return true;
            case "ind" or "inductor" or "inductors":
                deviceClass = SpectreModelDeviceClass.Inductor;
                return true;
            case "tline" or "tl" or "transmissionline":
                deviceClass = SpectreModelDeviceClass.TransmissionLine;
                return true;
            case "other":
                deviceClass = SpectreModelDeviceClass.Other;
                return true;
            case "unknown" or "uncat" or "uncategorized" or "unmatched":
                deviceClass = SpectreModelDeviceClass.Unknown;
                return true;
            default:
                return false;
        }
    }

    private static string BuildModelSummaryTitle(IEnumerable<SpectreModelDeviceClass> filters)
    {
        var filterList = filters?.ToList() ?? new List<SpectreModelDeviceClass>();
        if (filterList.Count == 0)
        {
            return "Model Catalog";
        }

        var labels = filterList
            .Select(FormatDeviceClassName)
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return string.Join(" / ", labels) + " Models";
    }

    private static string BuildClassSummaryLine(
        int displayedClassCount,
        int scopedClassCount,
        int displayedModelCount,
        int scopedModelCount,
        int totalModelCount,
        IEnumerable<SpectreModelDeviceClass> filters,
        bool limited,
        bool includeUncategorized)
    {
        var filterList = filters?.ToList() ?? new List<SpectreModelDeviceClass>();
        var filterLabel = filterList.Count == 0
            ? "All device classes"
            : "Filters → " + string.Join(", ", filterList.Select(FormatDeviceClassName));

        var scopedModelsLabel = scopedModelCount > 0
            ? $"covering {displayedModelCount} of {scopedModelCount} models in scope"
            : $"covering {displayedModelCount} models";

        var line = $"Showing {displayedClassCount} of {scopedClassCount} classes {scopedModelsLabel}. {filterLabel}.";

        if (scopedModelCount != totalModelCount)
        {
            line += $" Catalog total: {totalModelCount} models.";
        }

        if (includeUncategorized)
        {
            line += " Uncategorized devices are highlighted.";
        }

        if (limited)
        {
            line += " Use --limit to include more classes.";
        }

        return line;
    }

    private static string BuildClassStatsLine(
        IEnumerable<(SpectreModelDeviceClass Class, int Count)> categorizedCounts,
        IReadOnlyList<SpectreModel>? uncategorized)
    {
        var parts = new List<string>();

        var topCategories = categorizedCounts
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => FormatDeviceClassName(entry.Class), StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .Select(entry => $"{FormatDeviceClassName(entry.Class)}: {entry.Count}")
            .ToArray();

        if (topCategories.Length > 0)
        {
            parts.Add("Top classes → " + string.Join(", ", topCategories));
        }

        var uncategorizedCount = uncategorized?.Count ?? 0;

        if (uncategorizedCount > 0)
        {
            var deckSource = uncategorized ?? Array.Empty<SpectreModel>();
            var decks = FormatDecks(deckSource.SelectMany(model => model.Decks).ToList());
            var segment = decks == "-"
                ? $"Uncategorized: {uncategorizedCount}"
                : $"Uncategorized: {uncategorizedCount} ({decks})";
            parts.Add(segment);
        }
        else
        {
            parts.Add("Uncategorized: 0");
        }

        return parts.Count == 0 ? string.Empty : string.Join(" | ", parts) + ".";
    }

    private static string BuildDetailSummaryLine(
        IReadOnlyList<string> filterLabels,
        int offset,
        int pageSize,
        int totalCount)
    {
        if (totalCount == 0)
        {
            return "No models matched the selected filters.";
        }

        var start = offset + 1;
        var end = Math.Min(totalCount, offset + pageSize);
        var label = filterLabels.Count == 0 ? "All device classes" : string.Join(", ", filterLabels);
        return $"Showing models {start}-{end} of {totalCount} ({label}). Use Shift+Up/Down to scroll.";
    }

    private static string BuildDetailStatsLine(IReadOnlyCollection<SpectreModel> models)
    {
        if (models.Count == 0)
        {
            return string.Empty;
        }

        var voltage = FormatDistinctSummary(models.Select(model => model.VoltageDomain));
        var thresholds = FormatDistinctSummary(models.Select(model => model.ThresholdFlavor));
        var corners = FormatDistinctSummary(models.SelectMany(model => model.Corners));
        var decks = FormatDecks(models.SelectMany(model => model.Decks).ToList());

        var parts = new List<string>();
        if (voltage != "-")
        {
            parts.Add($"VDD → {voltage}");
        }
        if (thresholds != "-")
        {
            parts.Add($"VT → {thresholds}");
        }
        if (corners != "-")
        {
            parts.Add($"Corners → {corners}");
        }
        if (decks != "-")
        {
            parts.Add($"Decks → {decks}");
        }

        return parts.Count == 0 ? string.Empty : string.Join(" | ", parts);
    }

    private static string BuildModelSuggestionText()
    {
        return "Tip: Use Shift+Up/Down to scroll, 'pdk models nmos' to focus, 'pdk match' to classify, and 'home' to exit.";
    }

    private static ModelClassSummaryRow CreateClassSummaryRow(
        SpectreModelDeviceClass deviceClass,
        IReadOnlyList<SpectreModel> models,
        bool isUncategorized)
    {
        var deviceLabel = isUncategorized
            ? "Uncategorized"
            : FormatDeviceClassName(deviceClass);

        var modelCount = models.Count.ToString(CultureInfo.InvariantCulture);
        var voltageDomains = FormatDistinctSummary(models.Select(model => model.VoltageDomain));
        var thresholds = FormatDistinctSummary(models.Select(model => model.ThresholdFlavor));
        var corners = FormatDistinctSummary(models.SelectMany(model => model.Corners));
        var decks = FormatDecks(models.SelectMany(model => model.Decks).ToList());
        var exampleModel = models
            .Select(model => model.Name)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() ?? "-";

        return new ModelClassSummaryRow(
            deviceLabel,
            modelCount,
            decks,
            voltageDomains,
            thresholds,
            corners,
            exampleModel,
            isUncategorized);
    }

    private static ModelSummaryRow CreateModelSummaryRow(SpectreModel model, int index)
    {
        var threshold = string.IsNullOrWhiteSpace(model.ThresholdFlavor) ? "-" : model.ThresholdFlavor!;
        var voltage = string.IsNullOrWhiteSpace(model.VoltageDomain) ? "-" : model.VoltageDomain!;
        var corners = FormatDistinctSummary(model.Corners);
        var decks = FormatDecks(model.Decks.ToList());

        return new ModelSummaryRow(
            index,
            model.Name,
            FormatDeviceClassName(model.DeviceClass),
            threshold,
            voltage,
            corners,
            decks);
    }

    private static string FormatDeviceClassName(SpectreModelDeviceClass deviceClass)
    {
        return deviceClass switch
        {
            SpectreModelDeviceClass.Unknown => "Unknown",
            SpectreModelDeviceClass.Nmos => "NMOS",
            SpectreModelDeviceClass.Pmos => "PMOS",
            SpectreModelDeviceClass.Bipolar => "Bipolar",
            SpectreModelDeviceClass.Diode => "Diode",
            SpectreModelDeviceClass.Resistor => "Resistor",
            SpectreModelDeviceClass.Capacitor => "Capacitor",
            SpectreModelDeviceClass.Inductor => "Inductor",
            SpectreModelDeviceClass.Moscap => "MOSCAP",
            SpectreModelDeviceClass.TransmissionLine => "Transmission Line",
            SpectreModelDeviceClass.Other => "Other",
            _ => deviceClass.ToString()
        };
    }

    private static string FormatDistinctSummary(IEnumerable<string?> values, int maxItems = 5)
    {
        if (values is null)
        {
            return "-";
        }

        var distinct = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinct.Count == 0)
        {
            return "-";
        }

        if (distinct.Count <= maxItems)
        {
            return string.Join(", ", distinct);
        }

        return string.Join(", ", distinct.Take(maxItems)) + $" … ({distinct.Count - maxItems} more)";
    }

    private static string FormatDecks(IReadOnlyList<string> decks)
    {
        if (decks is null || decks.Count == 0)
        {
            return "-";
        }

        var names = decks
            .Select(deck => Path.GetFileName(deck) ?? deck)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (names.Count == 0)
        {
            return "-";
        }

        if (names.Count <= 3)
        {
            return string.Join(", ", names);
        }

        return string.Join(", ", names.Take(3)) + $" … ({names.Count - 3} more)";
    }

    private bool TryAdjustDetailOffset(int delta)
    {
        var view = _state.ModelSummary;
        if (view is null || !view.HasDetailRows)
        {
            return false;
        }

        var pageSize = view.DetailPageSize > 0 ? view.DetailPageSize : view.DetailRows.Count;
        var maxOffset = Math.Max(0, view.DetailRows.Count - pageSize);
        var newOffset = Math.Clamp(view.DetailOffset + delta, 0, maxOffset);
        if (newOffset == view.DetailOffset)
        {
            return false;
        }

        var summaryLine = BuildDetailSummaryLine(view.DetailFilters, newOffset, pageSize, view.DetailRows.Count);
        var updatedView = view.WithDetail(newOffset, summaryLine);
        _state.ReplaceModelSummary(updatedView);
        return true;
    }

    private int GetDetailScrollStep()
    {
        var view = _state.ModelSummary;
        if (view is null || !view.HasDetailRows)
        {
            return 1;
        }

        var pageSize = view.DetailPageSize > 0 ? view.DetailPageSize : view.DetailRows.Count;
        return Math.Max(1, pageSize / 4);
    }

    private CommandResult ShowVersion(string[] args)
    {
        var version = typeof(CascodeShell).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        _state.AddMessage(version);
        return CommandResult.Success;
    }

    private CommandResult ShowPdkUsage(string[] args)
    {
        _state.AddMessage("Usage: pdk <subcommand>");
        var subcommands = _commands.GetSubcommands(new[] { "pdk" }).ToArray();
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
        if (_isInteractive)
        {
            // In interactive mode, output to log panel
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
            _state.AddMessage("  - If SPECTRE_BIN isn't set, runs are skipped (generation only).");
            _state.AddMessage("  - Results live under ~/.cascode/workspaces/<id>/char/<backend>/<corner>/<model>/<ts>/");
        }
        else
        {
            // In non-interactive mode, use fancy formatting
            var header = new Rule("[bold]PDK Characterization[/]") { Justification = Justify.Left };
            AnsiConsole.Write(header);

            // Overview
            var overview = new Table().NoBorder();
            overview.AddColumn(""); overview.AddColumn("");
            overview.AddRow("Goal", "Build device LUTs (gm/Id, etc.) for synthesis and sizing.");
            overview.AddRow("Outputs", "Netlists, results.csv, derived.csv stored in workspace cache.");
            AnsiConsole.Write(overview);

            // Subcommands
            var subs = new Table().Border(TableBorder.Rounded).AddColumn("Command").AddColumn("What it does");
            subs.AddRow("pdk char config", "Interactive form to set defaults (backend/corner/filters/jobs).");
            subs.AddRow("pdk char config --show", "Show the saved defaults.");
            subs.AddRow("pdk char run", "Run a batch using saved defaults (flags override). Shows progress.");
            subs.AddRow("pdk char read <model>", "Preview latest LUT for model — table + sparklines.");
            AnsiConsole.Write(subs);

            // Flags summary
            var flags = new Table().NoBorder();
            flags.AddColumn("Flag"); flags.AddColumn("Meaning");
            flags.AddRow("--backend spectre|ngspice", "Pick simulator (Spectre-first; ngspice for CI).");
            flags.AddRow("--corner <name>", "Model section/corner, e.g., tt/ff/ss.");
            flags.AddRow("--limit <n>", "Cap how many models to process (0 = all).");
            flags.AddRow("--jobs <n>", "Planned: parallelize bench generation (not Spectre).");
            flags.AddRow("--class nmos,pmos", "Filter device classes.");
            flags.AddRow("--name-contains <csv>", "Only names containing any token.");
            flags.AddRow("--name-excludes <csv>", "Skip names containing any token.");
            flags.AddRow("--vt <csv>", "Only VT flavors (e.g., LVT,HVT).");
            AnsiConsole.Write(new Panel(flags) { Header = new PanelHeader("Common Flags"), Border = BoxBorder.Rounded });

            // Examples
            var examples = new Table().Border(TableBorder.Rounded).AddColumn("Example").AddColumn("Explanation");
            examples.AddRow(
                "pdk char config",
                "Open the defaults form; save corner/backend/filters/jobs to workspace.");
            examples.AddRow(
                "pdk char run",
                "Start a batch with saved defaults; shows a live progress bar chart.");
            examples.AddRow(
                "pdk char run --class nmos --limit 5 --name-excludes esd,io --vt LVT",
                "Quick LVT-only NMOS subset; skips ESD/IO variants.");
            examples.AddRow(
                "pdk char run --corner tt --backend spectre --jobs 4",
                "Spectre-first; prepare to parallelize bench generation with 4 jobs.");
            examples.AddRow(
                "pdk char read sky130_fd_pr__nfet_01v8",
                "Show table and gm/Id sparkline for the latest run of that model.");
            AnsiConsole.Write(examples);

            // Notes
            AnsiConsole.MarkupLine("[dim]- If SPECTRE_BIN isn't set, runs are skipped (generation only).[/]");
            AnsiConsole.MarkupLine("[dim]- Results live under ~/.cascode/workspaces/<id>/char/<backend>/<corner>/<model>/<ts>/[/]");
        }
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

        // Interactive form
        cfg.Backend = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
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
        foreach (var c in cfg.Classes ?? new())
        {
            classPrompt.Select(c);
        }
        var selectedClasses = AnsiConsole.Prompt(classPrompt);
        cfg.Classes = selectedClasses.ToList();

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
        var vtSelected = AnsiConsole.Prompt(vtPrompt);
        cfg.Vt = vtSelected.ToList();

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
        foreach (var warning in result.Warnings)
        {
            _state.AddMessage($"Warning: {warning}");
        }

        return CommandResult.Success;
    }

    private CommandResult PdkModels(string[] args)
    {
        var scan = EnsureScan();
        if (scan is null)
        {
            return CommandResult.Failure;
        }

        var models = scan.Models;
        if (models.Count == 0)
        {
            var emptyLine = "No models discovered. Run pdk scan.";
            _state.AddMessage(emptyLine);

            if (_isInteractive)
            {
                var emptyView = new ModelSummaryViewState(
                    "Model Catalog",
                    emptyLine,
                    string.Empty,
                    BuildModelSuggestionText(),
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

        if (filters.Count == 0)
        {
            return RenderClassSummary(models, categorizedClassCount, filters, limit);
        }

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
        if (limit <= 0)
        {
            limit = _isInteractive ? Math.Min(8, maxClassCount) : maxClassCount;
        }
        limit = Math.Clamp(limit, 1, maxClassCount);

        var uncategorizedList = models
            .Where(model => model.DeviceClass == SpectreModelDeviceClass.Unknown)
            .ToList();
        var includeUncategorized = uncategorizedList.Count > 0;

        var categorizedGroups = models
            .Where(model => model.DeviceClass != SpectreModelDeviceClass.Unknown)
            .GroupBy(model => model.DeviceClass)
            .Select(group => (Class: group.Key, Models: group.ToList()))
            .OrderByDescending(entry => entry.Models.Count)
            .ThenBy(entry => FormatDeviceClassName(entry.Class), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var matchingClassCount = categorizedGroups.Count + (includeUncategorized ? 1 : 0);
        var matchingModelCount = categorizedGroups.Sum(entry => entry.Models.Count) + uncategorizedList.Count;

        var limitedGroups = categorizedGroups.Take(limit).ToList();
        var displayedClassCount = limitedGroups.Count + (includeUncategorized ? 1 : 0);
        var displayedModelCount = limitedGroups.Sum(entry => entry.Models.Count) + (includeUncategorized ? uncategorizedList.Count : 0);

        var classRows = new List<ModelClassSummaryRow>(displayedClassCount);
        foreach (var entry in limitedGroups)
        {
            classRows.Add(CreateClassSummaryRow(entry.Class, entry.Models, isUncategorized: false));
        }

        if (includeUncategorized)
        {
            classRows.Add(CreateClassSummaryRow(SpectreModelDeviceClass.Unknown, uncategorizedList, isUncategorized: true));
        }

        var title = BuildModelSummaryTitle(filters);
        var limitedCategorized = categorizedGroups.Count > 0 && limitedGroups.Count < categorizedGroups.Count;
        var summaryLine = BuildClassSummaryLine(
            displayedClassCount,
            matchingClassCount,
            displayedModelCount,
            matchingModelCount,
            models.Count,
            filters,
            limitedCategorized,
            includeUncategorized && uncategorizedList.Count > 0);

        var statsLine = BuildClassStatsLine(
            categorizedGroups.Select(entry => (entry.Class, entry.Models.Count)),
            includeUncategorized ? uncategorizedList : null);

        var suggestionLine = BuildModelSuggestionText();

        var view = new ModelSummaryViewState(
            title,
            summaryLine,
            statsLine,
            suggestionLine,
            Array.Empty<ModelSummaryRow>(),
            classRows,
            detailOffset: 0,
            detailPageSize: 0,
            detailFilters: Array.Empty<string>());

        if (_isInteractive)
        {
            _state.ShowModelSummary(view);
        }
        else
        {
            var table = ShellRenderer.CreateModelClassSummaryTable(classRows);
            AnsiConsole.Write(table);
            if (!string.IsNullOrWhiteSpace(summaryLine))
            {
                AnsiConsole.MarkupLine(Markup.Escape(summaryLine));
            }
            if (!string.IsNullOrWhiteSpace(statsLine))
            {
                AnsiConsole.MarkupLine(Markup.Escape(statsLine));
            }
            if (!string.IsNullOrWhiteSpace(suggestionLine))
            {
                AnsiConsole.MarkupLine(Markup.Escape(suggestionLine));
            }
        }

        _state.AddMessage(summaryLine);
        if (!string.IsNullOrWhiteSpace(statsLine))
        {
            _state.AddMessage(statsLine);
        }
        if (!string.IsNullOrWhiteSpace(suggestionLine))
        {
            _state.AddMessage(suggestionLine);
        }

        if (categorizedGroups.Count == 0)
        {
            _state.AddMessage(includeUncategorized
                ? "No categorized classes to display. Showing uncategorized models for review."
                : "No categorized classes to display. Adjust filters or scan again.");
        }
        else if (limitedCategorized)
        {
            _state.AddMessage($"Showing top {limitedGroups.Count} of {categorizedGroups.Count} categorized classes. Use --limit for more.");
        }
        else
        {
            _state.AddMessage("Displayed all categorized classes in scope.");
        }

        if (includeUncategorized && uncategorizedList.Count > 0)
        {
            _state.AddMessage($"Uncategorized models: {uncategorizedList.Count}. Run 'pdk match' to classify them.");
        }

        return CommandResult.Success;
    }

    private CommandResult RenderDetailSummary(
        IReadOnlyList<SpectreModel> models,
        HashSet<SpectreModelDeviceClass> filters,
        int parsedLimit)
    {
        var filteredModels = models
            .Where(model => filters.Contains(model.DeviceClass))
            .ToList();

        if (filteredModels.Count == 0)
        {
            var title = BuildModelSummaryTitle(filters);
            var message = "No models matched the selected device filters.";
            _state.ShowModelSummary(new ModelSummaryViewState(
                title,
                message,
                string.Empty,
                BuildModelSuggestionText(),
                Array.Empty<ModelSummaryRow>(),
                Array.Empty<ModelClassSummaryRow>()));
            _state.AddMessage(message);
            return CommandResult.Success;
        }

        var filterLabels = filters
            .Select(FormatDeviceClassName)
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var pageSize = parsedLimit > 0
            ? Math.Clamp(parsedLimit, 1, filteredModels.Count)
            : (_isInteractive ? Math.Min(20, filteredModels.Count) : filteredModels.Count);

        var detailRows = new List<ModelSummaryRow>(filteredModels.Count);
        for (var i = 0; i < filteredModels.Count; i++)
        {
            detailRows.Add(CreateModelSummaryRow(filteredModels[i], i + 1));
        }

        var offset = 0;
        var summaryLine = BuildDetailSummaryLine(filterLabels, offset, pageSize, filteredModels.Count);
        var statsLine = BuildDetailStatsLine(filteredModels);
        var suggestionLine = BuildModelSuggestionText();
        var viewTitle = BuildModelSummaryTitle(filters);

        var view = new ModelSummaryViewState(
            viewTitle,
            summaryLine,
            statsLine,
            suggestionLine,
            detailRows,
            Array.Empty<ModelClassSummaryRow>(),
            detailOffset: offset,
            detailPageSize: pageSize,
            detailFilters: filterLabels);

        if (_isInteractive)
        {
            _state.ShowModelSummary(view);
        }
        else
        {
            var table = ShellRenderer.CreateModelDetailTable(view);
            AnsiConsole.Write(table);
            if (!string.IsNullOrWhiteSpace(summaryLine))
            {
                AnsiConsole.MarkupLine(Markup.Escape(summaryLine));
            }
            if (!string.IsNullOrWhiteSpace(statsLine))
            {
                AnsiConsole.MarkupLine(Markup.Escape(statsLine));
            }
            if (!string.IsNullOrWhiteSpace(suggestionLine))
            {
                AnsiConsole.MarkupLine(Markup.Escape(suggestionLine));
            }
        }

        _state.AddMessage(summaryLine);
        if (!string.IsNullOrWhiteSpace(statsLine))
        {
            _state.AddMessage(statsLine);
        }
        if (!string.IsNullOrWhiteSpace(suggestionLine))
        {
            _state.AddMessage(suggestionLine);
        }

        return CommandResult.Success;
    }

    private CommandResult PdkModel(string[] args)
    {
        var scan = EnsureScan();
        if (scan is null)
        {
            return CommandResult.Failure;
        }

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
            if (parsedIndex >= 0 && parsedIndex < models.Count)
            {
                model = models[parsedIndex];
            }
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

        var detail = new Table()
            .AddColumn("Field")
            .AddColumn("Value")
            .Border(TableBorder.Rounded);

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

    private CommandResult PdkCharRunCommand(string[] args)
    {
        // Load defaults from config
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
            if (arg.Equals("--backend", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                backend = args[++i];
            }
            else if (arg.Equals("--corner", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                corner = args[++i];
            }
            else if (arg.Equals("--out", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                outRoot = NormalizePath(args[++i]);
            }
            else if (arg.Equals("--limit", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                limit = Math.Max(0, parsed);
            }
            else if (arg.Equals("--jobs", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedJobs))
            {
                jobs = Math.Clamp(parsedJobs, 1, 64);
            }
            else if (arg.Equals("--class", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                cfg.Classes = SplitCsv(args[++i]);
            }
            else if (arg.Equals("--name-contains", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                cfg.NameContains = SplitCsv(args[++i]);
            }
            else if (arg.Equals("--name-excludes", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                cfg.NameExcludes = SplitCsv(args[++i]);
            }
            else if (arg.Equals("--vt", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                cfg.Vt = SplitCsv(args[++i]).Select(v => v.ToUpperInvariant()).ToList();
            }
        }

        if (!backend.Equals("spectre", StringComparison.OrdinalIgnoreCase))
        {
            _state.AddMessage("Only the Spectre backend is supported for 'pdk char run' at the moment. Using spectre.");
            backend = "spectre";
        }

        Directory.CreateDirectory(outRoot);
        var spectreEnv = Environment.GetEnvironmentVariable("SPECTRE_BIN");
        if (string.IsNullOrWhiteSpace(spectreEnv))
        {
            spectreEnv = TryDetectSpectreBin();
            if (string.IsNullOrWhiteSpace(spectreEnv))
            {
                _state.AddMessage("[warn] SPECTRE_BIN is not set and could not auto-detect from SPECTRE_HOME; this run will only generate benches and skip execution.");
            }
        }
        var scan = EnsureScan();
        if (scan is null)
        {
            return CommandResult.Failure;
        }

        var candidates = scan.Models
            .Where(model => model.DeviceClass is SpectreModelDeviceClass.Nmos or SpectreModelDeviceClass.Pmos)
            .OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Apply config filters
        if (cfg.Classes?.Count > 0)
        {
            bool wantN = cfg.Classes.Any(c => string.Equals(c, "nmos", StringComparison.OrdinalIgnoreCase));
            bool wantP = cfg.Classes.Any(c => string.Equals(c, "pmos", StringComparison.OrdinalIgnoreCase));
            candidates = candidates.Where(m => (m.DeviceClass == SpectreModelDeviceClass.Nmos && wantN) || (m.DeviceClass == SpectreModelDeviceClass.Pmos && wantP)).ToList();
        }

        if (cfg.NameContains is { Count: > 0 })
        {
            candidates = candidates.Where(m => cfg.NameContains.Any(s => m.Name.Contains(s, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        if (cfg.NameExcludes is { Count: > 0 })
        {
            candidates = candidates.Where(m => !cfg.NameExcludes.Any(s => m.Name.Contains(s, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        if (cfg.Vt is { Count: > 0 })
        {
            var want = new HashSet<string>(cfg.Vt.Select(v => v.Trim().ToUpperInvariant()));
            candidates = candidates.Where(m => !string.IsNullOrWhiteSpace(m.ThresholdFlavor) && want.Contains(m.ThresholdFlavor!.Trim().ToUpperInvariant())).ToList();
        }

        if (limit > 0)
        {
            candidates = candidates.Take(limit).ToList();
        }

        if (candidates.Count == 0)
        {
            _state.AddMessage("No NMOS/PMOS models discovered. Run 'pdk scan' first.");
            return CommandResult.Success;
        }

        var registry = Cascode.Bench.HarnessService.CreateDefault(_state.WorkspaceRoot);
        var backendKey = backend.ToLowerInvariant();
        var cornerKey = string.IsNullOrWhiteSpace(corner) ? "default" : corner;

        int generated = 0;
        int executed = 0;
        int exported = 0;
        int skipped = 0;

        var total = candidates.Count;
        _state.AddMessage($"Characterizing {total} models (backend: {backend}, corner: {corner})…");

        if (_isInteractive)
        {
            // Kick off on a background thread and return control to the UI.
            _state.StartCharJob(total, backend, corner);
            Task.Run(() =>
            {
                foreach (var model in candidates)
                {
                    _state.UpdateCharProgress(model.Name);
                    ProcessOneModel(model);
                }

                _state.CompleteCharJob();
                _state.AddMessage($"pdk char run complete → generated {generated}, Spectre ran {executed}, exported {exported}, skipped {skipped}.");
                _state.AddMessage($"Output root: {outRoot}");
            });

            _state.AddMessage("Characterization started in background. You can keep using the CLI; progress is docked on the sidebar.");
            return CommandResult.Success;
        }
        else
        {
            // Non-interactive: keep a live-updating bar chart
            var initial = BuildProgressChart(total, 0, 0, 0, 0, "starting…");
            AnsiConsole.Live(initial).Start(ctx =>
            {
                foreach (var model in candidates)
                {
                    ProcessOneModel(model);
                    ctx.UpdateTarget(BuildProgressChart(total, generated, executed, exported, skipped, model.Name));
                }
            });

            _state.AddMessage($"pdk char run complete → generated {generated}, Spectre ran {executed}, exported {exported}, skipped {skipped}.");
            _state.AddMessage($"Output root: {outRoot}");
            return CommandResult.Success;
        }

        void ProcessOneModel(SpectreModel model)
        {
            var harnessId = ResolveHarnessForModel(model);
            if (harnessId is null)
            {
                skipped++;
                _state.UpdateCharProgress(model.Name, skippedDelta: 1);
                return;
            }

            if (!registry.TryGet(harnessId, out var _))
            {
                _state.AddMessage($"Harness '{harnessId}' not available; skipping {model.Name}.");
                skipped++;
                _state.UpdateCharProgress(model.Name, skippedDelta: 1);
                return;
            }

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var modelFolder = Path.Combine(outRoot, backendKey, cornerKey, Sanitize(model.Name));
            Directory.CreateDirectory(modelFolder);
            var jobDir = Path.Combine(modelFolder, timestamp);
            Directory.CreateDirectory(jobDir);

            var tokens = new List<string>
            {
                model.Name,
                "--harness", harnessId,
                "--backend", backend,
                "--out", jobDir
            };

            if (!string.IsNullOrWhiteSpace(corner))
            {
                tokens.Add("--corner");
                tokens.Add(corner);
            }

            foreach (var param in GetDefaultParamArguments(model, harnessId))
            {
                tokens.Add("--param");
                tokens.Add(param);
            }

            var result = CharacterizationGenerate(tokens.ToArray());
            if (result.ExitCode != 0)
            {
                _state.AddMessage($"Failed to generate characterization bench for {model.Name}.");
                skipped++;
                _state.UpdateCharProgress(model.Name, skippedDelta: 1);
                return;
            }

            generated++;
            _state.UpdateCharProgress(model.Name, generatedDelta: 1);
            if (TryRunSpectre(jobDir, backend))
            {
                executed++;
                _state.UpdateCharProgress(model.Name, ranDelta: 1);
            }

            var exportResult = CharacterizationExportCommand(new[] { jobDir });
            if (exportResult.ExitCode == 0)
            {
                exported++;
                _state.UpdateCharProgress(model.Name, exportedDelta: 1);
            }
        }
    }

    private static IRenderable BuildProgressChart(int total, int generated, int executed, int exported, int skipped, string current)
    {
        var remaining = Math.Max(0, total - Math.Max(Math.Max(generated, executed), Math.Max(exported, skipped)));
        var width =  Math.Clamp(GetConsoleWidthSafe() - 8, 40, 100);
        var label = $"[green bold underline]PDK Characterization Progress[/]  [grey]current:[/] {EscapeMarkupSafe(current)}";
        var chart = new BarChart()
            .Width(width)
            .Label(label)
            .CenterLabel()
            .AddItem("Generated", generated, Color.Yellow)
            .AddItem("Ran", executed, Color.Blue)
            .AddItem("Exported", exported, Color.Green)
            .AddItem("Skipped", skipped, Color.Grey)
            .AddItem("Remaining", remaining, Color.Red);
        return chart;
    }

    private static int GetConsoleWidthSafe()
    {
        try { return Console.WindowWidth; } catch { return 80; }
    }

    private static string EscapeMarkupSafe(string s)
        => s.Replace("[", "[[").Replace("]", "]]");

    private static List<string> SplitCsv(string value)
        => string.IsNullOrWhiteSpace(value)
            ? new List<string>()
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

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
            if (arg.Equals("--backend", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                backend = args[++i];
            }
            else if (arg.Equals("--corner", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                corner = args[++i];
            }
            else if (arg.Equals("--head", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                head = Math.Max(1, parsed);
            }
            else if (arg.Equals("--job", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                jobOverride = NormalizePath(args[++i]);
            }
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
            if (!Directory.Exists(backendFolder))
            {
                _state.AddMessage($"No runs stored for backend '{backend}'.");
                return CommandResult.Failure;
            }

            var cornerFolder = Path.Combine(backendFolder, string.IsNullOrWhiteSpace(corner) ? "default" : corner);
            if (!Directory.Exists(cornerFolder))
            {
                _state.AddMessage($"No runs stored for corner '{corner}'.");
                return CommandResult.Failure;
            }

            var sanitizedQuery = Sanitize(model);
            var modelFolder = Path.Combine(cornerFolder, sanitizedQuery);
            if (!Directory.Exists(modelFolder))
            {
                var match = Directory.EnumerateDirectories(cornerFolder)
                    .FirstOrDefault(path => Path.GetFileName(path).Contains(sanitizedQuery, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                {
                    _state.AddMessage($"No characterization recorded for model '{model}'.");
                    return CommandResult.Failure;
                }

                modelFolder = match;
            }

            var latest = Directory.EnumerateDirectories(modelFolder)
                .Select(path => new DirectoryInfo(path))
                .OrderByDescending(di => di.LastWriteTimeUtc)
                .FirstOrDefault();

            if (latest is null)
            {
                _state.AddMessage($"Model '{model}' has no completed runs.");
                return CommandResult.Failure;
            }

            jobDir = latest.FullName;
        }

        var derivedPath = Path.Combine(jobDir, "derived.csv");
        if (!File.Exists(derivedPath))
        {
            _state.AddMessage($"Derived metrics not found at {derivedPath}. Run 'char export {jobDir}' first.");
            return CommandResult.Failure;
        }

        var (headers, samples) = LoadDerivedCsv(derivedPath);
        if (headers.Count == 0 || samples.Count == 0)
        {
            _state.AddMessage("Derived CSV did not contain numeric samples.");
            return CommandResult.Failure;
        }

        var (controlIdx, controlName) = FindColumn(headers, "vgs", "vsg");
        var (idIdx, _) = FindColumn(headers, "id");
        var (gmIdx, _) = FindColumn(headers, "gm");
        var (gmIdIdx, _) = FindColumn(headers, "gm_over_id");
        var (vthIdx, _) = FindColumn(headers, "vth");

        var preview = Math.Min(head, samples.Count);
        var table = new Table().Border(TableBorder.SimpleHeavy)
            .AddColumn("#")
            .AddColumn(controlName.ToUpperInvariant())
            .AddColumn("Id");

        if (gmIdx >= 0) table.AddColumn("gm");
        if (gmIdIdx >= 0) table.AddColumn("gm/Id");
        if (vthIdx >= 0) table.AddColumn("Vth");

        static double sampleSafe(IReadOnlyList<double> data, int idx)
            => idx >= 0 && idx < data.Count ? data[idx] : double.NaN;

        for (var i = 0; i < preview; i++)
        {
            var sample = samples[i];
            var row = new List<string>
            {
                (i + 1).ToString(CultureInfo.InvariantCulture),
                FormatNumber(sampleSafe(sample, controlIdx)),
                FormatNumber(sampleSafe(sample, idIdx))
            };

            if (gmIdx >= 0) row.Add(FormatNumber(sampleSafe(sample, gmIdx)));
            if (gmIdIdx >= 0) row.Add(FormatNumber(sampleSafe(sample, gmIdIdx)));
            if (vthIdx >= 0) row.Add(FormatNumber(sampleSafe(sample, vthIdx)));

            table.AddRow(row.ToArray());
        }

        AnsiConsole.Write(new Rule($"[bold]{model}[/] — {backend} / {corner}") { Justification = Justify.Left });
        AnsiConsole.Write(table);

        if (gmIdIdx >= 0)
        {
            RenderSparkline(samples, gmIdIdx, "gm/Id");
        }
        if (idIdx >= 0)
        {
            RenderSparkline(samples, idIdx, "Id");
        }

        _state.AddMessage($"Derived source: {derivedPath}");
        return CommandResult.Success;
    }

    private CommandResult HomeCommand(string[] args)
    {
        if (_state.ViewMode == ShellViewMode.Home)
        {
            _state.AddMessage("Already on dashboard layout.");
            return CommandResult.Success;
        }

        _state.ShowHome();
        _state.AddMessage("Returned to dashboard layout.");
        return CommandResult.Success;
    }

    private CommandResult ApplyPdkDirectory(string path)
    {
        try
        {
            var resolved = NormalizePath(path);
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

    private CommandResult BuildCommand(string[] args)
    {
        if (args.Length == 0)
        {
            _state.AddMessage("Usage: build <file.cas>");
            return CommandResult.Success;
        }

        if (!File.Exists(args[0]))
        {
            _state.AddMessage($"Input file '{args[0]}' not found.");
            return CommandResult.Failure;
        }

        _state.AddMessage($"[preview] build for '{args[0]}' not implemented yet.");
        return CommandResult.Success;
    }

    private CommandResult ShowCharUsage(string[] args)
    {
        _state.AddMessage("Usage: char <subcommand>");
        var subs = _commands.GetSubcommands(new[] { "char" }).ToArray();
        var width = subs.Length == 0 ? 0 : subs.Max(c => c.DisplayPath.Length);

        foreach (var sub in subs)
        {
            var padded = width > 0 ? sub.DisplayPath.PadRight(width) : sub.DisplayPath;
            var description = string.IsNullOrEmpty(sub.Description) ? string.Empty : $"  {sub.Description}";
            _state.AddMessage($"  {padded}{description}");
        }

        return CommandResult.Success;
    }

    private CommandResult ShowBenchUsage(string[] args)
    {
        _state.AddMessage("Usage: bench <subcommand>");
        var subs = _commands.GetSubcommands(new[] { "bench" }).ToArray();
        var width = subs.Length == 0 ? 0 : subs.Max(c => c.DisplayPath.Length);

        foreach (var sub in subs)
        {
            var padded = width > 0 ? sub.DisplayPath.PadRight(width) : sub.DisplayPath;
            var description = string.IsNullOrEmpty(sub.Description) ? string.Empty : $"  {sub.Description}";
            _state.AddMessage($"  {padded}{description}");
        }

        return CommandResult.Success;
    }

    private CommandResult ShowBenchHarnessUsage(string[] args)
    {
        _state.AddMessage("Usage: bench harness <list|show>");
        return CommandResult.Success;
    }

    private CommandResult CharacterizationGenerateCommand(string[] args)
    {
        if (args.Length == 0)
        {
            _state.AddMessage("Usage: char gen <model> [--harness <id>] [--backend spectre|ngspice] [--out <dir>] [--corner <name>]");
            return CommandResult.Success;
        }

        return CharacterizationGenerate(args);
    }

    private CommandResult CharacterizationReadCommand(string[] args)
    {
        if (args.Length == 0)
        {
            _state.AddMessage("Usage: char read <job-dir> [--head <n>]");
            return CommandResult.Success;
        }

        return CharacterizationRead(args);
    }

    private static CommandResult Quit(string[] args) => new(0, true);

    private CommandResult BenchHarnessListCommand(string[] args)
    {
        try
        {
            var registry = Cascode.Bench.HarnessService.CreateDefault(_state.WorkspaceRoot);
            var debug = Environment.GetEnvironmentVariable("CASCODE_DEBUG") == "1";
            if (debug)
            {
                _state.AddMessage($"[debug] workspaceRoot = {_state.WorkspaceRoot}");
                var discovered = Cascode.Bench.HarnessService.Discover(_state.WorkspaceRoot).ToArray();
                _state.AddMessage($"[debug] discovered YAML harnesses: {discovered.Length}");
                foreach (var h in discovered)
                {
                    _state.AddMessage($"[debug]  - {h.Id}");
                }
            }
            var all = registry.All.OrderBy(h => h.Id, StringComparer.OrdinalIgnoreCase).ToArray();
            if (all.Length == 0)
            {
                _state.AddMessage("No harnesses registered.");
                return CommandResult.Success;
            }

            _state.AddMessage("Harnesses:");
            var width = all.Max(h => h.Id.Length);
            foreach (var h in all)
            {
                var backends = string.Join('/', h.SupportedBackends);
                _state.AddMessage($"  {h.Id.PadRight(width)}  {backends}  {h.Description}");
            }
            return CommandResult.Success;
        }
        catch (Exception ex)
        {
            _state.AddMessage($"Failed to list harnesses: {ex.Message}");
            return CommandResult.Failure;
        }
    }

    private CommandResult BenchHarnessShowCommand(string[] args)
    {
        if (args.Length == 0)
        {
            _state.AddMessage("Usage: bench harness show <id>");
            return CommandResult.Success;
        }

        var id = args[0];
        try
        {
            var registry = Cascode.Bench.HarnessService.CreateDefault(_state.WorkspaceRoot);
            if (!registry.TryGet(id, out var h))
            {
                _state.AddMessage("Harness not found.");
                return CommandResult.Failure;
            }

            _state.AddMessage($"Id: {h.Id}");
            _state.AddMessage($"Description: {h.Description}");
            _state.AddMessage($"Backends: {string.Join(", ", h.SupportedBackends)}");
            if (h.Params.Count > 0)
            {
                _state.AddMessage("Params:");
                var w = h.Params.Max(p => p.Name.Length);
                foreach (var p in h.Params)
                {
                    var choices = p.Choices is null || p.Choices.Count == 0 ? string.Empty : $" choices=[{string.Join('/', p.Choices)}]";
                    var def = p.DefaultValue is null ? string.Empty : $" default={p.DefaultValue}";
                    var req = p.Required ? " required" : string.Empty;
                    _state.AddMessage($"  {p.Name.PadRight(w)}  {p.Type}{req}{def}{choices} — {p.Description}");
                }
            }

            return CommandResult.Success;
        }
        catch (Exception ex)
        {
            _state.AddMessage($"Failed to show harness: {ex.Message}");
            return CommandResult.Failure;
        }
    }

    private CommandResult CharacterizationExportCommand(string[] args)
    {
        if (args.Length == 0)
        {
            _state.AddMessage("Usage: char export <job-dir> [--out <file.csv>] [--metrics <list>]");
            return CommandResult.Success;
        }

        var jobDir = NormalizePath(args[0]);
        string outFile = Path.Combine(jobDir, "derived.csv");
        var metricFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i].Equals("--out", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                outFile = args[++i];
            }
            else if (args[i].Equals("--metrics", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                foreach (var m in args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    metricFilter.Add(m);
                }
            }
        }

        try
        {
            // Load spec first (used both for PMOS normalization and as hint)
            double w_m = 0, l_m = 0;
            bool isPmosHarness = false;
            string controlLabel = "vgs";
            var specPath = Path.Combine(jobDir, "spec.json");
            if (File.Exists(specPath))
            {
                try
                {
                    var json = File.ReadAllText(specPath);
                    var spec = System.Text.Json.JsonSerializer.Deserialize<Cascode.Bench.TestbenchSpec>(json);
                    if (spec is not null)
                    {
                        w_m = spec.W_M;
                        l_m = spec.L_M;
                        if (spec.Name?.Contains("pmos", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            isPmosHarness = true;
                        }

                        if (!isPmosHarness && spec.ModelName?.Contains("pfet", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            isPmosHarness = true;
                        }

                        if (!isPmosHarness && spec.ModelName?.Contains("pmos", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            isPmosHarness = true;
                        }

                        controlLabel = isPmosHarness ? "vsg" : "vgs";
                    }
                }
                catch { /* ignore malformed spec */ }
            }

            // 1) Prefer oppoint-per-step ASCII files from braced sweep
            if (TryExportFromOppointFiles(jobDir, isPmosHarness, w_m, controlLabel, out var createdCsv, out var msgOpp))
            {
                _state.AddMessage(msgOpp);
            }

            var csv = Path.Combine(jobDir, "results.csv");
            if (!File.Exists(csv))
            {
                // Attempt to recover by parsing Spectre nutascii output (-raw raw)
                if (TryBuildResultsCsvFromNutascii(jobDir, isPmosHarness, out var buildMsg))
                {
                    _state.AddMessage(buildMsg);
                }
            }
            if (!File.Exists(csv))
            {
                _state.AddMessage($"Results file not found: {csv}");
                return CommandResult.Failure;
            }

            var lines = File.ReadAllLines(csv);
            if (lines.Length == 0)
            {
                _state.AddMessage("Empty results file.");
                return CommandResult.Failure;
            }


            static bool TryParseInvariant(string? text, out double value)
            {
                value = double.NaN;
                if (string.IsNullOrWhiteSpace(text))
                {
                    return false;
                }

                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }

            static string Format(double value)
            {
                if (double.IsNaN(value))
                {
                    return string.Empty;
                }

                return value.ToString("G", CultureInfo.InvariantCulture);
            }

            static string? FindFirstColumn(IReadOnlyDictionary<string, int> map, params string[] names)
            {
                foreach (var name in names)
                {
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    if (map.ContainsKey(name))
                    {
                        return name;
                    }
                }

                return null;
            }

            var headerCells = lines[0].Split(',', StringSplitOptions.None)
                .Select(h => h.Trim())
                .ToArray();
            if (headerCells.Length == 0)
            {
                _state.AddMessage("Results CSV is missing a header row.");
                return CommandResult.Failure;
            }

            var columnIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headerCells.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(headerCells[i]))
                {
                    columnIndex[headerCells[i]] = i;
                }
            }

            string? controlColumn = FindFirstColumn(columnIndex, controlLabel, "vgs", "vsg", "control");
            if (controlColumn is null)
            {
                controlColumn = headerCells[0];
            }

            var exportRows = new List<ExportRow>();

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var cells = line.Split(',', StringSplitOptions.None);
                if (cells.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                var normalized = new string[headerCells.Length];
                for (int i = 0; i < headerCells.Length; i++)
                {
                    normalized[i] = i < cells.Length ? cells[i].Trim() : string.Empty;
                }

                double Value(params string[] names)
                {
                    foreach (var name in names)
                    {
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            continue;
                        }

                        if (columnIndex.TryGetValue(name, out var idx) && idx < normalized.Length)
                        {
                            if (TryParseInvariant(normalized[idx], out var val))
                            {
                                return val;
                            }
                        }
                    }

                    return double.NaN;
                }

                var row = new ExportRow
                {
                    Control = Value(controlColumn ?? controlLabel, controlLabel, "vgs", "vsg", "control"),
                    Vds = Value("vds", "vd"),
                    Id = Value("id", "ids"),
                    Gm = Value("gm"),
                    Gmbs = Value("gmbs"),
                    Gds = Value("gds"),
                    Vth = Value("vth"),
                    Vdsat = Value("vdsat"),
                    Cgs = Value("cgs"),
                    Cgd = Value("cgd"),
                    Cgg = Value("cgg"),
                    GmOverIdRaw = Value("gmoverid", "gm_over_id", "gm/id"),
                    Ueff = Value("ueff"),
                    Ron = Value("ron"),
                    RsEff = Value("rseff"),
                    RdEff = Value("rdeff"),
                    Weff = Value("w_eff", "weff", "w")
                };

                if (isPmosHarness)
                {
                    row.Control = Math.Abs(row.Control);
                    row.Id = Math.Abs(row.Id);
                    row.Gm = Math.Abs(row.Gm);
                    row.Gmbs = Math.Abs(row.Gmbs);
                    row.Gds = Math.Abs(row.Gds);
                    row.Vth = Math.Abs(row.Vth);
                    row.Vdsat = Math.Abs(row.Vdsat);
                    row.Cgs = Math.Abs(row.Cgs);
                    row.Cgd = Math.Abs(row.Cgd);
                    row.Cgg = Math.Abs(row.Cgg);
                    row.GmOverIdRaw = Math.Abs(row.GmOverIdRaw);
                    row.Ueff = Math.Abs(row.Ueff);
                    row.Ron = Math.Abs(row.Ron);
                    row.RsEff = Math.Abs(row.RsEff);
                    row.RdEff = Math.Abs(row.RdEff);
                }

                if ((double.IsNaN(row.Weff) || row.Weff <= 0) && w_m > 0)
                {
                    row.Weff = w_m;
                }

                if (double.IsNaN(row.Control) && double.IsNaN(row.Id) && double.IsNaN(row.Gm))
                {
                    continue;
                }

                exportRows.Add(row);
            }

            if (exportRows.Count == 0)
            {
                _state.AddMessage("No numeric samples parsed from results.");
                return CommandResult.Failure;
            }

            ExportRow? FindNeighbor(int index, int step)
            {
                for (int j = index + step; j >= 0 && j < exportRows.Count; j += step)
                {
                    var candidate = exportRows[j];
                    if (!double.IsNaN(candidate.Control) && !double.IsNaN(candidate.Id))
                    {
                        return candidate;
                    }
                }

                return null;
            }

            for (int i = 0; i < exportRows.Count; i++)
            {
                var row = exportRows[i];
                if (double.IsNaN(row.Gm))
                {
                    if (!double.IsNaN(row.GmOverIdRaw) && !double.IsNaN(row.Id))
                    {
                        row.Gm = row.GmOverIdRaw * row.Id;
                    }
                    else
                    {
                        var prev = FindNeighbor(i, -1);
                        var next = FindNeighbor(i, +1);
                        if (prev is not null && next is not null)
                        {
                            var dv = next.Control - prev.Control;
                            if (Math.Abs(dv) > 1e-30)
                            {
                                row.Gm = (next.Id - prev.Id) / dv;
                            }
                        }
                    }
                }

                if (double.IsNaN(row.GmOverIdRaw) && !double.IsNaN(row.Gm) && Math.Abs(row.Id) > 0)
                {
                    row.GmOverIdRaw = row.Gm / row.Id;
                }
            }

            bool Wants(string metric) => metricFilter.Count == 0 || metricFilter.Contains(metric);

            var optionalMetricOrder = new[]
            {
                "gm",
                "gmbs",
                "gds",
                "ro",
                "gm_over_id",
                "gm_ro",
                "vstar",
                "cgs",
                "cgd",
                "cgg",
                "gm_per_w",
                "id_per_w",
                "ft",
                "vth",
                "vdsat",
                "gmoverid",
                "ueff",
                "ron",
                "rseff",
                "rdeff",
                "w_eff"
            };

            var header = new List<string> { controlLabel, "vds", "id" };
            foreach (var metric in optionalMetricOrder)
            {
                if (Wants(metric))
                {
                    header.Add(metric);
                }
            }

            var outLines = new List<string> { string.Join(',', header) };

            foreach (var row in exportRows)
            {
                var gm = row.Gm;
                var gds = row.Gds;
                var gmOverId = !double.IsNaN(row.GmOverIdRaw) ? row.GmOverIdRaw :
                    (!double.IsNaN(row.Id) && Math.Abs(row.Id) > 0 ? row.Gm / row.Id : double.NaN);
                var ro = (!double.IsNaN(gds) && Math.Abs(gds) > 1e-30) ? 1.0 / gds : (!double.IsNaN(row.Ron) ? row.Ron : double.NaN);
                var gmRo = (!double.IsNaN(gm) && !double.IsNaN(ro)) ? gm * ro : double.NaN;
                var vstar = (!double.IsNaN(gm) && Math.Abs(gm) > 1e-30) ? (2.0 * row.Id) / gm : double.NaN;

                double totalCap = 0.0;
                if (!double.IsNaN(row.Cgs)) totalCap += Math.Abs(row.Cgs);
                if (!double.IsNaN(row.Cgd)) totalCap += Math.Abs(row.Cgd);
                var ft = (totalCap > 0 && !double.IsNaN(gm))
                    ? Math.Abs(gm) / (2.0 * Math.PI * totalCap)
                    : double.NaN;

                var gmPerW = (!double.IsNaN(gm) && row.Weff > 0) ? gm / row.Weff : double.NaN;
                var idPerW = (!double.IsNaN(row.Id) && row.Weff > 0) ? row.Id / row.Weff : double.NaN;

                var metrics = new Dictionary<string, double>
                {
                    ["gm"] = gm,
                    ["gmbs"] = row.Gmbs,
                    ["gds"] = gds,
                    ["ro"] = ro,
                    ["gm_over_id"] = gmOverId,
                    ["gm_ro"] = gmRo,
                    ["vstar"] = vstar,
                    ["cgs"] = row.Cgs,
                    ["cgd"] = row.Cgd,
                    ["cgg"] = row.Cgg,
                    ["gm_per_w"] = gmPerW,
                    ["id_per_w"] = idPerW,
                    ["ft"] = ft,
                    ["vth"] = row.Vth,
                    ["vdsat"] = row.Vdsat,
                    ["gmoverid"] = row.GmOverIdRaw,
                    ["ueff"] = row.Ueff,
                    ["ron"] = row.Ron,
                    ["rseff"] = row.RsEff,
                    ["rdeff"] = row.RdEff,
                    ["w_eff"] = row.Weff
                };

                var rowValues = new List<string>
                {
                    Format(row.Control),
                    Format(row.Vds),
                    Format(row.Id)
                };

                foreach (var metric in optionalMetricOrder)
                {
                    if (!Wants(metric))
                    {
                        continue;
                    }

                    metrics.TryGetValue(metric, out var val);
                    rowValues.Add(Format(val));
                }

                outLines.Add(string.Join(',', rowValues));
            }

            File.WriteAllLines(outFile, outLines);
            _state.AddMessage($"Exported derived metrics → {outFile}");
            return CommandResult.Success;
        }
        catch (Exception ex)
        {
            _state.AddMessage($"Export failed: {ex.Message}");
            return CommandResult.Failure;
        }
    }

    // Parse per-step oppoint + element files emitted by: sweep { dc; info what=oppoint where=file file="oppoint.%A"; element info what=inst where=file file="elem.%A" }
    private bool TryExportFromOppointFiles(string jobDir, bool isPmos, double specWidth, string controlLabel, out string csvPath, out string message)
    {
        csvPath = Path.Combine(jobDir, "results.csv");
        message = string.Empty;
        try
        {
            var oppFiles = Directory.EnumerateFiles(jobDir, "oppoint.*", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (oppFiles.Length == 0)
            {
                return false;
            }

            var elemFiles = Directory.EnumerateFiles(jobDir, "elem.*", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var rows = new List<string>();
            rows.Add(string.Join(',', new[]
            {
                controlLabel,
                "vds",
                "id",
                "gm",
                "gmbs",
                "gds",
                "vth",
                "vdsat",
                "cgs",
                "cgd",
                "cgg",
                "gmoverid",
                "ueff",
                "ron",
                "rseff",
                "rdeff",
                "w_eff"
            }));

            string? detectedInst = null;

            for (int n = 0; n < oppFiles.Length; n++)
            {
                if (!TryParseOppointAscii(oppFiles[n], detectedInst, out var op, out var matchedInst))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(matchedInst))
                {
                    detectedInst = matchedInst;
                }

                double control = GetOrNaN(op, controlLabel, "vgs", "vsg");
                double vds = GetOrNaN(op, "vds", "vd");
                double id = GetOrNaN(op, "ids", "id");
                double gm = GetOrNaN(op, "gm");
                double gmbs = GetOrNaN(op, "gmbs");
                double gds = GetOrNaN(op, "gds");
                double vth = GetOrNaN(op, "vth");
                double vdsat = GetOrNaN(op, "vdsat");
                double cgs = GetOrNaN(op, "cgs");
                double cgd = GetOrNaN(op, "cgd");
                double cgg = GetOrNaN(op, "cgg");
                double gmOverId = GetOrNaN(op, "gmoverid", "gm_over_id", "gm/id");
                double ueff = GetOrNaN(op, "ueff");
                double ron = GetOrNaN(op, "ron");
                double rseff = GetOrNaN(op, "rseff");
                double rdeff = GetOrNaN(op, "rdeff");

                if (double.IsNaN(control))
                {
                    control = GetOrNaN(op, "vgs", "vsg");
                }

                if (double.IsNaN(vds))
                {
                    vds = GetOrNaN(op, "vd");
                }

                if (double.IsNaN(id))
                {
                    id = GetOrNaN(op, "id");
                }

                double weff = double.NaN;
                if (elemFiles.Length == oppFiles.Length && n < elemFiles.Length)
                {
                    weff = TryGetWidthFromElemAscii(elemFiles[n], detectedInst);
                }
                if (double.IsNaN(weff) || weff <= 0) weff = Math.Abs(specWidth);

                if (isPmos)
                {
                    control = Math.Abs(control);
                    id = Math.Abs(id);
                    gm = Math.Abs(gm);
                    gmbs = Math.Abs(gmbs);
                    gds = Math.Abs(gds);
                    vth = Math.Abs(vth);
                    vdsat = Math.Abs(vdsat);
                    cgs = Math.Abs(cgs);
                    cgd = Math.Abs(cgd);
                    cgg = Math.Abs(cgg);
                    gmOverId = Math.Abs(gmOverId);
                    ueff = Math.Abs(ueff);
                    ron = Math.Abs(ron);
                    rseff = Math.Abs(rseff);
                    rdeff = Math.Abs(rdeff);
                }

                if (double.IsNaN(control) && double.IsNaN(id))
                {
                    continue;
                }

                var record = new[]
                {
                    Format(control),
                    Format(vds),
                    Format(id),
                    Format(gm),
                    Format(gmbs),
                    Format(gds),
                    Format(vth),
                    Format(vdsat),
                    Format(cgs),
                    Format(cgd),
                    Format(cgg),
                    Format(gmOverId),
                    Format(ueff),
                    Format(ron),
                    Format(rseff),
                    Format(rdeff),
                    Format(weff)
                };
                rows.Add(string.Join(',', record));
            }

            if (rows.Count <= 1)
            {
                message = "oppoint files parsed but no numeric rows assembled.";
                return false;
            }

            File.WriteAllLines(csvPath, rows);
            message = $"Built results.csv from per-step oppoint files ({rows.Count - 1} samples).";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Failed to build results from oppoint files: {ex.Message}";
            return false;
        }

        static double GetOrNaN(Dictionary<string,double> dict, params string[] keys)
        {
            foreach (var k in keys)
            {
                if (dict.TryGetValue(k, out var v)) return v;
            }
            return double.NaN;
        }

        static string Format(double value) =>
            double.IsNaN(value) ? string.Empty : value.ToString("G", CultureInfo.InvariantCulture);
    }


    private static (List<string> Fields, bool Ok) LoadPsfInfoTypeFields(string path, string structName)
    {
        var fields = new List<string>();
        try
        {
            var lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("TYPE", StringComparison.OrdinalIgnoreCase))
                {
                    // Scan forward for the structName definition
                    for (int j = i + 1; j < lines.Length; j++)
                    {
                        var t = lines[j].Trim();
                        if (t.StartsWith('"') && t.Contains('"'))
                        {
                            var firstQuote = t.IndexOf('"');
                            var secondQuote = t.IndexOf('"', firstQuote + 1);
                            if (secondQuote > firstQuote)
                            {
                                var name = t.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
                                if (string.Equals(name, structName, StringComparison.OrdinalIgnoreCase))
                                {
                                    // Expect: "bsim4" STRUCT(
                                    // Next lines contain "field" TYPE ... until a closing )
                                    // Collect field names in order of appearance
                                    for (int k = j + 1; k < lines.Length; k++)
                                    {
                                        var u = lines[k].Trim();
                                        if (u.StartsWith(")"))
                                        {
                                            return (fields, fields.Count > 0);
                                        }
                                        if (u.StartsWith('"'))
                                        {
                                            var q1 = u.IndexOf('"');
                                            var q2 = u.IndexOf('"', q1 + 1);
                                            if (q2 > q1)
                                            {
                                                var field = u.Substring(q1 + 1, q2 - q1 - 1);
                                                fields.Add(field);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch { }
        return (fields, false);
    }

    private static List<double>? LoadFirstRecordValues(string path, string structName)
    {
        try
        {
            var lines = File.ReadAllLines(path);
            bool inData = false;
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (!inData)
                {
                    if (line.Equals("END", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                    // Look for a record like: "NM0...." "bsim4" (
                    if (line.StartsWith('"') && line.Contains(structName, StringComparison.OrdinalIgnoreCase) && line.Contains("\" " + structName + "\"", StringComparison.Ordinal))
                    {
                        // Advance to line containing '(' then parse until ')'
                        // The current line typically ends with (
                        var values = new List<double>();
                        // Move to next line which should start numeric values
                        for (int k = i + 1; k < lines.Length; k++)
                        {
                            var v = lines[k].Trim();
                            if (v.StartsWith(")"))
                            {
                                return values;
                            }
                            foreach (var tok in v.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                if (double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
                                {
                                    values.Add(num);
                                }
                            }
                        }
                        return values;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    private static double TryGetWidthFromElem(string elemPath)
    {
        try
        {
            var lines = File.ReadAllLines(elemPath);
            // Find first numeric group following a "bsim4~instparams" record
            for (int i = 0; i < lines.Length; i++)
            {
                var t = lines[i].Trim();
                if (t.Contains("\"bsim4~instparams\"", StringComparison.Ordinal))
                {
                    // TYPE section above already lists field order; but for width we can search after '(' for the first occurrence of a numeric line after a field named "w"
                    // For simplicity, scan forward for a line with a single number that is plausibly a width (<= 0.1)
                    for (int k = i + 1; k < lines.Length; k++)
                    {
                        var v = lines[k].Trim();
                        if (v.StartsWith(")")) break;
                        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
                        {
                            if (num > 0 && num < 0.1) return num;
                        }
                    }
                }
            }
        }
        catch { }
        return double.NaN;
    }

    // New helpers for ASCII oppoint/element parsing
    private static double TryGetWidthFromElemAscii(string elemPath, string? preferredInst)
    {
        try
        {
            var lines = File.ReadAllLines(elemPath);
            string? matchedInst = null;
            bool capturing = string.IsNullOrWhiteSpace(preferredInst);

            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.StartsWith("Instance:", StringComparison.OrdinalIgnoreCase))
                {
                    var current = ParseInstanceName(line);
                    if (matchedInst is null)
                    {
                        if (string.IsNullOrWhiteSpace(preferredInst) ||
                            (!string.IsNullOrWhiteSpace(preferredInst) &&
                             (line.IndexOf(preferredInst, StringComparison.OrdinalIgnoreCase) >= 0 ||
                              (current is not null && current.Equals(preferredInst, StringComparison.OrdinalIgnoreCase)))))
                        {
                            matchedInst = current ?? preferredInst;
                            capturing = true;
                        }
                        else
                        {
                            capturing = false;
                        }
                    }
                    else
                    {
                        if (current is not null && matchedInst.Equals(current, StringComparison.OrdinalIgnoreCase))
                        {
                            capturing = true;
                        }
                        else
                        {
                            break;
                        }
                    }
                    continue;
                }

                if (!capturing)
                {
                    continue;
                }

                if (line.StartsWith("w =", StringComparison.OrdinalIgnoreCase) || line.StartsWith("W =", StringComparison.OrdinalIgnoreCase))
                {
                    var val = line[(line.IndexOf('=') + 1)..].Trim();
                    if (TryParseWithUnits(val, out var meters)) return meters;
                }
                else if (line.StartsWith("weff", StringComparison.OrdinalIgnoreCase))
                {
                    var val = line[(line.IndexOf('=') + 1)..].Trim();
                    if (TryParseWithUnits(val, out var meters)) return meters;
                }
            }
        }
        catch { }
        return double.NaN;
    }

    private static string? ParseInstanceName(string line)
    {
        var colon = line.IndexOf(':');
        if (colon < 0 || colon + 1 >= line.Length)
        {
            return null;
        }

        var remainder = line[(colon + 1)..].Trim();
        if (string.IsNullOrEmpty(remainder))
        {
            return null;
        }

        var end = remainder.IndexOfAny(new[] { ' ', '\t', '(' });
        return end >= 0 ? remainder[..end] : remainder;
    }

    private sealed class ExportRow
    {
        public double Control;
        public double Vds;
        public double Id;
        public double Gm;
        public double Gmbs;
        public double Gds;
        public double Vth;
        public double Vdsat;
        public double Cgs;
        public double Cgd;
        public double Cgg;
        public double GmOverIdRaw;
        public double Ueff;
        public double Ron;
        public double RsEff;
        public double RdEff;
        public double Weff;
    }

    private static bool TryParseOppointAscii(string path, string? preferredInst, out Dictionary<string,double> values, out string? matchedInst)
    {
        values = new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
        matchedInst = null;
        try
        {
            var lines = File.ReadAllLines(path);
            string? currentInst = null;
            bool capturing = string.IsNullOrWhiteSpace(preferredInst);

            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.StartsWith("Instance:", StringComparison.OrdinalIgnoreCase))
                {
                    currentInst = ParseInstanceName(line);
                    if (matchedInst is null)
                    {
                        if (string.IsNullOrWhiteSpace(preferredInst) ||
                            (!string.IsNullOrWhiteSpace(preferredInst) &&
                             (line.IndexOf(preferredInst, StringComparison.OrdinalIgnoreCase) >= 0 ||
                              (currentInst is not null && currentInst.Equals(preferredInst, StringComparison.OrdinalIgnoreCase)))))
                        {
                            capturing = true;
                            matchedInst = currentInst ?? preferredInst;
                        }
                        else
                        {
                            capturing = false;
                        }
                    }
                    else
                    {
                        if (currentInst is not null && matchedInst.Equals(currentInst, StringComparison.OrdinalIgnoreCase))
                        {
                            capturing = true;
                        }
                        else
                        {
                            break;
                        }
                    }
                    continue;
                }

                if (!capturing || string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var eq = line.IndexOf('=');
                if (eq < 1) continue;
                var name = line[..eq].Trim().TrimEnd(':').ToLowerInvariant();
                var rhs = line[(eq + 1)..].Trim();
                if (TryParseWithUnits(rhs, out var num)) values[name] = num;
            }

            return values.Count > 0;
        }
        catch { }
        return false;
    }

    private static bool TryParseWithUnits(string text, out double value)
    {
        value = double.NaN;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Replace("Ohm", "", StringComparison.OrdinalIgnoreCase).Trim();
        var parts = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        if (parts[0].Equals("inf", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("infinity", StringComparison.OrdinalIgnoreCase))
        { value = double.PositiveInfinity; return true; }
        if (parts[0].Equals("nan", StringComparison.OrdinalIgnoreCase)) { value = double.NaN; return true; }
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num)) return false;
        double scale = 1.0;
        if (parts.Length >= 2) scale = SiScale(parts[1]);
        value = num * scale;
        return true;
    }

    private static double SiScale(string unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return 1.0;
        unit = unit.Trim();
        char p = unit[0];
        return p switch
        {
            'T' => 1e12,
            'G' => 1e9,
            'M' => 1e6,
            'k' => 1e3,
            'm' => 1e-3,
            'u' or 'µ' => 1e-6,
            'n' => 1e-9,
            'p' => 1e-12,
            'f' => 1e-15,
            'a' => 1e-18,
            _ => 1.0
        };
    }
    private CommandResult CharacterizationGenerate(string[] args)
    {
        // Parse args
        var modelName = args[0];
        var backend = "ngspice";
        string? outDir = null;
        string? corner = null;
        string harness = "gm_id.v1";
        var userParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 1; i < args.Length; i++)
        {
            var a = args[i];
            if (a.Equals("--backend", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                backend = args[++i];
            }
            else if (a.Equals("--out", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                outDir = args[++i];
            }
            else if (a.Equals("--corner", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                corner = args[++i];
            }
            else if (a.Equals("--harness", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                harness = args[++i];
            }
            else if (a.Equals("--param", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                var kv = args[++i];
                var eq = kv.IndexOf('=');
                if (eq > 0)
                {
                    var key = kv[..eq].Trim();
                    var value = kv[(eq + 1)..].Trim();
                    if (key.Length > 0) userParams[key] = value;
                }
            }
        }

        var scan = EnsureScan();
        if (scan is null)
        {
            return CommandResult.Failure;
        }

        var model = scan.Models.FirstOrDefault(m => string.Equals(m.Name, modelName, StringComparison.OrdinalIgnoreCase))
                   ?? scan.Models.FirstOrDefault(m => m.Name.Contains(modelName, StringComparison.OrdinalIgnoreCase));
        if (model is null)
        {
            _state.AddMessage("Model not found.");
            return CommandResult.Failure;
        }

        var jobRoot = outDir ?? Path.Combine(_state.WorkspaceRoot, "build", "char", Sanitize(model.Name), DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(jobRoot);

        // Apply known --param overrides (parse before spec creation)
        double TryParseDouble(string key, double fallback)
            => userParams.TryGetValue(key, out var s) && double.TryParse(s, out var v) ? v : fallback;
        int TryParseInt(string key, int fallback)
            => userParams.TryGetValue(key, out var s) && int.TryParse(s, out var v) ? v : fallback;

        var w_m = TryParseDouble("w", TryParseDouble("w_m", 1e-6));
        var l_m = TryParseDouble("l", TryParseDouble("l_m", 0.18e-6));
        var vsbVal = TryParseDouble("vsb", 0.0);
        var vdsVal = TryParseDouble("vds", 0.9);
        var start = TryParseDouble("start", 0.0);
        var stop = TryParseDouble("stop", 1.2);
        var step = TryParseDouble("step", 0.01);
        var multVal = TryParseInt("mult", 1);
        var nfVal = TryParseInt("nf", 1);

        static string? TryNormalizeInclude(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                return NormalizePath(path);
            }
            catch
            {
                return File.Exists(path) ? Path.GetFullPath(path) : null;
            }
        }

        var rawDecks = model.Decks ?? Array.Empty<string>();
        var decksWithSection = rawDecks
            .Select(TryNormalizeInclude)
            .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p!))
            .Select(p => p!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<string> extraIncludes = new();
        var sourceIncludesAll = (model.SourceFiles ?? Array.Empty<string>())
            .Select(TryNormalizeInclude)
            .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p!))
            .Select(p => p!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // If a corner is selected, prefer only files that match the corner tag (e.g., __tt, _tt.)
        List<string> sourceIncludes = sourceIncludesAll;
        if (!string.IsNullOrWhiteSpace(corner))
        {
            var key = corner!.Trim();
            sourceIncludes = sourceIncludesAll
                .Where(p => Path.GetFileName(p)!.IndexOf($"_{key}", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
        }

        if (decksWithSection.Count == 0)
        {
            // Fallback for raw models when the deck (with section) could not be resolved
            extraIncludes = sourceIncludes;
        }
        else
        {
            // When a main deck (with section) is present, rely on it exclusively to avoid double-definitions
            extraIncludes.Clear();
        }

        var resolvedIncludes = new List<string>(decksWithSection.Count + extraIncludes.Count);
        resolvedIncludes.AddRange(decksWithSection);
        resolvedIncludes.AddRange(extraIncludes);

        static string ResolveModelNameForNetlist(SpectreModel m)
        {
            var name = m.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            var modelMarker = name.IndexOf("__model", StringComparison.OrdinalIgnoreCase);
            if (modelMarker < 0)
            {
                return name;
            }

            var basePart = name.Substring(0, modelMarker);
            var lastSeparator = basePart.LastIndexOf("__", StringComparison.Ordinal);
            if (lastSeparator >= 0 && lastSeparator + 2 < basePart.Length)
            {
                basePart = basePart[(lastSeparator + 2)..];
            }

            return basePart.Replace('.', '_');
        }

        var netlistModelName = ResolveModelNameForNetlist(model);

        if (resolvedIncludes.Count == 0)
        {
            _state.AddMessage($"[warn] No include decks located for model '{model.Name}'. Spectre run may fail.");
        }

        var spec = new Cascode.Bench.TestbenchSpec
        {
            Backend = backend.Equals("spectre", StringComparison.OrdinalIgnoreCase) ? Cascode.Bench.BenchBackendType.Spectre : Cascode.Bench.BenchBackendType.Ngspice,
            Name = harness,
            ModelName = netlistModelName,
            IsSubckt = string.Equals(model.ModelType, "subckt", StringComparison.OrdinalIgnoreCase),
            Corner = corner,
            TemperatureC = 27,
            SupplyV = 0,
            W_M = w_m,
            L_M = l_m,
            Mult = multVal,
            Nfingers = nfVal,
            Vgs = new Cascode.Bench.SweepSpec(start, stop, step),
            Vds = vdsVal,
            Vsb = vsbVal,
            Includes = resolvedIncludes,
            Section = corner,
            JobDir = jobRoot,
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
            Args = userParams.ToDictionary(kv => kv.Key, kv => (object?)kv.Value, StringComparer.OrdinalIgnoreCase),
        };

        try
        {
            var reg = Cascode.Bench.HarnessService.CreateDefault(_state.WorkspaceRoot);
            if (Environment.GetEnvironmentVariable("CASCODE_DEBUG") == "1")
            {
                if (reg.TryGet(harness, out var h))
                {
                    _state.AddMessage($"[debug] using harness '{harness}' type: {h.GetType().FullName}");
                }
                else
                {
                    _state.AddMessage($"[debug] harness '{harness}' not found in registry");
                }
            }
            var gen = new Cascode.Bench.TestbenchGenerator(reg);
            var files = gen.Generate(ctx);
            _state.AddMessage($"Generated testbench: {files.NetlistPath}");
            _state.AddMessage($"Spec: {files.SpecPath}");
            _state.AddMessage("Run your simulator manually and then 'char export' to derive gm/Id.");
            return CommandResult.Success;
        }
        catch (Exception ex)
        {
            _state.AddMessage($"Generation failed: {ex.Message}");
            return CommandResult.Failure;
        }
    }

    private static string? ResolveHarnessForModel(SpectreModel model)
    {
        return model.DeviceClass switch
        {
            SpectreModelDeviceClass.Nmos => "gm_id.v1",
            SpectreModelDeviceClass.Pmos => "gm_id_pmos.v1",
            _ => null
        };
    }

    private IEnumerable<string> GetDefaultParamArguments(SpectreModel model, string harnessId)
    {
        if (string.Equals(harnessId, "gm_id.v1", StringComparison.OrdinalIgnoreCase))
        {
            yield return "w=2e-6";
            yield return "l=4.5e-8";
            yield return "drain_bias_mode=scaled";
            yield return "drain_alpha=0.5";
            yield return "inst_name=NM0";
        }
        else if (string.Equals(harnessId, "gm_id_pmos.v1", StringComparison.OrdinalIgnoreCase))
        {
            yield return "w=2e-6";
            yield return "l=4.5e-8";
            yield return "drain_bias_mode=scaled";
            yield return "drain_alpha=0.5";
            yield return "vdd=1.0";
            yield return "vsd=0.9";
            yield return "inst_name=PM0";
        }
    }

    private static string? TryDetectSpectreBin()
    {
        var spectreHome = Environment.GetEnvironmentVariable("SPECTRE_HOME");
        if (string.IsNullOrWhiteSpace(spectreHome))
        {
            return null;
        }

        var candidates = new[]
        {
            Path.Combine(spectreHome, "bin", "spectre"),
            Path.Combine(spectreHome, "tools", "bin", "spectre")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private bool TryRunSpectre(string jobDir, string backend)
    {
        if (!backend.Equals("spectre", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var spectreEnv = Environment.GetEnvironmentVariable("SPECTRE_BIN");
        var spectreBin = ResolveSpectreExecutable(spectreEnv);
        if (spectreBin is null)
        {
            if (!string.IsNullOrWhiteSpace(spectreEnv))
            {
                _state.AddMessage($"SPECTRE_BIN points to '{spectreEnv}', but no spectre binary was found there.");
            }

            var detected = TryDetectSpectreBin();
            spectreBin = ResolveSpectreExecutable(detected);
            if (spectreBin is null)
            {
                _state.AddMessage("SPECTRE_BIN not set and could not auto-detect spectre executable from SPECTRE_HOME; skipping Spectre execution.");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(detected))
            {
                _state.AddMessage($"Auto-detected Spectre at {spectreBin}");
            }
        }

        var netlist = Directory.EnumerateFiles(jobDir, "*.scs").FirstOrDefault();
        if (netlist is null)
        {
            _state.AddMessage("Spectre netlist (.scs) not found; skipping run.");
            return false;
        }

        try
        {
            var logBuilder = new StringBuilder();
            var psi = new ProcessStartInfo
            {
                FileName = spectreBin,
                Arguments = $"-format nutascii -raw raw \"{netlist}\"",
                WorkingDirectory = jobDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                _state.AddMessage("Failed to launch Spectre process.");
                return false;
            }

            process.OutputDataReceived += (_, e) => { if (e.Data is not null) logBuilder.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) logBuilder.AppendLine(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            var logPath = Path.Combine(jobDir, "spectre.log");
            File.WriteAllText(logPath, logBuilder.ToString());

            if (process.ExitCode != 0)
            {
                _state.AddMessage($"Spectre exited with code {process.ExitCode}. See {logPath}.");
                return false;
            }

            _state.AddMessage($"Spectre completed → {Path.GetFileName(jobDir)}");
            return true;
        }
        catch (Exception ex)
        {
            _state.AddMessage($"Spectre run failed: {ex.Message}");
            return false;
        }
    }

    // Attempt to build results.csv by parsing Spectre nutascii raw output (written to ./raw by TryRunSpectre)
    private static bool TryBuildResultsCsvFromNutascii(string jobDir, bool isPmos, out string message)
    {
        message = string.Empty;
        try
        {
            var rawRoot = Path.Combine(jobDir, "raw");
            var candidates = new List<string>();
            if (Directory.Exists(rawRoot))
            {
                candidates.AddRange(Directory.EnumerateFiles(rawRoot, "*", SearchOption.AllDirectories));
            }
            else if (File.Exists(rawRoot))
            {
                candidates.Add(rawRoot);
            }
            // Fallback: look in jobDir directly
            candidates.AddRange(Directory.EnumerateFiles(jobDir, "*.raw", SearchOption.TopDirectoryOnly));
            var plainRaw = Path.Combine(jobDir, "raw");
            if (File.Exists(plainRaw) && !candidates.Contains(plainRaw, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(plainRaw);
            }

            // Quick probe for nutascii signature
            bool LooksLikeNutAscii(string path)
            {
                try
                {
                    using var sr = new StreamReader(path);
                    for (int i = 0; i < 8; i++)
                    {
                        var line = sr.ReadLine();
                        if (line is null) break;
                        if (line.StartsWith("Title:", StringComparison.OrdinalIgnoreCase)) return true;
                    }
                }
                catch { }
                return false;
            }

            var rawFiles = candidates
                .Where(path => LooksLikeNutAscii(path))
                .ToList();

            if (rawFiles.Count == 0)
            {
                message = "No Spectre nutascii raw output found to recover results.";
                return false;
            }

            // Prefer a file whose header mentions dc
            string? chosen = null;
            foreach (var f in rawFiles)
            {
                try
                {
                    var header = File.ReadLines(f).Take(6).ToArray();
                    if (header.Any(l => l.Contains("Plotname:", StringComparison.OrdinalIgnoreCase) && l.Contains("dc", StringComparison.OrdinalIgnoreCase)))
                    {
                        chosen = f; break;
                    }
                }
                catch { }
            }
            chosen ??= rawFiles[0];

            // Parse nutascii
            var allLines = File.ReadAllLines(chosen);
            int varCount = 0;
            int valuesStart = -1;
            var names = new List<string>();
            for (int i = 0; i < allLines.Length; i++)
            {
                var line = allLines[i].Trim();
                if (line.StartsWith("No. Variables:", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(':', StringSplitOptions.TrimEntries);
                    if (parts.Length == 2 && int.TryParse(parts[1], out var n)) varCount = n;
                }
                else if (line.StartsWith("Variables:", StringComparison.OrdinalIgnoreCase))
                {
                    // Next varCount lines hold index, name, type
                    for (int j = 0; j < varCount && i + 1 + j < allLines.Length; j++)
                    {
                        var vline = allLines[i + 1 + j].Trim();
                        var toks = vline.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (toks.Length >= 3) names.Add(toks[1].Trim());
                    }
                }
                else if (line.StartsWith("Values:", StringComparison.OrdinalIgnoreCase))
                {
                    valuesStart = i + 1; break;
                }
            }

            if (varCount <= 0 || names.Count != varCount || valuesStart < 0)
            {
                message = $"Unexpected nutascii format in {Path.GetFileName(chosen)}.";
                return false;
            }

            // Tokenize all numeric values after Values:
            var nums = new List<double>();
            for (int i = valuesStart; i < allLines.Length; i++)
            {
                var line = allLines[i];
                var toks = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var t in toks)
                {
                    if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    {
                        nums.Add(v);
                    }
                }
            }

            var rowWidth = varCount + 1; // index + N variables per point
            if (nums.Count < rowWidth)
            {
                message = $"Not enough samples found in {Path.GetFileName(chosen)}.";
                return false;
            }

            int idxVar(string key) => names.FindIndex(n => string.Equals(n, key, StringComparison.OrdinalIgnoreCase));
            int idxContains(params string[] parts)
                => names.FindIndex(n => parts.All(p => n.Contains(p, StringComparison.OrdinalIgnoreCase)));

            // Node voltages: accept either v(g)/v(s)/v(d) or plain g/s/d
            int findNode(string vname, string plain)
            {
                var idx = idxVar(vname);
                if (idx >= 0) return idx;
                return idxVar(plain);
            }

            var ig = findNode("v(g)", "g");
            var isrc = findNode("v(s)", "s");
            var idn = findNode("v(d)", "d");
            // Drain/source branch currents
            var iVdr = Math.Max(idxVar("i(vdr)"), idxContains("vdr", "branch"));
            var iVsd = Math.Max(idxVar("i(vsd)"), idxContains("vsd", "branch"));
            // Device drain current (prefer <inst>:d if present; else first MOS current)
            int iIMos = -1;
            int iIdTerm = -1;
            for (int k = 0; k < names.Count; k++)
            {
                var nm = names[k];
                if (nm.EndsWith(":d", StringComparison.OrdinalIgnoreCase))
                {
                    iIdTerm = k;
                }
                if (iIMos < 0 && nm.StartsWith("i(m", StringComparison.OrdinalIgnoreCase))
                {
                    iIMos = k;
                }
            }

            if (ig < 0 || isrc < 0 || idn < 0)
            {
                message = "Required variables v(g), v(s), v(d) not found in raw output.";
                return false;
            }

            int points = nums.Count / rowWidth;
            var sb = new List<string>();
            sb.Add(isPmos ? "vsg,vd,id" : "vgs,vd,id");
            for (int p = 0; p < points; p++)
            {
                int baseIdx = p * rowWidth + 1; // skip row index
                double vg = nums[baseIdx + ig];
                double vs = nums[baseIdx + isrc];
                double vd = nums[baseIdx + idn];
                double id;
                if (isPmos)
                {
                    // Use VSD supply current if available; else fallback to sign from VDR if present
                    double cur = double.NaN;
                    if (iVsd >= 0) cur = nums[baseIdx + iVsd];
                    else if (iVdr >= 0) cur = nums[baseIdx + iVdr];
                    else if (iIdTerm >= 0) cur = nums[baseIdx + iIdTerm];
                    else if (iIMos >= 0) cur = nums[baseIdx + iIMos];
                    id = double.IsNaN(cur) ? double.NaN : -cur; // -I(VSD) per convention
                    var vsg = vs - vg;
                    sb.Add(string.Join(',', vsg.ToString(CultureInfo.InvariantCulture), vd.ToString(CultureInfo.InvariantCulture), id.ToString(CultureInfo.InvariantCulture)));
                }
                else
                {
                    double cur = double.NaN;
                    if (iVdr >= 0) cur = nums[baseIdx + iVdr];
                    else if (iVsd >= 0) cur = nums[baseIdx + iVsd];
                    else if (iIdTerm >= 0) cur = nums[baseIdx + iIdTerm];
                    else if (iIMos >= 0) cur = nums[baseIdx + iIMos];
                    // Keep -I(VDR) convention when VDR exists; if using device current, treat positive as drain current flowing into device
                    var useDeviceCurrent = iIMos >= 0 && iVdr < 0 && iVsd < 0;
                    id = double.IsNaN(cur) ? double.NaN : (useDeviceCurrent ? cur : -cur);
                    var vgs = vg - vs;
                    sb.Add(string.Join(',', vgs.ToString(CultureInfo.InvariantCulture), vd.ToString(CultureInfo.InvariantCulture), id.ToString(CultureInfo.InvariantCulture)));
                }
            }

            var outCsv = Path.Combine(jobDir, "results.csv");
            File.WriteAllLines(outCsv, sb);
            message = $"Recovered results.csv from Spectre raw: {Path.GetFileName(chosen)}";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Failed to recover results from Spectre raw: {ex.Message}";
            return false;
        }
    }

    private static (List<string> Headers, List<double[]> Samples) LoadDerivedCsv(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length == 0)
        {
            return (new List<string>(), new List<double[]>());
        }

        var headers = lines[0].Split(',', StringSplitOptions.TrimEntries).ToList();
        var samples = new List<double[]>();
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split(',', StringSplitOptions.None);
            var values = new double[headers.Count];
            for (var j = 0; j < headers.Count; j++)
            {
                if (j < parts.Length && double.TryParse(parts[j], NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
                {
                    values[j] = val;
                }
                else
                {
                    values[j] = double.NaN;
                }
            }
            samples.Add(values);
        }

        return (headers, samples);
    }

    private static (int Index, string Name) FindColumn(IReadOnlyList<string> headers, params string[] aliases)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            var header = headers[i];
            foreach (var alias in aliases)
            {
                if (header.Equals(alias, StringComparison.OrdinalIgnoreCase))
                {
                    return (i, header);
                }
            }
        }
        return (-1, aliases.FirstOrDefault() ?? string.Empty);
    }

    private static double[] BuildSeries(IReadOnlyList<double[]> samples, int columnIndex)
    {
        var result = new double[samples.Count];
        for (var i = 0; i < samples.Count; i++)
        {
            var value = (columnIndex >= 0 && columnIndex < samples[i].Length) ? samples[i][columnIndex] : double.NaN;
            result[i] = double.IsFinite(value) ? value : 0.0;
        }
        return result;
    }

    private static void RenderSparkline(IReadOnlyList<double[]> samples, int columnIndex, string label)
    {
        var series = BuildSeries(samples, columnIndex);
        var finite = series.Where(double.IsFinite).ToList();
        if (finite.Count == 0)
        {
            return;
        }

        var min = finite.Min();
        var max = finite.Max();
        if (Math.Abs(max - min) < 1e-12)
        {
            max = min + 1e-12;
        }

        var glyphs = "▁▂▃▄▅▆▇█";
        var spark = new StringBuilder();
        foreach (var value in series)
        {
            var idx = 0;
            if (double.IsFinite(value))
            {
                var normalized = (value - min) / (max - min);
                normalized = Math.Clamp(normalized, 0.0, 1.0);
                idx = (int)Math.Round(normalized * (glyphs.Length - 1));
            }
            spark.Append(glyphs[idx]);
        }

        AnsiConsole.MarkupLine($"[cyan]{label}[/]: {spark} [grey](min {FormatNumber(min)} / max {FormatNumber(max)})[/]");
    }

    private static string FormatNumber(double value)
    {
        if (!double.IsFinite(value))
        {
            return string.Empty;
        }

        var abs = Math.Abs(value);
        if (abs >= 1e3 || (abs > 0 && abs < 1e-3))
        {
            return value.ToString("0.###E+0", CultureInfo.InvariantCulture);
        }

        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    private CommandResult CharacterizationRead(string[] args)
    {
        var jobDir = NormalizePath(args[0]);
        var head = 20;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i].Equals("--head", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                int.TryParse(args[++i], out head);
            }
        }

        var csv = Path.Combine(jobDir, "results.csv");
        if (!File.Exists(csv))
        {
            _state.AddMessage($"Results file not found: {csv}");
            return CommandResult.Failure;
        }

        try
        {
            using var reader = new StreamReader(csv);
            for (var i = 0; i < head && !reader.EndOfStream; i++)
            {
                _state.AddMessage(reader.ReadLine() ?? string.Empty);
            }
            if (!reader.EndOfStream) _state.AddMessage("…");
            return CommandResult.Success;
        }
        catch (Exception ex)
        {
            _state.AddMessage($"Failed to read: {ex.Message}");
            return CommandResult.Failure;
        }
    }

    private void TryLoadCachedScan(string workspaceRoot, bool logFailure)
    {
        var scanPath = WorkspaceState.GetScanPath(workspaceRoot);
        if (!File.Exists(scanPath))
        {
            return;
        }

        try
        {
            var scan = _storage.Load(scanPath);
            _state.Scan = scan;
            _state.SelectedDeckIndex = scan.ModelDecks.Count > 0 ? 0 : null;
        }
        catch (Exception ex)
        {
            if (logFailure)
            {
                _state.AddMessage($"Failed to load cached scan: {ex.Message}");
            }
        }
    }

    private void FlushLogToConsole()
    {
        foreach (var message in _state.Messages)
        {
            Console.WriteLine(message);
        }
    }

    private CommandResult HandleLog(string[] args)
    {
        if (args.Length == 0)
        {
            _state.AddMessage("Usage: log <up|down|pageup|pagedown|top|bottom> [count]");
            return CommandResult.Success;
        }

        var action = args[0].ToLowerInvariant();
        var defaultStep = Math.Max(1, _state.LogViewport / 4);
        var count = defaultStep;
        if (args.Length > 1 && int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            count = Math.Max(1, parsed);
        }

        switch (action)
        {
            case "up":
                _state.ScrollLogUp(count);
                break;
            case "down":
                _state.ScrollLogDown(count);
                break;
            case "pageup":
                _state.ScrollLogUp(_state.LogViewport);
                break;
            case "pagedown":
                _state.ScrollLogDown(_state.LogViewport);
                break;
            case "top" or "home":
                _state.ScrollLogHome();
                break;
            case "bottom" or "end":
                _state.ScrollLogEnd();
                break;
            default:
                _state.AddMessage($"Unknown log action '{action}'.");
                return CommandResult.Failure;
        }

        return CommandResult.Success;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path must not be empty", nameof(path));
        }

        var expanded = ExpandHomePath(path);
        return Path.GetFullPath(expanded);
    }

    private static string ExpandHomePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("~", StringComparison.Ordinal))
        {
            return path;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
        {
            return path;
        }

        if (path.Length == 1)
        {
            return home;
        }

        var remainder = path[1..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.Combine(home, remainder);
    }

    private CommandResult UnknownCommand(IReadOnlyList<string> tokens, int matchedLength)
    {
        var typed = string.Join(' ', tokens);
        _state.AddMessage($"Unknown command '{typed}'. Type 'help' for a list.");

        if (matchedLength > 0)
        {
            var prefix = tokens.Take(matchedLength).ToArray();
            var suggestions = _commands.GetSubcommands(prefix).ToArray();
            if (suggestions.Length > 0)
            {
                _state.AddMessage("Available subcommands:");
                var width = suggestions.Max(s => s.DisplayPath.Length);
                foreach (var suggestion in suggestions)
                {
                    var padded = width > 0 ? suggestion.DisplayPath.PadRight(width) : suggestion.DisplayPath;
                    var description = string.IsNullOrEmpty(suggestion.Description) ? string.Empty : $"  {suggestion.Description}";
                    _state.AddMessage($"  {padded}{description}");
                }
            }
        }

        return CommandResult.Failure;
    }

    private WorkspaceScanResult? EnsureScan()
    {
        if (_state.Scan is not null)
        {
            return _state.Scan;
        }

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

    private static string? ResolveSpectreExecutable(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var candidate = Environment.ExpandEnvironmentVariables(input.Trim());
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        if (File.Exists(candidate))
        {
            return candidate;
        }

        if (Directory.Exists(candidate))
        {
            foreach (var guess in EnumerateSpectreGuesses(candidate))
            {
                if (File.Exists(guess))
                {
                    return guess;
                }
            }
        }
        else if (!candidate.Contains(Path.DirectorySeparatorChar) && !candidate.Contains(Path.AltDirectorySeparatorChar))
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrWhiteSpace(pathEnv))
            {
                foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var full = Path.Combine(dir, candidate);
                    if (File.Exists(full))
                    {
                        return full;
                    }
                }
            }
        }

        return null;

        static IEnumerable<string> EnumerateSpectreGuesses(string root)
        {
            yield return Path.Combine(root, "spectre");
            yield return Path.Combine(root, "bin", "spectre");
            yield return Path.Combine(root, "tools", "bin", "spectre");
            yield return Path.Combine(root, "tools.lnx86", "bin", "spectre");
        }
    }

    private static string FormatList(IEnumerable<string> values)
    {
        var distinct = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (distinct.Length == 0)
        {
            return "-";
        }

        if (distinct.Length <= 5)
        {
            return string.Join(", ", distinct);
        }

        return string.Join(", ", distinct.Take(5)) + $" … ({distinct.Length - 5} more)";
    }

    private void RenderModelsToLog(IReadOnlyList<SpectreModel> models)
    {
        if (models.Count == 0)
        {
            return;
        }

        const int nameWidth = 32;
        const int classWidth = 10;
        const int vtWidth = 5;
        const int vddWidth = 6;
        const int cornerWidth = 18;

        var header = string.Format(
            "{0,4} {1,-" + nameWidth + "} {2,-" + classWidth + "} {3,-" + vtWidth + "} {4,-" + vddWidth + "} {5,-" + cornerWidth + "}",
            "#",
            "Model",
            "Class",
            "VT",
            "VDD",
            "Corners");

        _state.AddMessage(header);
        _state.AddMessage(new string('-', Math.Min(header.Length, 80)));

        for (var i = 0; i < models.Count; i++)
        {
            var model = models[i];
            var classLabel = model.DeviceClass == SpectreModelDeviceClass.Unknown ? "Unknown" : model.DeviceClass.ToString();
            var vtLabel = string.IsNullOrWhiteSpace(model.ThresholdFlavor) ? "-" : model.ThresholdFlavor!;
            var vddLabel = string.IsNullOrWhiteSpace(model.VoltageDomain) ? "-" : model.VoltageDomain!;
            var cornerLabel = model.Corners.Count == 0
                ? "-"
                : string.Join(",", model.Corners.Take(2)) + (model.Corners.Count > 2 ? "…" : string.Empty);

            var line = string.Format(
                "{0,4} {1,-" + nameWidth + "} {2,-" + classWidth + "} {3,-" + vtWidth + "} {4,-" + vddWidth + "} {5,-" + cornerWidth + "}",
                i + 1,
                TruncateWithEllipsis(model.Name, nameWidth),
                TruncateWithEllipsis(classLabel, classWidth),
                TruncateWithEllipsis(vtLabel, vtWidth),
                TruncateWithEllipsis(vddLabel, vddWidth),
                TruncateWithEllipsis(cornerLabel, cornerWidth));

            _state.AddMessage(line);
        }
    }

    private static string TruncateWithEllipsis(string value, int maxWidth)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxWidth)
        {
            return value ?? string.Empty;
        }

        if (maxWidth <= 1)
        {
            return value.Substring(0, Math.Max(0, maxWidth));
        }

        return value[..(maxWidth - 1)] + "…";
    }

    private void Render()
    {
        AnsiConsole.Clear();
        var layout = ShellRenderer.Build(_state);
        AnsiConsole.Write(layout);
    }

    private string? Prompt()
    {
        var console = AnsiConsole.Console;

        try
        {
            var buffer = new StringBuilder();
            _state.ResetHistoryCursor();
            WritePrompt(buffer.ToString());

            while (true)
            {
                var keyInfo = console.Input.ReadKey(intercept: true);
                if (keyInfo is null)
                {
                    continue;
                }

                var key = keyInfo.Value;

                if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.C)
                {
                    return null;
                }

                if ((key.Modifiers & ConsoleModifiers.Shift) != 0 && key.Key == ConsoleKey.UpArrow)
                {
                    var detailStep = GetDetailScrollStep();
                    if (TryAdjustDetailOffset(-detailStep))
                    {
                        Render();
                        WritePrompt(buffer.ToString());
                        continue;
                    }

                    if (_state.ModelSummary?.HasDetailRows == true)
                    {
                        continue;
                    }

                    var step = Math.Max(1, _state.LogViewport / 4);
                    _state.ScrollLogUp(step);
                    Render();
                    WritePrompt(buffer.ToString());
                    continue;
                }

                if ((key.Modifiers & ConsoleModifiers.Shift) != 0 && key.Key == ConsoleKey.DownArrow)
                {
                    var detailStep = GetDetailScrollStep();
                    if (TryAdjustDetailOffset(detailStep))
                    {
                        Render();
                        WritePrompt(buffer.ToString());
                        continue;
                    }

                    if (_state.ModelSummary?.HasDetailRows == true)
                    {
                        continue;
                    }

                    var step = Math.Max(1, _state.LogViewport / 4);
                    _state.ScrollLogDown(step);
                    Render();
                    WritePrompt(buffer.ToString());
                    continue;
                }

                if (key.Key == ConsoleKey.UpArrow)
                {
                    if (_state.TryHistoryPrevious(out var command))
                    {
                        buffer.Clear();
                        buffer.Append(command);
                        WritePrompt(buffer.ToString());
                    }
                    continue;
                }

                if (key.Key == ConsoleKey.DownArrow)
                {
                    if (_state.TryHistoryNext(out var command))
                    {
                        buffer.Clear();
                        buffer.Append(command);
                        WritePrompt(buffer.ToString());
                    }
                    continue;
                }

                if (key.Key == ConsoleKey.PageUp)
                {
                    _state.ScrollLogUp(_state.LogViewport);
                    Render();
                    WritePrompt(buffer.ToString());
                    continue;
                }

                if (key.Key == ConsoleKey.PageDown)
                {
                    _state.ScrollLogDown(_state.LogViewport);
                    Render();
                    WritePrompt(buffer.ToString());
                    continue;
                }

                if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.Home)
                {
                    _state.ScrollLogHome();
                    Render();
                    WritePrompt(buffer.ToString());
                    continue;
                }

                if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.End)
                {
                    _state.ScrollLogEnd();
                    Render();
                    WritePrompt(buffer.ToString());
                    continue;
                }

                if (key.Key == ConsoleKey.Enter)
                {
                    console.WriteLine();
                    return buffer.ToString();
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (buffer.Length > 0)
                    {
                        buffer.Length--;
                        WritePrompt(buffer.ToString());
                    }
                    continue;
                }

                if (key.Key == ConsoleKey.Escape)
                {
                    buffer.Clear();
                    _state.ResetHistoryCursor();
                    Render();
                    WritePrompt(buffer.ToString());
                    continue;
                }

                var ch = key.KeyChar;
                if (!char.IsControl(ch))
                {
                    buffer.Append(ch);
                    WritePrompt(buffer.ToString());
                }
            }
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static void WritePrompt(string buffer)
    {
        ClearPromptLine();
        AnsiConsole.Markup("[green]cascode[/]> ");
        if (!string.IsNullOrEmpty(buffer))
        {
            AnsiConsole.Console.Write(buffer);
        }
    }

    private static void ClearPromptLine()
    {
        const string ClearSequence = "\u001b[2K\r";
        try
        {
            AnsiConsole.Console.Write(ClearSequence);
        }
        catch
        {
            try
            {
                System.Console.Write('\r');
                var width = Math.Max(0, System.Console.BufferWidth - 1);
                if (width > 0)
                {
                    System.Console.Write(new string(' ', width));
                }
                System.Console.Write('\r');
            }
            catch
            {
                // ignored
            }
        }
    }

    private static string[] Tokenize(string input)
    {
        var tokens = new List<string>();
        var current = new List<char>();
        var inQuotes = false;

        foreach (var ch in input)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Count > 0)
                {
                    tokens.Add(new string(current.ToArray()));
                    current.Clear();
                }
                continue;
            }

            current.Add(ch);
        }

        if (current.Count > 0)
        {
            tokens.Add(new string(current.ToArray()));
        }

        return tokens.ToArray();
    }
}
