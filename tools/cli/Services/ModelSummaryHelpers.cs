using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Cascode.Cli.Services;

using Cascode.Workspace;

internal static class ModelSummaryHelpers
{
    internal static string BuildModelSummaryTitle(IEnumerable<SpectreModelDeviceClass> filters)
    {
        var filterList = filters?.ToList() ?? new List<SpectreModelDeviceClass>();
        if (filterList.Count == 0) return "Model Catalog";
        var labels = filterList.Select(FormatDeviceClassName)
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return string.Join(" / ", labels) + " Models";
    }

    internal static string BuildClassSummaryLine(
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

    internal static string BuildClassStatsLine(
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

    internal static string BuildDetailSummaryLine(
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
        var end = Math.Min(offset + pageSize, totalCount);

        var label = filterLabels is null || filterLabels.Count == 0
            ? "All models"
            : string.Join(" / ", filterLabels);

        return $"Showing {start}-{end} of {totalCount} {label}. Use Shift+Up/Down to scroll.";
    }

    internal static string BuildDetailStatsLine(IReadOnlyCollection<SpectreModel> models)
    {
        if (models.Count == 0) return string.Empty;

        var voltage = FormatDistinctSummary(models.Select(model => model.VoltageDomain));
        var thresholds = FormatDistinctSummary(models.Select(model => model.ThresholdFlavor));
        var corners = FormatDistinctSummary(models.SelectMany(model => model.Corners));
        var decks = FormatDecks(models.SelectMany(model => model.Decks).ToList());

        var parts = new List<string>();
        if (voltage != "-") parts.Add($"VDD → {voltage}");
        if (thresholds != "-") parts.Add($"VT → {thresholds}");
        if (corners != "-") parts.Add($"Corners → {corners}");
        if (decks != "-") parts.Add($"Decks → {decks}");
        return parts.Count == 0 ? string.Empty : string.Join(" | ", parts);
    }

    internal static string BuildModelSuggestionText()
        => "Tip: Use Shift+Up/Down to scroll, 'pdk models nmos' to focus, 'pdk match' to classify, and 'home' to exit.";

    internal static ModelClassSummaryRow CreateClassSummaryRow(
        SpectreModelDeviceClass deviceClass,
        IReadOnlyList<SpectreModel> models,
        bool isUncategorized)
    {
        var deviceLabel = isUncategorized ? "Uncategorized" : FormatDeviceClassName(deviceClass);
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

    internal static ModelSummaryRow CreateModelSummaryRow(SpectreModel model, int index)
    {
        var threshold = string.IsNullOrWhiteSpace(model.ThresholdFlavor) ? "-" : model.ThresholdFlavor!;
        var voltage = string.IsNullOrWhiteSpace(model.VoltageDomain) ? "-" : model.VoltageDomain!;
        var corners = FormatDistinctSummary(model.Corners);
        var decks = FormatDecks(model.Decks.ToList());

        return new ModelSummaryRow(index, model.Name, FormatDeviceClassName(model.DeviceClass), threshold, voltage, corners, decks);
    }

    internal static string FormatDeviceClassName(SpectreModelDeviceClass deviceClass)
        => deviceClass switch
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

    internal static string FormatDistinctSummary(IEnumerable<string?> values, int maxItems = 5)
    {
        if (values is null) return "-";
        var distinct = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (distinct.Count == 0) return "-";
        if (distinct.Count <= maxItems) return string.Join(", ", distinct);
        return string.Join(", ", distinct.Take(maxItems)) + $" … ({distinct.Count - maxItems} more)";
    }

    internal static string FormatDecks(IReadOnlyList<string> decks)
    {
        if (decks is null || decks.Count == 0) return "-";
        var names = decks.Select(deck => Path.GetFileName(deck) ?? deck)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count == 0) return "-";
        if (names.Count <= 3) return string.Join(", ", names);
        return string.Join(", ", names.Take(3)) + $" … ({names.Count - 3} more)";
    }
}
