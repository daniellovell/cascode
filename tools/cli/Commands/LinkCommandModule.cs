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
            new DelegateCliCommand(
                "link",
                "Link Cascode source (resolve includes)",
                LinkCommand,
                helpCategory: CommandHelpCategory.Design
            )
        );
    }

    private CommandResult LinkCommand(string[] args)
    {
        var output = _output.Get();
        if (args.Length == 0 || (args.Length == 1 && (args[0] == "-h" || args[0] == "--help")))
        {
            ShowUsage(output);
            return CommandResult.Success;
        }

        var inputPath = args[0];
        string? outDir = null;
        var linkBenchMode = LinkBenchMode.Full;
        var includePolicy = LinkIncludePolicy.Default;

        for (var i = 1; i < args.Length; i++)
        {
            if ((args[i] == "-o" || args[i] == "--out") && i + 1 < args.Length)
            {
                outDir = args[++i];
                continue;
            }

            if (args[i].Equals("--no-link-benches", StringComparison.OrdinalIgnoreCase))
            {
                linkBenchMode = LinkBenchMode.None;
                continue;
            }

            if (TryReadOptionValue(args, ref i, "--include-policy", out var includePolicyRaw))
            {
                if (!TryParseIncludePolicy(includePolicyRaw, out includePolicy))
                {
                    output.Error(
                        $"Error: invalid --include-policy value '{includePolicyRaw}'. Expected: default or explicit-only."
                    );
                    return new CommandResult(2, false);
                }
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

        var searchRoots = Cascode.Cli.Services.BenchRunHelpers.BuildSearchRoots(workspaceRoot);
        var options = new CascodeLinkOptions(linkBenchMode, includePolicy);
        var result = CascodeLinker.LinkFile(inputPath, outputDir, searchRoots, options, logger);
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

    private static void ShowUsage(ICliOutput output)
    {
        output.WriteLine(
            "Usage: link <cascode_file> [-o|--out <dir>] [--no-link-benches] [--include-policy <default|explicit-only>]"
        );
        output.WriteLine(string.Empty);
        output.WriteLine(
            "  --no-link-benches             Preserve bench bindings but omit linked bench definitions."
        );
        output.WriteLine(
            "  --include-policy explicit-only Restrict available symbols to explicit include closure."
        );
    }

    private static bool TryReadOptionValue(
        string[] args,
        ref int index,
        string optionName,
        out string value
    )
    {
        value = string.Empty;
        if (!args[index].StartsWith(optionName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (args[index].Length > optionName.Length && args[index][optionName.Length] == '=')
        {
            if (args[index].Length == optionName.Length + 1)
            {
                value = string.Empty;
                return true;
            }

            value = args[index][(optionName.Length + 1)..];
            return true;
        }

        if (
            args[index].Equals(optionName, StringComparison.OrdinalIgnoreCase)
            && index + 1 < args.Length
        )
        {
            value = args[++index];
            return true;
        }

        return false;
    }

    private static bool TryParseIncludePolicy(string raw, out LinkIncludePolicy policy)
    {
        policy = LinkIncludePolicy.Default;
        if (raw.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            policy = LinkIncludePolicy.Default;
            return true;
        }

        if (raw.Equals("explicit-only", StringComparison.OrdinalIgnoreCase))
        {
            policy = LinkIncludePolicy.ExplicitOnly;
            return true;
        }

        return false;
    }
}
