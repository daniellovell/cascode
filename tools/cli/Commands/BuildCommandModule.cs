using System;
using System.IO;

namespace Cascode.Cli.Commands;

internal sealed class BuildCommandModule : ICommandModule
{
    private readonly ShellState _state;

    public BuildCommandModule(ShellState state)
    {
        _state = state;
    }

    public void Register(CommandRegistry registry)
    {
        registry.Register(new DelegateCliCommand("build", "Compile ADL (preview)", BuildCommand));
    }

    private CommandResult BuildCommand(string[] args)
    {
        if (args.Length == 0)
        {
            _state.AddMessage("Usage: build <file.cas>");
            return CommandResult.Success;
        }
        if (!File.Exists(args[0]))
        {
            _state.AddMessage($"Input file '{args[0]}' not found.");
            return CommandResult.Failure;
        }
        _state.AddMessage($"[preview] build for '{args[0]}' not implemented yet.");
        return CommandResult.Success;
    }
}

