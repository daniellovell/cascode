using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Cascode.Cli;

internal static class WorkspaceState
{
    private const string RootFolderName = ".cascode";

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
