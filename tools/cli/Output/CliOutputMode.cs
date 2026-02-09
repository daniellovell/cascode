namespace Cascode.Cli.Output;

public enum CliOutputMode
{
    /// <summary>
    /// Interactive TUI mode: command output is appended to the ShellState log.
    /// </summary>
    Shell,

    /// <summary>
    /// Plain text mode (stable, pipe-friendly).
    /// </summary>
    Plain,

    /// <summary>
    /// Rich terminal mode (Spectre.Console tables, colors, live status).
    /// </summary>
    Spectre,
}
