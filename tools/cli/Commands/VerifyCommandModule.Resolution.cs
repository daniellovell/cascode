using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Cascode.Bench;
using Cascode.Cli.Output;
using Cascode.Language;

namespace Cascode.Cli.Commands;

internal sealed partial class VerifyCommandModule
{
    private enum VerifyInputKind
    {
        Results,
        Trace,
    }

    private sealed record ParsedVerifyArgs(
        string? CascodePath,
        string? ResultsPath,
        string? TracePath,
        bool NoRun
    );

    private sealed record VerifyInput(VerifyInputKind Kind, string Path);

    private sealed record VerifyRunContext(
        string CascodePath,
        Circuit Circuit,
        IReadOnlyList<Circuit> ElCircuits
    );

    private static string? ResolveBenchOutputDirectoryHint(ParsedVerifyArgs parsed)
    {
        var candidate = parsed.ResultsPath ?? parsed.TracePath;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var full = Path.GetFullPath(candidate);
        if (Directory.Exists(full) || LooksLikeDirectory(full))
        {
            return full;
        }

        return Path.GetDirectoryName(full);
    }

    private bool TryBuildRunContext(
        ParsedVerifyArgs parsed,
        ICliOutput output,
        out VerifyRunContext context
    )
    {
        context = null!;
        if (string.IsNullOrWhiteSpace(parsed.CascodePath))
        {
            var supplied = parsed.ResultsPath ?? parsed.TracePath ?? "(none)";
            output.Error(
                $"Cascode source file is required. Received '{supplied}'. Provide a Cascode source file as the first argument or via --cascode."
            );
            ShowUsage(output);
            return false;
        }

        var cascodePath = Path.GetFullPath(parsed.CascodePath);
        if (!File.Exists(cascodePath))
        {
            output.Error($"Cascode file '{cascodePath}' not found.");
            return false;
        }

        if (!TryReadCascodeDocument(cascodePath, output, out var doc))
        {
            return false;
        }

        var elCircuits = doc.Circuits.Where(c => c.Level == CascodeLevel.EL).ToList();
        if (elCircuits.Count == 0)
        {
            output.Error("No EL-level circuits found in Cascode document.");
            return false;
        }

        context = new VerifyRunContext(cascodePath, elCircuits[0], elCircuits);
        return true;
    }

    private static bool TryReadCascodeDocument(
        string cascodePath,
        ICliOutput output,
        out CascodeDocument document
    )
    {
        document = null!;
        CascodeReadResult readResult;
        using (var reader = File.OpenText(cascodePath))
        {
            readResult = CascodeReader.TryRead(reader, cascodePath);
        }

        if (!readResult.Success)
        {
            foreach (
                var diag in readResult.Diagnostics.Where(d =>
                    d.Severity == DiagnosticSeverity.Error
                )
            )
            {
                output.Error($"{diag.FilePath}:{diag.Line}: {diag.Message}");
            }
            return false;
        }

        document = readResult.Document!;
        return true;
    }

    private static bool NeedsBenchRun(string cascodePath, VerifyInput input, out string reason)
    {
        if (!File.Exists(input.Path))
        {
            reason = $"{InputKindLabel(input.Kind)} file '{input.Path}' does not exist";
            return true;
        }

        if (File.GetLastWriteTimeUtc(cascodePath) > File.GetLastWriteTimeUtc(input.Path))
        {
            reason = $"{InputKindLabel(input.Kind)} file is older than the Cascode source";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static string InputKindLabel(VerifyInputKind kind) =>
        kind == VerifyInputKind.Trace ? "Trace" : "Results";

    private static bool TryResolveVerifyInput(
        ParsedVerifyArgs parsed,
        VerifyRunContext runContext,
        JsonSerializerOptions jsonOptions,
        string? preferredDirectory,
        out VerifyInput input,
        out string resolutionNote
    )
    {
        if (
            TryResolveExplicitInput(
                parsed.ResultsPath,
                parsed.TracePath,
                jsonOptions,
                out input,
                out resolutionNote
            )
        )
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(preferredDirectory))
        {
            var preferredFull = Path.GetFullPath(preferredDirectory);
            if (
                TryResolveFromDirectory(
                    preferredFull,
                    jsonOptions,
                    out input,
                    out var preferredResolution
                )
            )
            {
                resolutionNote = preferredResolution;
                return true;
            }
        }

        return TryResolveDefaultInput(runContext, jsonOptions, out input, out resolutionNote);
    }

    private static bool TryResolveExplicitInput(
        string? resultsPath,
        string? tracePath,
        JsonSerializerOptions jsonOptions,
        out VerifyInput input,
        out string resolutionNote
    )
    {
        input = null!;
        resolutionNote = string.Empty;
        if (!string.IsNullOrWhiteSpace(tracePath))
        {
            input = new VerifyInput(VerifyInputKind.Trace, Path.GetFullPath(tracePath));
            return true;
        }

        if (string.IsNullOrWhiteSpace(resultsPath))
        {
            return false;
        }

        var full = Path.GetFullPath(resultsPath);
        if (Directory.Exists(full) || LooksLikeDirectory(full))
        {
            return TryResolveFromDirectory(full, jsonOptions, out input, out resolutionNote);
        }

        input = new VerifyInput(VerifyInputKind.Results, full);
        return true;
    }

    private static bool TryResolveDefaultInput(
        VerifyRunContext runContext,
        JsonSerializerOptions jsonOptions,
        out VerifyInput input,
        out string resolutionNote
    )
    {
        input = null!;
        resolutionNote = string.Empty;

        var sourceDir =
            Path.GetDirectoryName(Path.GetFullPath(runContext.CascodePath))
            ?? Directory.GetCurrentDirectory();
        var candidateResults = new List<string>();
        foreach (var circuitName in runContext.ElCircuits.Select(c => c.Name).Distinct())
        {
            var circuitDir = Path.Combine(sourceDir, "build", "bench", circuitName);
            var combined = Path.Combine(circuitDir, $"{circuitName}_results.json");
            if (File.Exists(combined))
            {
                candidateResults.Add(combined);
            }

            if (
                TryResolveFromDirectory(
                    circuitDir,
                    jsonOptions,
                    out var dirInput,
                    out _,
                    includeMissingDirectoryErrors: false
                )
            )
            {
                candidateResults.Add(dirInput.Path);
            }
        }

        var multiDir = Path.Combine(sourceDir, "build", "bench", "multi");
        var multiCombined = Path.Combine(multiDir, "results.json");
        if (File.Exists(multiCombined))
        {
            candidateResults.Add(multiCombined);
        }

        if (
            TryResolveFromDirectory(
                multiDir,
                jsonOptions,
                out var multiInput,
                out _,
                includeMissingDirectoryErrors: false
            )
        )
        {
            candidateResults.Add(multiInput.Path);
        }

        if (candidateResults.Count == 0)
        {
            resolutionNote =
                $"No verification artifacts found under '{Path.Combine(sourceDir, "build", "bench")}'.";
            return false;
        }

        var selected = candidateResults
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .First();
        input = new VerifyInput(VerifyInputKind.Results, selected);
        resolutionNote = $"Discovered results file '{selected}'.";
        return true;
    }

    private static bool TryResolveFromDirectory(
        string directoryPath,
        JsonSerializerOptions jsonOptions,
        out VerifyInput input,
        out string resolutionNote,
        bool includeMissingDirectoryErrors = true
    )
    {
        input = null!;
        resolutionNote = string.Empty;
        if (!Directory.Exists(directoryPath))
        {
            if (includeMissingDirectoryErrors)
            {
                resolutionNote = $"Results directory '{directoryPath}' does not exist.";
            }
            return false;
        }

        var resultFiles = Directory
            .GetFiles(directoryPath, "*_results.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (resultFiles.Length == 0)
        {
            resolutionNote =
                $"No '*_results.json' files were found in results directory '{directoryPath}'.";
            return false;
        }

        var selected = resultFiles
            .Select(path => new
            {
                Path = path,
                Bench = TryReadBenchName(path, jsonOptions),
                NameLength = Path.GetFileName(path).Length,
            })
            .OrderByDescending(x =>
                string.Equals(x.Bench, "all", StringComparison.OrdinalIgnoreCase)
            )
            .ThenBy(x => x.NameLength)
            .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .First();

        input = new VerifyInput(VerifyInputKind.Results, selected.Path);
        resolutionNote = $"Discovered results file '{selected.Path}'.";
        return true;
    }

    private static string? TryReadBenchName(string path, JsonSerializerOptions jsonOptions)
    {
        try
        {
            var text = File.ReadAllText(path);
            return JsonSerializer.Deserialize<BenchResult>(text, jsonOptions)?.Bench;
        }
        catch
        {
            return null;
        }
    }
}
