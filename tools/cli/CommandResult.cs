namespace Cascode.Cli;

internal readonly record struct CommandResult(int ExitCode, bool ExitImmediate)
{
    public static CommandResult Success => new(0, false);
    public static CommandResult Failure => new(1, false);
}
