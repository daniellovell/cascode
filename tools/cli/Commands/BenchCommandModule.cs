using System;
using System.Linq;
using Cascode.Cli.Output;
using Cascode.Cli.Services;
using Microsoft.Extensions.Logging;

namespace Cascode.Cli.Commands;

internal sealed class BenchCommandModule : ICommandModule
{
    private readonly ShellState _state;
    private readonly CliOutputProvider _output;

    public BenchCommandModule(ShellState state, CliOutputProvider output)
    {
        _state = state;
        _output = output;
    }

    public void Register(CommandRegistry registry)
    {
        registry.Register(
            new DelegateCliCommand("bench", "Bench and harness commands", ShowBenchUsage)
        );
        registry.Register(
            new DelegateCliCommand("bench harness", "Harness helpers", ShowBenchHarnessUsage)
        );
        registry.Register(
            new DelegateCliCommand(
                "bench harness list",
                "List available harnesses",
                BenchHarnessListCommand
            )
        );
        registry.Register(
            new DelegateCliCommand(
                "bench harness show",
                "Show harness details",
                BenchHarnessShowCommand
            )
        );
        registry.Register(
            new DelegateCliCommand(
                "bench run",
                "Run a bench simulation and emit trace/results",
                BenchRunCommand
            )
        );
    }

    private CommandResult ShowBenchUsage(string[] args)
    {
        _output.Get().WriteLine("Usage: bench <subcommand>");
        return CommandResult.Success;
    }

    private CommandResult ShowBenchHarnessUsage(string[] args)
    {
        _output.Get().WriteLine("Usage: bench harness <list|show>");
        return CommandResult.Success;
    }

    private CommandResult BenchRunCommand(string[] args)
    {
        var output = _output.Get();

        if (!BenchRunService.TryParseArgs(args, out var parsed, out var error))
        {
            output.Error(error);
            output.WriteLine(
                "Usage: bench run <cascode_file> [<bench>] [-b|--bench <name>] [-c|--circuit <name>] [-o|--out <dir>] [--backend <ngspice>] [-v|--verbose] [--strict]"
            );
            output.WriteLine(
                "Runs all benches for all circuits with benches (in dependency order)."
            );
            output.WriteLine("Use --circuit to run benches for a specific circuit only.");
            output.WriteLine(
                "By default, compliance failures do not cause a non-zero exit code (use --strict)."
            );
            return CommandResult.Failure;
        }

        ILoggerFactory? localFactory = null;
        try
        {
            var loggerFactory =
                _state.LoggerFactory
                ?? (
                    localFactory = LoggerFactory.Create(builder =>
                    {
                        builder.SetMinimumLevel(LogLevel.Warning);
                        builder.AddSimpleConsole(o =>
                        {
                            o.SingleLine = true;
                        });
                    })
                );

            var result = output.RunWithProgress(
                "bench run: start",
                progress =>
                {
                    var service = new BenchRunService(
                        loggerFactory.CreateLogger<BenchRunService>(),
                        progress: progress
                    );
                    return service.RunAll(_state.WorkspaceRoot, _state.PdkRoot, parsed);
                }
            );

            BenchRunRenderer.Render(result.Summary, parsed.Verbose, output);
            if (result.Summary.Timing is not null)
            {
                BenchRunRenderer.RenderTiming(result.Summary.Timing, parsed.Verbose, output);
            }

            return result.ExitCode == 0
                ? CommandResult.Success
                : new CommandResult(result.ExitCode, false);
        }
        catch (Exception ex)
        {
            output.Error($"bench run failed: {ex.Message}");
            return CommandResult.Failure;
        }
        finally
        {
            localFactory?.Dispose();
        }
    }

    private CommandResult BenchHarnessListCommand(string[] args)
    {
        var output = _output.Get();
        try
        {
            var registry = Cascode.Bench.HarnessService.CreateDefault(_state.WorkspaceRoot);
            var all = registry.All.OrderBy(h => h.Id, StringComparer.OrdinalIgnoreCase).ToArray();
            if (all.Length == 0)
            {
                output.WriteLine("No harnesses registered.");
                return CommandResult.Success;
            }
            output.WriteLine("Harnesses:");
            var width = all.Max(h => h.Id.Length);
            foreach (var h in all)
            {
                var backends = string.Join('/', h.SupportedBackends);
                output.WriteLine($"  {h.Id.PadRight(width)}  {backends}  {h.Description}");
            }
            return CommandResult.Success;
        }
        catch (Exception ex)
        {
            output.Error($"Failed to list harnesses: {ex.Message}");
            return CommandResult.Failure;
        }
    }

    private CommandResult BenchHarnessShowCommand(string[] args)
    {
        var output = _output.Get();
        if (args.Length == 0)
        {
            output.WriteLine("Usage: bench harness show <id>");
            return CommandResult.Success;
        }

        var id = args[0];
        try
        {
            var registry = Cascode.Bench.HarnessService.CreateDefault(_state.WorkspaceRoot);
            if (!registry.TryGet(id, out var h))
            {
                output.Error("Harness not found.");
                return CommandResult.Failure;
            }

            output.WriteLine($"Id: {h.Id}");
            output.WriteLine($"Description: {h.Description}");
            output.WriteLine($"Backends: {string.Join(", ", h.SupportedBackends)}");
            if (h.Params.Count > 0)
            {
                output.WriteLine("Params:");
                var w = h.Params.Max(p => p.Name.Length);
                foreach (var p in h.Params)
                {
                    var choices =
                        p.Choices is null || p.Choices.Count == 0
                            ? string.Empty
                            : $" choices=[{string.Join('/', p.Choices)}]";
                    var def = p.DefaultValue is null ? string.Empty : $" default={p.DefaultValue}";
                    var req = p.Required ? " required" : string.Empty;
                    output.WriteLine(
                        $"  {p.Name.PadRight(w)}  {p.Type}{req}{def}{choices} — {p.Description}"
                    );
                }
            }
            return CommandResult.Success;
        }
        catch (Exception ex)
        {
            output.Error($"Failed to show harness: {ex.Message}");
            return CommandResult.Failure;
        }
    }
}
