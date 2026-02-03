using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Cascode.Cli.Output;
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
    private readonly CliOutputProvider _output;

    public ErcCommandModule(ShellState state, CliOutputProvider output)
    {
        _state = state;
        _output = output;
    }

    public void Register(CommandRegistry registry)
    {
        registry.Register(
            new DelegateCliCommand("erc", "Run electrical rule check on Cascode file", ErcCommand)
        );
    }

    private CommandResult ErcCommand(string[] args)
    {
        var output = _output.Get();
        if (args.Length == 0)
        {
            ShowUsage(output);
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
            BuildJsonOutput(output, combinedResult, exitCode);
        }
        else
        {
            BuildHumanOutput(output, combinedResult, circuits.Count);
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
        var output = _output.Get();
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
                output.WriteLine(errorResult.ToJson(2));
            }
            else
            {
                output.Error($"Input file '{inputPath}' not found.");
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
                output.WriteLine(errorResult.ToJson(2));
            }
            else
            {
                foreach (
                    var diag in readResult.Diagnostics.Where(d =>
                        d.Severity == DiagnosticSeverity.Error
                    )
                )
                {
                    output.Error($"{diag.FilePath}:{diag.Line}: {diag.Message}");
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
                output.WriteLine(errorResult.ToJson(2));
            }
            else
            {
                output.Error(
                    "No EL or ML level circuits found. ERC requires EL or ML level Cascode."
                );
            }
            earlyResult = new CommandResult(2, false);
            return false;
        }

        return true;
    }

    private static void BuildJsonOutput(ICliOutput output, ValidationResult result, int exitCode)
    {
        output.WriteLine(result.ToJson(exitCode));
    }

    private static void BuildHumanOutput(
        ICliOutput output,
        ValidationResult result,
        int circuitCount
    )
    {
        // Display errors
        foreach (var error in result.GetErrors())
        {
            output.Error(error.ToString());
        }

        // Display warnings
        foreach (var warning in result.GetWarnings())
        {
            output.Warning(warning.ToString());
        }

        // Summary
        if (result.HasErrors)
        {
            output.Error(
                $"ERC failed: {result.ErrorCount} error(s), {result.WarningCount} warning(s)."
            );
        }
        else if (result.HasWarnings)
        {
            output.Warning($"ERC passed with {result.WarningCount} warning(s).");
        }
        else
        {
            output.Success($"ERC passed: {circuitCount} circuit(s) validated.");
        }
    }

    private static void ShowUsage(ICliOutput output)
    {
        output.WriteLine("Usage: erc <cascode_file> [--require-pdk] [--json]");
        output.WriteLine("");
        output.WriteLine("Runs electrical rule checking on an Cascode EL or ML document.");
        output.WriteLine("ERC validates circuit topology and works on both sized (EL) and");
        output.WriteLine("unsized (ML with ??) circuits.");
        output.WriteLine("");
        output.WriteLine("Options:");
        output.WriteLine(
            "  --require-pdk    Treat missing PDK device names as errors (default: warnings)"
        );
        output.WriteLine("  --json           Output results as JSON for machine processing");
        output.WriteLine("");
        output.WriteLine("Exit codes:");
        output.WriteLine("  0 = ERC passed");
        output.WriteLine("  1 = ERC failed (errors found)");
        output.WriteLine("  2 = Parse error / invalid input");
    }
}
