using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cascode.Workspace;

public sealed record PdkIncludeResolution(
    IReadOnlyList<string> IncludePaths,
    IReadOnlyList<string> IncludePathsWithSection,
    IReadOnlyList<string> IncludePathsWithoutSection,
    string? Section
);

public static partial class PdkIncludeResolver
{
    private static readonly Regex LibraryRegex = LibraryKeywordPattern();

    public static PdkIncludeResolution ResolveModelIncludes(
        string dbPath,
        SpectreModel model,
        string? corner
    )
    {
        var resolvedIncludes = new List<string>();
        var withSection = new List<string>();
        var extraIncludes = new List<string>();
        string? resolvedSection = corner;

        var contexts = PdkDatabaseReader.GetContextsForModelAndCorner(dbPath, model.Name, corner);
        if (contexts.Count == 0)
        {
            contexts = PdkDatabaseReader.GetAllContextsForModel(dbPath, model.Name);
        }

        if (contexts.Count > 0)
        {
            var chosen = contexts[0];
            var inc = TryNormalizeInclude(chosen.IncludePath);
            if (!string.IsNullOrWhiteSpace(inc))
            {
                resolvedIncludes.Add(inc);
                if (FileHasLibrarySections(inc))
                {
                    withSection.Add(inc);
                    resolvedSection = string.IsNullOrWhiteSpace(chosen.Section)
                        ? corner
                        : chosen.Section;
                }
                else
                {
                    extraIncludes.Add(inc);
                    resolvedSection = null;
                }
            }
        }
        else
        {
            var decks = (model.Decks ?? Array.Empty<string>())
                .Select(TryNormalizeInclude)
                .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p!))
                .Select(p => p!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (decks.Count > 0)
            {
                foreach (var deck in decks)
                {
                    resolvedIncludes.Add(deck);
                    if (FileHasLibrarySections(deck))
                    {
                        withSection.Add(deck);
                    }
                    else
                    {
                        extraIncludes.Add(deck);
                    }
                }

                if (withSection.Count == 0)
                {
                    resolvedSection = null;
                }
            }
            else
            {
                var sources = (model.SourceFiles ?? Array.Empty<string>())
                    .Select(TryNormalizeInclude)
                    .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p!))
                    .Select(p => p!)
                    .ToList();

                if (!string.IsNullOrWhiteSpace(corner))
                {
                    var key = corner.Trim();
                    var pattern = "_" + Regex.Escape(key) + "(?:\\.|$)";
                    sources = sources
                        .Where(p =>
                            Regex.IsMatch(
                                Path.GetFileName(p) ?? string.Empty,
                                pattern,
                                RegexOptions.IgnoreCase
                            )
                        )
                        .ToList();
                }

                extraIncludes.AddRange(sources);
                resolvedIncludes.AddRange(sources);
            }
        }

        return new PdkIncludeResolution(
            resolvedIncludes.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            withSection.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            extraIncludes.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            resolvedSection
        );
    }

    private static string? TryNormalizeInclude(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }

    private static bool FileHasLibrarySections(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        try
        {
            using var reader = new StreamReader(path);
            string? line;
            var lineCount = 0;
            const int maxLinesToCheck = 200;

            while ((line = reader.ReadLine()) != null && lineCount < maxLinesToCheck)
            {
                lineCount++;
                var trimmed = line.TrimStart();
                if (
                    trimmed.StartsWith(".lib", StringComparison.OrdinalIgnoreCase)
                    || trimmed.StartsWith("section", StringComparison.OrdinalIgnoreCase)
                    || LibraryRegex.IsMatch(trimmed)
                )
                {
                    return true;
                }
            }
        }
        catch
        {
            // If we can't read the file, assume it doesn't have sections.
        }

        return false;
    }

    [GeneratedRegex(@"^library\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex LibraryKeywordPattern();
}
