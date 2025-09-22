using System;
using System.IO;

namespace Cascode.Workspace;

internal static class PathUtilities
{
    private const string WorkDirToken = "$WORK_DIR";
    private const string WorkDirTokenBraced = "${WORK_DIR}";

    public static string? NormalizeWorkspacePath(string rawPath, string workspaceRoot, string relativeBase)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        var trimmed = TrimQuotes(rawPath.Trim());
        if (trimmed.Length == 0)
        {
            return null;
        }

        var substituted = SubstituteWorkDir(trimmed, workspaceRoot);
        var expanded = Environment.ExpandEnvironmentVariables(substituted);
        expanded = ExpandHome(expanded);

        if (Path.IsPathRooted(expanded))
        {
            return Path.GetFullPath(expanded);
        }

        var baseDir = string.IsNullOrWhiteSpace(relativeBase) ? workspaceRoot : relativeBase;
        return Path.GetFullPath(Path.Combine(baseDir, expanded));
    }

    private static string TrimQuotes(string value)
    {
        var trimmed = value.Trim();

        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[^1] == '"') || (trimmed[0] == '\'' && trimmed[^1] == '\'')))
        {
            return trimmed[1..^1];
        }

        return trimmed.Trim('"', '\'');
    }

    private static string SubstituteWorkDir(string value, string workspaceRoot)
    {
        var result = value;

        if (result.StartsWith(WorkDirTokenBraced, StringComparison.OrdinalIgnoreCase))
        {
            var remainder = result[WorkDirTokenBraced.Length..].TrimStart('/', '\\');
            result = Path.Combine(workspaceRoot, remainder);
        }
        else if (result.StartsWith(WorkDirToken, StringComparison.OrdinalIgnoreCase))
        {
            var remainder = result[WorkDirToken.Length..].TrimStart('/', '\\');
            result = Path.Combine(workspaceRoot, remainder);
        }

        return result;
    }

    private static string ExpandHome(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value[0] != '~')
        {
            return value;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
        {
            return value;
        }

        if (value.Length == 1)
        {
            return home;
        }

        var remainder = value[1..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.Combine(home, remainder);
    }
}
