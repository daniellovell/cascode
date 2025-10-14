using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Cascode.Workspace;

internal static class EnvironmentVariableScanner
{
    private static readonly Regex VariablePattern = new(
        @"\$(\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}|(?<name2>[A-Za-z_][A-Za-z0-9_]*))",
        RegexOptions.Compiled);

    public static IReadOnlyCollection<string> FromFile(string path)
    {
        if (!File.Exists(path))
        {
            return Array.Empty<string>();
        }

        var collector = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(path))
        {
            CollectFromText(line, collector);
        }

        return collector.Count == 0 ? Array.Empty<string>() : collector.ToArray();
    }

    public static IReadOnlyCollection<string> FromText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<string>();
        }

        var collector = new HashSet<string>(StringComparer.Ordinal);
        CollectFromText(text, collector);
        return collector.Count == 0 ? Array.Empty<string>() : collector.ToArray();
    }

    public static string Expand(string text, Func<string, string?> valueProvider)
    {
        if (string.IsNullOrEmpty(text) || valueProvider is null)
        {
            return text;
        }

        return VariablePattern.Replace(text, match =>
        {
            var name = match.Groups["name"].Value;
            if (string.IsNullOrEmpty(name))
            {
                name = match.Groups["name2"].Value;
            }

            if (string.IsNullOrEmpty(name))
            {
                return match.Value;
            }

            var value = valueProvider(name);
            return value ?? match.Value;
        });
    }

    private static void CollectFromText(string text, HashSet<string> collector)
    {
        foreach (Match match in VariablePattern.Matches(text))
        {
            var name = match.Groups["name"].Value;
            if (string.IsNullOrEmpty(name))
            {
                name = match.Groups["name2"].Value;
            }

            if (name.Length != 0)
            {
                collector.Add(name);
            }
        }
    }
}
