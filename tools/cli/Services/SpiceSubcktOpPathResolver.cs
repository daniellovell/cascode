using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Cascode.Cli.Services;

internal static class SpiceSubcktOpPathResolver
{
    // Maximum .subckt nesting depth for DFS traversal of wrapper hierarchies.
    private const int MaxSubcktNestingDepth = 6;

    public sealed record SubcktDefinition(
        IReadOnlyList<string> Terminals,
        IReadOnlyList<string> ParameterNames,
        IReadOnlyList<string> BodyLines
    );

    public static IReadOnlyDictionary<string, SubcktDefinition> IndexSubcktDefinitions(
        IReadOnlyList<string> files
    )
    {
        var map = new Dictionary<string, SubcktDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in files)
        {
            try
            {
                IndexSubcktBodiesFromFile(path, map);
            }
            catch (Exception ex)
                when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Best-effort indexing. If a file can't be read/parsed, skip it.
            }
        }
        return map;
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> IndexSubcktBodies(
        IReadOnlyList<string> files
    )
    {
        return IndexSubcktDefinitions(files)
            .ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.BodyLines,
                StringComparer.OrdinalIgnoreCase
            );
    }

    public static IReadOnlyList<string>? TryResolveUniqueOpSegments(
        string wrapperSubcktName,
        IReadOnlyDictionary<string, IReadOnlyList<string>> subcktBodiesByName
    )
    {
        if (string.IsNullOrWhiteSpace(wrapperSubcktName))
        {
            return null;
        }

        if (!subcktBodiesByName.ContainsKey(wrapperSubcktName))
        {
            return null;
        }

        var paths = new List<IReadOnlyList<string>>();
        var stack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Dfs(string subcktName, List<string> prefix, int depth)
        {
            if (depth > MaxSubcktNestingDepth)
            {
                return;
            }

            if (!subcktBodiesByName.TryGetValue(subcktName, out var body))
            {
                return;
            }

            if (!stack.Add(subcktName))
            {
                return;
            }

            var mos = FindMosInstances(body);
            foreach (var m in mos)
            {
                paths.Add(prefix.Concat(new[] { m }).ToArray());
            }

            if (mos.Count > 0)
            {
                stack.Remove(subcktName);
                return;
            }

            var xInstances = FindSubcktInstances(body);
            foreach (var (xName, child) in xInstances)
            {
                var next = new List<string>(prefix.Count + 1);
                next.AddRange(prefix);
                next.Add(xName);
                Dfs(child, next, depth + 1);
            }

            stack.Remove(subcktName);
        }

        Dfs(wrapperSubcktName, new List<string>(), 0);

        var normalized = paths
            .Where(p => p.Count > 0)
            .Select(p => p.Select(s => s.Trim()).Where(s => s.Length > 0).ToArray())
            .Where(p => p.Length > 0 && p.All(IsValidCasIdentifier))
            .Select(p => (Segments: (IReadOnlyList<string>)p, Leaf: p[^1]))
            .ToList();

        if (normalized.Count > 1)
        {
            var needle = wrapperSubcktName.Trim().ToLowerInvariant();
            var filtered = normalized
                .Where(p => p.Leaf.ToLowerInvariant().Contains(needle))
                .ToList();
            if (filtered.Count > 0)
            {
                normalized = filtered;
            }
        }

        if (normalized.Count == 0)
        {
            return null;
        }

        static bool SeqEqual(IReadOnlyList<string> a, IReadOnlyList<string> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            for (var i = 0; i < a.Count; i++)
            {
                if (!a[i].Equals(b[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        var first = normalized[0].Segments.Select(s => s.ToLowerInvariant()).ToArray();
        for (var i = 1; i < normalized.Count; i++)
        {
            var other = normalized[i].Segments.Select(s => s.ToLowerInvariant()).ToArray();
            if (!SeqEqual(first, other))
            {
                return null;
            }
        }

        return first;
    }

    private static void IndexSubcktBodiesFromFile(
        string path,
        Dictionary<string, SubcktDefinition> into
    )
    {
        if (!File.Exists(path))
        {
            return;
        }

        string? currentName = null;
        IReadOnlyList<string> currentTerminals = Array.Empty<string>();
        IReadOnlyList<string> currentParameterNames = Array.Empty<string>();
        var currentBody = new List<string>();
        string? currentLogical = null;

        void ProcessLogicalLine(string logical)
        {
            var trimmed = logical.Trim();
            if (trimmed.Length == 0)
            {
                return;
            }

            if (trimmed.StartsWith(".subckt", StringComparison.OrdinalIgnoreCase))
            {
                var parts = SplitTokens(trimmed);
                if (parts.Length >= 2)
                {
                    currentName = parts[1];
                    currentTerminals = ParseSubcktTerminals(parts);
                    currentParameterNames = ParseSubcktParameterNames(parts);
                    currentBody = new List<string>();
                }
                return;
            }

            if (trimmed.StartsWith(".ends", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(currentName) && currentBody.Count > 0)
                {
                    into[currentName] = new SubcktDefinition(
                        currentTerminals,
                        currentParameterNames,
                        currentBody.ToArray()
                    );
                }
                currentName = null;
                currentTerminals = Array.Empty<string>();
                currentParameterNames = Array.Empty<string>();
                currentBody = new List<string>();
                return;
            }

            if (currentName is not null)
            {
                currentBody.Add(trimmed);
            }
        }

        foreach (var raw in File.ReadLines(path))
        {
            var line = StripInlineComment(raw);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('*'))
            {
                continue;
            }

            if (trimmed.StartsWith('+'))
            {
                var cont = trimmed.Substring(1).Trim();
                if (cont.Length == 0)
                {
                    continue;
                }

                currentLogical = currentLogical is null ? cont : currentLogical + " " + cont;
                continue;
            }

            if (currentLogical is not null)
            {
                ProcessLogicalLine(currentLogical);
            }

            currentLogical = trimmed.TrimEnd();
        }

        if (currentLogical is not null)
        {
            ProcessLogicalLine(currentLogical);
        }

        if (!string.IsNullOrWhiteSpace(currentName) && currentBody.Count > 0)
        {
            into[currentName] = new SubcktDefinition(
                currentTerminals,
                currentParameterNames,
                currentBody.ToArray()
            );
        }
    }

    private static IReadOnlyList<string> ParseSubcktTerminals(string[] tokens)
    {
        if (tokens.Length <= 2)
        {
            return Array.Empty<string>();
        }

        var terminals = new List<string>();
        for (var i = 2; i < tokens.Length; i++)
        {
            var token = tokens[i].Trim();
            if (token.Length == 0)
            {
                continue;
            }

            if (token.Equals("params:", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (token.StartsWith("params:", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (token.Contains('='))
            {
                break;
            }

            terminals.Add(token);
        }

        return terminals;
    }

    private static IReadOnlyList<string> ParseSubcktParameterNames(string[] tokens)
    {
        if (tokens.Length <= 2)
        {
            return Array.Empty<string>();
        }

        var parameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inParams = false;

        for (var i = 2; i < tokens.Length; i++)
        {
            var token = tokens[i].Trim();
            if (token.Length == 0)
            {
                continue;
            }

            if (token.Equals("params:", StringComparison.OrdinalIgnoreCase))
            {
                inParams = true;
                continue;
            }

            if (token.StartsWith("params:", StringComparison.OrdinalIgnoreCase))
            {
                inParams = true;
                token = token.Substring("params:".Length).Trim();
                if (token.Length == 0)
                {
                    continue;
                }
            }

            if (!inParams && !token.Contains('='))
            {
                continue;
            }

            inParams = true;

            var eq = token.IndexOf('=');
            var name = eq >= 0 ? token[..eq] : token;
            name = name.Trim().TrimEnd(',', ';');

            if (name.Length > 0)
            {
                parameters.Add(name);
            }
        }

        return parameters.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string StripInlineComment(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return string.Empty;
        }

        var idx = line.IndexOf(';');
        if (idx >= 0)
        {
            line = line.Substring(0, idx);
        }

        return line.TrimEnd();
    }

    private static string[] SplitTokens(string line)
    {
        return line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }

    private static IReadOnlyList<string> FindMosInstances(IReadOnlyList<string> bodyLines)
    {
        var names = new List<string>();
        foreach (var line in bodyLines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var c = trimmed[0];
            if (c != 'M' && c != 'm')
            {
                continue;
            }

            var parts = SplitTokens(trimmed);
            if (parts.Length >= 1)
            {
                names.Add(parts[0]);
            }
        }
        return names;
    }

    private static IReadOnlyList<(string InstanceName, string SubcktName)> FindSubcktInstances(
        IReadOnlyList<string> bodyLines
    )
    {
        var inst = new List<(string, string)>();
        foreach (var line in bodyLines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var c = trimmed[0];
            if (c != 'X' && c != 'x')
            {
                continue;
            }

            var parts = SplitTokens(trimmed);
            if (parts.Length < 2)
            {
                continue;
            }

            var name = parts[0];
            string? subckt = null;
            for (var i = parts.Length - 1; i >= 1; i--)
            {
                if (parts[i].Contains('='))
                {
                    continue;
                }
                subckt = parts[i];
                break;
            }

            if (!string.IsNullOrWhiteSpace(subckt))
            {
                inst.Add((name, subckt));
            }
        }

        return inst;
    }

    private static bool IsValidCasIdentifier(string ident)
    {
        if (string.IsNullOrWhiteSpace(ident))
        {
            return false;
        }

        if (!(char.IsLetter(ident[0]) || ident[0] == '_'))
        {
            return false;
        }

        for (var i = 1; i < ident.Length; i++)
        {
            var c = ident[i];
            if (!(char.IsLetterOrDigit(c) || c == '_'))
            {
                return false;
            }
        }

        return true;
    }
}
