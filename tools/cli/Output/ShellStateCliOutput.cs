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

    public T RunWithMultiTaskProgress<T>(Func<IBenchProgressContext, T> run)
    {
        var context = new ShellProgressContext(_state);
        return run(context);
    }

    private sealed class ShellProgressContext : IBenchProgressContext
    {
        private readonly ShellState _state;

        public ShellProgressContext(ShellState state) => _state = state;

        public IBenchTask AddTask(string description) => new ShellProgressTask(_state, description);
    }

    private sealed class ShellProgressTask : IBenchTask
    {
        private readonly ShellState _state;
        private string _description;

        public ShellProgressTask(ShellState state, string description)
        {
            _state = state;
            _description = description;
        }

        public void UpdateDescription(string description) => _description = description;

        public void StartTask() => _state.AddMessage($"Started: {_description}");

        public void StopTask() => _state.AddMessage($"Completed: {_description}");
    }
}
