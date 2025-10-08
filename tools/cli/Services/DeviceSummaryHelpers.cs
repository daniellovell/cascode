using System;
using System.Collections.Generic;
using System.Linq;
using Cascode.Workspace;

namespace Cascode.Cli.Services;

internal static class DeviceSummaryHelpers
{
    internal static string BuildDetailSummaryLine(
        IReadOnlyList<string> filterLabels,
        int offset,
        int pageSize,
        int totalCount)
    {
        if (totalCount == 0) return "No devices matched the selected filters.";

        var start = offset + 1;
        var end = Math.Min(offset + pageSize, totalCount);
        var label = filterLabels is null || filterLabels.Count == 0
            ? "All devices"
            : string.Join(" / ", filterLabels);

        return $"Showing {start}-{end} of {totalCount} {label}. Use Shift+Up/Down to page; Ctrl+Up/Down to step.";
    }

    internal static string BuildSuggestionText()
        => "Tip: Use Shift+Up/Down to page; Ctrl+Up/Down to step; 'pdk devices' to browse; 'home' to exit.";

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
}
