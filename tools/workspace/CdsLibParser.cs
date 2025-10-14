using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Cascode.Workspace;

internal sealed class CdsLibParser
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly HashSet<string> _attemptedVariables = new(StringComparer.Ordinal);
    private WorkspaceBashEnvironment? _workspaceEnvironment;
    private string _workspaceRoot = string.Empty;

    public IReadOnlyList<WorkspaceLibrary> Parse(string rootPath, ICollection<string>? warnings = null, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        _workspaceRoot = Path.GetFullPath(rootPath);
        _attemptedVariables.Clear();
        _workspaceEnvironment = null;

        var libraries = new List<WorkspaceLibrary>();
        var visited = new HashSet<string>(PathComparer);

        var cdsLibPath = Path.Combine(_workspaceRoot, "cds.lib");
        if (!File.Exists(cdsLibPath))
        {
            warnings?.Add($"cds.lib not found under '{_workspaceRoot}'.");
            return libraries;
        }

        logger?.LogInformation("Root cds.lib: {Path}", cdsLibPath);
        EnsureRootEnvironmentVariables(cdsLibPath, logger);

        ParseFile(cdsLibPath, libraries, visited, warnings, logger);
        return libraries;
    }

    private void EnsureRootEnvironmentVariables(string cdsLibPath, ILogger? logger)
    {
        var variableNames = EnvironmentVariableScanner.FromFile(cdsLibPath);
        if (variableNames.Count > 0)
        {
            logger?.LogDebug("Found {Count} env var references in root cds.lib", variableNames.Count);
        }
        LoadMissingVariables(variableNames);
    }

    private void ParseFile(
        string filePath,
        List<WorkspaceLibrary> libraries,
        HashSet<string> visited,
        ICollection<string>? warnings,
        ILogger? logger)
    {
        if (!visited.Add(Path.GetFullPath(filePath)))
        {
            return;
        }

        if (!File.Exists(filePath))
        {
            warnings?.Add($"cds.lib include '{filePath}' does not exist.");
            return;
        }

        logger?.LogDebug("Reading {File}", filePath);

        foreach (var rawLine in File.ReadAllLines(filePath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (StartsWithToken(line, "DEFINE"))
            {
                ParseDefine(line, filePath, libraries, warnings, logger);
            }
            else if (StartsWithToken(line, "INCLUDE") || StartsWithToken(line, "SOFTINCLUDE"))
            {
                var includePath = ExtractPath(line, 1, filePath);
                if (includePath is null)
                {
                    warnings?.Add($"Unable to parse include in '{filePath}': '{line}'.");
                    continue;
                }

                logger?.LogDebug("Including {Include}", includePath);
                ParseFile(includePath, libraries, visited, warnings, logger);
            }
        }
    }

    private void ParseDefine(
        string line,
        string filePath,
        ICollection<WorkspaceLibrary> libraries,
        ICollection<string>? warnings,
        ILogger? logger)
    {
        var parts = SplitTokens(line);
        if (parts.Length < 3)
        {
            warnings?.Add($"Malformed DEFINE in '{filePath}': '{line}'.");
            return;
        }

        var name = parts[1];
        var baseDirectory = Path.GetDirectoryName(filePath) ?? _workspaceRoot;
        var libraryPath = NormalizePath(parts[2], baseDirectory);
        libraries.Add(new WorkspaceLibrary(name, libraryPath));
        logger?.LogInformation("DEFINE {Name} -> {Path}", name, libraryPath);
    }

    private string? ExtractPath(string line, int tokenIndex, string currentFile)
    {
        var parts = SplitTokens(line);
        if (tokenIndex >= parts.Length)
        {
            return null;
        }

        var rawPath = parts[tokenIndex];
        var baseDirectory = Path.GetDirectoryName(currentFile) ?? _workspaceRoot;
        return NormalizePath(rawPath, baseDirectory);
    }

    private string NormalizePath(string rawPath, string baseDirectory)
    {
        EnsureVariablesForRawPath(rawPath);

        var trimmed = rawPath.Trim('"', '\'', '`');
        var expanded = Environment.ExpandEnvironmentVariables(trimmed);
        expanded = EnvironmentVariableScanner.Expand(expanded, Environment.GetEnvironmentVariable);
        if (string.IsNullOrEmpty(expanded))
        {
            return trimmed;
        }

        if (Path.IsPathRooted(expanded))
        {
            return Path.GetFullPath(expanded);
        }

        var root = string.IsNullOrEmpty(baseDirectory) ? _workspaceRoot : baseDirectory;
        return Path.GetFullPath(Path.Combine(root, expanded));
    }

    private void EnsureVariablesForRawPath(string rawPath)
    {
        var candidateNames = EnvironmentVariableScanner.FromText(rawPath);
        if (candidateNames.Count == 0)
        {
            return;
        }

        var missing = new List<string>();
        foreach (var name in candidateNames)
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
            {
                continue;
            }

            if (_attemptedVariables.Add(name))
            {
                missing.Add(name);
            }
        }

        if (missing.Count == 0)
        {
            return;
        }

        LoadMissingVariables(missing);
    }

    private void LoadMissingVariables(IReadOnlyCollection<string> variableNames)
    {
        if (variableNames.Count == 0)
        {
            return;
        }

        _workspaceEnvironment ??= new WorkspaceBashEnvironment(_workspaceRoot);
        _workspaceEnvironment.LoadVariables(variableNames);
    }

    private static bool StartsWithToken(string line, string token)
        => line.StartsWith(token, StringComparison.OrdinalIgnoreCase);

    private static string[] SplitTokens(string line)
        => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
