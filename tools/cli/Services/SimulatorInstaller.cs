namespace Cascode.Cli.Services;

/// <summary>
/// Standard result payload returned by simulator installers.
/// </summary>
internal sealed record SimulatorInstallResult(
    bool Success,
    int ExitCode,
    string Message,
    string? InstallPath = null
);

/// <summary>
/// Contract for installing external simulator prerequisites.
/// </summary>
internal interface ISimulatorInstaller
{
    string Name { get; }
    SimulatorInstallResult Install(bool force);
}
