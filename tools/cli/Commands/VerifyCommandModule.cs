using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cascode.Bench;
using Cascode.Cli.Output;
using Cascode.Cli.Services;
using Cascode.Language;
using Microsoft.Extensions.Logging;

namespace Cascode.Cli.Commands;

/// <summary>
/// Command module for verifying constraint compliance against bench results.
/// </summary>
internal sealed partial class VerifyCommandModule : ICommandModule
{
    private readonly ShellState _state;
    private readonly CliOutputProvider _output;

    /// <summary>
    /// Initializes a new instance of the <see cref="VerifyCommandModule"/> class.
    /// </summary>
    public VerifyCommandModule(ShellState state, CliOutputProvider output)
    {
        _state = state;
        _output = output;
    }

    /// <summary>
    /// Registers the verify command with the command registry.
    /// </summary>
    public void Register(CommandRegistry registry)
    {
        registry.Register(
            new DelegateCliCommand(
                "verify",
                "Verify constraint compliance from bench results",
                VerifyCommand
            )
        );
    }

    /// <summary>
    /// Executes the verify command to check constraint compliance.
    /// </summary>
    private CommandResult VerifyCommand(string[] args)
    {
        var output = _output.Get();
        if (args.Length == 0 || args.Any(a => a is "-h" or "--help"))
        {
            ShowUsage(output);
            return CommandResult.Success;
        }

        if (!TryParseArguments(args, out var parsed, out var parseError))
        {
            output.Error(parseError);
            ShowUsage(output);
            return CommandResult.Failure;
        }

        if (!TryBuildRunContext(parsed, output, out var runContext))
        {
            return CommandResult.Failure;
        }

        var jsonOptions = new JsonSerializerOptions
        {
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };

        if (
            !TryResolveVerifyInput(
                parsed,
                runContext,
                jsonOptions,
                preferredDirectory: null,
                out var input,
                out var resolutionNote
            )
        )
        {
            return HandleMissingInputWithOptionalRun(
                parsed,
                runContext,
                output,
                jsonOptions,
                resolutionNote
            );
        }

        if (NeedsBenchRun(runContext.CascodePath, input, out var runReason))
        {
            return RunThenVerify(parsed, runContext, output, jsonOptions, runReason);
        }

        return VerifyFromInput(runContext, input, output, jsonOptions);
    }

    private CommandResult HandleMissingInputWithOptionalRun(
        ParsedVerifyArgs parsed,
        VerifyRunContext runContext,
        ICliOutput output,
        JsonSerializerOptions jsonOptions,
        string resolutionNote
    )
    {
        if (parsed.NoRun)
        {
            output.Error(
                $"{resolutionNote} Auto-run is disabled by --no-run. Run `bench run` first or provide a results/trace artifact."
            );
            return CommandResult.Failure;
        }

        output.Info(
            $"{resolutionNote} Running bench pipeline to generate fresh verification artifacts."
        );
        return RunThenVerify(parsed, runContext, output, jsonOptions, resolutionNote);
    }

    private CommandResult RunThenVerify(
        ParsedVerifyArgs parsed,
        VerifyRunContext runContext,
        ICliOutput output,
        JsonSerializerOptions jsonOptions,
        string runReason
    )
    {
        if (parsed.NoRun)
        {
            output.Error(
                $"Verification input is missing or stale ({runReason}), and --no-run was provided."
            );
            return CommandResult.Failure;
        }

        output.Info(
            $"Verification input is missing or stale ({runReason}). Running bench pipeline."
        );
        BenchRunService.MultiCircuitBenchRunResult benchRunResult;
        try
        {
            var outputDirHint = ResolveBenchOutputDirectoryHint(parsed);
            benchRunResult = RunBenchPipeline(runContext.CascodePath, outputDirHint, output);
            BenchRunRenderer.Render(benchRunResult.Summary, verbose: false, output);
            if (benchRunResult.Summary.Timing is not null)
            {
                BenchRunRenderer.RenderTiming(
                    benchRunResult.Summary.Timing,
                    verbose: false,
                    output
                );
            }
        }
        catch (Exception ex)
        {
            output.Error($"Auto bench pipeline failed: {ex.Message}");
            return CommandResult.Failure;
        }

        if (
            !TryResolveVerifyInput(
                parsed,
                runContext,
                jsonOptions,
                benchRunResult.Summary.OutputDir,
                out var refreshedInput,
                out var resolutionNote
            )
        )
        {
            output.Error(
                $"Bench pipeline completed but verify could not find results to read. {resolutionNote}"
            );
            return CommandResult.Failure;
        }

        return VerifyFromInput(runContext, refreshedInput, output, jsonOptions);
    }

    private CommandResult VerifyFromInput(
        VerifyRunContext runContext,
        VerifyInput input,
        ICliOutput output,
        JsonSerializerOptions jsonOptions
    )
    {
        BenchResult results;
        try
        {
            results = input.Kind switch
            {
                VerifyInputKind.Results => JsonSerializer.Deserialize<BenchResult>(
                    System.IO.File.ReadAllText(input.Path),
                    jsonOptions
                ) ?? throw new InvalidOperationException("Failed to deserialize results JSON"),
                VerifyInputKind.Trace => ReadResultsFromTrace(input.Path, jsonOptions),
                _ => throw new InvalidOperationException("Unsupported verify input kind"),
            };
        }
        catch (Exception ex)
        {
            output.Error($"Failed to read verification input '{input.Path}': {ex.Message}");
            return CommandResult.Failure;
        }

        var circuit =
            runContext.ElCircuits.FirstOrDefault(c =>
                c.Name.Equals(results.Circuit, StringComparison.OrdinalIgnoreCase)
            ) ?? runContext.Circuit;

        var report = ComplianceChecker.Check(
            circuit,
            results,
            ConstraintEvaluationMode.AllDeclared
        );
        DisplayComplianceReport(output, circuit, results.Bench, report);
        return report.FailedCount == 0 ? CommandResult.Success : CommandResult.Failure;
    }

    private BenchRunService.MultiCircuitBenchRunResult RunBenchPipeline(
        string cascodePath,
        string? outputDir,
        ICliOutput output
    )
    {
        ILoggerFactory? localFactory = null;
        try
        {
            var loggerFactory =
                _state.LoggerFactory
                ?? (
                    localFactory = LoggerFactory.Create(builder =>
                    {
                        builder.SetMinimumLevel(LogLevel.Warning);
                        builder.AddSimpleConsole(o => o.SingleLine = true);
                    })
                );

            return output.RunWithMultiTaskProgress(progressCtx =>
            {
                var service = new BenchRunService(
                    loggerFactory.CreateLogger<BenchRunService>(),
                    progressCtx,
                    output
                );
                var benchArgs = new BenchRunService.BenchRunArgs(
                    CascodePath: cascodePath,
                    BenchName: null,
                    OutputDir: outputDir,
                    Backend: BenchBackendType.Ngspice,
                    Verbose: false,
                    StrictCompliance: false,
                    Parallelism: 0
                );
                return service.RunAll(_state.WorkspaceRoot, _state.PdkRoot, benchArgs);
            });
        }
        finally
        {
            localFactory?.Dispose();
        }
    }

    private static void ShowUsage(ICliOutput output)
    {
        output.WriteLine(
            "Usage: verify <cascode_file> [results_json|trace_jsonl|results_dir] [--no-run]"
        );
        output.WriteLine(
            "       verify --cascode <cascode_file> [--results <results_json|results_dir> | --trace <trace_jsonl>] [--no-run]"
        );
        output.WriteLine(string.Empty);
        output.WriteLine(
            "Verifies numeric constraints against bench outputs. If results are missing or stale, verify automatically runs the bench pipeline unless --no-run is provided."
        );
    }

    private static void DisplayComplianceReport(
        ICliOutput output,
        Circuit circuit,
        string benchName,
        ComplianceReport report
    )
    {
        var header = string.IsNullOrEmpty(benchName)
            ? $"Constraint Compliance Report for {circuit.Name}"
            : $"Constraint Compliance Report for {circuit.Name} ({benchName})";
        output.WriteLine(header);
        output.WriteLine(new string('-', 50));

        if (report.TotalCount == 0 && report.UncheckedCount == 0)
        {
            output.Warning("No numeric constraints found in circuit.");
        }

        foreach (var result in report.Results)
        {
            var status = result.Passed ? "PASS" : "FAIL";
            var nodeStr = result.Node != null ? $" @ {result.Node}" : "";
            var expectedStr = ValueFormatter.FormatValue(
                result.Expected,
                GetUnitFromConstraint(circuit, result.Id)
            );
            var actualStr =
                result.Actual.HasValue
                    ? $" (measured: {ValueFormatter.FormatValue(result.Actual.Value, GetUnitFromConstraint(circuit, result.Id))})"
                : result.FailureReason == "bench_error" ? " (measurement error)"
                : " (not measured)";

            output.WriteLine(
                $"{result.Id, -8} {result.Metric}{nodeStr} {result.Operator} {expectedStr, -12} {status}{actualStr}"
            );
        }

        output.WriteLine(new string('-', 50));
        output.WriteLine($"Result: {report.PassedCount}/{report.TotalCount} constraints satisfied");

        if (report.UncheckedByBench.Count > 0)
        {
            output.WriteLine("");
            foreach (var kvp in report.UncheckedByBench)
            {
                var ids = string.Join(", ", kvp.Value.Select(c => c.Id));
                var constraintWord = kvp.Value.Count == 1 ? "constraint" : "constraints";
                output.WriteLine(
                    $"Note: {kvp.Value.Count} {constraintWord} ({ids}) measured by {kvp.Key}."
                );
            }
            output.Warning("Run `verify` with combined results to check all constraints.");
        }
    }

    private static string GetUnitFromConstraint(Circuit circuit, string constraintId)
    {
        var constraint = circuit.Constraints?.Numeric?.FirstOrDefault(c => c.Id == constraintId);
        return constraint?.Unit ?? "";
    }
}
