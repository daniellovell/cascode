using System;
using System.Diagnostics;
using System.Globalization;

namespace Cascode.Cli.Output;

internal sealed class PlainConsoleCliOutput : ICliOutput
{
    public CliOutputMode Mode => CliOutputMode.Plain;

    public Spectre.Console.IAnsiConsole? Out => null;

    public Spectre.Console.IAnsiConsole? Err => null;

    public void WriteLine(string text) => Console.Out.WriteLine(text);

    public void WriteErrorLine(string text) => Console.Error.WriteLine(text);

    public void Info(string text) => Console.Out.WriteLine(text);

    public void Success(string text) => Console.Out.WriteLine(text);

    public void Warning(string text) => Console.Error.WriteLine($"WARN: {text}");

    public void Error(string text) => Console.Error.WriteLine($"ERROR: {text}");

    public T RunWithProgress<T>(string initialStatus, Func<Action<string>, T> run)
    {
        var sw = Stopwatch.StartNew();
        Console.Error.WriteLine($"[{sw.Elapsed:hh\\:mm\\:ss}] {initialStatus}");

        void Progress(string msg)
        {
            var ts = sw.Elapsed;
            Console.Error.WriteLine($"[{ts:hh\\:mm\\:ss}] {msg}");
        }

        return run(Progress);
    }
}
