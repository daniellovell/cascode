using System;
using Spectre.Console;

namespace Cascode.Cli.Output;

public interface ICliOutput
{
    CliOutputMode Mode { get; }

    /// <summary>
    /// When in Spectre mode, provides an IAnsiConsole for stdout rendering. Otherwise null.
    /// </summary>
    IAnsiConsole? Out { get; }

    /// <summary>
    /// When in Spectre mode, provides an IAnsiConsole for stderr rendering. Otherwise null.
    /// </summary>
    IAnsiConsole? Err { get; }

    void WriteLine(string text);
    void WriteErrorLine(string text);

    void Info(string text);
    void Success(string text);
    void Warning(string text);
    void Error(string text);

    /// <summary>
    /// Runs a long operation with a progress reporter. In rich terminals this is a transient
    /// status line (cleared on completion). In plain mode it logs timestamped lines to stderr.
    /// </summary>
    T RunWithProgress<T>(string initialStatus, Func<Action<string>, T> run);
}
