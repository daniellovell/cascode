using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cascode.Bench;
using Cascode.Cli.Output;
using Cascode.Language;

namespace Cascode.Cli.Commands;

/// <summary>
/// Command module for verifying constraint compliance against bench results.
/// </summary>
internal sealed class VerifyCommandModule : ICommandModule
{
    private readonly ShellState _state;
    private readonly CliOutputProvider _output;

    /// <summary>
    /// Initializes a new instance of the <see cref="VerifyCommandModule"/> class.
    /// </summary>
    /// <param name="state">Shell state for messaging.</param>
    public VerifyCommandModule(ShellState state, CliOutputProvider output)
    {
        _state = state;
        _output = output;
    }

    /// <summary>
    /// Registers the verify command with the command registry.
    /// </summary>
    /// <param name="registry">Command registry.</param>
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
    /// <param name="args">Command arguments: --cascode <file> --results <json>.</param>
    /// <returns>Command result indicating success or failure.</returns>
    private CommandResult VerifyCommand(string[] args)
    {
        var output = _output.Get();
        if (args.Length == 0)
        {
            output.WriteLine("Usage: verify <cascode_file> <results_json|trace_jsonl>");
            output.WriteLine(
                "       verify --cascode <cascode_file> (--results <results_json> | --trace <trace_jsonl>)"
            );
            output.WriteLine("");
            output.WriteLine(
                "Verifies numeric constraints from Cascode against bench measurement results."
            );
            return CommandResult.Success;
        }

        if (!ParseArguments(args, out var cascodePath, out var resultsPath, out var tracePath))
        {
            output.Error(
                "Error: provide an Cascode path plus either a results.json or trace.jsonl path."
            );
            return CommandResult.Failure;
        }

        if (!File.Exists(cascodePath))
        {
            output.Error($"Cascode file '{cascodePath}' not found.");
            return CommandResult.Failure;
        }

        if (resultsPath != null && !File.Exists(resultsPath))
        {
            output.Error($"Results file '{resultsPath}' not found.");
            return CommandResult.Failure;
        }

        if (tracePath != null && !File.Exists(tracePath))
        {
            output.Error($"Trace file '{tracePath}' not found.");
            return CommandResult.Failure;
        }

        // Read Cascode document
        CascodeReadResult readResult;
        using (var reader = File.OpenText(cascodePath))
        {
            readResult = CascodeReader.TryRead(reader, cascodePath);
        }

        if (!readResult.Success)
        {
            foreach (
                var diag in readResult.Diagnostics.Where(d =>
                    d.Severity == DiagnosticSeverity.Error
                )
            )
            {
                output.Error($"{diag.FilePath}:{diag.Line}: {diag.Message}");
            }
            return CommandResult.Failure;
        }

        var doc = readResult.Document!;

        // Find EL-level circuit (use first one, or match by name from results)
        var elCircuits = doc.Circuits.Where(c => c.Level == CascodeLevel.EL).ToList();
        if (elCircuits.Count == 0)
        {
            output.Error("No EL-level circuits found in Cascode document.");
            return CommandResult.Failure;
        }

        BenchResult? results;
        try
        {
            var jsonOptions = new JsonSerializerOptions
            {
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            };

            if (resultsPath != null)
            {
                var jsonText = File.ReadAllText(resultsPath);
                results =
                    JsonSerializer.Deserialize<BenchResult>(jsonText, jsonOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize results JSON");
            }
            else
            {
                results = ReadResultsFromTrace(tracePath!, jsonOptions);
            }
        }
        catch (Exception ex)
        {
            output.Error($"Failed to read results file: {ex.Message}");
            return CommandResult.Failure;
        }

        // Find matching circuit by name
        var circuit =
            elCircuits.FirstOrDefault(c =>
                c.Name.Equals(results.Circuit, StringComparison.OrdinalIgnoreCase)
            ) ?? elCircuits[0];

        // Check compliance
        var report = ComplianceChecker.Check(
            circuit,
            results,
            ConstraintEvaluationMode.AllDeclared
        );

        DisplayComplianceReport(output, circuit, results.Bench, report);

        return report.FailedCount == 0 ? CommandResult.Success : CommandResult.Failure;
    }

    /// <summary>
    /// Parses command-line arguments to extract Cascode and results file paths.
    /// </summary>
    /// <param name="args">Command arguments array.</param>
    /// <param name="cascodePath">Output parameter for Cascode file path.</param>
    /// <param name="resultsPath">Output parameter for results JSON file path.</param>
    /// <returns>True if both arguments were found, false otherwise.</returns>
    private static bool ParseArguments(
        string[] args,
        out string? cascodePath,
        out string? resultsPath,
        out string? tracePath
    )
    {
        cascodePath = null;
        resultsPath = null;
        tracePath = null;
        var positionals = new System.Collections.Generic.List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--cascode" && i + 1 < args.Length)
            {
                cascodePath = args[i + 1];
                i++;
            }
            else if (args[i] == "--results" && i + 1 < args.Length)
            {
                resultsPath = args[i + 1];
                i++;
            }
            else if (args[i] == "--trace" && i + 1 < args.Length)
            {
                tracePath = args[i + 1];
                i++;
            }
            else if (!args[i].StartsWith('-'))
            {
                positionals.Add(args[i]);
            }
        }

        if (cascodePath == null && positionals.Count >= 1)
        {
            cascodePath = positionals[0];
        }

        if (resultsPath == null && tracePath == null && positionals.Count >= 2)
        {
            var path = positionals[1];
            if (path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
            {
                tracePath = path;
            }
            else
            {
                resultsPath = path;
            }
        }

        return cascodePath != null && (resultsPath != null || tracePath != null);
    }

    private static BenchResult ReadResultsFromTrace(
        string tracePath,
        JsonSerializerOptions jsonOptions
    )
    {
        BenchResult? last = null;

        foreach (var line in File.ReadLines(tracePath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var doc = JsonDocument.Parse(line);
            if (
                !doc.RootElement.TryGetProperty("type", out var typeEl)
                || typeEl.GetString() != "summary"
            )
            {
                continue;
            }

            if (!doc.RootElement.TryGetProperty("results", out var resultsEl))
            {
                continue;
            }

            last = JsonSerializer.Deserialize<BenchResult>(resultsEl.GetRawText(), jsonOptions);
        }

        return last
            ?? throw new InvalidOperationException(
                "No summary record with results found in trace.jsonl."
            );
    }

    /// <summary>
    /// Displays the compliance report for a circuit.
    /// </summary>
    /// <param name="circuit">The circuit being verified.</param>
    /// <param name="benchName">Name of the bench that produced the results.</param>
    /// <param name="report">The compliance report to display.</param>
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

        // Show hint about unchecked constraints from other benches
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
