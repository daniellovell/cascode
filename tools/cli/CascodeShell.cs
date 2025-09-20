using Cascode.Workspace;
using Spectre.Console;
using System.Globalization;
using System.Linq;

namespace Cascode.Cli;

internal sealed class CascodeShell
{
    private readonly WorkspaceScanner _scanner = new();
    private readonly WorkspaceScanStorage _storage = new();
    private readonly ShellState _state;

    public CascodeShell(string workspaceRoot)
    {
        _state = new ShellState(Path.GetFullPath(workspaceRoot));
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

            var tokens = Tokenize(input);
            if (tokens.Length == 0)
            {
                continue;
            }

            if (IsExit(tokens[0]))
            {
                return 0;
            }

            var result = Execute(tokens);
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

        var result = Execute(tokens);
        return result.ExitCode;
    }

    private CommandResult Execute(string[] tokens)
    {
        var command = tokens[0].ToLowerInvariant();
        var args = tokens.Skip(1).ToArray();

        return command switch
        {
            "help" or "--help" or "-h" => ShowHelp(),
            "--version" or "-v" => ShowVersion(),
            "pdk" => HandlePdk(args),
            "build" => HandleBuild(args),
            "char" => HandleChar(args),
            "quit" or "exit" => new CommandResult(0, true),
            _ => UnknownCommand(tokens[0])
        };
    }

    private CommandResult ShowHelp()
    {
        var table = new Table().AddColumn("Command").AddColumn("Description").Border(TableBorder.Rounded);
        table.AddRow("help", "Show this message");
        table.AddRow("pdk scan [path]", "Scan workspace for decks");
        table.AddRow("pdk models", "List discovered decks");
        table.AddRow("pdk model <id|name>", "Inspect a specific deck");
        table.AddRow("char gen <model>", "Generate gm/Id characterization (preview)");
        table.AddRow("char read <model>", "View characterization (preview)");
        table.AddRow("build <file.cas>", "Compile ADL (preview)");
        table.AddRow("quit", "Exit the CLI");

        AnsiConsole.Write(table);
        return CommandResult.Success;
    }

    private CommandResult ShowVersion()
    {
        var version = typeof(CascodeShell).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        AnsiConsole.MarkupLine($"[white]{version}[/]");
        return CommandResult.Success;
    }

    private CommandResult HandlePdk(string[] args)
    {
        if (args.Length == 0)
        {
            _state.AddMessage("Usage: pdk <scan|models|model>");
            return CommandResult.Success;
        }

        return args[0].ToLowerInvariant() switch
        {
            "scan" => PdkScan(args.Skip(1).ToArray()),
            "models" => PdkModels(),
            "model" => PdkModel(args.Skip(1).ToArray()),
            _ => UnknownCommand($"pdk {args[0]}")
        };
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

    private CommandResult PdkModels()
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

    private CommandResult HandleBuild(string[] args)
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

    private CommandResult HandleChar(string[] args)
    {
        if (args.Length < 2)
        {
            _state.AddMessage("Usage: char <gen|read> <model>");
            return CommandResult.Success;
        }

        var sub = args[0].ToLowerInvariant();
        var model = args[1];

        return sub switch
        {
            "gen" => CharacterizationGenerate(model),
            "read" => CharacterizationRead(model),
            _ => UnknownCommand($"char {sub}")
        };
    }

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

    private CommandResult UnknownCommand(string name)
    {
        _state.AddMessage($"Unknown command '{name}'. Type 'help' for a list.");
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

    private static string? Prompt()
    {
        try
        {
            var prompt = new TextPrompt<string>("[green]cascode[/]> ").AllowEmpty();
            return AnsiConsole.Prompt(prompt);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static bool IsExit(string token)
        => token.Equals("quit", StringComparison.OrdinalIgnoreCase)
        || token.Equals("exit", StringComparison.OrdinalIgnoreCase);

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

    private readonly record struct CommandResult(int ExitCode, bool ExitImmediate)
    {
        public static CommandResult Success => new(0, false);
        public static CommandResult Failure => new(1, false);
    }
}
