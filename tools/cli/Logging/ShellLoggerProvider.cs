using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;

namespace Cascode.Cli.Logging;

internal sealed class ShellLoggerProvider : ILoggerProvider
{
    private readonly ShellState _state;
    private readonly ConcurrentDictionary<string, ILogger> _loggers = new(StringComparer.OrdinalIgnoreCase);
    private readonly LogLevel _minLevel;

    public ShellLoggerProvider(ShellState state, LogLevel minLevel = LogLevel.Information)
    {
        _state = state;
        _minLevel = minLevel;
    }

    public ILogger CreateLogger(string categoryName)
        => _loggers.GetOrAdd(categoryName, c => new ShellLogger(_state, c, _minLevel));

    public void Dispose() => _loggers.Clear();

    private sealed class ShellLogger : ILogger
    {
        private readonly ShellState _state;
        private readonly string _category;
        private readonly LogLevel _minLevel;

        public ShellLogger(ShellState state, string category, LogLevel minLevel)
        {
            _state = state;
            _category = category;
            _minLevel = minLevel;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel) || formatter is null)
            {
                return;
            }

            var message = formatter(state, exception);
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            var levelTag = logLevel switch
            {
                LogLevel.Trace => "trace",
                LogLevel.Debug => "debug",
                LogLevel.Information => "info",
                LogLevel.Warning => "warn",
                LogLevel.Error => "error",
                LogLevel.Critical => "crit",
                _ => "log"
            };

            var line = string.IsNullOrEmpty(_category)
                ? $"[{levelTag}] {message}"
                : $"[{levelTag}][{_category}] {message}";

            _state.AddMessage(line);
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}

