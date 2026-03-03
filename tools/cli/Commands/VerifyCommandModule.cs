using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cascode.Bench;
using Cascode.Cli.Output;
using Cascode.Cli.Services;
using Cascode.Language;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace Cascode.Cli.Commands;

/// <summary>
/// Command module for verifying constraint compliance against bench results.
/// </summary>
internal sealed partial class VerifyCommandModule : ICommandModule
{
    private sealed record VerifyArtifactEntry(
        VerifyInput Input,
        BenchResult Results,
        Circuit Circuit
    );

    private sealed record VerifyCircuitResult(
        string CircuitName,
        IReadOnlyList<string> Benches,
        IReadOnlyList<VerifyArtifactEntry> Artifacts,
        ComplianceReport Compliance
    );

    private sealed record VerifyGlobalSummary(
        int ArtifactCount,
        int TotalCircuits,
        int PassedCircuits,
        int FailedCircuits,
        int TotalConstraints,
        int PassedConstraints
    );

    private sealed record VerifySummary(
        IReadOnlyList<VerifyCircuitResult> Circuits,
        VerifyGlobalSummary Global
    );

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
            !TryResolveVerifyInputs(
                parsed,
                runContext,
                preferredDirectory: null,
                out var inputs,
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

        if (NeedsBenchRun(runContext.CascodePath, inputs, out var runReason))
        {
            return RunThenVerify(parsed, runContext, output, jsonOptions, runReason);
        }

        return VerifyFromInputs(runContext, inputs, output, jsonOptions);
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
            output.Info("Bench pipeline completed. Rendering verification report.");
        }
        catch (Exception ex)
        {
            output.Error($"Auto bench pipeline failed: {ex.Message}");
            return CommandResult.Failure;
        }

        if (
            !TryResolveVerifyInputs(
                parsed,
                runContext,
                benchRunResult.Summary.OutputDir,
                out var refreshedInputs,
                out var resolutionNote
            )
        )
        {
            output.Error(
                $"Bench pipeline completed but verify could not find results to read. {resolutionNote}"
            );
            return CommandResult.Failure;
        }

        return VerifyFromInputs(runContext, refreshedInputs, output, jsonOptions);
    }

    private CommandResult VerifyFromInputs(
        VerifyRunContext runContext,
        IReadOnlyList<VerifyInput> inputs,
        ICliOutput output,
        JsonSerializerOptions jsonOptions
    )
    {
        var artifacts = new List<VerifyArtifactEntry>(inputs.Count);
        foreach (var input in inputs)
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

            Circuit circuit;
            try
            {
                circuit = ResolveResultCircuitOrThrow(runContext, results.Circuit);
            }
            catch (InvalidOperationException ex)
            {
                output.Error(ex.Message);
                return CommandResult.Failure;
            }

            artifacts.Add(new VerifyArtifactEntry(input, results, circuit));
        }

        var summary = BuildVerifySummary(artifacts);
        if (summary.Global.TotalConstraints == 0)
        {
            output.Warning("No numeric constraints found in resolved circuits.");
        }

        RenderVerifyReport(output, summary);
        return summary.Global.FailedCircuits == 0 ? CommandResult.Success : CommandResult.Failure;
    }

    private static Circuit ResolveResultCircuitOrThrow(
        VerifyRunContext runContext,
        string requestedCircuitName
    )
    {
        var circuit = runContext.ElCircuits.FirstOrDefault(c =>
            c.Name.Equals(requestedCircuitName, StringComparison.OrdinalIgnoreCase)
        );
        if (circuit is not null)
        {
            return circuit;
        }

        var requested = string.IsNullOrWhiteSpace(requestedCircuitName)
            ? "(empty circuit name)"
            : requestedCircuitName;
        var available = string.Join(
            ", ",
            runContext
                .ElCircuits.Select(c => c.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        );
        throw new InvalidOperationException(
            $"Verification results request circuit '{requested}', but no matching EL circuit was found in the Cascode source. Available EL circuits: {available}."
        );
    }

    private static VerifySummary BuildVerifySummary(IReadOnlyList<VerifyArtifactEntry> artifacts)
    {
        var circuits = artifacts
            .GroupBy(a => a.Circuit.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var orderedArtifacts = group
                    .OrderBy(a => a.Input.Path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var benches = orderedArtifacts
                    .Select(a => a.Results.Bench)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var combinedResults = CombineResults(group.Key, benches, orderedArtifacts);
                var compliance = ComplianceChecker.Check(
                    orderedArtifacts[0].Circuit,
                    combinedResults,
                    ConstraintEvaluationMode.AllDeclared
                );
                return new VerifyCircuitResult(group.Key, benches, orderedArtifacts, compliance);
            })
            .OrderBy(c => c.CircuitName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var passedCircuits = circuits.Count(c => c.Compliance.FailedCount == 0);
        var totalConstraints = circuits.Sum(c => c.Compliance.TotalCount);
        var passedConstraints = circuits.Sum(c => c.Compliance.PassedCount);
        var global = new VerifyGlobalSummary(
            ArtifactCount: artifacts.Count,
            TotalCircuits: circuits.Length,
            PassedCircuits: passedCircuits,
            FailedCircuits: circuits.Length - passedCircuits,
            TotalConstraints: totalConstraints,
            PassedConstraints: passedConstraints
        );
        return new VerifySummary(circuits, global);
    }

    private static BenchResult CombineResults(
        string circuitName,
        IReadOnlyList<string> benches,
        IReadOnlyList<VerifyArtifactEntry> artifacts
    )
    {
        var mergedMeasurements = new Dictionary<string, MeasurementResult>(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var artifact in artifacts)
        {
            BenchResultParser.MergeMeasurements(
                mergedMeasurements,
                artifact.Results.Measurements.Values
            );
        }

        return BenchResultParser.CreateCombinedResults(circuitName, benches, mergedMeasurements);
    }

    private static void RenderVerifyReport(ICliOutput output, VerifySummary summary)
    {
        if (output.Mode == CliOutputMode.Spectre && output.Out is not null)
        {
            RenderVerifySpectre(summary, output.Out);
            return;
        }

        RenderVerifyPlain(summary, output.WriteLine);
    }

    private static void RenderVerifySpectre(VerifySummary summary, IAnsiConsole console)
    {
        if (summary.Circuits.Count == 1)
        {
            RenderVerifySpectreCircuit(
                summary.Circuits[0],
                console,
                summary.Global.TotalCircuits > 1
            );
            return;
        }

        console.Write(new Rule("[bold]Verify[/]") { Style = Style.Parse("grey") });
        console.MarkupLine($"[grey]Artifacts:[/] {summary.Global.ArtifactCount}");
        console.MarkupLine($"[grey]Circuits:[/] {summary.Global.TotalCircuits}");
        console.MarkupLine(
            $"[grey]Global Compliance:[/] {Markup.Escape(FormatComplianceSummary(summary.Global.PassedConstraints, summary.Global.TotalConstraints))}"
        );
        console.MarkupLine(
            $"[grey]Global Result:[/] {summary.Global.PassedCircuits}/{summary.Global.TotalCircuits} circuits compliant"
        );
        console.WriteLine();

        foreach (var circuit in summary.Circuits)
        {
            RenderVerifySpectreCircuit(circuit, console, includeResult: true);
            console.WriteLine();
        }
    }

    private static void RenderVerifySpectreCircuit(
        VerifyCircuitResult circuit,
        IAnsiConsole console,
        bool includeResult
    )
    {
        console.Write(
            new Rule($"[bold]{Markup.Escape(circuit.CircuitName)}[/]")
            {
                Style = Style.Parse("grey"),
            }
        );
        if (circuit.Benches.Count > 0)
        {
            console.MarkupLine(
                $"[grey]Benches:[/] {Markup.Escape(string.Join(", ", circuit.Benches))}"
            );
        }

        if (circuit.Artifacts.Count == 1)
        {
            console.MarkupLine(
                $"[grey]Artifact:[/] {Markup.Escape(circuit.Artifacts[0].Input.Path)}"
            );
        }
        else
        {
            console.MarkupLine($"[grey]Artifacts:[/] {circuit.Artifacts.Count}");
            foreach (var artifact in circuit.Artifacts)
            {
                console.MarkupLine($"  [grey]-[/] {Markup.Escape(artifact.Input.Path)}");
            }
        }

        console.WriteLine();
        ComplianceReportRenderer.RenderComplianceTable(circuit.Compliance, console);
        if (includeResult)
        {
            console.MarkupLine(
                $"[grey]Result:[/] {circuit.Compliance.PassedCount}/{circuit.Compliance.TotalCount} constraints satisfied"
            );
        }
    }

    private static void RenderVerifyPlain(VerifySummary summary, Action<string> writeLine)
    {
        if (summary.Circuits.Count == 1)
        {
            RenderVerifyPlainSingle(summary.Circuits[0], writeLine);
            return;
        }

        writeLine($"Artifacts: {summary.Global.ArtifactCount}");
        writeLine($"Circuits: {summary.Global.TotalCircuits}");
        writeLine("Circuit Compliance:");
        foreach (var circuit in summary.Circuits)
        {
            var status = circuit.Compliance.FailedCount == 0 ? "PASS" : "FAIL";
            writeLine(
                $"  {circuit.CircuitName}: {status} ({ComplianceReportRenderer.FormatComplianceSummary(circuit.Compliance)})"
            );
        }

        writeLine(
            $"Global Compliance: {FormatComplianceSummary(summary.Global.PassedConstraints, summary.Global.TotalConstraints)}"
        );
        writeLine(
            $"Global Result: {summary.Global.PassedCircuits}/{summary.Global.TotalCircuits} circuits compliant"
        );
        writeLine(string.Empty);

        foreach (var circuit in summary.Circuits)
        {
            RenderVerifyPlainCircuit(circuit, writeLine);
        }
    }

    private static void RenderVerifyPlainSingle(
        VerifyCircuitResult circuit,
        Action<string> writeLine
    )
    {
        writeLine($"Circuit: {circuit.CircuitName}");
        WriteBenchAndArtifactInfo(circuit, writeLine, indent: string.Empty);
        ComplianceReportRenderer.WriteCompliancePlain(writeLine, circuit.Compliance);
        writeLine(
            $"Result: {circuit.Compliance.PassedCount}/{circuit.Compliance.TotalCount} constraints satisfied"
        );
    }

    private static void RenderVerifyPlainCircuit(
        VerifyCircuitResult circuit,
        Action<string> writeLine
    )
    {
        writeLine($"=== {circuit.CircuitName} ===");
        WriteBenchAndArtifactInfo(circuit, writeLine, indent: "  ");
        ComplianceReportRenderer.WriteCompliancePlain(
            line => writeLine($"  {line}"),
            circuit.Compliance
        );
        writeLine(
            $"  Result: {circuit.Compliance.PassedCount}/{circuit.Compliance.TotalCount} constraints satisfied"
        );
        writeLine(string.Empty);
    }

    private static void WriteBenchAndArtifactInfo(
        VerifyCircuitResult circuit,
        Action<string> writeLine,
        string indent
    )
    {
        if (circuit.Benches.Count > 0)
        {
            writeLine($"{indent}Benches: {string.Join(", ", circuit.Benches)}");
        }

        if (circuit.Artifacts.Count == 1)
        {
            writeLine($"{indent}Artifact: {circuit.Artifacts[0].Input.Path}");
            return;
        }

        writeLine($"{indent}Artifacts: {circuit.Artifacts.Count}");
        foreach (var artifact in circuit.Artifacts)
        {
            writeLine($"{indent}  - {artifact.Input.Path}");
        }
    }

    private static string FormatComplianceSummary(int passed, int total)
    {
        var passPercentage = total > 0 ? (int)Math.Round(100.0 * passed / total) : 0;
        return $"{passed}/{total} ({passPercentage}% PASS)";
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
}
