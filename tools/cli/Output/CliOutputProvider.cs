using System;

namespace Cascode.Cli.Output;

internal sealed class CliOutputProvider
{
    private readonly ShellState _state;
    private readonly Func<bool> _isInteractive;

    public CliOutputProvider(ShellState state, Func<bool> isInteractive)
    {
        _state = state;
        _isInteractive = isInteractive;
    }

    public ICliOutput Get() => CliOutputFactory.Create(_state, _isInteractive);
}
