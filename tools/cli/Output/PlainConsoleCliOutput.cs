using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

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

    public T RunWithMultiTaskProgress<T>(Func<IBenchProgressContext, T> run)
    {
        var sw = Stopwatch.StartNew();
        var context = new PlainProgressContext(sw);
        return run(context);
    }

    private sealed class PlainProgressContext : IBenchProgressContext
    {
        private readonly Stopwatch _sw;
        private readonly List<PlainProgressTask> _tasks = new();
        private readonly object _lock = new();
        private string _lastRunningMessage = string.Empty;

        public PlainProgressContext(Stopwatch sw) => _sw = sw;

        public IBenchTask AddTask(string description)
        {
            var task = new PlainProgressTask(description, _sw, this);
            lock (_lock)
            {
                _tasks.Add(task);
            }
            return task;
        }

        public void OnTaskStarted(PlainProgressTask task)
        {
            lock (_lock)
            {
                var running = _tasks.Where(t => t.IsRunning).Select(t => t.ShortName).ToList();
                var total = _tasks.Count;
                if (running.Count > 0)
                {
                    var runningStr = string.Join(", ", running);
                    var message = $"Running {running.Count}/{total}: {runningStr}";
                    if (message != _lastRunningMessage)
                    {
                        _lastRunningMessage = message;
                        Console.Error.WriteLine($"[{_sw.Elapsed:hh\\:mm\\:ss}] {message}");
                    }
                }
            }
        }

        public void OnTaskStopped(PlainProgressTask task)
        {
            lock (_lock)
            {
                var total = _tasks.Count;
                var completed = _tasks.Count(t => t.IsComplete);
                Console.Error.WriteLine(
                    $"[{_sw.Elapsed:hh\\:mm\\:ss}] Completed {completed}/{total}: {task.ShortName}"
                );
            }
        }
    }

    private sealed class PlainProgressTask : IBenchTask
    {
        private readonly Stopwatch _sw;
        private readonly PlainProgressContext _context;
        private string _description;

        public PlainProgressTask(string description, Stopwatch sw, PlainProgressContext context)
        {
            _description = description;
            _sw = sw;
            _context = context;
        }

        public string ShortName => ExtractShortName(_description);
        public bool IsRunning { get; private set; }
        public bool IsComplete { get; private set; }

        public void UpdateDescription(string description) => _description = description;

        public void StartTask()
        {
            IsRunning = true;
            _context.OnTaskStarted(this);
        }

        public void StopTask()
        {
            IsRunning = false;
            IsComplete = true;
            _context.OnTaskStopped(this);
        }

        private static string ExtractShortName(string description)
        {
            var trimmed = description.TrimStart('○', '●', '✓', '✗', ' ');
            var spaceIdx = trimmed.IndexOf(' ');
            return spaceIdx > 0 ? trimmed[..spaceIdx] : trimmed;
        }
    }
}
