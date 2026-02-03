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

    /// <summary>
    /// Runs a long operation with multi-task progress tracking. In rich terminals this shows
    /// multiple concurrent tasks with spinners. In plain mode it logs aggregate progress.
    /// </summary>
    T RunWithMultiTaskProgress<T>(Func<IBenchProgressContext, T> run);
}

/// <summary>
/// Context for creating and managing multiple progress tasks.
/// </summary>
public interface IBenchProgressContext
{
    /// <summary>
    /// Adds a new task to the progress display with the given description.
    /// </summary>
    IBenchTask AddTask(string description);
}

/// <summary>
/// Represents a single task in the multi-task progress display.
/// </summary>
public interface IBenchTask
{
    /// <summary>
    /// Updates the description shown for this task.
    /// </summary>
    void UpdateDescription(string description);

    /// <summary>
    /// Marks the task as actively running (shows spinner in Spectre mode).
    /// </summary>
    void StartTask();

    /// <summary>
    /// Marks the task as complete (stops spinner in Spectre mode).
    /// </summary>
    void StopTask();
}
