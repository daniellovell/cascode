using System;
using System.Globalization;
using System.Reflection;
using Cascode.Cli.Output;

namespace Cascode.Cli.Commands;

// System-level commands (help/version/home/log/quit) extracted from the shell.
internal sealed class SystemCommandModule : ICommandModule
{
    private readonly ShellState _state;
    private readonly CliOutputProvider _output;
    private CommandRegistry? _registry;
    internal static readonly string[] HelpAliases = new[] { "-h", "--help" };
    internal static readonly string[] VersionAliases = new[] { "--version", "-v" };
    internal static readonly string[] ExitAliases = new[] { "exit" };

    public SystemCommandModule(ShellState state, CliOutputProvider output)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    public void Register(CommandRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));

        registry.Register(
            new DelegateCliCommand(
                path: "help",
                description: "Show this message",
                handler: ShowHelp,
                helpCategory: CommandHelpCategory.Shell,
                aliases: HelpAliases
            )
        );

        registry.Register(
            new DelegateCliCommand(
                path: "version",
                description: "Show CLI version",
                handler: ShowVersion,
                hidden: true,
                helpCategory: CommandHelpCategory.Shell,
                aliases: VersionAliases
            )
        );

        registry.Register(
            new DelegateCliCommand(
                path: "home",
                description: "Return to dashboard layout",
                handler: Home,
                helpCategory: CommandHelpCategory.Shell
            )
        );

        registry.Register(
            new DelegateCliCommand(
                path: "log",
                description: "Scroll the log history",
                handler: Log,
                hidden: true,
                helpCategory: CommandHelpCategory.Shell
            )
        );

        registry.Register(
            new DelegateCliCommand(
                path: "quit",
                description: "Exit the CLI",
                handler: Quit,
                helpCategory: CommandHelpCategory.Shell,
                aliases: ExitAliases
            )
        );
    }

    private CommandResult ShowHelp(string[] args)
    {
        HelpRenderer.RenderRootHelp(_output.Get(), _registry!.GetCanonicalCommands());
        return CommandResult.Success;
    }

    private CommandResult ShowVersion(string[] args)
    {
        var output = _output.Get();
        var asm = typeof(SystemCommandModule).Assembly;
        var info =
            asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = string.IsNullOrWhiteSpace(info)
            ? (asm.GetName().Version?.ToString() ?? "0.0.0")
            : info.Split('+', 2)[0]; // strip build metadata if present
        output.WriteLine(version);
        return CommandResult.Success;
    }

    private CommandResult Home(string[] args)
    {
        var output = _output.Get();
        if (_state.ViewMode == ShellViewMode.Home)
        {
            output.WriteLine("Already on dashboard layout.");
            return CommandResult.Success;
        }
        _state.ShowHome();
        output.WriteLine("Returned to dashboard layout.");
        return CommandResult.Success;
    }

    private CommandResult Log(string[] args)
    {
        var output = _output.Get();
        if (args.Length == 0)
        {
            output.WriteLine("Usage: log <up|down|pageup|pagedown|top|bottom> [count]");
            return CommandResult.Success;
        }

        var action = args[0].ToLowerInvariant();
        var defaultStep = Math.Max(1, _state.LogViewport / 4);
        var count = defaultStep;
        if (
            args.Length > 1
            && int.TryParse(
                args[1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed
            )
        )
        {
            count = Math.Max(1, parsed);
        }

        switch (action)
        {
            case "up":
                _state.ScrollLogUp(count);
                break;
            case "down":
                _state.ScrollLogDown(count);
                break;
            case "pageup":
                _state.ScrollLogUp(_state.LogViewport);
                break;
            case "pagedown":
                _state.ScrollLogDown(_state.LogViewport);
                break;
            case "top" or "home":
                _state.ScrollLogHome();
                break;
            case "bottom" or "end":
                _state.ScrollLogEnd();
                break;
            default:
                output.Error($"Unknown log action '{action}'.");
                return CommandResult.Failure;
        }

        return CommandResult.Success;
    }

    private static CommandResult Quit(string[] args) => new(0, true);
}
