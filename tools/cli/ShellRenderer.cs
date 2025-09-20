using Cascode.Workspace;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Globalization;
using System.Linq;

namespace Cascode.Cli;

internal static class ShellRenderer
{
    public static Layout Build(ShellState state)
    {
        var layout = new Layout("Root")
            .SplitRows(
                new Layout("Body").Ratio(3),
                new Layout("Log").Size(5));

        layout["Body"].SplitColumns(
            new Layout("Sidebar").Size(40),
            new Layout("Content"));

        layout["Sidebar"].Update(BuildSidebar(state));
        layout["Content"].Update(BuildContent(state));
        layout["Log"].Update(BuildLog(state));

        return layout;
    }

    private static IRenderable BuildSidebar(ShellState state)
    {
        var panel = new Panel(new Markup($"[bold]Workspace[/]\n{Escape(state.WorkspaceRoot)}"))
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader("Workspace"),
            Padding = new Padding(1, 1, 1, 0)
        };

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

        var summary = new Rows(panel, tree);
        return new Panel(summary)
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader("Navigator"),
            Padding = new Padding(1, 1, 1, 1)
        };
    }

    private static IRenderable BuildContent(ShellState state)
    {
        var decks = state.Scan?.ModelDecks ?? Array.Empty<ModelDeckRecord>();
        if (decks.Count == 0)
        {
            var text = new Markup("[grey]No model decks discovered. Run [bold]pdk scan[/] to get started.[/]");
            return new Panel(text) { Border = BoxBorder.Rounded, Header = new PanelHeader("Details") };
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
            Header = new PanelHeader($"Deck Details (#{index + 1})")
        };
    }

    private static IRenderable BuildLog(ShellState state)
    {
        if (state.Messages.Count == 0)
        {
            return new Panel(new Markup("[grey]Log is empty. Commands typed will appear here.[/]"))
            {
                Border = BoxBorder.Rounded,
                Header = new PanelHeader("Log")
            };
        }

        var lines = state.Messages.TakeLast(4).Select(Escape);
        var renderable = new Markup(string.Join('\n', lines));
        return new Panel(renderable)
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader("Log")
        };
    }

    private static string Escape(string input) => Markup.Escape(input);
}
