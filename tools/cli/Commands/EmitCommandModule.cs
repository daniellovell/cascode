using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cascode.Bench;
using Cascode.Cli.Output;
using Cascode.Cli.Services;
using Cascode.Language;
using Cascode.Language.Validation;
using Cascode.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cascode.Cli.Commands;

/// <summary>
/// Command module for emitting SPICE netlists from Cascode EL documents.
/// </summary>
/// <remarks>
/// The emit command generates both design subcircuits and testbench files from
/// Cascode EL-level circuits. It reads the harness and benches sections to generate
/// complete simulation-ready SPICE files targeting ngspice or spectre backends.
/// The default backend is ngspice.
/// </remarks>
internal sealed class EmitCommandModule : ICommandModule
{
    private readonly ShellState _state;
    private readonly CliOutputProvider _output;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmitCommandModule"/> class.
    /// </summary>
    /// <param name="state">Shell state for messaging.</param>
    public EmitCommandModule(ShellState state, CliOutputProvider output)
    {
        _state = state;
        _output = output;
    }

    /// <summary>
    /// Registers the emit command with the command registry.
    /// </summary>
    /// <param name="registry">Command registry.</param>
    public void Register(CommandRegistry registry)
    {
        registry.Register(
            new DelegateCliCommand("emit", "Emit SPICE netlist from Cascode EL", EmitCommand)
        );
    }

    /// <summary>
    /// Executes the emit command to generate SPICE netlists.
    /// </summary>
    /// <param name="args">Command arguments: [cascode_file] [--out output_dir] [--backend ngspice|spectre] [--json].</param>
    /// <returns>Command result indicating success or failure.</returns>
    private CommandResult EmitCommand(string[] args)
    {
        var output = _output.Get();

        if (args.Length == 0)
        {
            ShowUsage(output);
            return CommandResult.Success;
        }

        var inputPath = args[0];
        var (outputDir, backend, jsonOutput) = ParseEmitOptions(args);

        if (!File.Exists(inputPath))
        {
            if (jsonOutput)
            {
                OutputEmitJson(
                    output,
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
                output.Error($"Input file '{inputPath}' not found.");
            }
            return new CommandResult(2, false);
        }

        inputPath = Path.GetFullPath(inputPath);

        var loadLogger = _state.LoggerFactory?.CreateLogger("CascodeLinker") ?? NullLogger.Instance;
        if (
            !CascodeLoadLinkService.TryLoadAndLinkIfNeeded(
                inputPath,
                _state.WorkspaceRoot,
                outputDir,
                loadLogger,
                out var loaded,
                out var diagnostics
            )
        )
        {
            if (jsonOutput)
            {
                var errorResult = new ValidationResult();
                foreach (var diag in diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
                {
                    var code = string.IsNullOrWhiteSpace(diag.Code) ? "EMIT-LOAD" : diag.Code;
                    errorResult.AddError(code, diag.Message, $"{diag.FilePath}:{diag.Line}");
                }
                OutputEmitJson(
                    output,
                    false,
                    2,
                    errorResult,
                    new List<string>(),
                    new List<string>()
                );
            }
            else
            {
                foreach (var diag in diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
                {
                    output.Error($"{diag.FilePath}:{diag.Line}: {diag.Message}");
                }
            }
            return new CommandResult(2, false);
        }

        var doc = loaded.Document;
        var workspaceRoot = loaded.WorkspaceRoot;

        var elCircuits = doc.Circuits.Where(c => c.Level == CascodeLevel.EL).ToList();
        if (elCircuits.Count == 0)
        {
            if (jsonOutput)
            {
                OutputEmitJson(
                    output,
                    false,
                    2,
                    new ValidationResult(),
                    new List<string>(),
                    new List<string>(),
                    "No EL-level circuits found. SPICE emission requires EL-level Cascode."
                );
            }
            else
            {
                output.Error(
                    "No EL-level circuits found. SPICE emission requires EL-level Cascode."
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

                return !deviceKey.Equals("nmos_level1", StringComparison.OrdinalIgnoreCase)
                    && !deviceKey.Equals("pmos_level1", StringComparison.OrdinalIgnoreCase);
            }) == true
        );

        ILoggerFactory? localFactory = null;
        try
        {
            var pdkRoot = _state.PdkRoot ?? _state.WorkspaceRoot;
            if (usesPdkDevices && !jsonOutput)
            {
                var dbPath = WorkspacePaths.GetDatabasePath(pdkRoot);
                var status = File.Exists(dbPath) ? string.Empty : " (no pdk.db; run 'pdk scan')";
                output.Info($"PDK workspace: {pdkRoot}{status}");
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
            var result = CascodeEmitPipeline.ValidateAndEmit(
                doc,
                outputDir,
                backend,
                workspaceRoot,
                pdkRoot,
                loggerFactory.CreateLogger<PdkBenchIncludeResolver>()
            );

            if (!result.Validation.IsValid)
            {
                if (jsonOutput)
                {
                    OutputEmitJson(
                        output,
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
                        output.Error(error.ToString());
                    }
                    output.Error(
                        $"Emission failed: {result.Validation.ErrorCount} error(s) found."
                    );
                }
                return new CommandResult(2, false);
            }

            if (jsonOutput)
            {
                OutputEmitJson(
                    output,
                    true,
                    0,
                    result.Validation,
                    result.Emit.DesignPaths,
                    result.Emit.TestbenchPaths
                );
            }
            else
            {
                EmitRenderer.Render(result, output);
            }

            return CommandResult.Success;
        }
        catch (Exception ex)
        {
            if (jsonOutput)
            {
                OutputEmitJson(
                    output,
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
                output.Error($"SPICE emission failed: {ex.Message}");
            }
            return CommandResult.Failure;
        }
        finally
        {
            localFactory?.Dispose();
        }
    }

    private void OutputEmitJson(
        ICliOutput cliOutput,
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

        var json = new EmitJsonOutput
        {
            Success = success,
            ExitCode = exitCode,
            Validation = JsonSerializer.Deserialize<JsonElement>(validationCopy.ToJson(exitCode)),
            DesignPaths = designPaths,
            TestbenchPaths = testbenchPaths,
        };

        cliOutput.WriteLine(JsonSerializer.Serialize(json, EmitJsonOutput.SerializerOptions));
    }

    private static void ShowUsage(ICliOutput output)
    {
        output.WriteLine(
            "Usage: emit <cascode_file> [--out <dir>] [--backend <ngspice|spectre>] [--json]"
        );
        output.WriteLine("");
        output.WriteLine("Emits SPICE netlists from an Cascode EL document.");
        output.WriteLine("Generates both design subcircuit and testbench files.");
        output.WriteLine("");
        output.WriteLine("Options:");
        output.WriteLine("  --out <dir>      Output directory (default: <input_dir>/build)");
        output.WriteLine(
            "  --backend <type> Simulator backend: ngspice or spectre (default: ngspice)"
        );
        output.WriteLine("  --json           Output results as JSON for machine processing");
    }

    private static (string OutputDir, BenchBackendType Backend, bool JsonOutput) ParseEmitOptions(
        string[] args
    )
    {
        var outputDir = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(args[0])) ?? Directory.GetCurrentDirectory(),
            "build"
        );
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
