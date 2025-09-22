using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cascode.Workspace;

internal sealed class CdsInitScanner
{
    private static readonly Regex ModelFilesRegex = new(
        "envSetVal\\(\"spectre\\.envOpts\"\\s+\"modelFiles\"\\s+`string\\s+(?<path>\"[^\"]+\"|[^\\s)]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public IReadOnlyList<string> FindModelDecks(string workspaceRoot, ICollection<string>? warnings = null)
    {
        var candidates = EnumerateCandidateFiles(workspaceRoot).Distinct(StringComparer.OrdinalIgnoreCase);
        var decks = new List<string>();

        foreach (var file in candidates)
        {
            foreach (var path in ExtractModelPaths(workspaceRoot, file, warnings))
            {
                if (File.Exists(path) && !decks.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    decks.Add(path);
                }
                else if (!File.Exists(path))
                {
                    warnings?.Add($"Model deck '{path}' referenced by {file} does not exist.");
                }
            }
        }

        return decks;
    }

    private static IEnumerable<string> EnumerateCandidateFiles(string workspaceRoot)
    {
        var candidates = new List<string>();

        void TryAdd(string path)
        {
            if (File.Exists(path))
            {
                candidates.Add(Path.GetFullPath(path));
            }
        }

        TryAdd(Path.Combine(workspaceRoot, ".cdsinit"));
        TryAdd(Path.Combine(workspaceRoot, "cdsinit"));

        var cdsSite = Environment.GetEnvironmentVariable("CDS_SITE");
        if (!string.IsNullOrEmpty(cdsSite))
        {
            TryAdd(Path.Combine(cdsSite, ".cdsinit"));
        }

        var cdsHome = Environment.GetEnvironmentVariable("CDS_HOME");
        if (!string.IsNullOrEmpty(cdsHome))
        {
            TryAdd(Path.Combine(cdsHome, ".cdsinit"));
        }

        return candidates;
    }

    private static IEnumerable<string> ExtractModelPaths(string workspaceRoot, string filePath, ICollection<string>? warnings)
    {
        var result = new List<string>();
        var root = Path.GetDirectoryName(filePath) ?? Directory.GetCurrentDirectory();

        try
        {
            foreach (var line in File.ReadLines(filePath))
            {
                var match = ModelFilesRegex.Match(line);
                if (!match.Success)
                {
                    continue;
                }

                var raw = match.Groups["path"].Value;
                var normalized = PathUtilities.NormalizeWorkspacePath(raw, workspaceRoot, root);
                if (normalized is not null)
                {
                    result.Add(normalized);
                }
            }
        }
        catch (Exception ex)
        {
            warnings?.Add($"Failed to parse {filePath}: {ex.Message}");
        }

        return result;
    }
}
