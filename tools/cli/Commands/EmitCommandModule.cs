using System;
using System.IO;
using Cascode.ACIR;
using Cascode.Bench;

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
        registry.Register(new DelegateCliCommand("emit", "Emit SPICE netlist from ACIR EL", EmitCommand));
    }

    /// <summary>
    /// Executes the emit command to generate SPICE netlists.
    /// </summary>
    /// <param name="args">Command arguments: [acir_file] [--out output_dir] [--backend ngspice|spectre].</param>
    /// <returns>Command result indicating success or failure.</returns>
    private CommandResult EmitCommand(string[] args)
    {
        if (args.Length == 0)
        {
            ShowUsage();
            return CommandResult.Success;
        }

        var inputPath = args[0];
        if (!File.Exists(inputPath))
        {
            _state.AddMessage($"Input file '{inputPath}' not found.");
            return CommandResult.Failure;
        }

        inputPath = Path.GetFullPath(inputPath);

        var (outputDir, backend) = ParseEmitOptions(args);

        var doc = TryReadAcirDocument(inputPath);
        if (doc == null)
        {
            return CommandResult.Failure;
        }

        var elCircuits = doc.Circuits.Where(c => c.Level == ACIRLevel.EL).ToList();
        if (elCircuits.Count == 0)
        {
            _state.AddMessage("No EL-level circuits found. SPICE emission requires EL-level ACIR.");
            return CommandResult.Failure;
        }

        try
        {
            var workspaceRoot = FindWorkspaceRoot(inputPath) ?? Directory.GetCurrentDirectory();
            var result = SpiceEmitter.Emit(doc, outputDir, backend, workspaceRoot);

            foreach (var path in result.DesignPaths)
            {
                _state.AddMessage($"Design netlist: {path}");
            }
            foreach (var path in result.TestbenchPaths)
            {
                _state.AddMessage($"Testbench: {path}");
            }

            _state.AddMessage($"Emitted {result.DesignPaths.Count} design(s) and {result.TestbenchPaths.Count} testbench(es).");
            return CommandResult.Success;
        }
        catch (Exception ex)
        {
            _state.AddMessage($"SPICE emission failed: {ex.Message}");
            return CommandResult.Failure;
        }
    }

    private void ShowUsage()
    {
        _state.AddMessage("Usage: emit <acir_file> [--out <dir>] [--backend <ngspice|spectre>]");
        _state.AddMessage("");
        _state.AddMessage("Emits SPICE netlists from an ACIR EL document.");
        _state.AddMessage("Generates both design subcircuit and testbench files.");
        _state.AddMessage("Default backend is ngspice.");
    }

    private static (string OutputDir, BenchBackendType Backend) ParseEmitOptions(string[] args)
    {
        var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "build");
        var backend = BenchBackendType.Ngspice;

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
                    _ => throw new InvalidOperationException($"Unknown backend: {args[i + 1]}. Use 'ngspice' or 'spectre'.")
                };
                i++;
            }
        }

        return (outputDir, backend);
    }

    private ACIRDocument? TryReadAcirDocument(string inputPath)
    {
        try
        {
            using var reader = File.OpenText(inputPath);
            return ACIRReader.Read(reader);
        }
        catch (Exception ex)
        {
            _state.AddMessage($"Failed to read ACIR file: {ex.Message}");
            return null;
        }
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
