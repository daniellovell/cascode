using System;
using System.Runtime.InteropServices;

namespace Cascode.Cli.Services;

/// <summary>
/// Computes the current .NET runtime identifier used by Cascode tooling.
/// </summary>
internal static class RuntimeIdentifier
{
    private const string RidOverrideEnvironmentVariable = "CASCODE_RUNTIME_RID";
    private static readonly string[] SupportedRids =
    [
        "win-x64",
        "win-arm64",
        "linux-x64",
        "linux-arm64",
        "osx-x64",
        "osx-arm64",
    ];

    /// <summary>
    /// Returns the current RID or <c>null</c> when the OS/architecture is unsupported.
    /// </summary>
    public static string? CurrentRid() =>
        ResolveCurrentRid(Environment.GetEnvironmentVariable(RidOverrideEnvironmentVariable));

    internal static string? ResolveCurrentRid(string? overriddenRid)
    {
        if (overriddenRid is not null)
        {
            if (IsSupportedRid(overriddenRid.Trim(), out var normalizedOverride))
            {
                return normalizedOverride;
            }

            throw new InvalidOperationException(
                $"Invalid {RidOverrideEnvironmentVariable} value '{overriddenRid}'. "
                    + $"Supported values: {string.Join(", ", SupportedRids)}."
            );
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
        return SupportedRids.Contains(normalized, StringComparer.Ordinal);
    }
}
