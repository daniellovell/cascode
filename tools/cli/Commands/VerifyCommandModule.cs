using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Cascode.ACIR;
using Cascode.Bench;

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
        registry.Register(new DelegateCliCommand("verify", "Verify constraint compliance from bench results", VerifyCommand));
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
            _state.AddMessage("Usage: verify --acir <acir_file> --results <results_json>");
            _state.AddMessage("");
            _state.AddMessage("Verifies numeric constraints from ACIR against bench measurement results.");
            return CommandResult.Success;
        }

        string? acirPath = null;
        string? resultsPath = null;

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
        }

        if (acirPath == null || resultsPath == null)
        {
            _state.AddMessage("Error: Both --acir and --results are required.");
            return CommandResult.Failure;
        }

        if (!File.Exists(acirPath))
        {
            _state.AddMessage($"ACIR file '{acirPath}' not found.");
            return CommandResult.Failure;
        }

        if (!File.Exists(resultsPath))
        {
            _state.AddMessage($"Results file '{resultsPath}' not found.");
            return CommandResult.Failure;
        }

        // Read ACIR document
        ACIRDocument doc;
        try
        {
            using var reader = File.OpenText(acirPath);
            doc = ACIRReader.Read(reader);
        }
        catch (Exception ex)
        {
            _state.AddMessage($"Failed to read ACIR file: {ex.Message}");
            return CommandResult.Failure;
        }

        // Find EL-level circuit (use first one, or match by name from results)
        var elCircuits = doc.Circuits.Where(c => c.Level == ACIRLevel.EL).ToList();
        if (elCircuits.Count == 0)
        {
            _state.AddMessage("No EL-level circuits found in ACIR document.");
            return CommandResult.Failure;
        }

        // Read results JSON
        BenchResult results;
        try
        {
            var jsonText = File.ReadAllText(resultsPath);
            results = JsonSerializer.Deserialize<BenchResult>(jsonText) ?? throw new InvalidOperationException("Failed to deserialize results JSON");
        }
        catch (Exception ex)
        {
            _state.AddMessage($"Failed to read results file: {ex.Message}");
            return CommandResult.Failure;
        }

        // Find matching circuit by name
        var circuit = elCircuits.FirstOrDefault(c => c.Name.Equals(results.Circuit, StringComparison.OrdinalIgnoreCase))
            ?? elCircuits[0];

        // Check compliance
        var report = ComplianceChecker.Check(circuit, results);

        // Display report
        _state.AddMessage($"Constraint Compliance Report for {circuit.Name}");
        _state.AddMessage(new string('-', 50));

        if (circuit.Constraints?.Numeric == null || circuit.Constraints.Numeric.Count == 0)
        {
            _state.AddMessage("No numeric constraints found in circuit.");
        }

        foreach (var result in report.Results)
        {
            var status = result.Passed ? "PASS" : "FAIL";
            var nodeStr = result.Node != null ? $" @ {result.Node}" : "";
            var expectedStr = FormatValue(result.Expected, GetUnitFromConstraint(circuit, result.Id));
            var actualStr = result.Actual.HasValue ? $" (measured: {FormatValue(result.Actual.Value, GetUnitFromConstraint(circuit, result.Id))})" : " (not measured)";

            _state.AddMessage($"{result.Id,-8} {result.Metric}{nodeStr} {result.Operator} {expectedStr,-12} {status}{actualStr}");
        }

        _state.AddMessage(new string('-', 50));
        _state.AddMessage($"Result: {report.PassedCount}/{report.TotalCount} constraints satisfied");

        return report.FailedCount == 0 ? CommandResult.Success : CommandResult.Failure;
    }

    private static string FormatValue(double value, string unit)
    {
        // Simple formatting - use ComplianceChecker's logic if available, otherwise basic
        if (Math.Abs(value) >= 1e9)
            return $"{value / 1e9:G3}G {unit}";
        if (Math.Abs(value) >= 1e6)
            return $"{value / 1e6:G3}M {unit}";
        if (Math.Abs(value) >= 1e3)
            return $"{value / 1e3:G3}k {unit}";
        if (Math.Abs(value) >= 1.0)
            return $"{value:G3} {unit}";
        if (Math.Abs(value) >= 1e-3)
            return $"{value * 1e3:G3}m {unit}";
        if (Math.Abs(value) >= 1e-6)
            return $"{value * 1e6:G3}u {unit}";
        return $"{value:G3} {unit}";
    }

    private static string GetUnitFromConstraint(Circuit circuit, string constraintId)
    {
        var constraint = circuit.Constraints?.Numeric?.FirstOrDefault(c => c.Id == constraintId);
        return constraint?.Unit ?? "";
    }
}

