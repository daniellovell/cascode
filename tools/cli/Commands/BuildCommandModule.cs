using System;
using System.IO;
using System.Text.Json;
using Cascode.Casir;
using Cascode.Compiler;

namespace Cascode.Cli.Commands;

internal sealed class BuildCommandModule : ICommandModule
{
    private readonly ShellState _state;

    public BuildCommandModule(ShellState state)
    {
        _state = state;
    }

    public void Register(CommandRegistry registry)
    {
        registry.Register(new DelegateCliCommand("build", "Compile ADL to CasIR", BuildCommand));
    }

    private CommandResult BuildCommand(string[] args)
    {
        if (args.Length == 0)
        {
            _state.AddMessage("Usage: build <file.cas>");
            return CommandResult.Success;
        }
        if (!File.Exists(args[0]))
        {
            _state.AddMessage($"Input file '{args[0]}' not found.");
            return CommandResult.Failure;
        }

        var inputPath = Path.GetFullPath(args[0]);
        string text;
        try
        {
            text = File.ReadAllText(inputPath);
        }
        catch (Exception ex)
        {
            _state.AddMessage($"Failed to read '{inputPath}': {ex.Message}");
            return CommandResult.Failure;
        }

        var compiler = new SimpleCascodeCompiler();
        // TODO: We need to intelligently detect which level of CasIR should be compiled to.
        var options = new CompileOptions(string.Empty, CasirLevel.ML);
        var result = compiler.CompileToCasir(
            new[] { new SourceUnit(inputPath, text) },
            options);

        foreach (var diag in result.Diagnostics)
        {
            var severity = diag.Severity.ToString().ToLowerInvariant();
            _state.AddMessage($"{inputPath}:{diag.Line}:{diag.Column}: {severity}: {diag.Message}");
        }

        if (result.Casir is null)
        {
            _state.AddMessage("Build failed; no CasIR produced.");
            return CommandResult.Failure;
        }

        var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "build");
        Directory.CreateDirectory(outputDir);
        var baseName = Path.GetFileNameWithoutExtension(inputPath);
        var outputPath = Path.Combine(outputDir, baseName + ".cir");

        try
        {
            var json = JsonSerializer.Serialize(result.Casir, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(outputPath, json);
        }
        catch (Exception ex)
        {
            _state.AddMessage($"Failed to write CasIR to '{outputPath}': {ex.Message}");
            return CommandResult.Failure;
        }

        _state.AddMessage($"CasIR written to '{outputPath}'.");
        return CommandResult.Success;
    }
}

