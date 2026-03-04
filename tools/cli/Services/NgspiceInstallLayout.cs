using System.IO;

namespace Cascode.Cli.Services;

/// <summary>
/// Defines the install directory layout for managed ngspice binaries.
/// </summary>
internal static class NgspiceInstallLayout
{
    public const string Version = "45.2";

    /// <summary>
    /// Returns the target bin directory under CASCODE_HOME for a RID.
    /// </summary>
    public static string GetBinDirectory(string cascodeHome, string rid)
    {
        return Path.Combine(cascodeHome, "tools", "ngspice", Version, rid, "bin");
    }

    /// <summary>
    /// Returns the expected ngspice executable path under CASCODE_HOME for a RID.
    /// </summary>
    public static string GetExecutablePath(string cascodeHome, string rid)
    {
        var executable = OperatingSystem.IsWindows() ? "ngspice.exe" : "ngspice";
        return Path.Combine(GetBinDirectory(cascodeHome, rid), executable);
    }
}
