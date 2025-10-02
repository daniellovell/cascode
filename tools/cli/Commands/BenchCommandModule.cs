using System;
using System.Linq;

namespace Cascode.Cli.Commands;

internal sealed class BenchCommandModule : ICommandModule
{
    private readonly ShellState _state;

    public BenchCommandModule(ShellState state)
    {
        _state = state;
    }

    public void Register(CommandRegistry registry)
    {
        registry.Register(new DelegateCliCommand("bench", "Bench and harness commands", ShowBenchUsage));
        registry.Register(new DelegateCliCommand("bench harness", "Harness helpers", ShowBenchHarnessUsage));
        registry.Register(new DelegateCliCommand("bench harness list", "List available harnesses", BenchHarnessListCommand));
        registry.Register(new DelegateCliCommand("bench harness show", "Show harness details", BenchHarnessShowCommand));
    }

    private CommandResult ShowBenchUsage(string[] args)
    {
        _state.AddMessage("Usage: bench <subcommand>");
        return CommandResult.Success;
    }

    private CommandResult ShowBenchHarnessUsage(string[] args)
    {
        _state.AddMessage("Usage: bench harness <list|show>");
        return CommandResult.Success;
    }

    private CommandResult BenchHarnessListCommand(string[] args)
    {
        try
        {
            var registry = Cascode.Bench.HarnessService.CreateDefault(_state.WorkspaceRoot);
            var all = registry.All.OrderBy(h => h.Id, StringComparer.OrdinalIgnoreCase).ToArray();
            if (all.Length == 0)
            {
                _state.AddMessage("No harnesses registered.");
                return CommandResult.Success;
            }
            _state.AddMessage("Harnesses:");
            var width = all.Max(h => h.Id.Length);
            foreach (var h in all)
            {
                var backends = string.Join('/', h.SupportedBackends);
                _state.AddMessage($"  {h.Id.PadRight(width)}  {backends}  {h.Description}");
            }
            return CommandResult.Success;
        }
        catch (Exception ex)
        {
            _state.AddMessage($"Failed to list harnesses: {ex.Message}");
            return CommandResult.Failure;
        }
    }

    private CommandResult BenchHarnessShowCommand(string[] args)
    {
        if (args.Length == 0)
        {
            _state.AddMessage("Usage: bench harness show <id>");
            return CommandResult.Success;
        }

        var id = args[0];
        try
        {
            var registry = Cascode.Bench.HarnessService.CreateDefault(_state.WorkspaceRoot);
            if (!registry.TryGet(id, out var h))
            {
                _state.AddMessage("Harness not found.");
                return CommandResult.Failure;
            }
            _state.AddMessage($"Id: {h.Id}");
            _state.AddMessage($"Description: {h.Description}");
            _state.AddMessage($"Backends: {string.Join(", ", h.SupportedBackends)}");
            if (h.Params.Count > 0)
            {
                _state.AddMessage("Params:");
                var w = h.Params.Max(p => p.Name.Length);
                foreach (var p in h.Params)
                {
                    var choices = p.Choices is null || p.Choices.Count == 0 ? string.Empty : $" choices=[{string.Join('/', p.Choices)}]";
                    var def = p.DefaultValue is null ? string.Empty : $" default={p.DefaultValue}";
                    var req = p.Required ? " required" : string.Empty;
                    _state.AddMessage($"  {p.Name.PadRight(w)}  {p.Type}{req}{def}{choices} — {p.Description}");
                }
            }
            return CommandResult.Success;
        }
        catch (Exception ex)
        {
            _state.AddMessage($"Failed to show harness: {ex.Message}");
            return CommandResult.Failure;
        }
    }
}

