namespace Cascode.Cli.Services;

internal static class SimulatorInstallModes
{
    public const string ReleaseBinary = "release-binary";
    public const string SourceBuild = "source-build";
}

internal sealed record SimulatorInstallOptions(
    bool Force = false,
    bool FromSource = false,
    Action<string>? Log = null
);

/// <summary>
/// Standard result payload returned by simulator installers.
/// </summary>
internal sealed record SimulatorInstallResult(
    bool Success,
    int ExitCode,
    string Message,
    string? InstallPath = null,
    string InstallMode = SimulatorInstallModes.ReleaseBinary
);

/// <summary>
/// Contract for installing external simulator prerequisites.
/// </summary>
internal interface ISimulatorInstaller
{
    string Name { get; }
    SimulatorInstallResult Install(SimulatorInstallOptions options);
}
