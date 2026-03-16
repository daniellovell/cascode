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
            new DelegateCliCommand(
                "bench",
                "Bench and harness commands",
                ShowBenchUsage,
                helpCategory: CommandHelpCategory.Bench
            )
        );
        registry.Register(
            new DelegateCliCommand(
                "bench run",
                "Run a bench simulation and emit trace/results",
                BenchRunCommand,
                helpCategory: CommandHelpCategory.Bench
            )
        );
    }

    private CommandResult ShowBenchUsage(string[] args)
    {
        _output.Get().WriteLine("Usage: bench <subcommand>");
        return CommandResult.Success;
    }

    private CommandResult BenchRunCommand(string[] args)
    {
        var output = _output.Get();

        if (!BenchRunService.TryParseArgs(args, out var parsed, out var error))
        {
            output.Error(error);
            output.WriteLine(
                "Usage: bench run <cascode_file> [<bench>] [-b|--bench <name>] [-c|--circuit <name>] [-o|--out <dir>] [--backend <ngspice>] [--parallel <n>] [-v|--verbose] [--strict]"
            );
            output.WriteLine(
                "Runs benches required by numeric constraints for all circuits with benches (in dependency order)."
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

            var result = output.RunWithMultiTaskProgress(progressCtx =>
            {
                var service = new BenchRunService(
                    loggerFactory.CreateLogger<BenchRunService>(),
                    progressCtx,
                    output
                );
                return service.RunAll(_state.WorkspaceRoot, _state.PdkRoot, parsed);
            });

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
}
