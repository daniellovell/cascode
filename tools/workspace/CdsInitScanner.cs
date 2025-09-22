using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cascode.Workspace;

internal sealed class CdsInitScanner
{
    private static readonly Regex ModelFilesRegex = new(
        "envSetVal\\(\\s*\"spectre\\.envOpts\"\\s+\"modelFiles\"\\s+`string\\s+(?<paths>\"[^\"]+\"|[^)]*)\\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline);

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
            var content = File.ReadAllText(filePath);
            foreach (Match match in ModelFilesRegex.Matches(content))
            {
                var rawPaths = match.Groups["paths"].Value;
                foreach (var token in SplitModelPathTokens(rawPaths))
                {
                    if (string.IsNullOrWhiteSpace(token))
                    {
                        continue;
                    }

                    var pathSegment = ExtractPathSegment(token, out _);
                    var normalized = PathUtilities.NormalizeWorkspacePath(pathSegment, workspaceRoot, root);
                    if (normalized is not null)
                    {
                        result.Add(normalized);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            warnings?.Add($"Failed to parse {filePath}: {ex.Message}");
        }

        return result;
    }

    private static IEnumerable<string> SplitModelPathTokens(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            yield break;
        }

        var trimmed = raw.Trim();

        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[^1] == '"') || (trimmed[0] == '\'' && trimmed[^1] == '\'')))
        {
            trimmed = trimmed[1..^1];
        }

        var pieces = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        foreach (var piece in pieces)
        {
            var token = piece.Trim().Trim('\"', '\'');
            if (!string.IsNullOrWhiteSpace(token))
            {
                yield return token;
            }
        }
    }

    private static string ExtractPathSegment(string token, out string? section)
    {
        section = null;
        if (string.IsNullOrWhiteSpace(token))
        {
            return token;
        }

        var trimmed = token.Trim();
        var separatorIndex = trimmed.IndexOf(';');
        if (separatorIndex < 0)
        {
            return trimmed;
        }

        section = trimmed[(separatorIndex + 1)..].Trim();
        return trimmed[..separatorIndex].Trim();
    }
}
