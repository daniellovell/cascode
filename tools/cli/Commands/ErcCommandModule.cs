using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Cascode.Language;
using Cascode.Language.Validation;

namespace Cascode.Cli.Commands;

/// <summary>
/// Command module for electrical rule checking (ERC) on Cascode documents.
/// </summary>
/// <remarks>
/// The ERC command validates that an Cascode EL or ML document represents a legal circuit
/// that can be simulated. It checks for electrical issues such as floating gates,
/// VDD-GND shorts, and dangling nets. ERC operates on topology (connectivity) and
/// does not require concrete sizing values, so both EL (sized) and ML (unsized with ??)
/// circuits are supported.
///
/// Exit codes:
///   0 = ERC passed
///   1 = ERC failed (errors in stderr)
///   2 = Parse error / invalid input
/// </remarks>
internal sealed class ErcCommandModule : ICommandModule
{
    private readonly ShellState _state;

    public ErcCommandModule(ShellState state)
    {
        _state = state;
    }

    public void Register(CommandRegistry registry)
    {
        registry.Register(
            new DelegateCliCommand("erc", "Run electrical rule check on Cascode file", ErcCommand)
        );
    }

    private CommandResult ErcCommand(string[] args)
    {
        if (args.Length == 0)
        {
            ShowUsage();
            return CommandResult.Success;
        }

        if (
            !ValidateAndReadInput(
                args,
                out var doc,
                out var earlyResult,
                out var requirePdk,
                out var jsonOutput
            )
        )
        {
            return earlyResult.Value;
        }

        // Run ERC on all EL and ML circuits (topology-based checks work on both)
        var circuits = doc!
            .Circuits.Where(c => c.Level is CascodeLevel.EL or CascodeLevel.ML)
            .ToList();
        var combinedResult = new ValidationResult();
        foreach (var circuit in circuits)
        {
            var circuitResult = ElectricalRuleChecker.Check(circuit, requirePdk);
            combinedResult.Merge(circuitResult);
        }

        var exitCode = combinedResult.HasErrors ? 1 : 0;

        if (jsonOutput)
        {
            BuildJsonOutput(combinedResult, exitCode);
        }
        else
        {
            BuildHumanOutput(combinedResult, circuits.Count);
        }

        return new CommandResult(exitCode, false);
    }

    private bool ValidateAndReadInput(
        string[] args,
        out CascodeDocument? doc,
        [NotNullWhen(false)] out CommandResult? earlyResult,
        out bool requirePdk,
        out bool jsonOutput
    )
    {
        doc = null;
        earlyResult = null;

        var inputPath = args[0];
        requirePdk = args.Contains("--require-pdk");
        jsonOutput = args.Contains("--json");

        if (!File.Exists(inputPath))
        {
            if (jsonOutput)
            {
                var errorResult = new ValidationResult();
                errorResult.AddError("ERC-PARSE", $"Input file '{inputPath}' not found");
                _state.AddMessage(errorResult.ToJson(2));
            }
            else
            {
                _state.AddMessage($"Input file '{inputPath}' not found.");
            }
            earlyResult = new CommandResult(2, false);
            return false;
        }

        inputPath = Path.GetFullPath(inputPath);

        // Parse Cascode document
        CascodeReadResult readResult;
        using (var reader = File.OpenText(inputPath))
        {
            readResult = CascodeReader.TryRead(reader, inputPath);
        }

        if (!readResult.Success)
        {
            if (jsonOutput)
            {
                var errorResult = new ValidationResult();
                foreach (
                    var diag in readResult.Diagnostics.Where(d =>
                        d.Severity == DiagnosticSeverity.Error
                    )
                )
                {
                    errorResult.AddError("ERC-PARSE", diag.Message, $"{diag.FilePath}:{diag.Line}");
                }
                _state.AddMessage(errorResult.ToJson(2));
            }
            else
            {
                foreach (
                    var diag in readResult.Diagnostics.Where(d =>
                        d.Severity == DiagnosticSeverity.Error
                    )
                )
                {
                    _state.AddMessage($"{diag.FilePath}:{diag.Line}: {diag.Message}");
                }
            }
            earlyResult = new CommandResult(2, false);
            return false;
        }

        doc = readResult.Document!;

        // Find EL or ML circuits (topology-based ERC works on both)
        var circuits = doc
            .Circuits.Where(c => c.Level is CascodeLevel.EL or CascodeLevel.ML)
            .ToList();
        if (circuits.Count == 0)
        {
            if (jsonOutput)
            {
                var errorResult = new ValidationResult();
                errorResult.AddError(
                    "ERC-PARSE",
                    "No EL or ML level circuits found. ERC requires EL or ML level Cascode."
                );
                _state.AddMessage(errorResult.ToJson(2));
            }
            else
            {
                _state.AddMessage(
                    "No EL or ML level circuits found. ERC requires EL or ML level Cascode."
                );
            }
            earlyResult = new CommandResult(2, false);
            return false;
        }

        return true;
    }

    private void BuildJsonOutput(ValidationResult result, int exitCode)
    {
        _state.AddMessage(result.ToJson(exitCode));
    }

    private void BuildHumanOutput(ValidationResult result, int circuitCount)
    {
        // Display errors
        foreach (var error in result.GetErrors())
        {
            _state.AddMessage(error.ToString());
        }

        // Display warnings
        foreach (var warning in result.GetWarnings())
        {
            _state.AddMessage(warning.ToString());
        }

        // Summary
        if (result.HasErrors)
        {
            _state.AddMessage(
                $"ERC failed: {result.ErrorCount} error(s), {result.WarningCount} warning(s)."
            );
        }
        else if (result.HasWarnings)
        {
            _state.AddMessage($"ERC passed with {result.WarningCount} warning(s).");
        }
        else
        {
            _state.AddMessage($"ERC passed: {circuitCount} circuit(s) validated.");
        }
    }

    private void ShowUsage()
    {
        _state.AddMessage("Usage: erc <acir_file> [--require-pdk] [--json]");
        _state.AddMessage("");
        _state.AddMessage("Runs electrical rule checking on an Cascode EL or ML document.");
        _state.AddMessage("ERC validates circuit topology and works on both sized (EL) and");
        _state.AddMessage("unsized (ML with ??) circuits.");
        _state.AddMessage("");
        _state.AddMessage("Options:");
        _state.AddMessage(
            "  --require-pdk    Treat missing PDK device names as errors (default: warnings)"
        );
        _state.AddMessage("  --json           Output results as JSON for machine processing");
        _state.AddMessage("");
        _state.AddMessage("Exit codes:");
        _state.AddMessage("  0 = ERC passed");
        _state.AddMessage("  1 = ERC failed (errors found)");
        _state.AddMessage("  2 = Parse error / invalid input");
    }
}
