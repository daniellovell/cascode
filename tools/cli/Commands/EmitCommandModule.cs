using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cascode.Bench;
using Cascode.Cli.Services;
using Cascode.Language;
using Cascode.Language.Validation;
using Cascode.Parser;
using Cascode.Workspace;
using Microsoft.Extensions.Logging;

namespace Cascode.Cli.Commands;

/// <summary>
/// Command module for emitting SPICE netlists from ACIR EL documents.
/// </summary>
/// <remarks>
/// The emit command generates both design subcircuits and testbench files from
/// ACIR EL-level circuits. It reads the harness and benches sections to generate
/// complete simulation-ready SPICE files targeting ngspice or spectre backends.
/// The default backend is ngspice.
/// </remarks>
internal sealed class EmitCommandModule : ICommandModule
{
    private readonly ShellState _state;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmitCommandModule"/> class.
    /// </summary>
    /// <param name="state">Shell state for messaging.</param>
    public EmitCommandModule(ShellState state)
    {
        _state = state;
    }

    /// <summary>
    /// Registers the emit command with the command registry.
    /// </summary>
    /// <param name="registry">Command registry.</param>
    public void Register(CommandRegistry registry)
    {
        registry.Register(
            new DelegateCliCommand("emit", "Emit SPICE netlist from ACIR EL", EmitCommand)
        );
    }

    /// <summary>
    /// Executes the emit command to generate SPICE netlists.
    /// </summary>
    /// <param name="args">Command arguments: [acir_file] [--out output_dir] [--backend ngspice|spectre] [--json].</param>
    /// <returns>Command result indicating success or failure.</returns>
    private CommandResult EmitCommand(string[] args)
    {
        if (args.Length == 0)
        {
            ShowUsage();
            return CommandResult.Success;
        }

        var inputPath = args[0];
        var (outputDir, backend, jsonOutput) = ParseEmitOptions(args);

        if (!File.Exists(inputPath))
        {
            if (jsonOutput)
            {
                OutputEmitJson(
                    false,
                    2,
                    new ValidationResult(),
                    new List<string>(),
                    new List<string>(),
                    $"Input file '{inputPath}' not found."
                );
            }
            else
            {
                _state.AddMessage($"Input file '{inputPath}' not found.");
            }
            return new CommandResult(2, false);
        }

        inputPath = Path.GetFullPath(inputPath);

        var doc = TryReadAcirDocument(inputPath, jsonOutput);
        if (doc == null)
        {
            return new CommandResult(2, false); // Parse error
        }

        var elCircuits = doc.Circuits.Where(c => c.Level == ACIRLevel.EL).ToList();
        if (elCircuits.Count == 0)
        {
            if (jsonOutput)
            {
                OutputEmitJson(
                    false,
                    2,
                    new ValidationResult(),
                    new List<string>(),
                    new List<string>(),
                    "No EL-level circuits found. SPICE emission requires EL-level ACIR."
                );
            }
            else
            {
                _state.AddMessage(
                    "No EL-level circuits found. SPICE emission requires EL-level ACIR."
                );
            }
            return new CommandResult(2, false);
        }

        var primitivesByName = doc.Primitives.ToDictionary(p => p.Name, StringComparer.Ordinal);
        var usesPdkDevices = elCircuits.Any(c =>
            c.Fill?.Devices.Any(d =>
            {
                if (!primitivesByName.TryGetValue(d.Primitive, out var primitive))
                {
                    return false;
                }

                var deviceKey = primitive.Device;
                if (string.IsNullOrWhiteSpace(deviceKey))
                {
                    return false;
                }

                return !deviceKey.Equals("level1_nmos", StringComparison.OrdinalIgnoreCase)
                    && !deviceKey.Equals("level1_pmos", StringComparison.OrdinalIgnoreCase);
            }) == true
        );

        ILoggerFactory? localFactory = null;
        try
        {
            var workspaceRoot = FindWorkspaceRoot(inputPath) ?? Directory.GetCurrentDirectory();
            var pdkRoot = _state.PdkRoot ?? _state.WorkspaceRoot;
            if (usesPdkDevices && !jsonOutput)
            {
                var dbPath = WorkspacePaths.GetDatabasePath(pdkRoot);
                var status = File.Exists(dbPath) ? string.Empty : " (no pdk.db; run 'pdk scan')";
                _state.AddMessage($"PDK workspace: {pdkRoot}{status}");
            }
            var loggerFactory =
                _state.LoggerFactory
                ?? (
                    localFactory = LoggerFactory.Create(builder =>
                    {
                        builder.SetMinimumLevel(LogLevel.Warning);
                        builder.AddSimpleConsole(o =>
                        {
                            o.SingleLine = true;
                        });
                    })
                );
            var includeResolver = PdkBenchIncludeResolver.Create(
                pdkRoot,
                loggerFactory.CreateLogger<PdkBenchIncludeResolver>()
            );
            var result = SpiceEmitter.ValidateAndEmit(
                doc,
                outputDir,
                backend,
                workspaceRoot,
                includeResolver
            );

            if (!result.Validation.IsValid)
            {
                if (jsonOutput)
                {
                    OutputEmitJson(
                        false,
                        2,
                        result.Validation,
                        new List<string>(),
                        new List<string>()
                    );
                }
                else
                {
                    foreach (var error in result.Validation.GetErrors())
                    {
                        _state.AddMessage(error.ToString());
                    }
                    _state.AddMessage(
                        $"Emission failed: {result.Validation.ErrorCount} error(s) found."
                    );
                }
                return new CommandResult(2, false);
            }

            if (jsonOutput)
            {
                OutputEmitJson(
                    true,
                    0,
                    result.Validation,
                    result.Emit.DesignPaths,
                    result.Emit.TestbenchPaths
                );
            }
            else
            {
                foreach (var warning in result.Validation.GetWarnings())
                {
                    _state.AddMessage(warning.ToString());
                }
                foreach (var path in result.Emit.DesignPaths)
                {
                    _state.AddMessage($"Design netlist: {path}");
                }
                foreach (var path in result.Emit.TestbenchPaths)
                {
                    _state.AddMessage($"Testbench: {path}");
                }
                _state.AddMessage(
                    $"Emitted {result.Emit.DesignPaths.Count} design(s) and {result.Emit.TestbenchPaths.Count} testbench(es)."
                );
            }

            return CommandResult.Success;
        }
        catch (Exception ex)
        {
            if (jsonOutput)
            {
                OutputEmitJson(
                    false,
                    1,
                    new ValidationResult(),
                    new List<string>(),
                    new List<string>(),
                    $"SPICE emission failed: {ex.Message}"
                );
            }
            else
            {
                _state.AddMessage($"SPICE emission failed: {ex.Message}");
            }
            return CommandResult.Failure;
        }
        finally
        {
            localFactory?.Dispose();
        }
    }

    private void OutputEmitJson(
        bool success,
        int exitCode,
        ValidationResult validation,
        List<string> designPaths,
        List<string> testbenchPaths,
        string? additionalError = null
    )
    {
        var validationCopy = validation;
        if (additionalError != null)
        {
            validationCopy = new ValidationResult();
            validationCopy.Merge(validation);
            validationCopy.AddError("EMIT-FAIL", additionalError);
        }

        var output = new EmitJsonOutput
        {
            Success = success,
            ExitCode = exitCode,
            Validation = JsonSerializer.Deserialize<JsonElement>(validationCopy.ToJson(exitCode)),
            DesignPaths = designPaths,
            TestbenchPaths = testbenchPaths,
        };

        _state.AddMessage(JsonSerializer.Serialize(output, EmitJsonOutput.SerializerOptions));
    }

    private void ShowUsage()
    {
        _state.AddMessage(
            "Usage: emit <acir_file> [--out <dir>] [--backend <ngspice|spectre>] [--json]"
        );
        _state.AddMessage("");
        _state.AddMessage("Emits SPICE netlists from an ACIR EL document.");
        _state.AddMessage("Generates both design subcircuit and testbench files.");
        _state.AddMessage("");
        _state.AddMessage("Options:");
        _state.AddMessage("  --out <dir>      Output directory (default: ./build)");
        _state.AddMessage(
            "  --backend <type> Simulator backend: ngspice or spectre (default: ngspice)"
        );
        _state.AddMessage("  --json           Output results as JSON for machine processing");
    }

    private static (string OutputDir, BenchBackendType Backend, bool JsonOutput) ParseEmitOptions(
        string[] args
    )
    {
        var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "build");
        var backend = BenchBackendType.Ngspice;
        var jsonOutput = false;

        for (var i = 1; i < args.Length; i++)
        {
            if ((args[i] == "--out" || args[i] == "-o") && i + 1 < args.Length)
            {
                outputDir = args[i + 1];
                i++;
            }
            else if (args[i] == "--backend" && i + 1 < args.Length)
            {
                var backendStr = args[i + 1].ToLowerInvariant();
                backend = backendStr switch
                {
                    "ngspice" => BenchBackendType.Ngspice,
                    "spectre" => BenchBackendType.Spectre,
                    _ => throw new InvalidOperationException(
                        $"Unknown backend: {args[i + 1]}. Use 'ngspice' or 'spectre'."
                    ),
                };
                i++;
            }
            else if (args[i] == "--json")
            {
                jsonOutput = true;
            }
        }

        return (outputDir, backend, jsonOutput);
    }

    private ACIRDocument? TryReadAcirDocument(string inputPath, bool jsonOutput = false)
    {
        ACIRReadResult readResult;
        using (var reader = File.OpenText(inputPath))
        {
            readResult = ACIRReader.TryRead(reader, inputPath);
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
                    var code = string.IsNullOrWhiteSpace(diag.Code) ? "EMIT-PARSE" : diag.Code;
                    errorResult.AddError(code, diag.Message, $"{diag.FilePath}:{diag.Line}");
                }
                OutputEmitJson(false, 2, errorResult, new List<string>(), new List<string>());
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
            return null;
        }

        return readResult.Document;
    }

    private static string? FindWorkspaceRoot(string filePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "lib")))
            {
                return dir;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }
}

/// <summary>
/// JSON output model for emit command results.
/// </summary>
internal sealed class EmitJsonOutput
{
    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("exitCode")]
    public int ExitCode { get; init; }

    [JsonPropertyName("validation")]
    public JsonElement Validation { get; init; }

    [JsonPropertyName("designPaths")]
    public List<string> DesignPaths { get; init; } = new();

    [JsonPropertyName("testbenchPaths")]
    public List<string> TestbenchPaths { get; init; } = new();
}
