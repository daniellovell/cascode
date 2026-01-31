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
                        // Status text is parsed as Spectre markup; escape to avoid accidental style tags.
                        ctx.Status(Markup.Escape($"[{ts:hh\\:mm\\:ss}] {msg}"));
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
}
