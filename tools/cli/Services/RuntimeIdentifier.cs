using System.Runtime.InteropServices;

namespace Cascode.Cli.Services;

/// <summary>
/// Computes the current .NET runtime identifier used by Cascode tooling.
/// </summary>
internal static class RuntimeIdentifier
{
    /// <summary>
    /// Returns the current RID or <c>null</c> when the OS/architecture is unsupported.
    /// </summary>
    public static string? CurrentRid()
    {
        string os;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            os = "win";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            os = "linux";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            os = "osx";
        else
            return null;

        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => null,
        };

        return arch is null ? null : $"{os}-{arch}";
    }
}
