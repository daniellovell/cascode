using System;

namespace Cascode.Cli.Output;

internal sealed class ShellStateCliOutput : ICliOutput
{
    private readonly ShellState _state;

    public ShellStateCliOutput(ShellState state)
    {
        _state = state;
    }

    public CliOutputMode Mode => CliOutputMode.Shell;

    public Spectre.Console.IAnsiConsole? Out => null;

    public Spectre.Console.IAnsiConsole? Err => null;

    public void WriteLine(string text) => _state.AddMessage(text);

    public void WriteErrorLine(string text) => _state.AddMessage(text);

    public void Info(string text) => _state.AddMessage(text);

    public void Success(string text) => _state.AddMessage(text);

    public void Warning(string text) => _state.AddMessage($"WARN: {text}");

    public void Error(string text) => _state.AddMessage($"ERROR: {text}");

    public T RunWithProgress<T>(string initialStatus, Func<Action<string>, T> run)
    {
        _state.AddMessage(initialStatus);
        return run(_state.AddMessage);
    }
}
