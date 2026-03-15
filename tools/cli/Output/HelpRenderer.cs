using System;
using System.Collections.Generic;
using System.Linq;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Cascode.Cli.Output;

internal static class HelpRenderer
{
    private static readonly HelpSectionDefinition[] SectionDefinitions =
    [
        new(
            CommandHelpCategory.Shell,
            "Shell",
            "Navigate the interactive shell and inspect the CLI surface.",
            Color.Grey70
        ),
        new(
            CommandHelpCategory.Design,
            "Design Flow",
            "Link source, emit artifacts, render schematics, and inspect connectivity.",
            Color.Teal
        ),
        new(
            CommandHelpCategory.Bench,
            "Bench And Verification",
            "Run benches and verify measured results against declared constraints.",
            Color.Green
        ),
        new(
            CommandHelpCategory.Characterization,
            "Characterization",
            "Generate, read, and export standalone characterization data.",
            Color.Blue
        ),
        new(
            CommandHelpCategory.Pdk,
            "PDK Workspace",
            "Scan process decks, inspect discovered devices, and emit derived PDK assets.",
            Color.Orange1
        ),
        new(
            CommandHelpCategory.PdkCharacterization,
            "PDK Characterization",
            "Configure and run workspace-scale device characterization batches.",
            Color.MediumPurple
        ),
        new(
            CommandHelpCategory.Environment,
            "Environment",
            "Install simulator prerequisites and manage CLI updates.",
            Color.Yellow
        ),
        new(
            CommandHelpCategory.Uncategorized,
            "Other",
            "Additional commands that do not belong to a primary workflow yet.",
            Color.Grey54
        ),
    ];

    public static void RenderRootHelp(ICliOutput output, IEnumerable<CommandDescriptor> commands)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(commands);

        var sections = BuildSections(commands).ToArray();
        if (output.Out is not null)
        {
            RenderRich(output.Out, sections);
            return;
        }

        foreach (var line in BuildPlainLines(sections))
        {
            output.WriteLine(line);
        }
    }

    internal static IReadOnlyList<string> BuildPlainLines(IEnumerable<CommandDescriptor> commands)
    {
        return BuildPlainLines(BuildSections(commands));
    }

    private static IReadOnlyList<HelpSection> BuildSections(IEnumerable<CommandDescriptor> commands)
    {
        var lookup = commands
            .Where(c => !c.Hidden && !c.IsAlias)
            .GroupBy(c => c.HelpCategory)
            .ToDictionary(g => g.Key, g => g.ToArray());
        var sections = new List<HelpSection>();
        foreach (var definition in SectionDefinitions)
        {
            if (!lookup.TryGetValue(definition.Category, out var entries) || entries.Length == 0)
            {
                continue;
            }

            sections.Add(new HelpSection(definition, BuildEntries(entries)));
        }

        return sections;
    }

    private static IReadOnlyList<string> BuildPlainLines(IEnumerable<HelpSection> sections)
    {
        var lines = new List<string>
        {
            "Cascode CLI",
            "Build, inspect, and simulate Cascode projects.",
            string.Empty,
            "Usage: cascode [--workspace <path>] <command> [options]",
        };

        foreach (var section in sections)
        {
            lines.Add(string.Empty);
            lines.Add($"{section.Definition.Title}:");
            lines.Add($"  {section.Definition.Description}");

            foreach (var entry in section.Entries)
            {
                var indent = new string(' ', entry.Depth * 2);
                lines.Add($"  {indent}{entry.Command.DisplayPath} - {entry.Command.Description}");
            }
        }

        return lines;
    }

    private static void RenderRich(IAnsiConsole console, IReadOnlyList<HelpSection> sections)
    {
        console.Write(BuildHeader());
        console.WriteLine();

        foreach (var section in sections)
        {
            console.Write(BuildSectionRule(section));
            console.WriteLine(section.Definition.Description);
            console.Write(BuildSectionBody(section));
            console.WriteLine();
        }
    }

    private static Rows BuildHeader()
    {
        var workspaceOption = Markup.Escape("[--workspace <path>]");
        var optionsPlaceholder = Markup.Escape("[options]");
        return new Rows(
            new Rule("[deepskyblue1]Cascode CLI[/]")
            {
                Style = new Style(Color.DeepSkyBlue1),
            }.LeftJustified(),
            new Text("Build, inspect, and simulate Cascode projects."),
            Text.Empty,
            new Markup(
                $"[grey]Usage:[/] [aqua]cascode[/] [silver]{workspaceOption}[/] [yellow]<command>[/] [silver]{optionsPlaceholder}[/]"
            )
        );
    }

    private static Rule BuildSectionRule(HelpSection section)
    {
        return new Rule(
            $"[{section.Definition.AccentColor.ToMarkup()}]{Markup.Escape(section.Definition.Title)}[/]"
        )
        {
            Style = new Style(section.Definition.AccentColor),
        }.LeftJustified();
    }

    private static Rows BuildSectionBody(HelpSection section)
    {
        return new Rows(
            section.Entries.Select(entry => (IRenderable)BuildCommandLine(entry)).ToArray()
        );
    }

    private static Markup BuildCommandLine(HelpCommandEntry entry)
    {
        var indent = new string(' ', entry.Depth * 2);
        return new Markup(
            $"{Markup.Escape(indent)}[aqua]{Markup.Escape(entry.Command.DisplayPath)}[/] [grey]-[/] {Markup.Escape(entry.Command.Description)}"
        );
    }

    private static IReadOnlyList<HelpCommandEntry> BuildEntries(
        IEnumerable<CommandDescriptor> commands
    )
    {
        var root = new HelpNode(string.Empty);
        foreach (var command in commands)
        {
            var current = root;
            foreach (var token in command.Tokens)
            {
                current = current.GetOrAdd(token);
            }

            current.Command = command;
        }

        var entries = new List<HelpCommandEntry>();
        AppendEntries(root, depth: 0, entries);
        return entries;
    }

    private static void AppendEntries(HelpNode node, int depth, List<HelpCommandEntry> entries)
    {
        foreach (
            var child in node.Children.Values.OrderBy(
                c => c.Token,
                StringComparer.OrdinalIgnoreCase
            )
        )
        {
            if (child.Command is null && child.Children.Count == 1)
            {
                AppendEntries(child, depth, entries);
                continue;
            }

            if (child.Command is not null)
            {
                entries.Add(new HelpCommandEntry(depth, child.Command));
                AppendEntries(child, depth + 1, entries);
                continue;
            }

            AppendEntries(child, depth, entries);
        }
    }

    private sealed record HelpSectionDefinition(
        CommandHelpCategory Category,
        string Title,
        string Description,
        Color AccentColor
    );

    private sealed record HelpSection(
        HelpSectionDefinition Definition,
        IReadOnlyList<HelpCommandEntry> Entries
    );

    private sealed record HelpCommandEntry(int Depth, CommandDescriptor Command);

    private sealed class HelpNode(string token)
    {
        public string Token { get; } = token;
        public SortedDictionary<string, HelpNode> Children { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public CommandDescriptor? Command { get; set; }

        public HelpNode GetOrAdd(string token)
        {
            if (!Children.TryGetValue(token, out var child))
            {
                child = new HelpNode(token);
                Children[token] = child;
            }

            return child;
        }
    }
}

internal static class HelpColorExtensions
{
    public static string ToMarkup(this Color color)
    {
        var value = color.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            value = color.ToHex();
        }

        return value.StartsWith('#') ? value
            : IsHex(value) ? $"#{value}"
            : value;
    }

    private static bool IsHex(string value)
    {
        return value.Length == 6 && value.All(Uri.IsHexDigit);
    }
}
