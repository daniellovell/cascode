using System;
using Spectre.Console;

namespace Cascode.Cli.Output;

internal static class SpectreTableSizing
{
    /// <summary>
    /// Avoid "widescreen tables" where columns drift far apart on large monitors.
    /// </summary>
    public static void ApplyStandardWidth(IAnsiConsole console, Table table, int maxWidth = 120)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(table);

        table.Expand = false;

        // When a console width is available, clamp the table width so the content stays scannable.
        var width = console.Profile.Width;
        if (width > 0)
        {
            table.Width = Math.Min(width, maxWidth);
        }
    }
}
