using System;

namespace Cascode.Cli.Output;

internal static class CliOutputFactory
{
    public static ICliOutput Create(ShellState state, Func<bool> isInteractive)
    {
        if (isInteractive())
        {
            return new ShellStateCliOutput(state);
        }

        // Non-interactive commands should stream directly, and suppress ShellState flush.
        state.MarkStreamedOutput();

        // If output is being captured/piped, keep output plain and stable.
        if (Console.IsOutputRedirected || Console.IsErrorRedirected || IsNoColorEnvironment())
        {
            return new PlainConsoleCliOutput();
        }

        return new SpectreCliOutput();
    }

    private static bool IsNoColorEnvironment()
    {
        if (Environment.GetEnvironmentVariable("NO_COLOR") is not null)
        {
            return true;
        }

        var term = Environment.GetEnvironmentVariable("TERM");
        return string.Equals(term, "dumb", StringComparison.OrdinalIgnoreCase);
    }
}
