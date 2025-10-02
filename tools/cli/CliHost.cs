using Cascode.Cli.Services;
using Cascode.Workspace;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Cascode.Cli;

internal sealed class CliHost
{
    private readonly WorkspaceScanner _scanner = new();
    private readonly WorkspaceScanStorage _storage = new();
    private readonly CliConfigStorage _configStorage = new();
    private readonly CommandRegistry _commands = new();
    private readonly CliConfig _config;
    private readonly string _initialWorkspaceRoot;
    private readonly ShellState _state;
    private bool _isInteractive;

    public CliHost(string workspaceRoot)
    {
        _initialWorkspaceRoot = Path.GetFullPath(workspaceRoot);
        _config = _configStorage.Load();

        var startingRoot = _config.PdkRoot ?? _initialWorkspaceRoot;
        _state = new ShellState(Path.GetFullPath(startingRoot));
        if (_config.PdkRoot is not null)
        {
            _state.UpdatePdkRoot(_config.PdkRoot);
        }

        RegisterCommands();
        TryLoadCachedScan(_state.WorkspaceRoot, logFailure: false);
    }

    private void RegisterCommands()
    {
        new Commands.SystemCommandModule(_state).Register(_commands);
        new Commands.PdkCommandModule(_state, _scanner, _storage, _config, _configStorage, _initialWorkspaceRoot, () => _isInteractive).Register(_commands);
        new Commands.CharacterizationCommandModule(_state, _storage).Register(_commands);
        new Commands.BenchCommandModule(_state).Register(_commands);
        new Commands.BuildCommandModule(_state).Register(_commands);
    }

    public int RunInteractive()
    {
        _isInteractive = true;
        while (true)
        {
            Render();
            var input = Prompt();
            if (input is null)
            {
                return 0;
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            _state.RecordCommand(input);
            var tokens = Tokenize(input);
            if (tokens.Length == 0)
            {
                continue;
            }

            var result = Execute(tokens);
            if (!result.ExitImmediate && !tokens[0].Equals("log", StringComparison.OrdinalIgnoreCase))
            {
                _state.PinLog();
            }

            if (result.ExitImmediate)
            {
                return result.ExitCode;
            }
        }
    }

    public int RunOnce(string[] tokens)
    {
        _isInteractive = false;
        if (tokens.Length == 0)
        {
            return 0;
        }

        _state.RecordCommand(string.Join(' ', tokens));
        var result = Execute(tokens);
        if (!tokens[0].Equals("log", StringComparison.OrdinalIgnoreCase))
        {
            _state.PinLog();
        }

        FlushLogToConsole();
        return result.ExitCode;
    }

    private CommandResult Execute(string[] tokens)
    {
        if (tokens.Length == 0)
        {
            return CommandResult.Success;
        }

        if (_commands.TryResolve(tokens, out var descriptor, out var args, out var matchedLength) && descriptor is not null)
        {
            return descriptor.Handler(args);
        }

        return UnknownCommand(tokens, matchedLength);
    }

    private bool TryAdjustDetailOffset(int delta)
    {
        var view = _state.ModelSummary;
        if (view is null || !view.HasDetailRows)
        {
            return false;
        }

        var pageSize = view.DetailPageSize > 0 ? view.DetailPageSize : view.DetailRows.Count;
        var maxOffset = Math.Max(0, view.DetailRows.Count - pageSize);
        var newOffset = Math.Clamp(view.DetailOffset + delta, 0, maxOffset);
        if (newOffset == view.DetailOffset)
        {
            return false;
        }

        var summaryLine = ModelSummaryHelpers.BuildDetailSummaryLine(view.DetailFilters, newOffset, pageSize, view.DetailRows.Count);
        var updatedView = view.WithDetail(newOffset, summaryLine);
        _state.ReplaceModelSummary(updatedView);
        return true;
    }

    private int GetDetailScrollStep()
    {
        var view = _state.ModelSummary;
        if (view is null || !view.HasDetailRows)
        {
            return 1;
        }

        var pageSize = view.DetailPageSize > 0 ? view.DetailPageSize : view.DetailRows.Count;
        return Math.Max(1, pageSize / 4);
    }

    private void TryLoadCachedScan(string workspaceRoot, bool logFailure)
    {
        var scanPath = WorkspaceState.GetScanPath(workspaceRoot);
        if (!File.Exists(scanPath))
        {
            return;
        }

        try
        {
            var scan = _storage.Load(scanPath);
            _state.Scan = scan;
            _state.SelectedDeckIndex = scan.ModelDecks.Count > 0 ? 0 : null;
        }
        catch (Exception ex)
        {
            if (logFailure)
            {
                _state.AddMessage($"Failed to load cached scan: {ex.Message}");
            }
        }
    }

    private void FlushLogToConsole()
    {
        foreach (var message in _state.Messages)
        {
            Console.WriteLine(message);
        }
    }

    private CommandResult UnknownCommand(IReadOnlyList<string> tokens, int matchedLength)
    {
        var typed = string.Join(' ', tokens);
        _state.AddMessage($"Unknown command '{typed}'. Type 'help' for a list.");

        if (matchedLength > 0)
        {
            var prefix = tokens.Take(matchedLength).ToArray();
            var suggestions = _commands.GetSubcommands(prefix).ToArray();
            if (suggestions.Length > 0)
            {
                _state.AddMessage("Available subcommands:");
                var width = suggestions.Max(s => s.DisplayPath.Length);
                foreach (var suggestion in suggestions)
                {
                    var padded = width > 0 ? suggestion.DisplayPath.PadRight(width) : suggestion.DisplayPath;
                    var description = string.IsNullOrEmpty(suggestion.Description) ? string.Empty : $"  {suggestion.Description}";
                    _state.AddMessage($"  {padded}{description}");
                }
            }
        }

        return CommandResult.Failure;
    }

    private void Render()
    {
        AnsiConsole.Clear();
        var layout = ShellRenderer.Build(_state);
        AnsiConsole.Write(layout);
    }

    private string? Prompt()
    {
        return ShellPrompt.ReadCommand(_state, GetDetailScrollStep, TryAdjustDetailOffset, Render);
    }

    private static string[] Tokenize(string input)
    {
        var tokens = new List<string>();
        var current = new List<char>();
        var inQuotes = false;

        foreach (var ch in input)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Count > 0)
                {
                    tokens.Add(new string(current.ToArray()));
                    current.Clear();
                }
                continue;
            }

            current.Add(ch);
        }

        if (current.Count > 0)
        {
            tokens.Add(new string(current.ToArray()));
        }

        return tokens.ToArray();
    }
}
