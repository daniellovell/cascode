using Cascode.Workspace;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Globalization;
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

        _commands.Register("char", "Characterization commands", ShowCharUsage);
        _commands.Register("char gen", "Generate gm/Id characterization (preview)", CharacterizationGenerateCommand);
        _commands.Register("char read", "View characterization output (preview)", CharacterizationReadCommand);

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

    private CommandResult CharacterizationGenerateCommand(string[] args)
    {
        if (args.Length == 0)
        {
            _state.AddMessage("Usage: char gen <model>");
            return CommandResult.Success;
        }

        return CharacterizationGenerate(args[0]);
    }

    private CommandResult CharacterizationReadCommand(string[] args)
    {
        if (args.Length == 0)
        {
            _state.AddMessage("Usage: char read <model>");
            return CommandResult.Success;
        }

        return CharacterizationRead(args[0]);
    }

    private static CommandResult Quit(string[] args) => new(0, true);

    private CommandResult CharacterizationGenerate(string model)
    {
        _state.AddMessage($"[preview] Characterization generation for '{model}' not implemented yet.");
        return CommandResult.Success;
    }

    private CommandResult CharacterizationRead(string model)
    {
        _state.AddMessage($"[preview] Characterization output for '{model}' not available yet.");
        return CommandResult.Success;
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
