using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cascode.ACIR;
using Cascode.Bench;
using Cascode.Parser;

namespace Cascode.Cli.Commands;

/// <summary>
/// Command module for verifying constraint compliance against bench results.
/// </summary>
internal sealed class VerifyCommandModule : ICommandModule
{
    private readonly ShellState _state;

    /// <summary>
    /// Initializes a new instance of the <see cref="VerifyCommandModule"/> class.
    /// </summary>
    /// <param name="state">Shell state for messaging.</param>
    public VerifyCommandModule(ShellState state)
    {
        _state = state;
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
    /// <param name="args">Command arguments: --acir <file> --results <json>.</param>
    /// <returns>Command result indicating success or failure.</returns>
    private CommandResult VerifyCommand(string[] args)
    {
        if (args.Length == 0)
        {
            _state.AddMessage("Usage: verify <acir_file> <results_json|trace_jsonl>");
            _state.AddMessage(
                "       verify --acir <acir_file> (--results <results_json> | --trace <trace_jsonl>)"
            );
            _state.AddMessage("");
            _state.AddMessage(
                "Verifies numeric constraints from ACIR against bench measurement results."
            );
            return CommandResult.Success;
        }

        if (!ParseArguments(args, out var acirPath, out var resultsPath, out var tracePath))
        {
            _state.AddMessage(
                "Error: provide an ACIR path plus either a results.json or trace.jsonl path."
            );
            return CommandResult.Failure;
        }

        if (!File.Exists(acirPath))
        {
            _state.AddMessage($"ACIR file '{acirPath}' not found.");
            return CommandResult.Failure;
        }

        if (resultsPath != null && !File.Exists(resultsPath))
        {
            _state.AddMessage($"Results file '{resultsPath}' not found.");
            return CommandResult.Failure;
        }

        if (tracePath != null && !File.Exists(tracePath))
        {
            _state.AddMessage($"Trace file '{tracePath}' not found.");
            return CommandResult.Failure;
        }

        // Read ACIR document
        ACIRReadResult readResult;
        using (var reader = File.OpenText(acirPath))
        {
            readResult = ACIRReader.TryRead(reader, acirPath);
        }

        if (!readResult.Success)
        {
            foreach (
                var diag in readResult.Diagnostics.Where(d =>
                    d.Severity == DiagnosticSeverity.Error
                )
            )
            {
                _state.AddMessage($"{diag.FilePath}:{diag.Line}: {diag.Message}");
            }
            return CommandResult.Failure;
        }

        var doc = readResult.Document!;

        // Find EL-level circuit (use first one, or match by name from results)
        var elCircuits = doc.Circuits.Where(c => c.Level == ACIRLevel.EL).ToList();
        if (elCircuits.Count == 0)
        {
            _state.AddMessage("No EL-level circuits found in ACIR document.");
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
            _state.AddMessage($"Failed to read results file: {ex.Message}");
            return CommandResult.Failure;
        }

        // Find matching circuit by name
        var circuit =
            elCircuits.FirstOrDefault(c =>
                c.Name.Equals(results.Circuit, StringComparison.OrdinalIgnoreCase)
            ) ?? elCircuits[0];

        // Check compliance
        var report = ComplianceChecker.Check(circuit, results);

        DisplayComplianceReport(circuit, results.Bench, report);

        return report.FailedCount == 0 ? CommandResult.Success : CommandResult.Failure;
    }

    /// <summary>
    /// Parses command-line arguments to extract ACIR and results file paths.
    /// </summary>
    /// <param name="args">Command arguments array.</param>
    /// <param name="acirPath">Output parameter for ACIR file path.</param>
    /// <param name="resultsPath">Output parameter for results JSON file path.</param>
    /// <returns>True if both arguments were found, false otherwise.</returns>
    private static bool ParseArguments(
        string[] args,
        out string? acirPath,
        out string? resultsPath,
        out string? tracePath
    )
    {
        acirPath = null;
        resultsPath = null;
        tracePath = null;
        var positionals = new System.Collections.Generic.List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--acir" && i + 1 < args.Length)
            {
                acirPath = args[i + 1];
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
            else if (!args[i].StartsWith("-", StringComparison.Ordinal))
            {
                positionals.Add(args[i]);
            }
        }

        if (acirPath == null && positionals.Count >= 1)
        {
            acirPath = positionals[0];
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

        return acirPath != null && (resultsPath != null || tracePath != null);
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
    private void DisplayComplianceReport(Circuit circuit, string benchName, ComplianceReport report)
    {
        var header = string.IsNullOrEmpty(benchName)
            ? $"Constraint Compliance Report for {circuit.Name}"
            : $"Constraint Compliance Report for {circuit.Name} ({benchName})";
        _state.AddMessage(header);
        _state.AddMessage(new string('-', 50));

        if (report.TotalCount == 0 && report.UncheckedCount == 0)
        {
            _state.AddMessage("No numeric constraints found in circuit.");
        }

        foreach (var result in report.Results)
        {
            var status = result.Passed ? "PASS" : "FAIL";
            var nodeStr = result.Node != null ? $" @ {result.Node}" : "";
            var expectedStr = ValueFormatter.FormatValue(
                result.Expected,
                GetUnitFromConstraint(circuit, result.Id)
            );
            var actualStr = result.Actual.HasValue
                ? $" (measured: {ValueFormatter.FormatValue(result.Actual.Value, GetUnitFromConstraint(circuit, result.Id))})"
                : " (not measured)";

            _state.AddMessage(
                $"{result.Id, -8} {result.Metric}{nodeStr} {result.Operator} {expectedStr, -12} {status}{actualStr}"
            );
        }

        _state.AddMessage(new string('-', 50));
        _state.AddMessage(
            $"Result: {report.PassedCount}/{report.TotalCount} constraints satisfied"
        );

        // Show hint about unchecked constraints from other benches
        if (report.UncheckedByBench.Count > 0)
        {
            _state.AddMessage("");
            var totalUnchecked = report.UncheckedCount;
            var constraintWord = totalUnchecked == 1 ? "constraint" : "constraints";

            foreach (var kvp in report.UncheckedByBench)
            {
                var ids = string.Join(", ", kvp.Value.Select(c => c.Id));
                _state.AddMessage(
                    $"Note: {kvp.Value.Count} {constraintWord} ({ids}) measured by {kvp.Key}."
                );
            }
            _state.AddMessage("Run `verify` with combined results to check all constraints.");
        }
    }

    private static string GetUnitFromConstraint(Circuit circuit, string constraintId)
    {
        var constraint = circuit.Constraints?.Numeric?.FirstOrDefault(c => c.Id == constraintId);
        return constraint?.Unit ?? "";
    }
}
