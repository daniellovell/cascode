using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Cascode.Workspace;

/// <summary>
/// Utilities for computing workspace-specific paths under CASCODE_HOME.
/// </summary>
public static class WorkspacePaths
{
    /// <summary>
    /// Gets the CASCODE_HOME directory, defaulting to ~/.cascode if not set.
    /// </summary>
    public static string GetCascodeHome()
    {
        var env = Environment.GetEnvironmentVariable("CASCODE_HOME");
        if (!string.IsNullOrWhiteSpace(env))
            return env;
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cascode"
        );
    }

    /// <summary>
    /// Gets the workspace-specific folder for a given PDK root directory.
    /// The folder is determined by hashing the absolute path of the workspace root.
    /// </summary>
    public static string GetWorkspaceFolder(string workspaceRoot)
    {
        ValidateWorkspaceRoot(workspaceRoot);
        var hash = ComputeHash(workspaceRoot);
        return Path.Combine(GetCascodeHome(), "workspaces", hash);
    }

    /// <summary>
    /// Gets the path to the PDK database for a given workspace root.
    /// </summary>
    public static string GetDatabasePath(string workspaceRoot)
    {
        ValidateWorkspaceRoot(workspaceRoot);
        return Path.Combine(GetWorkspaceFolder(workspaceRoot), "pdk.db");
    }

    /// <summary>
    /// Gets the characterization output folder for a given workspace root.
    /// </summary>
    public static string GetCharacterizationFolder(string workspaceRoot)
    {
        ValidateWorkspaceRoot(workspaceRoot);
        return Path.Combine(GetWorkspaceFolder(workspaceRoot), "char");
    }

    private static void ValidateWorkspaceRoot(string workspaceRoot)
    {
        if (workspaceRoot == null)
            throw new ArgumentNullException(nameof(workspaceRoot), "workspaceRoot cannot be null");
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            throw new ArgumentException(
                "workspaceRoot cannot be empty or whitespace",
                nameof(workspaceRoot)
            );
    }

    private static string ComputeHash(string input)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(Path.GetFullPath(input));
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
