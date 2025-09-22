using Cascode.Workspace;
using Spectre.Console;
using Spectre.Console.Rendering;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cascode.Cli;

internal static class ShellRenderer
{
    public static Layout Build(ShellState state)
    {
        return state.ViewMode switch
        {
            ShellViewMode.ModelSummary => BuildModelSummaryLayout(state),
            _ => BuildHomeLayout(state)
        };
    }

    private static Layout BuildHomeLayout(ShellState state)
    {
        var layout = new Layout("Root")
            .SplitColumns(
                new Layout("Main").Ratio(3),
                new Layout("Sidebar").Ratio(2));

        layout["Main"].SplitRows(
            new Layout("WorkspaceBar").Size(3),
            new Layout("Log").Ratio(1));

        layout["Sidebar"].SplitRows(
            new Layout("Navigator").Ratio(2),
            new Layout("Details").Ratio(1));

        layout["Main"]["WorkspaceBar"].Update(BuildWorkspaceBar(state));
        layout["Main"]["Log"].Update(BuildLog(state));
        layout["Sidebar"]["Navigator"].Update(BuildNavigator(state));
        layout["Sidebar"]["Details"].Update(BuildDeckDetails(state));

        return layout;
    }

    private static Layout BuildModelSummaryLayout(ShellState state)
    {
        var layout = new Layout("Root")
            .SplitRows(
                new Layout("WorkspaceBar").Size(3),
                new Layout("Content").Ratio(1));

        layout["WorkspaceBar"].Update(BuildWorkspaceBar(state));

        var summary = state.ModelSummary ?? ModelSummaryViewState.Empty;
        var contentRows = new Rows(BuildSummaryPanel(summary), BuildSummaryTip(summary));
        layout["Content"].Update(contentRows);

        return layout;
    }

    private static IRenderable BuildSummaryPanel(ModelSummaryViewState summary)
    {
        var contentItems = new List<IRenderable>();

        if (summary.HasClassRows)
        {
            contentItems.Add(CreateModelClassSummaryTable(summary.ClassRows));
        }
        else if (summary.HasDetailRows)
        {
            contentItems.Add(CreateModelDetailTable(summary));
        }
        else
        {
            contentItems.Add(new Markup("[grey]No models matched the current view. Run [bold]pdk scan[/] or adjust your filters.[/]"));
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
            _ => new Rows(contentItems.ToArray())
        };

        return new Panel(panelBody)
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader(summary.Title),
            Expand = true,
            Padding = new Padding(1, 1, 1, 1)
        };
    }

    private static IRenderable BuildSummaryTip(ModelSummaryViewState summary)
    {
        var tipText = summary.HasSuggestion
            ? summary.SuggestionLine
            : "Type 'home' to return to the dashboard.";

        var tip = new Markup("[dim]" + Markup.Escape(tipText) + "[/]");
        return new Panel(new Align(tip, HorizontalAlignment.Left, VerticalAlignment.Middle))
        {
            Border = BoxBorder.None,
            Padding = new Padding(1, 0, 1, 0),
            Expand = true
        };
    }

    internal static Table CreateModelDetailTable(ModelSummaryViewState summary)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Expand();

        table.AddColumn(new TableColumn("#").Centered());
        table.AddColumn(new TableColumn("Model"));
        table.AddColumn(new TableColumn("Class"));
        table.AddColumn(new TableColumn("VT"));
        table.AddColumn(new TableColumn("VDD"));
        table.AddColumn(new TableColumn("Corners"));
        table.AddColumn(new TableColumn("Decks"));

        var pageSize = summary.DetailPageSize > 0 ? summary.DetailPageSize : summary.DetailRows.Count;
        var visibleRows = summary.DetailRows
            .Skip(summary.DetailOffset)
            .Take(pageSize)
            .ToArray();

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
                Markup.Escape(row.Corners),
                Markup.Escape(row.Decks));
        }

        return table;
    }

    internal static Table CreateModelClassSummaryTable(IReadOnlyList<ModelClassSummaryRow> rows)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Expand();

        table.AddColumn(new TableColumn("Class"));
        table.AddColumn(new TableColumn("Models").Centered());
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
                Markup.Escape(row.ModelCount),
                Markup.Escape(row.Decks),
                Markup.Escape(row.VoltageDomains),
                Markup.Escape(row.Thresholds),
                Markup.Escape(row.Corners),
                Markup.Escape(row.ExampleModel));
        }

        return table;
    }

    private static IRenderable BuildNavigator(ShellState state)
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
            Expand = true
        };
    }

    private static IRenderable BuildDeckDetails(ShellState state)
    {
        var decks = state.Scan?.ModelDecks ?? Array.Empty<ModelDeckRecord>();
        if (decks.Count == 0)
        {
            var text = new Markup("[grey]No model decks discovered. Run [bold]pdk scan[/] to get started.[/]");
            return new Panel(text)
            {
                Border = BoxBorder.Rounded,
                Header = new PanelHeader("Details"),
                Expand = true,
                Padding = new Padding(1, 1, 1, 1)
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
        table.AddRow("Sections", deck.Sections.Count > 0 ? string.Join(", ", deck.Sections) : "(none)");
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
            Padding = new Padding(1, 1, 1, 1)
        };
    }

    private static IRenderable BuildWorkspaceBar(ShellState state)
    {
        var markup = new Markup($"[bold]Workspace[/]: {Escape(state.WorkspaceRoot)}");
        return new Panel(markup)
        {
            Border = BoxBorder.Rounded,
            Padding = new Padding(1, 0, 1, 0),
            Expand = true
        };
    }

    private static IRenderable BuildLog(ShellState state)
    {
        var visibleLines = GetLogVisibleLines();
        state.UpdateLogViewport(visibleLines);

        if (state.Messages.Count == 0)
        {
            return new Panel(new Markup("[grey]Log is empty. Commands typed will appear here.[/]"))
            {
                Border = BoxBorder.Rounded,
                Header = new PanelHeader("Log")
            };
        }

        var maxOffset = Math.Max(0, state.Messages.Count - visibleLines);
        var offset = Math.Clamp(state.LogScrollOffset, 0, maxOffset);
        var start = Math.Max(0, state.Messages.Count - visibleLines - offset);
        var slice = state.Messages.Skip(start).Take(visibleLines).Select(Escape);
        var renderable = new Markup(string.Join('\n', slice));
        var headerLabel = offset == 0 ? "Log" : $"Log (scroll {offset})";

        return new Panel(renderable)
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader(headerLabel),
            Expand = true
        };
    }

    private static string Escape(string input) => Markup.Escape(input);

    private static int GetLogVisibleLines()
    {
        var height = EstimateConsoleHeight();
        var desired = Math.Max(8, (int)Math.Round(height * 0.5));
        return desired;
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
}
