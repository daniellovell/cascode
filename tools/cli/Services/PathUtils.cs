using System;
using System.IO;

namespace Cascode.Cli.Services;

internal static class PathUtils
{
    internal static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path must not be empty", nameof(path));
        }

        var expanded = ExpandHomePath(path);
        return Path.GetFullPath(expanded);
    }

    internal static string ExpandHomePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('~'))
        {
            return path;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
        {
            return path;
        }

        if (path.Length == 1)
        {
            return home;
        }

        var remainder = path[1..]
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.Combine(home, remainder);
    }
}
