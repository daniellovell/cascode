using System;
using System.Collections.Generic;

namespace Cascode.Cli;

internal sealed class DeviceSummaryViewState
{
    public static readonly DeviceSummaryViewState Empty = new(
        "Device Catalog",
        "No devices available. Run pdk scan to populate the catalog.",
        string.Empty,
        string.Empty,
        Array.Empty<DeviceSummaryRow>(),
        Array.Empty<DeviceClassSummaryRow>(),
        detailOffset: 0,
        detailPageSize: 0,
        detailFilters: Array.Empty<string>()
    );

    public DeviceSummaryViewState(
        string title,
        string summaryLine,
        string statsLine,
        string suggestionLine,
        IReadOnlyList<DeviceSummaryRow> detailRows,
        IReadOnlyList<DeviceClassSummaryRow>? classRows = null,
        int detailOffset = 0,
        int detailPageSize = 0,
        IReadOnlyList<string>? detailFilters = null
    )
    {
        Title = title;
        SummaryLine = summaryLine ?? string.Empty;
        StatsLine = statsLine ?? string.Empty;
        SuggestionLine = suggestionLine ?? string.Empty;
        DetailRows = detailRows ?? Array.Empty<DeviceSummaryRow>();
        ClassRows = classRows ?? Array.Empty<DeviceClassSummaryRow>();
        DetailOffset = Math.Max(0, detailOffset);
        DetailPageSize = detailPageSize;
        DetailFilters = detailFilters ?? Array.Empty<string>();
    }

    public string Title { get; }

    public string SummaryLine { get; }

    public string StatsLine { get; }

    public string SuggestionLine { get; }

    public IReadOnlyList<DeviceSummaryRow> DetailRows { get; }

    public IReadOnlyList<DeviceClassSummaryRow> ClassRows { get; }

    public IReadOnlyList<string> DetailFilters { get; }

    public int DetailOffset { get; }

    public int DetailPageSize { get; }

    public int DetailTotalCount => DetailRows.Count;

    public int DetailVisibleCount
    {
        get
        {
            var pageSize = DetailPageSize > 0 ? DetailPageSize : DetailRows.Count;
            return Math.Min(pageSize, Math.Max(0, DetailRows.Count - DetailOffset));
        }
    }

    public bool HasDetailRows => DetailRows.Count > 0;

    public bool HasClassRows => ClassRows.Count > 0;

    public bool HasStats => !string.IsNullOrWhiteSpace(StatsLine);

    public bool HasSuggestion => !string.IsNullOrWhiteSpace(SuggestionLine);

    public DeviceSummaryViewState WithDetail(int detailOffset, string summaryLine)
    {
        return new DeviceSummaryViewState(
            Title,
            summaryLine,
            StatsLine,
            SuggestionLine,
            DetailRows,
            ClassRows,
            detailOffset,
            DetailPageSize,
            DetailFilters
        );
    }
}

internal sealed record DeviceSummaryRow(
    int Index,
    string Name,
    string DeviceClass,
    string Threshold,
    string Voltage,
    string Views,
    string Notes
);

internal sealed record DeviceClassSummaryRow(
    string DeviceClass,
    string DeviceCount,
    string Decks,
    string VoltageDomains,
    string Thresholds,
    string Corners,
    string ExampleDevice,
    bool IsUncategorized
);
