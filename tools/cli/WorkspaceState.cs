using System.Security.Cryptography;
using System.Text;

namespace Cascode.Cli;

internal static class WorkspaceState
{
    private const string RootFolderName = ".cascode";

    public static string GetRoot()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
        {
            home = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }

        if (string.IsNullOrEmpty(home))
        {
            home = Directory.GetCurrentDirectory();
        }

        return Path.Combine(home, RootFolderName);
    }

    public static string GetWorkspaceFolder(string workspaceRoot)
    {
        var hash = ComputeHash(workspaceRoot);
        return Path.Combine(GetRoot(), "workspaces", hash);
    }

    public static string GetScanPath(string workspaceRoot)
        => Path.Combine(GetWorkspaceFolder(workspaceRoot), "workspace-scan.json");

    private static string ComputeHash(string input)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(Path.GetFullPath(input));
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
