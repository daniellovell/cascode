using System;
using System.IO;
using System.Linq;
using Cascode.Language;
using Cascode.Language.Json;
using Cascode.Parser;

namespace Cascode.Cli.Commands;

/// <summary>
/// Command module for bidirectional conversion between Cascode text and JSON formats.
/// </summary>
/// <remarks>
/// The convert command supports:
/// - Cascode to JSON: cascode convert circuit.el.cas --json [-o output.json]
/// - JSON to Cascode: cascode convert circuit.el.json --cascode [-o output.cas]
///
/// Only EL-level circuits are supported.
///
/// Exit codes:
///   0 = Success
///   1 = Runtime error
///   2 = Parse error / invalid input
/// </remarks>
internal sealed class ConvertCommandModule : ICommandModule
{
    private readonly ShellState _state;

    public ConvertCommandModule(ShellState state)
    {
        _state = state;
    }

    public void Register(CommandRegistry registry)
    {
        registry.Register(
            new DelegateCliCommand(
                "convert",
                "Convert between Cascode text and JSON formats",
                ConvertCommand
            )
        );
    }

    private CommandResult ConvertCommand(string[] args)
    {
        if (args.Length == 0)
        {
            ShowUsage();
            return CommandResult.Success;
        }

        var (inputPath, toJson, toCascode, outputPath, toStdout, parseError) = ParseArgs(args);
        if (parseError != null)
        {
            _state.AddMessage(parseError);
            return new CommandResult(2, false);
        }

        if (!File.Exists(inputPath))
        {
            _state.AddMessage($"Input file '{inputPath}' not found.");
            return new CommandResult(2, false);
        }

        inputPath = Path.GetFullPath(inputPath);

        // Determine conversion direction from extension if not specified
        if (!toJson && !toCascode)
        {
            if (inputPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                toCascode = true;
            }
            else
            {
                toJson = true;
            }
        }

        try
        {
            if (toJson)
            {
                return ConvertToJson(inputPath, outputPath, toStdout);
            }
            else
            {
                return ConvertToCascode(inputPath, outputPath, toStdout);
            }
        }
        catch (Exception ex)
        {
            _state.AddMessage($"Conversion failed: {ex.Message}");
            return CommandResult.Failure;
        }
    }

    private CommandResult ConvertToJson(string inputPath, string? outputPath, bool toStdout)
    {
        ACIRReadResult readResult;
        using (var reader = File.OpenText(inputPath))
        {
            readResult = ACIRReader.TryRead(reader, inputPath);
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
            return new CommandResult(2, false);
        }

        var elCircuits = readResult.Document!.Circuits.Where(c => c.Level == ACIRLevel.EL).ToList();
        if (elCircuits.Count == 0)
        {
            _state.AddMessage(
                "No EL-level circuits found. Convert only supports EL-level Cascode."
            );
            return new CommandResult(2, false);
        }

        var json = AcirJsonConverter.ToJson(readResult.Document);

        if (toStdout)
        {
            _state.AddMessage(json);
        }
        else
        {
            outputPath ??= Path.ChangeExtension(inputPath, ".json");
            File.WriteAllText(outputPath, json);
            _state.AddMessage($"Wrote JSON: {outputPath}");
        }

        return CommandResult.Success;
    }

    private CommandResult ConvertToCascode(string inputPath, string? outputPath, bool toStdout)
    {
        string json;
        try
        {
            json = File.ReadAllText(inputPath);
        }
        catch (Exception ex)
        {
            _state.AddMessage($"Failed to read input file: {ex.Message}");
            return new CommandResult(2, false);
        }

        var result = AcirJsonConverter.FromJson(json, inputPath);

        if (!result.Success)
        {
            foreach (
                var diag in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            )
            {
                _state.AddMessage($"{diag.FilePath}:{diag.Line}: {diag.Message}");
            }
            return new CommandResult(2, false);
        }

        var doc = result.Document!;

        using var writer = new StringWriter();
        ACIRWriter.Write(doc, writer);
        var cascodeText = writer.ToString();

        if (toStdout)
        {
            _state.AddMessage(cascodeText);
        }
        else
        {
            outputPath ??= Path.ChangeExtension(inputPath, ".el.cas");
            File.WriteAllText(outputPath, cascodeText);
            _state.AddMessage($"Wrote Cascode: {outputPath}");
        }

        return CommandResult.Success;
    }

    private static (
        string inputPath,
        bool toJson,
        bool toCascode,
        string? outputPath,
        bool toStdout,
        string? error
    ) ParseArgs(string[] args)
    {
        string? inputPath = null;
        var toJson = false;
        var toCascode = false;
        string? outputPath = null;
        var toStdout = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg == "--json")
            {
                toJson = true;
            }
            else if (arg == "--cascode")
            {
                toCascode = true;
            }
            else if (arg == "--stdout")
            {
                toStdout = true;
            }
            else if (arg == "-o" || arg == "--output")
            {
                if (i + 1 >= args.Length)
                {
                    return (
                        string.Empty,
                        false,
                        false,
                        null,
                        false,
                        "Output path required after -o/--output"
                    );
                }
                outputPath = args[++i];
            }
            else if (!arg.StartsWith("-"))
            {
                inputPath = arg;
            }
            else
            {
                return (string.Empty, false, false, null, false, $"Unknown option: {arg}");
            }
        }

        if (inputPath == null)
        {
            return (string.Empty, false, false, null, false, "Input file is required.");
        }

        if (toJson && toCascode)
        {
            return (
                string.Empty,
                false,
                false,
                null,
                false,
                "Cannot specify both --json and --cascode."
            );
        }

        return (inputPath, toJson, toCascode, outputPath, toStdout, null);
    }

    private void ShowUsage()
    {
        _state.AddMessage(
            "Usage: convert <input_file> [--json|--cascode] [-o <output>] [--stdout]"
        );
        _state.AddMessage("");
        _state.AddMessage("Converts between Cascode text (.el.cas) and JSON (.json) formats.");
        _state.AddMessage("Only EL-level circuits are supported.");
        _state.AddMessage("");
        _state.AddMessage("Options:");
        _state.AddMessage("  --json      Convert Cascode to JSON");
        _state.AddMessage("  --cascode   Convert JSON to Cascode");
        _state.AddMessage("  -o <file>   Output file (default: input with changed extension)");
        _state.AddMessage("  --stdout    Write output to stdout instead of file");
        _state.AddMessage("");
        _state.AddMessage(
            "If neither --json nor --cascode is specified, direction is inferred from input extension."
        );
        _state.AddMessage("");
        _state.AddMessage("Exit codes:");
        _state.AddMessage("  0 = Success");
        _state.AddMessage("  1 = Runtime error");
        _state.AddMessage("  2 = Parse/input error");
    }
}
