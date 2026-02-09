using System;
using System.IO;
using Cascode.Cli.Output;
using Cascode.Language;

namespace Cascode.Cli.Commands;

internal sealed class LinkCommandModule : ICommandModule
{
    private readonly ShellState _state;
    private readonly CliOutputProvider _output;

    public LinkCommandModule(ShellState state, CliOutputProvider output)
    {
        _state = state;
        _output = output;
    }

    public void Register(CommandRegistry registry)
    {
        registry.Register(
            new DelegateCliCommand("link", "Link Cascode source (resolve includes)", LinkCommand)
        );
    }

    private CommandResult LinkCommand(string[] args)
    {
        var output = _output.Get();
        if (args.Length == 0)
        {
            output.WriteLine("Usage: link <cascode_file> [-o|--out <dir>]");
            return CommandResult.Success;
        }

        var inputPath = args[0];
        string? outDir = null;

        for (var i = 1; i < args.Length; i++)
        {
            if ((args[i] == "-o" || args[i] == "--out") && i + 1 < args.Length)
            {
                outDir = args[++i];
                continue;
            }

            if (args[i].StartsWith('-'))
            {
                output.Error($"Error: unknown option '{args[i]}'.");
                return new CommandResult(2, false);
            }
        }

        if (!File.Exists(inputPath))
        {
            output.Error($"Error: input file '{inputPath}' not found.");
            return new CommandResult(2, false);
        }

        var outputDir = Path.GetFullPath(
            outDir ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(inputPath))!, "build")
        );
        var workspaceRoot =
            Cascode.Cli.Services.BenchRunHelpers.FindWorkspaceRoot(inputPath)
            ?? _state.WorkspaceRoot;
        var logger = _state.LoggerFactory?.CreateLogger("CascodeLinker");

        var result = CascodeLinker.LinkFile(inputPath, outputDir, workspaceRoot, logger);
        foreach (var d in result.Diagnostics)
        {
            if (d.Severity == DiagnosticSeverity.Error)
            {
                output.Error(d.Message);
            }
        }

        if (!result.Success || result.LinkedCasPath is null)
        {
            output.Error("link failed.");
            return new CommandResult(2, false);
        }

        output.Success($"Linked: {result.LinkedCasPath}");
        if (result.SynthYamlPath is not null)
        {
            output.WriteLine($"Synth: {result.SynthYamlPath}");
        }

        return CommandResult.Success;
    }
}
