using System;
using System.Runtime.InteropServices;

namespace Cascode.Cli.Services;

/// <summary>
/// Computes the current .NET runtime identifier used by Cascode tooling.
/// </summary>
internal static class RuntimeIdentifier
{
    private const string RidOverrideEnvironmentVariable = "CASCODE_RUNTIME_RID";

    /// <summary>
    /// Returns the current RID or <c>null</c> when the OS/architecture is unsupported.
    /// </summary>
    public static string? CurrentRid()
    {
        var overriddenRid = Environment.GetEnvironmentVariable(RidOverrideEnvironmentVariable);
        if (
            overriddenRid is not null
            && IsSupportedRid(overriddenRid.Trim(), out var normalizedOverride)
        )
        {
            return normalizedOverride;
        }

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

    private static bool IsSupportedRid(string value, out string normalized)
    {
        normalized = value.ToLowerInvariant();
        return normalized
            is "win-x64"
                or "win-arm64"
                or "linux-x64"
                or "linux-arm64"
                or "osx-x64"
                or "osx-arm64";
    }
}
