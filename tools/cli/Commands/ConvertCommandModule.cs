using System;
using System.IO;
using System.Linq;
using Cascode.Cli.Output;
using Cascode.Language;
using Cascode.Language.Json;

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
    private readonly CliOutputProvider _output;

    public ConvertCommandModule(ShellState state, CliOutputProvider output)
    {
        _state = state;
        _output = output;
    }

    public void Register(CommandRegistry registry)
    {
        registry.Register(
            new DelegateCliCommand(
                "convert",
                "Convert between Cascode text and JSON formats",
                ConvertCommand,
                helpCategory: CommandHelpCategory.Design
            )
        );
    }

    private CommandResult ConvertCommand(string[] args)
    {
        var output = _output.Get();
        if (args.Length == 0)
        {
            ShowUsage(output);
            return CommandResult.Success;
        }

        var (inputPath, toJson, toCascode, outputPath, toStdout, parseError) = ParseArgs(args);
        if (parseError != null)
        {
            output.Error(parseError);
            return new CommandResult(2, false);
        }

        if (!File.Exists(inputPath))
        {
            output.Error($"Input file '{inputPath}' not found.");
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
            output.Error($"Conversion failed: {ex.Message}");
            return CommandResult.Failure;
        }
    }

    private CommandResult ConvertToJson(string inputPath, string? outputPath, bool toStdout)
    {
        var output = _output.Get();
        CascodeReadResult readResult;
        using (var reader = File.OpenText(inputPath))
        {
            readResult = CascodeReader.TryRead(reader, inputPath);
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
            return new CommandResult(2, false);
        }

        var elCircuits = readResult
            .Document!.Circuits.Where(c => c.Level == CascodeLevel.EL)
            .ToList();
        if (elCircuits.Count == 0)
        {
            output.Error("No EL-level circuits found. Convert only supports EL-level Cascode.");
            return new CommandResult(2, false);
        }

        var json = CascodeJsonConverter.ToJson(readResult.Document);

        if (toStdout)
        {
            output.WriteLine(json);
        }
        else
        {
            outputPath ??= Path.ChangeExtension(inputPath, ".json");
            File.WriteAllText(outputPath, json);
            output.Success($"Wrote JSON: {outputPath}");
        }

        return CommandResult.Success;
    }

    private CommandResult ConvertToCascode(string inputPath, string? outputPath, bool toStdout)
    {
        var output = _output.Get();
        string json;
        try
        {
            json = File.ReadAllText(inputPath);
        }
        catch (Exception ex)
        {
            output.Error($"Failed to read input file: {ex.Message}");
            return new CommandResult(2, false);
        }

        var result = CascodeJsonConverter.FromJson(json, inputPath);

        if (!result.Success)
        {
            foreach (
                var diag in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            )
            {
                output.Error($"{diag.FilePath}:{diag.Line}: {diag.Message}");
            }
            return new CommandResult(2, false);
        }

        var doc = result.Document!;

        using var writer = new StringWriter();
        CascodeWriter.Write(doc, writer);
        var cascodeText = writer.ToString();

        if (toStdout)
        {
            output.WriteLine(cascodeText);
        }
        else
        {
            outputPath ??= Path.ChangeExtension(inputPath, ".el.cai");
            File.WriteAllText(outputPath, cascodeText);
            output.Success($"Wrote Cascode: {outputPath}");
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

    private static void ShowUsage(ICliOutput output)
    {
        output.WriteLine("Usage: convert <input_file> [--json|--cascode] [-o <output>] [--stdout]");
        output.WriteLine("");
        output.WriteLine("Converts between Cascode text (.el.cas) and JSON (.json) formats.");
        output.WriteLine("Only EL-level circuits are supported.");
        output.WriteLine("");
        output.WriteLine("Options:");
        output.WriteLine("  --json      Convert Cascode to JSON");
        output.WriteLine("  --cascode   Convert JSON to Cascode");
        output.WriteLine("  -o <file>   Output file (default: input with changed extension)");
        output.WriteLine("  --stdout    Write output to stdout instead of file");
        output.WriteLine("");
        output.WriteLine(
            "If neither --json nor --cascode is specified, direction is inferred from input extension."
        );
        output.WriteLine("");
        output.WriteLine("Exit codes:");
        output.WriteLine("  0 = Success");
        output.WriteLine("  1 = Runtime error");
        output.WriteLine("  2 = Parse/input error");
    }
}
