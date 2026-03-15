using System;
using System.IO;
using Cascode.Cli.Output;
using Cascode.Cli.Services;
using Cascode.Workspace;

namespace Cascode.Cli.Commands;

internal sealed class PdkEmitCommandHandlersImpl
    : PdkCommandHandlersSupport,
        IPdkEmitCommandHandlers
{
    public PdkEmitCommandHandlersImpl(
        ShellState state,
        Func<bool> isInteractive,
        CliOutputProvider outputProvider
    )
        : base(state, isInteractive, outputProvider) { }

    public CommandResult ShowPdkEmitUsage(string[] args)
    {
        Output.WriteLine("Usage: pdk emit <subcommand>");
        Output.WriteLine(
            "  pdk emit primitives  Generate lib/pdk/<pdk>/{devices,resistors,capacitors,diodes}.cas"
        );
        return CommandResult.Success;
    }

    public CommandResult PdkEmitPrimitivesCommand(string[] args)
    {
        string? pdkName = null;
        string? outDirectory = null;
        var includeFixed = false;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--pdk", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                pdkName = args[++i];
            }
            else if (
                args[i].Equals("--out", StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length
            )
            {
                outDirectory = args[++i];
            }
            else if (args[i].Equals("--include-fixed", StringComparison.OrdinalIgnoreCase))
            {
                includeFixed = true;
            }
            else if (args[i].Equals("--help", StringComparison.OrdinalIgnoreCase))
            {
                Output.WriteLine(
                    "Usage: pdk emit primitives [--pdk <name>] [--out <dir>] [--include-fixed]\n\nDefaults:\n  --pdk            (derived from current PDK root directory name)\n  --out            lib/pdk/<pdk>\n  --include-fixed  disabled (emit only parametric primitive families)"
                );
                return CommandResult.Success;
            }
            else
            {
                Output.WriteLine($"Unknown option: {args[i]}");
                return CommandResult.Failure;
            }
        }

        pdkName ??= Path.GetFileName(Path.GetFullPath(_state.PdkRoot ?? _state.WorkspaceRoot));
        if (string.IsNullOrWhiteSpace(pdkName))
        {
            Output.Error("Unable to determine PDK name. Provide --pdk <name>.");
            return CommandResult.Failure;
        }

        outDirectory ??= PdkPrimitiveLibraryLayout.GetDefaultOutputDirectory(pdkName);

        var dbPath = Path.Combine(
            WorkspaceState.GetWorkspaceFolder(_state.WorkspaceRoot),
            "pdk.db"
        );
        var result = PdkEmitPrimitivesService.Emit(
            new PdkEmitPrimitivesService.EmitArgs(
                PdkName: pdkName,
                DbPath: dbPath,
                OutputDirectory: outDirectory,
                IncludeFixed: includeFixed
            )
        );

        if (!result.Succeeded)
        {
            Output.Error(result.Message);
            return CommandResult.Failure;
        }

        Output.WriteLine(result.Message);
        return CommandResult.Success;
    }
}
