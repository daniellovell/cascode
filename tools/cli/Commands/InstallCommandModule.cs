using System;
using System.Collections.Generic;
using System.Text.Json;
using Cascode.Cli.Output;
using Cascode.Cli.Services;

namespace Cascode.Cli.Commands;

/// <summary>
/// Installs external simulator dependencies required by bench execution.
/// </summary>
internal sealed class InstallCommandModule : ICommandModule
{
    private readonly Func<ICliOutput> _outputFactory;
    private readonly IReadOnlyDictionary<string, ISimulatorInstaller> _installers;

    public InstallCommandModule(CliOutputProvider output)
        : this(output.Get, null) { }

    internal InstallCommandModule(
        Func<ICliOutput> outputFactory,
        IReadOnlyDictionary<string, ISimulatorInstaller>? installers
    )
    {
        _outputFactory = outputFactory ?? throw new ArgumentNullException(nameof(outputFactory));
        _installers =
            installers
            ?? new Dictionary<string, ISimulatorInstaller>(StringComparer.OrdinalIgnoreCase)
            {
                ["ngspice"] = new NgspiceInstaller(),
            };
    }

    public void Register(CommandRegistry registry)
    {
        registry.Register(
            new DelegateCliCommand("install", "Install simulator prerequisites", ShowUsage)
        );
        registry.Register(
            new DelegateCliCommand(
                "install ngspice",
                $"Install ngspice {NgspiceInstallLayout.Version} under CASCODE_HOME",
                InstallNgspice
            )
        );
    }

    private CommandResult ShowUsage(string[] args)
    {
        var output = _outputFactory();
        output.WriteLine("Usage: install <tool> [--from-source] [--force] [--json]");
        output.WriteLine("");
        output.WriteLine("Tools:");
        output.WriteLine(
            $"  ngspice    Install ngspice {NgspiceInstallLayout.Version} to CASCODE_HOME."
        );
        return CommandResult.Success;
    }

    private CommandResult InstallNgspice(string[] args)
    {
        var output = _outputFactory();
        var force = false;
        var fromSource = false;
        var json = false;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--force", StringComparison.OrdinalIgnoreCase))
            {
                force = true;
                continue;
            }

            if (args[i].Equals("--json", StringComparison.OrdinalIgnoreCase))
            {
                json = true;
                continue;
            }

            if (args[i].Equals("--from-source", StringComparison.OrdinalIgnoreCase))
            {
                fromSource = true;
                continue;
            }

            output.Error($"Unknown option '{args[i]}'.");
            output.WriteLine("Usage: install ngspice [--from-source] [--force] [--json]");
            return CommandResult.Failure;
        }

        var installer = _installers["ngspice"];
        Action<string>? log = json ? null : output.WriteErrorLine;
        var result = installer.Install(new SimulatorInstallOptions(force, fromSource, log));
        if (json)
        {
            output.WriteLine(
                JsonSerializer.Serialize(
                    new InstallJson(
                        result.Success,
                        result.ExitCode,
                        result.Message,
                        result.InstallPath,
                        result.InstallMode
                    )
                )
            );
        }
        else
        {
            if (result.Success)
                output.Success(result.Message);
            else
                output.Error(result.Message);
        }

        return result.ExitCode == 0
            ? CommandResult.Success
            : new CommandResult(result.ExitCode, false);
    }

    private sealed record InstallJson(
        bool Success,
        int ExitCode,
        string Message,
        string? InstallPath,
        string InstallMode
    );
}
