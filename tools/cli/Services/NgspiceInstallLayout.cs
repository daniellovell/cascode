using System;
using System.IO;
using System.Reflection;

namespace Cascode.Cli.Services;

/// <summary>
/// Defines the install directory layout for managed ngspice binaries.
/// </summary>
internal static class NgspiceInstallLayout
{
    public static string Version { get; } = ReadVersion();

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

    private static string ReadVersion()
    {
        foreach (
            var metadata in typeof(NgspiceInstallLayout).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        )
        {
            if (
                string.Equals(metadata.Key, "NgspiceVersion", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(metadata.Value)
            )
            {
                return metadata.Value;
            }
        }

        throw new InvalidOperationException("Missing required assembly metadata: NgspiceVersion.");
    }
}
