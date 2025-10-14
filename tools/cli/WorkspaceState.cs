using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Cascode.Cli;

internal static class WorkspaceState
{
    private const string RootFolderName = ".cascode";

    /// <summary>
    /// Determine the filesystem root directory used to store Cascode state.
    /// </summary>
    /// <remarks>
    /// If the environment variable CASCODE_HOME is set and not empty, its value is treated as the complete Cascode root path and returned without modification.
    /// When CASCODE_HOME is absent or empty, the method selects a base path by checking the OS user profile, then the application data folder, and finally the current working directory if needed, and combines that base path with the RootFolderName (".cascode").
    /// </remarks>
    /// <returns>The full path to the Cascode root directory.</returns>
    public static string GetRoot()
    {
        // Prefer explicit CASCODE_HOME override to avoid ambiguity with HOME.
        var cascodeHome = Environment.GetEnvironmentVariable("CASCODE_HOME");
        if (!string.IsNullOrWhiteSpace(cascodeHome))
        {
            return cascodeHome!;
        }

        // Fallback to OS user profile (never read HOME directly).
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(userProfile))
        {
            userProfile = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }

        if (string.IsNullOrEmpty(userProfile))
        {
            userProfile = Directory.GetCurrentDirectory();
        }

        return Path.Combine(userProfile, RootFolderName);
    }

    /// <summary>
    /// Compute the per-workspace directory path used to store workspace-specific Cascode state.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root path used to identify and locate the workspace state.</param>
    /// <returns>The full filesystem path to the workspace's Cascode folder under the global root.</returns>
    public static string GetWorkspaceFolder(string workspaceRoot)
    {
        var hash = ComputeHash(workspaceRoot);
        return Path.Combine(GetRoot(), "workspaces", hash);
    }

    // JSON scan cache removed; database is authoritative.

    public static string GetCharacterizationFolder(string workspaceRoot)
        => Path.Combine(GetWorkspaceFolder(workspaceRoot), "char");

    public static string GetConfigPath()
        => Path.Combine(GetRoot(), "config.json");

    public static string GetCharConfigPath(string workspaceRoot)
        => Path.Combine(GetWorkspaceFolder(workspaceRoot), "pdk-char-config.json");

    private static string ComputeHash(string input)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(Path.GetFullPath(input));
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
