using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Spectre.Console;

namespace Cascode.Cli.Output;

internal sealed class SpectreCliOutput : ICliOutput
{
    private readonly IAnsiConsole _out;
    private readonly IAnsiConsole _err;

    public SpectreCliOutput()
    {
        _out = CreateConsole(Console.Out);
        _err = CreateConsole(Console.Error);
    }

    public CliOutputMode Mode => CliOutputMode.Spectre;

    public IAnsiConsole Out => _out;

    public IAnsiConsole Err => _err;

    public void WriteLine(string text) => _out.WriteLine(text);

    public void WriteErrorLine(string text) => _err.WriteLine(text);

    public void Info(string text) => _out.MarkupLine($"[grey]{Escape(text)}[/]");

    public void Success(string text) => _out.MarkupLine($"[green]{Escape(text)}[/]");

    public void Warning(string text) => _err.MarkupLine($"[yellow]{Escape(text)}[/]");

    public void Error(string text) => _err.MarkupLine($"[red]{Escape(text)}[/]");

    public T RunWithProgress<T>(string initialStatus, Func<Action<string>, T> run)
    {
        var sw = Stopwatch.StartNew();

        // Keep progress transient and clean: a single live status line on stderr.
        return _err.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("grey"))
            .Start(
                initialStatus,
                ctx =>
                {
                    void Progress(string msg)
                    {
                        var ts = sw.Elapsed;
                        // Status text is parsed as Spectre markup. Avoid any brackets in the raw
                        // text so we never accidentally create a style tag like "[00:00:00]".
                        ctx.Status(Markup.Escape($"{ts:hh\\:mm\\:ss} {msg}"));
                    }

                    try
                    {
                        return run(Progress);
                    }
                    finally
                    {
                        sw.Stop();
                    }
                }
            );
    }

    public T RunWithMultiTaskProgress<T>(Func<IBenchProgressContext, T> run)
    {
        return _err.Progress()
            .AutoClear(true)
            .HideCompleted(false)
            .Columns(
                new SpinnerColumn { Style = Style.Parse("grey") },
                new TaskDescriptionColumn { Alignment = Justify.Left }
            )
            .Start(ctx =>
            {
                var progressContext = new SpectreProgressContext(ctx);
                return run(progressContext);
            });
    }

    private static IAnsiConsole CreateConsole(TextWriter writer)
    {
        var ansi = writer == Console.Out ? Console.IsOutputRedirected : Console.IsErrorRedirected;
        var settings = new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = ansi ? AnsiSupport.No : AnsiSupport.Detect,
            ColorSystem = ansi ? ColorSystemSupport.NoColors : ColorSystemSupport.Detect,
        };
        return AnsiConsole.Create(settings);
    }

    private static string Escape(string text) => Markup.Escape(text);

    private sealed class SpectreProgressContext : IBenchProgressContext
    {
        private readonly ProgressContext _ctx;

        public SpectreProgressContext(ProgressContext ctx) => _ctx = ctx;

        public IBenchTask AddTask(string description)
        {
            var task = _ctx.AddTask(Markup.Escape(description), autoStart: false, maxValue: 1);
            return new SpectreProgressTask(task);
        }
    }

    private sealed class SpectreProgressTask : IBenchTask
    {
        private readonly ProgressTask _task;

        public SpectreProgressTask(ProgressTask task) => _task = task;

        public void UpdateDescription(string description) =>
            _task.Description = Markup.Escape(description);

        public void StartTask() => _task.StartTask();

        public void StopTask()
        {
            _task.Value = _task.MaxValue;
            _task.StopTask();
        }
    }
}
