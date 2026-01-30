using System;
using System.IO;
using Cascode.Language;

namespace Cascode.Cli.Commands;

internal sealed class LinkCommandModule : ICommandModule
{
    private readonly ShellState _state;

    public LinkCommandModule(ShellState state)
    {
        _state = state;
    }

    public void Register(CommandRegistry registry)
    {
        registry.Register(
            new DelegateCliCommand("link", "Link Cascode source (resolve includes)", LinkCommand)
        );
    }

    private CommandResult LinkCommand(string[] args)
    {
        if (args.Length == 0)
        {
            _state.AddMessage("Usage: link <cascode_file> [-o|--out <dir>]");
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
                _state.AddMessage($"Error: unknown option '{args[i]}'.");
                return new CommandResult(2, false);
            }
        }

        if (!File.Exists(inputPath))
        {
            _state.AddMessage($"Error: input file '{inputPath}' not found.");
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
                _state.AddMessage(d.Message);
            }
        }

        if (!result.Success || result.LinkedCasPath is null)
        {
            _state.AddMessage("link failed.");
            return new CommandResult(2, false);
        }

        _state.AddMessage($"Linked: {result.LinkedCasPath}");
        if (result.SynthYamlPath is not null)
        {
            _state.AddMessage($"Synth: {result.SynthYamlPath}");
        }

        return CommandResult.Success;
    }
}
