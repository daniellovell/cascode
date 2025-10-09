using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Cascode.Workspace;

internal sealed class WorkspaceBashEnvironment
{
    private readonly string _workspaceRoot;
    private IReadOnlyDictionary<string, string>? _assignments;

    public WorkspaceBashEnvironment(string workspaceRoot)
    {
        _workspaceRoot = workspaceRoot;
    }

    public void LoadVariables(IEnumerable<string> variableNames)
    {
        var map = GetAssignments();
        if (map.Count == 0)
        {
            return;
        }

        foreach (var name in variableNames)
        {
            if (!map.TryGetValue(name, out var value))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
            {
                continue;
            }

            var expanded = Environment.ExpandEnvironmentVariables(value);
            Environment.SetEnvironmentVariable(name, expanded);
        }
    }

    private IReadOnlyDictionary<string, string> GetAssignments()
    {
        if (_assignments is not null)
        {
            return _assignments;
        }

        var path = Path.Combine(_workspaceRoot, ".bashrc");
        if (!File.Exists(path))
        {
            _assignments = new Dictionary<string, string>(StringComparer.Ordinal);
            return _assignments;
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(path))
        {
            if (TryParseAssignment(line, out var name, out var value))
            {
                result[name] = value;
            }
        }

        _assignments = result;
        return _assignments;
    }

    private static bool TryParseAssignment(string line, out string name, out string value)
    {
        name = string.Empty;
        value = string.Empty;

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var trimmed = RemoveInlineComment(line).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed.StartsWith("export ", StringComparison.Ordinal))
        {
            trimmed = trimmed[7..].Trim();
        }

        var equalsIndex = trimmed.IndexOf('=');
        if (equalsIndex <= 0)
        {
            return false;
        }

        name = trimmed[..equalsIndex].Trim();
        if (!IsValidVariableName(name))
        {
            name = string.Empty;
            return false;
        }

        var rawValue = trimmed[(equalsIndex + 1)..].Trim().TrimEnd(';');
        value = UnwrapQuotes(rawValue.Trim());
        return true;
    }

    private static string RemoveInlineComment(string line)
    {
        var builder = new StringBuilder(line.Length);
        var inSingle = false;
        var inDouble = false;

        foreach (var c in line)
        {
            if (c == '\'' && !inDouble)
            {
                inSingle = !inSingle;
            }
            else if (c == '"' && !inSingle)
            {
                inDouble = !inDouble;
            }
            else if (c == '#' && !inSingle && !inDouble)
            {
                break;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    private static bool IsValidVariableName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (!char.IsLetter(name[0]) && name[0] != '_')
        {
            return false;
        }

        for (var i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static string UnwrapQuotes(string value)
    {
        if (value.Length >= 2)
        {
            if (value[0] == '"' && value[^1] == '"')
            {
                return value[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal);
            }

            if (value[0] == '\'' && value[^1] == '\'')
            {
                return value[1..^1];
            }
        }

        return value;
    }
}
