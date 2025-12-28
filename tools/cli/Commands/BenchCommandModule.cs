using System;
using System.IO;
using System.Linq;
using Cascode.ACIR;
using Cascode.Cli.Services;
using Microsoft.Extensions.Logging;

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
        registry.Register(new DelegateCliCommand("bench run", "Run a bench simulation and emit trace/results", BenchRunCommand));
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

    private CommandResult BenchRunCommand(string[] args)
    {
        if (!BenchRunService.TryParseArgs(args, out var parsed, out var error))
        {
            _state.AddMessage(error);
            _state.AddMessage("Usage: bench run <acir_file> [<bench>] [-b|--bench <name>] [-o|--out <dir>] [--backend <ngspice>] [-v|--verbose]");
            _state.AddMessage("If <bench> is omitted, runs all benches declared by the circuit.");
            return CommandResult.Failure;
        }

        try
        {
            ILoggerFactory? localFactory = null;
            var loggerFactory = _state.LoggerFactory ?? (localFactory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Warning);
                builder.AddSimpleConsole(o => { o.SingleLine = true; });
            }));

            var service = new BenchRunService(loggerFactory.CreateLogger<BenchRunService>());
            var result = service.Run(_state.WorkspaceRoot, _state.PdkRoot, parsed);
            WriteBenchRunSummary(result.Summary, parsed.Verbose);
            localFactory?.Dispose();
            return result.ExitCode == 0 ? CommandResult.Success : new CommandResult(result.ExitCode, false);
        }
        catch (Exception ex)
        {
            _state.AddMessage($"bench run failed: {ex.Message}");
            return CommandResult.Failure;
        }
    }

    private void WriteBenchRunSummary(BenchRunService.BenchRunSummary summary, bool verbose)
    {
        _state.AddMessage($"Circuit: {summary.CircuitName} ({summary.Backend.ToString().ToLowerInvariant()})");
        _state.AddMessage($"Artifacts: {FormatDir(summary.OutputDir, verbose)}");

        var succeeded = summary.Benches.Where(b => b.Succeeded).Select(b => b.Name).ToArray();
        var failed = summary.Benches.Where(b => !b.Succeeded).Select(b => b.Name).ToArray();
        if (succeeded.Length > 0)
        {
            _state.AddMessage($"Ran: {string.Join(", ", succeeded)}");
        }

        if (failed.Length > 0)
        {
            _state.AddMessage($"Simulation: FAIL ({string.Join(", ", failed)})");
        }

        if (summary.CombinedResultsPath != null)
        {
            _state.AddMessage($"Combined results: {FormatPath(summary.CombinedResultsPath, summary.OutputDir, verbose)}");
        }

        foreach (var bench in summary.Benches.Where(b => b.Succeeded))
        {
            if (bench.ResultsPath != null)
            {
                _state.AddMessage($"{bench.Name} results: {FormatPath(bench.ResultsPath, summary.OutputDir, verbose)}");
            }

            if (bench.TracePath != null)
            {
                _state.AddMessage($"{bench.Name} trace: {FormatPath(bench.TracePath, summary.OutputDir, verbose)}");
            }
        }

        foreach (var bench in summary.Benches.Where(b => !b.Succeeded))
        {
            if (!string.IsNullOrWhiteSpace(bench.Error))
            {
                _state.AddMessage($"{bench.Name} error: {bench.Error}");
            }

            if (verbose && !string.IsNullOrWhiteSpace(bench.Stderr))
            {
                _state.AddMessage($"{bench.Name} stderr:");
                foreach (var line in bench.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    _state.AddMessage($"  {line.TrimEnd()}");
                }
            }
        }

        var compliance = summary.Compliance;
        var passPercentage = compliance.TotalCount > 0 
            ? (int)Math.Round(100.0 * compliance.PassedCount / compliance.TotalCount) 
            : 0;
        _state.AddMessage($"Compliance: {compliance.PassedCount}/{compliance.TotalCount} ({passPercentage}% PASS)");

        // Helper to format a constraint result
        string FormatConstraint(ConstraintResult result)
        {
            var where = string.IsNullOrWhiteSpace(result.Node) ? result.Metric : $"{result.Metric}@{result.Node}";
            var expected = $"{result.Operator} {FormatNumber(result.Expected)} {result.Unit}".TrimEnd();
            var actual = result.Actual is null ? "missing" : $"{FormatNumber(result.Actual.Value)} {result.ActualUnit ?? result.Unit}".TrimEnd();
            return $"  {result.Id}: {where} {expected} (actual {actual})";
        }

        var passedConstraints = compliance.Results.Where(r => r.Passed).ToArray();
        var failedConstraints = compliance.Results.Where(r => !r.Passed).ToArray();

        if (passedConstraints.Length > 0)
        {
            _state.AddMessage("PASS:");
            foreach (var pass in passedConstraints)
            {
                _state.AddMessage(FormatConstraint(pass));
            }
        }

        if (failedConstraints.Length > 0)
        {
            _state.AddMessage("FAIL:");
            foreach (var failure in failedConstraints)
            {
                _state.AddMessage(FormatConstraint(failure));
            }
        }
    }

    private static string FormatDir(string path, bool verbose)
    {
        _ = verbose;
        return Path.GetFullPath(path);
    }

    private static string FormatPath(string path, string outputDir, bool verbose)
    {
        _ = outputDir;
        var full = Path.GetFullPath(path);
        _ = verbose;
        return full;
    }

    private static string FormatNumber(double value)
    {
        // Human-readable without being overly specific; stable across locales.
        return value.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
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
