using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Cascode.Bench;

namespace Cascode.Cli.Commands;

internal sealed partial class VerifyCommandModule
{
    private static bool LooksLikeDirectory(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            || path.EndsWith(Path.AltDirectorySeparatorChar);
    }

    private static bool LooksLikeResultsPath(string path)
    {
        return path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeTracePath(string path)
    {
        return path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseArguments(
        string[] args,
        out ParsedVerifyArgs parsed,
        out string error
    )
    {
        string? cascodePath = null;
        string? resultsPath = null;
        string? tracePath = null;
        var noRun = false;
        var positionals = new List<string>();
        error = string.Empty;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--no-run")
            {
                noRun = true;
                continue;
            }

            if (args[i] == "--cascode")
            {
                if (!TryReadOptionValue(args, ref i, out cascodePath))
                {
                    error = "Error: --cascode expects a file path.";
                    parsed = new ParsedVerifyArgs(null, null, null, false);
                    return false;
                }
                continue;
            }

            if (args[i] == "--results")
            {
                if (!TryReadOptionValue(args, ref i, out resultsPath))
                {
                    error = "Error: --results expects a file or directory path.";
                    parsed = new ParsedVerifyArgs(null, null, null, false);
                    return false;
                }
                continue;
            }

            if (args[i] == "--trace")
            {
                if (!TryReadOptionValue(args, ref i, out tracePath))
                {
                    error = "Error: --trace expects a .jsonl file path.";
                    parsed = new ParsedVerifyArgs(null, null, null, false);
                    return false;
                }
                continue;
            }

            if (args[i].StartsWith('-'))
            {
                error = $"Error: unknown option '{args[i]}'.";
                parsed = new ParsedVerifyArgs(null, null, null, false);
                return false;
            }

            positionals.Add(args[i]);
        }

        if (!string.IsNullOrWhiteSpace(resultsPath) && !string.IsNullOrWhiteSpace(tracePath))
        {
            error = "Error: provide either --results or --trace, not both.";
            parsed = new ParsedVerifyArgs(null, null, null, false);
            return false;
        }

        AssignPositionals(ref cascodePath, ref resultsPath, ref tracePath, positionals);
        parsed = new ParsedVerifyArgs(cascodePath, resultsPath, tracePath, noRun);
        return true;
    }

    private static void AssignPositionals(
        ref string? cascodePath,
        ref string? resultsPath,
        ref string? tracePath,
        IReadOnlyList<string> positionals
    )
    {
        if (positionals.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(cascodePath))
        {
            if (string.IsNullOrWhiteSpace(resultsPath) && LooksLikeResultsPath(positionals[0]))
            {
                resultsPath = positionals[0];
            }
            else if (string.IsNullOrWhiteSpace(tracePath) && LooksLikeTracePath(positionals[0]))
            {
                tracePath = positionals[0];
            }
            else
            {
                cascodePath = positionals[0];
            }
        }

        if (string.IsNullOrWhiteSpace(cascodePath) && positionals.Count >= 2)
        {
            cascodePath = positionals[1];
        }

        if (
            !string.IsNullOrWhiteSpace(cascodePath)
            && string.IsNullOrWhiteSpace(resultsPath)
            && string.IsNullOrWhiteSpace(tracePath)
            && positionals.Count >= 2
        )
        {
            if (LooksLikeTracePath(positionals[1]))
            {
                tracePath = positionals[1];
            }
            else
            {
                resultsPath = positionals[1];
            }
        }
    }

    private static bool TryReadOptionValue(string[] args, ref int index, out string? value)
    {
        value = null;
        if (index + 1 >= args.Length)
        {
            return false;
        }

        value = args[++index];
        return !string.IsNullOrWhiteSpace(value);
    }

    private static BenchResult ReadResultsFromTrace(
        string tracePath,
        JsonSerializerOptions jsonOptions
    )
    {
        BenchResult? last = null;

        foreach (var line in File.ReadLines(tracePath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var doc = JsonDocument.Parse(line);
            if (
                !doc.RootElement.TryGetProperty("type", out var typeEl)
                || typeEl.GetString() != "summary"
            )
            {
                continue;
            }

            if (!doc.RootElement.TryGetProperty("results", out var resultsEl))
            {
                continue;
            }

            last = JsonSerializer.Deserialize<BenchResult>(resultsEl.GetRawText(), jsonOptions);
        }

        return last
            ?? throw new InvalidOperationException(
                "No summary record with results found in trace.jsonl."
            );
    }
}
