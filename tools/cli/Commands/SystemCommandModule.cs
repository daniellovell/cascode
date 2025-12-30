using System;
using System.Globalization;
using System.Reflection;

namespace Cascode.Cli.Commands;

// System-level commands (help/version/home/log/quit) extracted from the shell.
internal sealed class SystemCommandModule : ICommandModule
{
    private readonly ShellState _state;
    private CommandRegistry? _registry;
    internal static readonly string[] HelpAliases = new[] { "-h", "--help" };
    internal static readonly string[] VersionAliases = new[] { "--version", "-v" };
    internal static readonly string[] ExitAliases = new[] { "exit" };

    public SystemCommandModule(ShellState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public void Register(CommandRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));

        registry.Register(
            new DelegateCliCommand(
                path: "help",
                description: "Show this message",
                handler: ShowHelp,
                aliases: HelpAliases
            )
        );

        registry.Register(
            new DelegateCliCommand(
                path: "version",
                description: "Show CLI version",
                handler: ShowVersion,
                hidden: true,
                aliases: VersionAliases
            )
        );

        registry.Register(
            new DelegateCliCommand(
                path: "home",
                description: "Return to dashboard layout",
                handler: Home
            )
        );

        registry.Register(
            new DelegateCliCommand(
                path: "log",
                description: "Scroll the log history",
                handler: Log,
                hidden: true
            )
        );

        registry.Register(
            new DelegateCliCommand(
                path: "quit",
                description: "Exit the CLI",
                handler: Quit,
                aliases: ExitAliases
            )
        );
    }

    private CommandResult ShowHelp(string[] args)
    {
        _state.AddMessage("Commands:");
        var commands = _registry!.GetCanonicalCommands();
        var array = System.Linq.Enumerable.ToArray(commands);
        var width =
            array.Length == 0 ? 0 : System.Linq.Enumerable.Max(array, c => c.DisplayPath.Length);
        foreach (var command in array)
        {
            var padded = width > 0 ? command.DisplayPath.PadRight(width) : command.DisplayPath;
            var description = string.IsNullOrEmpty(command.Description)
                ? string.Empty
                : $"  {command.Description}";
            _state.AddMessage($"  {padded}{description}");
        }
        return CommandResult.Success;
    }

    private CommandResult ShowVersion(string[] args)
    {
        var asm = typeof(SystemCommandModule).Assembly;
        var info =
            asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = string.IsNullOrWhiteSpace(info)
            ? (asm.GetName().Version?.ToString() ?? "0.0.0")
            : info.Split('+', 2)[0]; // strip build metadata if present
        _state.AddMessage(version);
        return CommandResult.Success;
    }

    private CommandResult Home(string[] args)
    {
        if (_state.ViewMode == ShellViewMode.Home)
        {
            _state.AddMessage("Already on dashboard layout.");
            return CommandResult.Success;
        }
        _state.ShowHome();
        _state.AddMessage("Returned to dashboard layout.");
        return CommandResult.Success;
    }

    private CommandResult Log(string[] args)
    {
        if (args.Length == 0)
        {
            _state.AddMessage("Usage: log <up|down|pageup|pagedown|top|bottom> [count]");
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
                _state.AddMessage($"Unknown log action '{action}'.");
                return CommandResult.Failure;
        }

        return CommandResult.Success;
    }

    private static CommandResult Quit(string[] args) => new(0, true);
}
