using System;
using System.IO;
using Cascode.ACIR;
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
        registry.Register(new DelegateCliCommand("build", "Compile Cascode to ACIR", BuildCommand));
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
        // TODO: We need to intelligently detect which level of ACIR should be compiled to.
        var options = new CompileOptions(string.Empty, ACIRLevel.ML);
        var result = compiler.CompileToACIR(new[] { new SourceUnit(inputPath, text) }, options);

        foreach (var diag in result.Diagnostics)
        {
            var severity = diag.Severity.ToString().ToLowerInvariant();
            var path = string.IsNullOrEmpty(diag.FilePath) ? inputPath : diag.FilePath;
            _state.AddMessage($"{path}:{diag.Line}:{diag.Column}: {severity}: {diag.Message}");
        }

        if (result.ACIR is null)
        {
            _state.AddMessage("Build failed; no ACIR produced.");
            return CommandResult.Failure;
        }

        var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "build");
        Directory.CreateDirectory(outputDir);
        var baseName = Path.GetFileNameWithoutExtension(inputPath);
        var levelStr = options.Level.ToString().ToLowerInvariant();
        var outputPath = Path.Combine(outputDir, $"{baseName}.{levelStr}.cir");

        try
        {
            using var writer = File.CreateText(outputPath);
            ACIRWriter.Write(result.ACIR, writer);
        }
        catch (Exception ex)
        {
            _state.AddMessage($"Failed to write ACIR to '{outputPath}': {ex.Message}");
            return CommandResult.Failure;
        }

        _state.AddMessage($"ACIR written to '{outputPath}'.");
        return CommandResult.Success;
    }
}
