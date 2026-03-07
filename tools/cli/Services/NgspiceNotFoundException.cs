using System;

namespace Cascode.Cli.Services;

internal sealed class NgspiceNotFoundException : InvalidOperationException
{
    public NgspiceNotFoundException(string message)
        : base(message) { }

    internal static string InstallInstructions { get; } =
        "Install ngspice 45:\n"
        + "  macOS:   brew install ngspice\n"
        + "  Linux:   build from source — https://sourceforge.net/projects/ngspice/files/ng-spice-rework/45.2/\n"
        + "  Windows: https://sourceforge.net/projects/ngspice/files/ng-spice-rework/45.2/";

    internal static NgspiceNotFoundException NotFound() =>
        new($"ngspice is not installed or not on PATH.\n\n{InstallInstructions}");

    internal static NgspiceNotFoundException WrongVersion(int foundMajor) =>
        new(
            $"ngspice {foundMajor} found, but Cascode requires ngspice {NgspiceLocator.RequiredMajor}.\n\n{InstallInstructions}"
        );

    internal static NgspiceNotFoundException Unparseable() =>
        new(
            $"Could not determine ngspice version. Cascode requires ngspice {NgspiceLocator.RequiredMajor}.\nRun 'ngspice --version' to verify your installation."
        );
}
