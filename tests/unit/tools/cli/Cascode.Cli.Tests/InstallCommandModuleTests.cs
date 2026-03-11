using System;
using System.Collections.Generic;
using Cascode.Cli.Commands;
using Cascode.Cli.Output;
using Cascode.Cli.Services;
using Spectre.Console;
using Xunit;

namespace Cascode.Cli.Tests;

public sealed class InstallCommandModuleTests
{
    [Fact]
    public void InstallNgspice_ParsesFromSourceFlag()
    {
        var installer = new FakeInstaller();
        var output = new CaptureCliOutput();
        var module = CreateModule(installer, output);

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
        var output = new CaptureCliOutput();
        var module = CreateModule(installer, output);
        var result = Execute(module, "install", "ngspice", "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(output.Lines, line => line.Contains("\"InstallMode\":\"release-binary\""));
    }

    [Fact]
    public void InstallNgspice_StreamsInstallerLogs_WhenNotJson()
    {
        var installer = new FakeInstaller
        {
            EmittedLogs = new[] { "configure: checking...", "make: all" },
        };
        var output = new CaptureCliOutput();
        var module = CreateModule(installer, output);

        var result = Execute(module, "install", "ngspice", "--from-source");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("configure: checking...", output.Lines);
        Assert.Contains("make: all", output.Lines);
        Assert.Contains("ok", output.Lines);
    }

    [Fact]
    public void InstallNgspice_DoesNotStreamInstallerLogs_InJsonMode()
    {
        var installer = new FakeInstaller { EmittedLogs = new[] { "configure: checking..." } };
        var output = new CaptureCliOutput();
        var module = CreateModule(installer, output);

        var result = Execute(module, "install", "ngspice", "--from-source", "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("configure: checking...", output.Lines);
        Assert.Contains(output.Lines, line => line.Contains("\"Message\":\"ok\""));
    }

    private static InstallCommandModule CreateModule(
        ISimulatorInstaller installer,
        CaptureCliOutput output
    )
    {
        return new InstallCommandModule(
            () => output,
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

        public IReadOnlyList<string> EmittedLogs { get; init; } = Array.Empty<string>();

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
            foreach (var line in EmittedLogs)
            {
                options.Log?.Invoke(line);
            }
            return NextResult;
        }
    }

    private sealed class CaptureCliOutput : ICliOutput
    {
        public CliOutputMode Mode => CliOutputMode.Plain;
        public IAnsiConsole? Out => null;
        public IAnsiConsole? Err => null;
        public List<string> Lines { get; } = new();

        public void WriteLine(string text) => Lines.Add(text);

        public void WriteErrorLine(string text) => Lines.Add(text);

        public void Info(string text) => Lines.Add(text);

        public void Success(string text) => Lines.Add(text);

        public void Warning(string text) => Lines.Add(text);

        public void Error(string text) => Lines.Add(text);

        public T RunWithProgress<T>(string initialStatus, Func<Action<string>, T> run) =>
            run(_ => { });

        public T RunWithMultiTaskProgress<T>(Func<IBenchProgressContext, T> run) =>
            throw new NotSupportedException();
    }
}
