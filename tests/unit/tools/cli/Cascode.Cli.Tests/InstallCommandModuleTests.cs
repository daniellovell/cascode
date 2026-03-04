using System;
using System.Collections.Generic;
using System.IO;
using Cascode.Cli;
using Cascode.Cli.Commands;
using Cascode.Cli.Output;
using Cascode.Cli.Services;
using Xunit;

namespace Cascode.Cli.Tests;

public sealed class InstallCommandModuleTests
{
    [Fact]
    public void InstallNgspice_ParsesFromSourceFlag()
    {
        var installer = new FakeInstaller();
        var module = CreateModule(installer);

        var result = Execute(module, "install", "ngspice", "--from-source", "--force");

        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(installer.LastOptions);
        Assert.True(installer.LastOptions!.Force);
        Assert.True(installer.LastOptions.FromSource);
    }

    [Fact]
    public void InstallNgspice_JsonIncludesInstallMode()
    {
        var installer = new FakeInstaller
        {
            NextResult = new SimulatorInstallResult(
                Success: true,
                ExitCode: 0,
                Message: "ok",
                InstallPath: "/tmp/ngspice",
                InstallMode: SimulatorInstallModes.ReleaseBinary
            ),
        };
        var module = CreateModule(installer);

        using var stdout = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(stdout);

        try
        {
            var result = Execute(module, "install", "ngspice", "--json");
            Assert.Equal(0, result.ExitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = stdout.ToString();
        Assert.Contains("\"InstallMode\":\"release-binary\"", output);
    }

    private static InstallCommandModule CreateModule(ISimulatorInstaller installer)
    {
        var state = new ShellState(Path.GetTempPath());
        var output = new CliOutputProvider(state, () => false);
        return new InstallCommandModule(
            output,
            new Dictionary<string, ISimulatorInstaller>(StringComparer.OrdinalIgnoreCase)
            {
                ["ngspice"] = installer,
            }
        );
    }

    private static CommandResult Execute(InstallCommandModule module, params string[] tokens)
    {
        var registry = new CommandRegistry();
        module.Register(registry);
        var resolved = registry.TryResolve(tokens, out var descriptor, out var args, out _);
        Assert.True(resolved);
        Assert.NotNull(descriptor);
        return descriptor!.Handler(args);
    }

    private sealed class FakeInstaller : ISimulatorInstaller
    {
        public string Name => "ngspice";

        public SimulatorInstallOptions? LastOptions { get; private set; }

        public SimulatorInstallResult NextResult { get; set; } =
            new(
                Success: true,
                ExitCode: 0,
                Message: "ok",
                InstallPath: null,
                InstallMode: SimulatorInstallModes.ReleaseBinary
            );

        public SimulatorInstallResult Install(SimulatorInstallOptions options)
        {
            LastOptions = options;
            return NextResult;
        }
    }
}
