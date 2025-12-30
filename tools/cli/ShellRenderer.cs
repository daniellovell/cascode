using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Cascode.Workspace;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Cascode.Cli;

internal static class ShellRenderer
{
    public static Layout Build(ShellState state)
    {
        return state.ViewMode switch
        {
            ShellViewMode.DeviceSummary => BuildDeviceSummaryLayout(state),
            ShellViewMode.CharRead => BuildCharReadLayout(state),
            _ => BuildHomeLayout(state),
        };
    }

    private static Layout BuildHomeLayout(ShellState state)
    {
        // Reserve a dedicated bottom line for the interactive prompt so it doesn't
        // overwrite the Log panel's bottom border. All content lives in "Content".
        var layout = new Layout("Root").SplitRows(
            new Layout("Content").Ratio(1),
            new Layout("PromptSpacer").Size(1)
        );

        layout["Content"].SplitColumns(new Layout("Main").Ratio(3), new Layout("Sidebar").Ratio(2));

        layout["Content"]
            ["Main"]
            .SplitRows(new Layout("WorkspaceBar").Size(3), new Layout("Log").Ratio(1));

        if (state.CharJobActive)
        {
            layout["Content"]
                ["Sidebar"]
                .SplitRows(
                    new Layout("Progress").Size(10),
                    new Layout("Navigator").Ratio(2),
                    new Layout("Details").Ratio(1)
                );
        }
        else
        {
            layout["Content"]
                ["Sidebar"]
                .SplitRows(new Layout("Navigator").Ratio(2), new Layout("Details").Ratio(1));
        }

        layout["Content"]["Main"]["WorkspaceBar"].Update(BuildWorkspaceBar(state));
        layout["Content"]["Main"]["Log"].Update(BuildLog(state));
        if (state.CharJobActive)
            layout["Content"]["Sidebar"]["Progress"].Update(BuildCharProgress(state));
        layout["Content"]["Sidebar"]["Navigator"].Update(BuildNavigator(state));
        layout["Content"]["Sidebar"]["Details"].Update(BuildDeckDetails(state));

        // Render the prompt (normal or busy)
        layout["PromptSpacer"].Update(BuildPrompt(state));

        return layout;
    }

    private static IRenderable BuildCharProgress(ShellState state)
    {
        var remaining = Math.Max(
            0,
            state.CharTotal
                - Math.Max(
                    Math.Max(state.CharGenerated, state.CharRan),
                    Math.Max(state.CharExported, state.CharSkipped)
                )
        );
        var label =
            $"[green bold underline]PDK Characterization Progress[/]  [grey]current:[/] {Markup.Escape(state.CharCurrent ?? string.Empty)}";
        var width = Math.Clamp(EstimateConsoleHeight() + 30, 40, 100);
        var chart = new BarChart()
            .Width(width)
            .Label(label)
            .CenterLabel()
            .AddItem("Generated", state.CharGenerated, Color.Yellow)
            .AddItem("Ran", state.CharRan, Color.Blue)
            .AddItem("Exported", state.CharExported, Color.Green)
            .AddItem("Skipped", state.CharSkipped, Color.Grey)
            .AddItem("Remaining", remaining, Color.Red);

        return new Panel(chart)
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader(
                $"Characterization ({Markup.Escape(state.CharBackend ?? "?")}/{Markup.Escape(state.CharCorner ?? "?")})"
            ),
            Expand = true,
            Padding = new Padding(1, 0, 1, 0),
        };
    }

    private static Layout BuildDeviceSummaryLayout(ShellState state)
    {
        // Reserve a bottom line for the prompt across views to avoid border clipping
        var layout = new Layout("Root").SplitRows(
            new Layout("MainRows").Ratio(1),
            new Layout("PromptSpacer").Size(1)
        );

        layout["MainRows"]
            .SplitRows(new Layout("WorkspaceBar").Size(3), new Layout("Content").Ratio(1));

        layout["MainRows"]["WorkspaceBar"].Update(BuildWorkspaceBar(state));

        var summary = state.DeviceSummary ?? DeviceSummaryViewState.Empty;
        var contentRows = new Rows(BuildSummaryPanel(summary), BuildSummaryTip(summary));
        layout["MainRows"]["Content"].Update(contentRows);

        layout["PromptSpacer"].Update(BuildPrompt(state));

        return layout;
    }

    private static Layout BuildCharReadLayout(ShellState state)
    {
        var layout = new Layout("Root").SplitRows(
            new Layout("MainRows").Ratio(1),
            new Layout("PromptSpacer").Size(1)
        );

        layout["MainRows"]
            .SplitRows(new Layout("WorkspaceBar").Size(3), new Layout("Content").Ratio(1));

        layout["MainRows"]["WorkspaceBar"].Update(BuildWorkspaceBar(state));
        layout["MainRows"]["Content"].Update(BuildCharReadPanel(state));

        layout["PromptSpacer"].Update(BuildPrompt(state));

        return layout;
    }

    private static IRenderable BuildCharReadPanel(ShellState state)
    {
        var view = state.CharRead ?? CharReadViewState.Empty;
        var table = new Table().Border(TableBorder.SimpleHeavy);
        foreach (var header in view.Headers)
        {
            table.AddColumn(header);
        }
        foreach (var row in view.Rows)
        {
            table.AddRow(row.ToArray());
        }

        var sparklines = new List<IRenderable>();
        foreach (var kvp in view.Sparklines)
        {
            sparklines.Add(BuildSparkline(kvp.Key, kvp.Value));
        }

        var content = new Rows(
            new Markup($"[bold]{Markup.Escape(view.Title)}[/] — {Markup.Escape(view.Subtitle)}"),
            new Rule { Style = Style.Parse("grey") },
            table,
            new Rule { Style = Style.Parse("grey") },
            new Rows(sparklines),
            new Rule { Style = Style.Parse("grey") },
            new Markup($"[dim]Derived source: {Markup.Escape(view.SourcePath)}[/]"),
            new Markup("[dim]Type 'home' to return to the dashboard.[/]")
        );

        return new Panel(content)
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader("Characterization Viewer"),
            Expand = true,
            Padding = new Padding(1, 1, 1, 1),
        };
    }

    internal static IRenderable BuildSparkline(string label, IEnumerable<double> values)
    {
        var list = values.ToList();
        var finite = list.Where(double.IsFinite).ToList();
        if (finite.Count == 0)
            return new Text($"{label}: (no data)");

        var min = finite.Min();
        var max = finite.Max();
        if (Math.Abs(max - min) < 1e-12)
            max = min + 1e-12;

        var glyphs = " ▂▃▄▅▆▇█";
        var spark = new System.Text.StringBuilder();
        foreach (var value in list)
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

        static string Fmt(double v)
        {
            if (!double.IsFinite(v))
                return string.Empty;
            var abs = Math.Abs(v);
            if (abs >= 1e3 || (abs > 0 && abs < 1e-3))
                return v.ToString("0.###E+0", CultureInfo.InvariantCulture);
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        return new Markup(
            $"[cyan]{Markup.Escape(label)}[/]: {spark} [grey](min {Fmt(min)} / max {Fmt(max)})[/]"
        );
    }

    private static IRenderable BuildSummaryPanel(DeviceSummaryViewState summary)
    {
        var contentItems = new List<IRenderable>();

        if (summary.HasClassRows)
        {
            contentItems.Add(CreateDeviceClassSummaryTable(summary.ClassRows));
        }
        else if (summary.HasDetailRows)
        {
            contentItems.Add(CreateDeviceDetailTable(summary));
        }
        else
        {
            contentItems.Add(
                new Markup(
                    "[grey]No devices matched the current view. Run [bold]pdk scan[/] or adjust your filters.[/]"
                )
            );
        }

        if (!string.IsNullOrWhiteSpace(summary.SummaryLine))
        {
            contentItems.Add(new Markup($"[grey53]{Markup.Escape(summary.SummaryLine)}[/]"));
        }

        if (summary.HasStats)
        {
            contentItems.Add(new Markup($"[grey42]{Markup.Escape(summary.StatsLine)}[/]"));
        }

        if (summary.HasSuggestion)
        {
            contentItems.Add(new Markup("[dim]" + Markup.Escape(summary.SuggestionLine) + "[/]"));
        }

        var panelBody = contentItems.Count switch
        {
            0 => new Markup(string.Empty),
            1 => contentItems[0],
            _ => new Rows(contentItems.ToArray()),
        };

        return new Panel(panelBody)
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader(summary.Title),
            Expand = true,
            Padding = new Padding(1, 1, 1, 1),
        };
    }

    private static IRenderable BuildSummaryTip(DeviceSummaryViewState summary)
    {
        var tipText = summary.HasSuggestion
            ? summary.SuggestionLine
            : "Type 'home' to return to the dashboard.";

        var tip = new Markup("[dim]" + Markup.Escape(tipText) + "[/]");
        return new Panel(new Align(tip, HorizontalAlignment.Left, VerticalAlignment.Middle))
        {
            Border = BoxBorder.None,
            Padding = new Padding(1, 0, 1, 0),
            Expand = true,
        };
    }

    internal static Table CreateDeviceDetailTable(DeviceSummaryViewState summary)
    {
        var table = new Table().Border(TableBorder.Rounded).Expand();

        table.AddColumn(new TableColumn("#").Centered());
        table.AddColumn(new TableColumn("Device"));
        table.AddColumn(new TableColumn("Class"));
        table.AddColumn(new TableColumn("VT"));
        table.AddColumn(new TableColumn("VDD"));
        table.AddColumn(new TableColumn("Views"));
        table.AddColumn(new TableColumn("Notes"));

        var pageSize =
            summary.DetailPageSize > 0 ? summary.DetailPageSize : summary.DetailRows.Count;
        var visibleRows = summary.DetailRows.Skip(summary.DetailOffset).Take(pageSize).ToArray();

        for (var i = 0; i < visibleRows.Length; i++)
        {
            var row = visibleRows[i];
            var index = summary.DetailOffset + i + 1;
            table.AddRow(
                index.ToString(CultureInfo.InvariantCulture),
                Markup.Escape(row.Name),
                Markup.Escape(row.DeviceClass),
                Markup.Escape(row.Threshold),
                Markup.Escape(row.Voltage),
                Markup.Escape(row.Views),
                Markup.Escape(row.Notes)
            );
        }

        return table;
    }

    internal static Table CreateDeviceClassSummaryTable(IReadOnlyList<DeviceClassSummaryRow> rows)
    {
        var table = new Table().Border(TableBorder.Rounded).Expand();

        table.AddColumn(new TableColumn("Class"));
        table.AddColumn(new TableColumn("Devices").Centered());
        table.AddColumn(new TableColumn("Decks"));
        table.AddColumn(new TableColumn("Voltage Domains"));
        table.AddColumn(new TableColumn("Thresholds"));
        table.AddColumn(new TableColumn("Corners"));
        table.AddColumn(new TableColumn("Example"));

        foreach (var row in rows)
        {
            var classCell = row.IsUncategorized
                ? $"[bold red]{Markup.Escape(row.DeviceClass)}[/]"
                : Markup.Escape(row.DeviceClass);

            table.AddRow(
                classCell,
                Markup.Escape(row.DeviceCount),
                Markup.Escape(row.Decks),
                Markup.Escape(row.VoltageDomains),
                Markup.Escape(row.Thresholds),
                Markup.Escape(row.Corners),
                Markup.Escape(row.ExampleDevice)
            );
        }

        return table;
    }

    internal static IRenderable BuildNavigator(ShellState state)
    {
        var tree = new Tree("[yellow]Model Decks[/]");
        var decks = state.Scan?.ModelDecks ?? Array.Empty<ModelDeckRecord>();
        for (var i = 0; i < decks.Count; i++)
        {
            var label = $"[white]{i + 1}. {Escape(Path.GetFileName(decks[i].DeckPath))}[/]";
            if (state.SelectedDeckIndex == i)
            {
                label = $"[bold green]>[/] {label}";
            }
            tree.AddNode(label);
        }

        return new Panel(tree)
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader("Navigator"),
            Padding = new Padding(1, 1, 1, 1),
            Expand = true,
        };
    }

    internal static IRenderable BuildDeckDetails(ShellState state)
    {
        var decks = state.Scan?.ModelDecks ?? Array.Empty<ModelDeckRecord>();
        if (decks.Count == 0)
        {
            var text = new Markup(
                "[grey]No model decks discovered. Run [bold]pdk scan[/] to get started.[/]"
            );
            return new Panel(text)
            {
                Border = BoxBorder.Rounded,
                Header = new PanelHeader("Details"),
                Expand = true,
                Padding = new Padding(1, 1, 1, 1),
            };
        }

        var index = state.SelectedDeckIndex ?? 0;
        index = Math.Clamp(index, 0, decks.Count - 1);
        state.SelectedDeckIndex = index;
        var deck = decks[index];

        var table = new Table().NoBorder();
        table.AddColumn("Key");
        table.AddColumn("Value");

        table.AddRow("Path", Escape(deck.DeckPath));
        table.AddRow(
            "Sections",
            deck.Sections.Count > 0 ? string.Join(", ", deck.Sections) : "(none)"
        );
        table.AddRow("Includes", deck.Includes.Count.ToString(CultureInfo.InvariantCulture));

        var includes = new Table();
        includes.AddColumn("Includes");
        foreach (var include in deck.Includes.Take(10))
        {
            includes.AddRow(Escape(include));
        }
        if (deck.Includes.Count > 10)
        {
            includes.AddRow($"... ({deck.Includes.Count - 10} more)");
        }

        var content = new Rows(table, new Markup(""), includes);
        return new Panel(content)
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader($"Deck Details (#{index + 1})"),
            Expand = true,
            Padding = new Padding(1, 1, 1, 1),
        };
    }

    private static IRenderable BuildWorkspaceBar(ShellState state)
    {
        var markup = new Markup($"[bold]Workspace[/]: {Escape(state.WorkspaceRoot)}");
        return new Panel(markup)
        {
            Border = BoxBorder.Rounded,
            Padding = new Padding(1, 0, 1, 0),
            Expand = true,
        };
    }

    internal static IRenderable BuildLog(ShellState state)
    {
        var visibleLines = GetLogVisibleLines();
        // Reserve one line at the bottom of the panel for a dimmed tooltip
        var messageLines = Math.Max(1, visibleLines - 1);
        state.UpdateLogViewport(messageLines);

        var messagesSnapshot = state.GetMessagesSnapshot();
        if (messagesSnapshot.Length == 0)
        {
            var empty = new Markup("[grey]Log is empty. Commands typed will appear here.[/]");
            var tip = new Align(
                new Markup("[dim]Shift+↑/↓ scroll the log[/]"),
                HorizontalAlignment.Left
            );
            var rows = new Rows(empty, tip);
            return new Panel(rows)
            {
                Border = BoxBorder.Rounded,
                Header = new PanelHeader("Log"),
                Expand = true,
            };
        }

        var maxOffset = Math.Max(0, messagesSnapshot.Length - messageLines);
        var offset = Math.Clamp(state.LogScrollOffset, 0, maxOffset);
        var start = Math.Max(0, messagesSnapshot.Length - messageLines - offset);

        // Calculate available width for the log panel (3/5 of console width, minus borders and padding)
        var consoleWidth = EstimateConsoleWidth();
        var logPanelWidth = (int)(consoleWidth * 0.6) - 6; // 3/5 ratio minus borders/padding
        logPanelWidth = Math.Max(40, logPanelWidth); // Minimum width

        // Truncate long lines to fit available width (defensively handles very small widths)
        var truncatedMessages = messagesSnapshot
            .Skip(start)
            .Take(messageLines)
            .Select(msg => TruncateToWidth(msg, logPanelWidth));

        var renderable = new Markup(string.Join('\n', truncatedMessages));
        var headerLabel = offset == 0 ? "Log" : $"Log (scroll {offset})";

        var tipLine = new Align(
            new Markup("[dim]Shift+↑/↓ scroll the log[/]"),
            HorizontalAlignment.Left
        );
        var content = new Rows(renderable, tipLine);

        return new Panel(content)
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader(headerLabel),
            Expand = true,
            Padding = new Padding(1, 0, 1, 0),
        };
    }

    private static string Escape(string input) => Markup.Escape(input);

    private static string TruncateToWidth(string text, int width)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        if (string.IsNullOrEmpty(text) || text.Length <= width)
        {
            return Escape(text ?? string.Empty);
        }

        if (width == 1)
        {
            return Escape(text.Substring(0, 1));
        }

        var safeCut = Math.Max(1, width - 1);
        safeCut = Math.Min(safeCut, text.Length);
        return Escape(text.Substring(0, safeCut) + "…");
    }

    private static int GetLogVisibleLines()
    {
        var height = EstimateConsoleHeight();
        // Height breakdown with a reserved prompt spacer line:
        // - WorkspaceBar row (fixed): 3 lines
        // - Log panel borders: 2 lines (header overlays top border)
        // - Prompt spacer: 1 line
        // Total overhead: 3 + 2 + 1 = 6 lines
        var overhead = 6;
        var availableHeight = Math.Max(8, height - overhead);
        return availableHeight;
    }

    private static int EstimateConsoleHeight()
    {
        try
        {
            if (Console.WindowHeight > 0)
            {
                return Console.WindowHeight;
            }
        }
        catch
        {
            // ignored
        }

        var profileHeight = AnsiConsole.Profile.Height;
        return profileHeight > 0 ? profileHeight : 24;
    }

    private static int EstimateConsoleWidth()
    {
        try
        {
            if (Console.WindowWidth > 0)
            {
                return Console.WindowWidth;
            }
        }
        catch
        {
            // ignored
        }

        var profileWidth = AnsiConsole.Profile.Width;
        return profileWidth > 0 ? profileWidth : 80;
    }

    internal static IRenderable BuildPrompt(ShellState state)
    {
        if (state.IsBusy)
        {
            var spinner = Markup.Escape(state.GetSpinnerFrame());
            var text = $"[grey]cascode[/]> [dim]{Escape(state.BusyText)}[/] {spinner}";
            return new Markup(text);
        }
        else
        {
            return new Markup("[green]cascode[/]> ");
        }
    }
}
