using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cascode.Cli.Output;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Cascode.Cli.Commands;

internal abstract class PdkCommandHandlersSupport
{
    protected readonly ShellState _state;
    protected readonly Func<bool> _isInteractive;
    private readonly CliOutputProvider _outputProvider;
    private readonly ICliOutput _shellOutput;
    private ICliOutput? _nonInteractiveOutput;

    protected PdkCommandHandlersSupport(
        ShellState state,
        Func<bool> isInteractive,
        CliOutputProvider outputProvider
    )
    {
        _state = state;
        _isInteractive = isInteractive;
        _outputProvider = outputProvider;
        _shellOutput = new ShellStateCliOutput(_state);
    }

    protected ICliOutput Output =>
        _isInteractive() ? _shellOutput : _nonInteractiveOutput ??= _outputProvider.Get();

    protected void WriteRenderable(IRenderable renderable)
    {
        if (Output.Out is not null)
        {
            Output.Out.Write(renderable);
            return;
        }

        AnsiConsole.Write(renderable);
    }

    protected static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return name;
    }

    protected static List<string> SplitCsv(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? new List<string>()
            : value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
}
