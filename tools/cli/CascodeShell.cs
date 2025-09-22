using Cascode.Workspace;
using Spectre.Console;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Cascode.Cli;

internal sealed class CascodeShell
{
    private readonly WorkspaceScanner _scanner = new();
    private readonly WorkspaceScanStorage _storage = new();
    private readonly CliConfigStorage _configStorage = new();
    private readonly CommandRegistry _commands = new();
    private readonly CliConfig _config;
    private readonly string _initialWorkspaceRoot;
    private readonly ShellState _state;

    public CascodeShell(string workspaceRoot)
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
        _commands.Register("help", "Show this message", ShowHelp, aliases: new[] { "-h", "--help" });
        _commands.Register("version", "Show CLI version", ShowVersion, hidden: true, aliases: new[] { "--version", "-v" });

        _commands.Register("pdk", "Manage PDK workspace", ShowPdkUsage);
        _commands.Register("pdk scan", "Scan workspace for decks", PdkScan);
        _commands.Register("pdk models", "List discovered decks", PdkModels);
        _commands.Register("pdk model", "Inspect a specific deck", PdkModel);
        _commands.Register("pdk set-dir", "Set or clear the default PDK workspace", PdkSetDir);

        _commands.Register("char", "Characterization commands", ShowCharUsage);
        _commands.Register("char gen", "Generate gm/Id characterization (preview)", CharacterizationGenerateCommand);
        _commands.Register("char read", "View characterization output (preview)", CharacterizationReadCommand);

        _commands.Register("build", "Compile ADL (preview)", BuildCommand);
        _commands.Register("log", "Scroll the log history", HandleLog, hidden: true);

        _commands.Register("quit", "Exit the CLI", Quit, aliases: new[] { "exit" });
    }

    public int RunInteractive()
    {
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
        if (tokens.Length == 0)
        {
            return 0;
        }

        var raw = string.Join(' ', tokens);
        _state.RecordCommand(raw);
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

    private CommandResult ShowHelp(string[] args)
    {
        _state.AddMessage("Commands:");

        var commands = _commands.GetCanonicalCommands().ToArray();
        var width = commands.Length == 0 ? 0 : commands.Max(c => c.DisplayPath.Length);

        foreach (var command in commands)
        {
            var padded = width > 0 ? command.DisplayPath.PadRight(width) : command.DisplayPath;
            var description = string.IsNullOrEmpty(command.Description) ? string.Empty : $"  {command.Description}";
            _state.AddMessage($"  {padded}{description}");
        }

        return CommandResult.Success;
    }

    private CommandResult ShowVersion(string[] args)
    {
        var version = typeof(CascodeShell).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        _state.AddMessage(version);
        return CommandResult.Success;
    }

    private CommandResult ShowPdkUsage(string[] args)
    {
        _state.AddMessage("Usage: pdk <subcommand>");
        var subcommands = _commands.GetSubcommands(new[] { "pdk" }).ToArray();
        var width = subcommands.Length == 0 ? 0 : subcommands.Max(c => c.DisplayPath.Length);

        foreach (var sub in subcommands)
        {
            var padded = width > 0 ? sub.DisplayPath.PadRight(width) : sub.DisplayPath;
            var description = string.IsNullOrEmpty(sub.Description) ? string.Empty : $"  {sub.Description}";
            _state.AddMessage($"  {padded}{description}");
        }

        return CommandResult.Success;
    }

    private CommandResult PdkScan(string[] args)
    {
        var targetRoot = args.Length > 0 ? args[0] : _state.WorkspaceRoot;
        targetRoot = Path.GetFullPath(targetRoot);

        _state.SetWorkspace(targetRoot);
        _state.AddMessage($"Scanning workspace {targetRoot}");

        var result = _scanner.Scan(targetRoot);
        _state.Scan = result;
        _state.SelectedDeckIndex = result.ModelDecks.Count > 0 ? 0 : null;

        var scanPath = WorkspaceState.GetScanPath(targetRoot);
        _storage.Save(result, scanPath);

        _state.AddMessage($"Found {result.Libraries.Count} libraries, {result.ModelDecks.Count} model decks.");
        foreach (var warning in result.Warnings)
        {
            _state.AddMessage($"Warning: {warning}");
        }

        return CommandResult.Success;
    }

    private CommandResult PdkModels(string[] args)
    {
        var scan = EnsureScan();
        if (scan is null)
        {
            return CommandResult.Failure;
        }

        if (scan.ModelDecks.Count == 0)
        {
            _state.AddMessage("No model decks discovered. Run pdk scan.");
            return CommandResult.Success;
        }

        var table = new Table().AddColumn("#").AddColumn("Deck").AddColumn("Sections");
        table.Border(TableBorder.Rounded);
        for (var i = 0; i < scan.ModelDecks.Count; i++)
        {
            var deck = scan.ModelDecks[i];
            table.AddRow((i + 1).ToString(), Path.GetFileName(deck.DeckPath), deck.Sections.Count.ToString());
        }
        AnsiConsole.Write(table);
        return CommandResult.Success;
    }

    private CommandResult PdkModel(string[] args)
    {
        var scan = EnsureScan();
        if (scan is null)
        {
            return CommandResult.Failure;
        }

        if (args.Length == 0)
        {
            _state.AddMessage("Usage: pdk model <index|name>");
            return CommandResult.Success;
        }

        ModelDeckRecord? deck = null;
        var index = -1;
        if (int.TryParse(args[0], out var idx))
        {
            idx -= 1;
            if (idx >= 0 && idx < scan.ModelDecks.Count)
            {
                deck = scan.ModelDecks[idx];
                index = idx;
            }
        }
        else
        {
            for (var i = 0; i < scan.ModelDecks.Count; i++)
            {
                var candidate = scan.ModelDecks[i];
                if (candidate.DeckPath.Contains(args[0], StringComparison.OrdinalIgnoreCase))
                {
                    deck = candidate;
                    index = i;
                    break;
                }
            }
        }

        if (deck is null || index < 0)
        {
            _state.AddMessage("Model deck not found.");
            return CommandResult.Failure;
        }

        _state.SelectedDeckIndex = index;

        var table = new Table().AddColumn("Field").AddColumn("Value").Border(TableBorder.Rounded);
        table.AddRow("Path", deck.DeckPath);
        table.AddRow("Sections", deck.Sections.Count > 0 ? string.Join(", ", deck.Sections) : "(none)");
        table.AddRow("Includes", deck.Includes.Count.ToString());
        AnsiConsole.Write(table);

        if (deck.Includes.Count > 0)
        {
            var includes = new Table().AddColumn("Includes - first 10").Border(TableBorder.None);
            foreach (var include in deck.Includes.Take(10))
            {
                includes.AddRow(include);
            }
            if (deck.Includes.Count > 10)
            {
                includes.AddRow($"... ({deck.Includes.Count - 10} more)");
            }
            AnsiConsole.Write(includes);
        }

        return CommandResult.Success;
    }

    private CommandResult PdkSetDir(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("--clear", StringComparison.OrdinalIgnoreCase))
        {
            _config.PdkRoot = null;
            _configStorage.Save(_config);
            _state.UpdatePdkRoot(null);
            _state.SetWorkspace(_initialWorkspaceRoot);
            _state.AddMessage("Cleared default PDK workspace preference.");
            return CommandResult.Success;
        }

        if (args.Length > 0)
        {
            return ApplyPdkDirectory(args[0]);
        }

        var current = _state.PdkRoot ?? _state.WorkspaceRoot;
        var input = AnsiConsole.Ask<string>("Enter PDK workspace directory (leave blank to cancel):", current);
        if (string.IsNullOrWhiteSpace(input))
        {
            _state.AddMessage("PDK workspace unchanged.");
            return CommandResult.Success;
        }

        return ApplyPdkDirectory(input);
    }

    private CommandResult ApplyPdkDirectory(string path)
    {
        try
        {
            var resolved = NormalizePath(path);
            if (!Directory.Exists(resolved))
            {
                _state.AddMessage($"Directory '{resolved}' not found.");
                return CommandResult.Failure;
            }

            _config.PdkRoot = resolved;
            _configStorage.Save(_config);
            _state.UpdatePdkRoot(resolved);
            _state.SetWorkspace(resolved);
            TryLoadCachedScan(resolved, logFailure: true);
            _state.AddMessage($"PDK workspace set to {resolved}");
            return CommandResult.Success;
        }
        catch (Exception ex)
        {
            _state.AddMessage($"Invalid path: {ex.Message}");
            return CommandResult.Failure;
        }
    }

    private CommandResult BuildCommand(string[] args)
    {
        if (args.Length == 0)
        {
            _state.AddMessage("Usage: build <file.cas>");
            return CommandResult.Success;
        }

        if (!File.Exists(args[0]))
        {
            _state.AddMessage($"Input file '{args[0]}' not found.");
            return CommandResult.Failure;
        }

        _state.AddMessage($"[preview] build for '{args[0]}' not implemented yet.");
        return CommandResult.Success;
    }

    private CommandResult ShowCharUsage(string[] args)
    {
        _state.AddMessage("Usage: char <subcommand>");
        var subs = _commands.GetSubcommands(new[] { "char" }).ToArray();
        var width = subs.Length == 0 ? 0 : subs.Max(c => c.DisplayPath.Length);

        foreach (var sub in subs)
        {
            var padded = width > 0 ? sub.DisplayPath.PadRight(width) : sub.DisplayPath;
            var description = string.IsNullOrEmpty(sub.Description) ? string.Empty : $"  {sub.Description}";
            _state.AddMessage($"  {padded}{description}");
        }

        return CommandResult.Success;
    }

    private CommandResult CharacterizationGenerateCommand(string[] args)
    {
        if (args.Length == 0)
        {
            _state.AddMessage("Usage: char gen <model>");
            return CommandResult.Success;
        }

        return CharacterizationGenerate(args[0]);
    }

    private CommandResult CharacterizationReadCommand(string[] args)
    {
        if (args.Length == 0)
        {
            _state.AddMessage("Usage: char read <model>");
            return CommandResult.Success;
        }

        return CharacterizationRead(args[0]);
    }

    private static CommandResult Quit(string[] args) => new(0, true);

    private CommandResult CharacterizationGenerate(string model)
    {
        _state.AddMessage($"[preview] Characterization generation for '{model}' not implemented yet.");
        return CommandResult.Success;
    }

    private CommandResult CharacterizationRead(string model)
    {
        _state.AddMessage($"[preview] Characterization output for '{model}' not available yet.");
        return CommandResult.Success;
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

    private CommandResult HandleLog(string[] args)
    {
        if (args.Length == 0)
        {
            _state.AddMessage("Usage: log <up|down|pageup|pagedown|top|bottom> [count]");
            return CommandResult.Success;
        }

        var action = args[0].ToLowerInvariant();
        var defaultStep = Math.Max(1, _state.LogViewport / 4);
        var count = defaultStep;
        if (args.Length > 1 && int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            count = Math.Max(1, parsed);
        }

        switch (action)
        {
            case "up":
                _state.ScrollLogUp(count);
                break;
            case "down":
                _state.ScrollLogDown(count);
                break;
            case "pageup":
                _state.ScrollLogUp(_state.LogViewport);
                break;
            case "pagedown":
                _state.ScrollLogDown(_state.LogViewport);
                break;
            case "top" or "home":
                _state.ScrollLogHome();
                break;
            case "bottom" or "end":
                _state.ScrollLogEnd();
                break;
            default:
                _state.AddMessage($"Unknown log action '{action}'.");
                return CommandResult.Failure;
        }

        return CommandResult.Success;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path must not be empty", nameof(path));
        }

        var expanded = ExpandHomePath(path);
        return Path.GetFullPath(expanded);
    }

    private static string ExpandHomePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("~", StringComparison.Ordinal))
        {
            return path;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
        {
            return path;
        }

        if (path.Length == 1)
        {
            return home;
        }

        var remainder = path[1..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.Combine(home, remainder);
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

    private WorkspaceScanResult? EnsureScan()
    {
        if (_state.Scan is not null)
        {
            return _state.Scan;
        }

        var scanPath = WorkspaceState.GetScanPath(_state.WorkspaceRoot);
        if (File.Exists(scanPath))
        {
            try
            {
                _state.Scan = _storage.Load(scanPath);
                _state.SelectedDeckIndex = _state.Scan.ModelDecks.Count > 0 ? 0 : null;
                return _state.Scan;
            }
            catch (Exception ex)
            {
                _state.AddMessage($"Failed to load cached scan: {ex.Message}");
            }
        }

        _state.AddMessage("No workspace scan available. Run pdk scan.");
        return null;
    }

    private void Render()
    {
        AnsiConsole.Clear();
        var layout = ShellRenderer.Build(_state);
        AnsiConsole.Write(layout);
    }

    private string? Prompt()
    {
        var console = AnsiConsole.Console;

        try
        {
            var buffer = new StringBuilder();
            _state.ResetHistoryCursor();
            WritePrompt(buffer.ToString());

            while (true)
            {
                var keyInfo = console.Input.ReadKey(intercept: true);
                if (keyInfo is null)
                {
                    continue;
                }

                var key = keyInfo.Value;

                if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.C)
                {
                    return null;
                }

                if ((key.Modifiers & ConsoleModifiers.Shift) != 0 && key.Key == ConsoleKey.UpArrow)
                {
                    var step = Math.Max(1, _state.LogViewport / 4);
                    _state.ScrollLogUp(step);
                    Render();
                    WritePrompt(buffer.ToString());
                    continue;
                }

                if ((key.Modifiers & ConsoleModifiers.Shift) != 0 && key.Key == ConsoleKey.DownArrow)
                {
                    var step = Math.Max(1, _state.LogViewport / 4);
                    _state.ScrollLogDown(step);
                    Render();
                    WritePrompt(buffer.ToString());
                    continue;
                }

                if (key.Key == ConsoleKey.UpArrow)
                {
                    if (_state.TryHistoryPrevious(out var command))
                    {
                        buffer.Clear();
                        buffer.Append(command);
                        WritePrompt(buffer.ToString());
                    }
                    continue;
                }

                if (key.Key == ConsoleKey.DownArrow)
                {
                    if (_state.TryHistoryNext(out var command))
                    {
                        buffer.Clear();
                        buffer.Append(command);
                        WritePrompt(buffer.ToString());
                    }
                    continue;
                }

                if (key.Key == ConsoleKey.PageUp)
                {
                    _state.ScrollLogUp(_state.LogViewport);
                    Render();
                    WritePrompt(buffer.ToString());
                    continue;
                }

                if (key.Key == ConsoleKey.PageDown)
                {
                    _state.ScrollLogDown(_state.LogViewport);
                    Render();
                    WritePrompt(buffer.ToString());
                    continue;
                }

                if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.Home)
                {
                    _state.ScrollLogHome();
                    Render();
                    WritePrompt(buffer.ToString());
                    continue;
                }

                if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.End)
                {
                    _state.ScrollLogEnd();
                    Render();
                    WritePrompt(buffer.ToString());
                    continue;
                }

                if (key.Key == ConsoleKey.Enter)
                {
                    console.WriteLine();
                    return buffer.ToString();
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (buffer.Length > 0)
                    {
                        buffer.Length--;
                        WritePrompt(buffer.ToString());
                    }
                    continue;
                }

                if (key.Key == ConsoleKey.Escape)
                {
                    buffer.Clear();
                    _state.ResetHistoryCursor();
                    Render();
                    WritePrompt(buffer.ToString());
                    continue;
                }

                var ch = key.KeyChar;
                if (!char.IsControl(ch))
                {
                    buffer.Append(ch);
                    WritePrompt(buffer.ToString());
                }
            }
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static void WritePrompt(string buffer)
    {
        ClearPromptLine();
        AnsiConsole.Markup("[green]cascode[/]> ");
        if (!string.IsNullOrEmpty(buffer))
        {
            AnsiConsole.Console.Write(buffer);
        }
    }

    private static void ClearPromptLine()
    {
        const string ClearSequence = "\u001b[2K\r";
        try
        {
            AnsiConsole.Console.Write(ClearSequence);
        }
        catch
        {
            try
            {
                System.Console.Write('\r');
                var width = Math.Max(0, System.Console.BufferWidth - 1);
                if (width > 0)
                {
                    System.Console.Write(new string(' ', width));
                }
                System.Console.Write('\r');
            }
            catch
            {
                // ignored
            }
        }
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
