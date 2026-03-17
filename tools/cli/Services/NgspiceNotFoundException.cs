using System;

namespace Cascode.Cli.Services;

internal sealed class NgspiceNotFoundException : InvalidOperationException
{
    public NgspiceNotFoundException(string message)
        : base(message) { }

    private static string InstallInstructions { get; } =
        "Run: cascode install ngspice\nThen rerun your command.";

    internal static NgspiceNotFoundException NotFound() =>
        new($"ngspice was not found in CASCODE_HOME tools or PATH.\n\n{InstallInstructions}");

    internal static NgspiceNotFoundException WrongVersion(int foundMajor) =>
        new(
            $"ngspice {foundMajor} found, but Cascode requires ngspice {NgspiceLocator.RequiredMajor}.\n\n{InstallInstructions}"
        );

    internal static NgspiceNotFoundException Unparseable() =>
        new(
            $"Could not determine ngspice version. Cascode requires ngspice {NgspiceLocator.RequiredMajor}.\n\n{InstallInstructions}"
        );

    internal static NgspiceNotFoundException PssUnsupported(string path, string probeOutput) =>
        new(
            $"ngspice at '{path}' does not support PSS, but this command requires a PSS-capable build."
                + $"\n\nProbe output: {probeOutput}\n\n{InstallInstructions}"
        );
}
