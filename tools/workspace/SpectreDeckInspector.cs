using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Cascode.Workspace;

internal sealed class SpectreDeckInspector
{
    private static readonly Regex IncludeRegex = new(
        "include\\s+(?<path>\"[^\"]+\"|[^\\s)]+)(\\s+section=(?<section>[^\\s]+))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public ModelDeckRecord Inspect(string workspaceRoot, string deckPath, ICollection<string>? warnings = null)
    {
        var sections = new List<string>();
        var includes = new List<string>();
        var currentSection = string.Empty;

        if (!File.Exists(deckPath))
        {
            warnings?.Add($"Model deck '{deckPath}' does not exist.");
            return new ModelDeckRecord(deckPath, deckPath, sections, includes);
        }

        try
        {
            var directory = Path.GetDirectoryName(deckPath) ?? Directory.GetCurrentDirectory();

            foreach (var rawLine in File.ReadLines(deckPath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("*", StringComparison.Ordinal))
                {
                    continue; // comment
                }

                if (line.StartsWith("section", StringComparison.OrdinalIgnoreCase))
                {
                    var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (tokens.Length >= 2)
                    {
                        currentSection = tokens[1];
                        if (!sections.Contains(currentSection, StringComparer.OrdinalIgnoreCase))
                        {
                            sections.Add(currentSection);
                        }
                    }
                    continue;
                }

                if (line.StartsWith("endsection", StringComparison.OrdinalIgnoreCase))
                {
                    currentSection = string.Empty;
                    continue;
                }

                var match = IncludeRegex.Match(line);
                if (match.Success)
                {
                    var rawInclude = match.Groups["path"].Value;
                    var normalized = PathUtilities.NormalizeWorkspacePath(rawInclude, workspaceRoot, directory);
                    if (normalized is not null)
                    {
                        includes.Add(normalized);
                    }
                    continue;
                }
            }
        }
        catch (Exception ex)
        {
            warnings?.Add($"Failed to inspect '{deckPath}': {ex.Message}");
        }

        return new ModelDeckRecord(deckPath, deckPath, sections, includes);
    }

}
