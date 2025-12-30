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
        int totalCount
    )
    {
        if (totalCount == 0)
            return "No devices matched the selected filters.";

        var start = offset + 1;
        var end = Math.Min(offset + pageSize, totalCount);
        var label =
            filterLabels is null || filterLabels.Count == 0
                ? "All devices"
                : string.Join(" / ", filterLabels);

        return $"Showing {start}-{end} of {totalCount} {label}. Use Shift+Up/Down to page; Ctrl+Up/Down to step.";
    }

    /// <summary>
    /// Provides a compact usage tip listing keyboard shortcuts and commands for navigating the device list UI.
    /// </summary>
    /// <returns>The tip text shown to users: "Tip: Use Shift+Up/Down to page; Ctrl+Up/Down to step; 'pdk devices' to browse; 'home' to exit."</returns>
    internal static string BuildSuggestionText() =>
        "Tip: Use Shift+Up/Down to page; Ctrl+Up/Down to step; 'pdk devices' to browse; 'home' to exit.";

    /// <summary>
    /// Formats a <see cref="DeviceClass"/> value into a user-friendly display name.
    /// </summary>
    /// <param name="deviceClass">The device class to format.</param>
    /// <returns>The display name corresponding to the provided device class.</returns>
    internal static string FormatDeviceClassName(DeviceClass deviceClass) =>
        deviceClass switch
        {
            DeviceClass.Unknown => "Unknown",
            DeviceClass.Nmos => "NMOS",
            DeviceClass.Pmos => "PMOS",
            DeviceClass.Bipolar => "Bipolar",
            DeviceClass.Diode => "Diode",
            DeviceClass.Resistor => "Resistor",
            DeviceClass.Capacitor => "Capacitor",
            DeviceClass.Inductor => "Inductor",
            DeviceClass.TransmissionLine => "Transmission Line",
            DeviceClass.Stdcell => "StdCell",
            DeviceClass.Other => "Other",
            _ => deviceClass.ToString(),
        };
}
