using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Cascode.Workspace;

internal sealed class LibInitScanner
{
    private static readonly string[] CandidateFileNames = { "libInit.il", "libinit.il" };
    private static readonly Regex StrcatLibPathPattern = new(
        @"strcat\s*\(\s*libPath\s*(?:,|\s)+""(?<path>[^""]+)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public IReadOnlyList<string> FindModelDecks(
        string workspaceRoot,
        IEnumerable<WorkspaceLibrary> libraries,
        ICollection<string>? warnings,
        Microsoft.Extensions.Logging.ILogger? logger = null)
    {
        var decks = new List<string>();
        var seenDecks = new HashSet<string>(PathComparer);
        var scannedFiles = new HashSet<string>(PathComparer);

        foreach (var library in libraries)
        {
            if (string.IsNullOrWhiteSpace(library.Path) || !Directory.Exists(library.Path))
            {
                logger?.LogDebug("[libInit] Skipping library '{Name}' – path missing ({Path})", library.Name, library.Path);
                continue;
            }

            foreach (var candidateName in CandidateFileNames)
            {
                var candidate = Path.Combine(library.Path, candidateName);
                if (!File.Exists(candidate))
                {
                    continue;
                }

                var fullCandidate = Path.GetFullPath(candidate);
                if (!scannedFiles.Add(fullCandidate))
                {
                    continue;
                }

                logger?.LogDebug("[libInit] Parsing {File} for library '{Name}'", fullCandidate, library.Name);

                foreach (var deck in ExtractDecks(workspaceRoot, library.Path, fullCandidate, warnings, logger))
                {
                    if (seenDecks.Add(deck))
                    {
                        decks.Add(deck);
                        logger?.LogInformation("[libInit] Discovered model deck: {Deck}", deck);
                    }
                }
            }
        }

        return decks;
    }

    private static IEnumerable<string> ExtractDecks(
        string workspaceRoot,
        string libraryPath,
        string libInitPath,
        ICollection<string>? warnings,
        Microsoft.Extensions.Logging.ILogger? logger)
    {
        string content;
        try
        {
            content = File.ReadAllText(libInitPath);
        }
        catch (Exception ex)
        {
            warnings?.Add($"Failed to parse {libInitPath}: {ex.Message}");
            logger?.LogWarning("[libInit] Failed to read {File}: {Message}", libInitPath, ex.Message);
            yield break;
        }

        foreach (Match match in StrcatLibPathPattern.Matches(content))
        {
            var suffix = match.Groups["path"].Value;
            if (string.IsNullOrEmpty(suffix))
            {
                continue;
            }

            var resolved = ResolveLibPathRelative(workspaceRoot, libraryPath, suffix);
            if (resolved is null)
            {
                logger?.LogDebug("[libInit] Unable to resolve suffix '{Suffix}' in {File}", suffix, libInitPath);
                continue;
            }

            if (!File.Exists(resolved))
            {
                warnings?.Add($"Model deck '{resolved}' referenced by {libInitPath} does not exist.");
                logger?.LogWarning("[libInit] Missing model deck '{Deck}' referenced by {File}", resolved, libInitPath);
                continue;
            }

            if (!PathUtilities.IsValidSpectreModelDeck(resolved))
            {
                logger?.LogDebug("[libInit] Skipping non-Spectre file: {Path}", resolved);
                continue;
            }

            yield return resolved;
        }
    }

    private static string? ResolveLibPathRelative(string workspaceRoot, string libraryPath, string suffix)
    {
        var expandedSuffix = EnvironmentVariableScanner.Expand(suffix, Environment.GetEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(expandedSuffix))
        {
            return null;
        }

        var normalized = expandedSuffix.Replace('/', Path.DirectorySeparatorChar);
        if (normalized.Length > 0 && normalized[0] == Path.DirectorySeparatorChar)
        {
            normalized = "." + normalized;
        }

        var combined = Path.Combine(libraryPath, normalized);
        var normalizedPath = PathUtilities.NormalizeWorkspacePath(combined, workspaceRoot, libraryPath);
        return normalizedPath ?? Path.GetFullPath(combined);
    }
}
