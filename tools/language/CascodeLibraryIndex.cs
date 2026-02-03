using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Cascode.Language;

/// <summary>
/// Lightweight index mapping file-level <c>library ...</c> declarations to source paths.
/// </summary>
/// <remarks>
/// The linker uses this to resolve <c>include</c> directives by library namespace without
/// fully parsing every candidate file (which may include legacy syntax in unrelated sources).
/// </remarks>
internal sealed class CascodeLibraryIndex
{
    private readonly Dictionary<string, List<string>> _pathsByLibrary = new(
        StringComparer.OrdinalIgnoreCase
    );

    private CascodeLibraryIndex() { }

    public static CascodeLibraryIndex Build(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        workspaceRoot = Path.GetFullPath(workspaceRoot);

        var index = new CascodeLibraryIndex();

        foreach (var path in EnumerateCascodeSources(workspaceRoot))
        {
            var lib = TryReadFileLibraryHeader(path);
            if (string.IsNullOrWhiteSpace(lib))
            {
                continue;
            }

            if (!index._pathsByLibrary.TryGetValue(lib, out var list))
            {
                list = new List<string>();
                index._pathsByLibrary[lib] = list;
            }
            list.Add(path);
        }

        foreach (var list in index._pathsByLibrary.Values)
        {
            list.Sort(StringComparer.OrdinalIgnoreCase);
        }

        return index;
    }

    public IReadOnlyList<string> FindByPrefix(string libraryPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPrefix);

        var prefix = NormalizeLibraryName(libraryPrefix);
        var boundary = prefix + ".";

        var matches = new List<string>();
        foreach (var (lib, paths) in _pathsByLibrary)
        {
            if (
                lib.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || lib.StartsWith(boundary, StringComparison.OrdinalIgnoreCase)
            )
            {
                matches.AddRange(paths);
            }
        }

        matches.Sort(StringComparer.OrdinalIgnoreCase);
        return matches;
    }

    public IReadOnlyList<string> FindExact(string libraryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryName);

        var key = NormalizeLibraryName(libraryName);
        return _pathsByLibrary.TryGetValue(key, out var list) ? list : Array.Empty<string>();
    }

    public static string NormalizeLibraryName(string raw)
    {
        raw = raw.Trim();
        // Accept either '.' or '_' as a namespace separator in tool inputs.
        return raw.Replace('_', '.');
    }

    private static IEnumerable<string> EnumerateCascodeSources(string workspaceRoot)
    {
        // Avoid indexing build artifacts and VCS directories.
        static bool ShouldSkipDir(string name)
        {
            return name.Equals("build", StringComparison.OrdinalIgnoreCase)
                || name.Equals(".git", StringComparison.OrdinalIgnoreCase)
                || name.Equals(".it", StringComparison.OrdinalIgnoreCase);
        }

        var stack = new Stack<string>();
        stack.Push(workspaceRoot);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();

            IEnumerable<string> subdirs;
            try
            {
                subdirs = Directory.EnumerateDirectories(dir);
            }
            catch
            {
                continue;
            }

            foreach (var d in subdirs)
            {
                var name = Path.GetFileName(d);
                if (string.IsNullOrWhiteSpace(name) || ShouldSkipDir(name))
                {
                    continue;
                }
                stack.Push(d);
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir, "*.cas", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var f in files)
            {
                yield return Path.GetFullPath(f);
            }
        }
    }

    /// <summary>
    /// Reads the file-level library namespace without parsing the full file.
    /// </summary>
    /// <remarks>
    /// We intentionally keep this tolerant of legacy syntax deeper in the file.
    /// Only the "header" is scanned: optional VERSION and a library declaration.
    /// </remarks>
    private static string? TryReadFileLibraryHeader(string path)
    {
        try
        {
            using var reader = File.OpenText(path);
            for (var i = 0; i < 80; i++)
            {
                var line = reader.ReadLine();
                if (line is null)
                {
                    break;
                }

                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                if (trimmed.StartsWith("VERSION", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (trimmed.StartsWith("library", StringComparison.OrdinalIgnoreCase))
                {
                    var rest = trimmed["library".Length..].Trim();
                    if (string.IsNullOrWhiteSpace(rest))
                    {
                        return null;
                    }

                    // Take the first token (qualified name).
                    var tokens = rest.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length == 0)
                    {
                        return null;
                    }
                    return NormalizeLibraryName(tokens[0]);
                }

                // Any other non-header construct ends the search.
                break;
            }
        }
        catch
        {
            // Ignore unreadable files.
        }

        return null;
    }
}
