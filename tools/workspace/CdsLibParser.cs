using System.Text;

namespace Cascode.Workspace;

internal sealed class CdsLibParser
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public IReadOnlyList<WorkspaceLibrary> Parse(string rootPath, ICollection<string>? warnings = null)
    {
        var libraries = new List<WorkspaceLibrary>();
        var visited = new HashSet<string>(PathComparer);

        var cdsLibPath = Path.Combine(rootPath, "cds.lib");
        if (!File.Exists(cdsLibPath))
        {
            warnings?.Add($"cds.lib not found under '{rootPath}'.");
            return libraries;
        }

        ParseFile(cdsLibPath, rootPath, libraries, visited, warnings);
        return libraries;
    }

    private static void ParseFile(
        string filePath,
        string workspaceRoot,
        List<WorkspaceLibrary> libraries,
        HashSet<string> visited,
        ICollection<string>? warnings)
    {
        if (!visited.Add(Path.GetFullPath(filePath)))
        {
            return; // prevent include cycles
        }

        if (!File.Exists(filePath))
        {
            warnings?.Add($"cds.lib include '{filePath}' does not exist.");
            return;
        }

        foreach (var rawLine in File.ReadAllLines(filePath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (StartsWithToken(line, "DEFINE"))
            {
                ParseDefine(line, filePath, libraries, warnings);
            }
            else if (StartsWithToken(line, "INCLUDE") || StartsWithToken(line, "SOFTINCLUDE"))
            {
                var includePath = ExtractPath(line, 1, filePath, workspaceRoot);
                if (includePath is null)
                {
                    warnings?.Add($"Unable to parse include in '{filePath}': '{line}'.");
                    continue;
                }

                ParseFile(includePath, workspaceRoot, libraries, visited, warnings);
            }
        }
    }

    private static void ParseDefine(
        string line,
        string filePath,
        ICollection<WorkspaceLibrary> libraries,
        ICollection<string>? warnings)
    {
        var parts = SplitTokens(line);
        if (parts.Length < 3)
        {
            warnings?.Add($"Malformed DEFINE in '{filePath}': '{line}'.");
            return;
        }

        var name = parts[1];
        var libraryPath = NormalizePath(parts[2], Path.GetDirectoryName(filePath));
        libraries.Add(new WorkspaceLibrary(name, libraryPath));
    }

    private static string? ExtractPath(string line, int tokenIndex, string currentFile, string workspaceRoot)
    {
        var parts = SplitTokens(line);
        if (tokenIndex >= parts.Length)
        {
            return null;
        }

        var rawPath = parts[tokenIndex];
        return NormalizePath(rawPath, Path.GetDirectoryName(currentFile) ?? workspaceRoot);
    }

    private static string NormalizePath(string rawPath, string? baseDirectory)
    {
        var trimmed = rawPath.Trim('"', '\'', '`');
        var expanded = Environment.ExpandEnvironmentVariables(trimmed);
        if (string.IsNullOrEmpty(expanded))
        {
            return trimmed;
        }

        if (Path.IsPathRooted(expanded))
        {
            return Path.GetFullPath(expanded);
        }

        var root = string.IsNullOrEmpty(baseDirectory) ? Directory.GetCurrentDirectory() : baseDirectory;
        return Path.GetFullPath(Path.Combine(root, expanded));
    }

    private static bool StartsWithToken(string line, string token)
        => line.StartsWith(token, StringComparison.OrdinalIgnoreCase);

    private static string[] SplitTokens(string line)
        => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
