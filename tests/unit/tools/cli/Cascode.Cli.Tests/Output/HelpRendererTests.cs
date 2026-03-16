using System.Linq;
using Cascode.Cli.Output;
using Spectre.Console;

namespace Cascode.Cli.Tests.Output;

public sealed class HelpRendererTests
{
    [Fact]
    public void BuildPlainLines_GroupsCommandsByHelpCategoryInDisplayOrder()
    {
        var registry = new CommandRegistry();
        registry.Register(
            "verify",
            "Verify constraint compliance from bench results",
            _ => CommandResult.Success,
            helpCategory: CommandHelpCategory.Bench
        );
        registry.Register(
            "help",
            "Show this message",
            _ => CommandResult.Success,
            helpCategory: CommandHelpCategory.Shell
        );
        registry.Register(
            "pdk scan",
            "Scan workspace for decks",
            _ => CommandResult.Success,
            helpCategory: CommandHelpCategory.Pdk
        );

        var lines = HelpRenderer.BuildPlainLines(registry.GetCanonicalCommands()).ToList();

        Assert.Contains("Usage: cascode [--workspace <path>] <command> [options]", lines);

        var shellIndex = lines.IndexOf("Shell:");
        var benchIndex = lines.IndexOf("Bench And Verification:");
        var pdkIndex = lines.IndexOf("PDK Workspace:");

        Assert.True(shellIndex >= 0, "Shell section was not rendered.");
        Assert.True(benchIndex > shellIndex, "Bench section should follow shell commands.");
        Assert.True(pdkIndex > benchIndex, "PDK section should follow bench commands.");

        Assert.Contains(lines, line => line.Contains("help") && line.Contains("Show this message"));
        Assert.Contains(
            lines,
            line =>
                line.Contains("verify")
                && line.Contains("Verify constraint compliance from bench results")
        );
        Assert.Contains(
            lines,
            line => line.Contains("pdk scan") && line.Contains("Scan workspace for decks")
        );
    }

    [Fact]
    public void BuildPlainLines_FiltersHiddenAndAliasCommandsFromInput()
    {
        static CommandResult Handle(string[] args) => CommandResult.Success;

        var help = new CommandDescriptor(
            "help",
            "Show this message",
            Handle,
            CommandHelpCategory.Shell
        );
        var alias = new CommandDescriptor(
            "--help",
            "Show this message",
            Handle,
            CommandHelpCategory.Shell,
            hidden: true,
            isAlias: true,
            canonical: help
        );
        var hidden = new CommandDescriptor(
            "pdk char help",
            "Show PDK characterization help",
            Handle,
            CommandHelpCategory.PdkCharacterization,
            hidden: true
        );
        var visible = new CommandDescriptor(
            "pdk scan",
            "Scan workspace for decks",
            Handle,
            CommandHelpCategory.Pdk
        );

        var lines = HelpRenderer.BuildPlainLines(new[] { help, alias, hidden, visible }).ToList();

        Assert.Equal(1, lines.Count(line => line.Contains("Show this message")));
        Assert.DoesNotContain(lines, line => line.Contains("pdk char help"));
        Assert.Contains(
            lines,
            line => line.Contains("pdk scan") && line.Contains("Scan workspace for decks")
        );
    }

    [Fact]
    public void ToMarkup_PrefixesHexColorsWithHash()
    {
        var markup = new Color(0x12, 0x34, 0x56).ToMarkup();

        Assert.Equal("#123456", markup);
    }
}
