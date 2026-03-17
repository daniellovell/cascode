using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cascode.Cli.Output;
using Cascode.Cli.Services;
using Cascode.Language;
using Cascode.Language.BenchRuntime;
using Microsoft.Extensions.Logging.Abstractions;

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
        string InputPath,
        IReadOnlyList<string> SourcePaths,
        IReadOnlyList<Circuit> AllElCircuits,
        IReadOnlyList<Circuit> VerifiableCircuits
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

        var inputPath = Path.GetFullPath(parsed.CascodePath);
        if (!File.Exists(inputPath))
        {
            output.Error($"Cascode file '{inputPath}' not found.");
            return false;
        }

        var inputDir = Path.GetDirectoryName(inputPath) ?? Directory.GetCurrentDirectory();
        var loadLogger = _state.LoggerFactory?.CreateLogger("CascodeLinker") ?? NullLogger.Instance;
        var linkArtifactsDir = Path.Combine(inputDir, "build", "link", "verify");
        if (
            !CascodeLoadLinkService.TryLoadAndLinkIfNeeded(
                inputPath,
                _state.WorkspaceRoot,
                linkArtifactsDir,
                loadLogger,
                out var loaded,
                out var diagnostics
            )
        )
        {
            foreach (var diag in diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
            {
                output.Error($"{diag.FilePath}:{diag.Line}: {diag.Message}");
            }
            return false;
        }

        var elCircuits = loaded.Document.Circuits.Where(c => c.Level == CascodeLevel.EL).ToList();
        if (elCircuits.Count == 0)
        {
            output.Error("No EL-level circuits found in Cascode document.");
            return false;
        }

        context = new VerifyRunContext(
            inputPath,
            loaded.SourcePaths,
            elCircuits,
            BenchVerificationTargets.CollectVerifiableCircuits(loaded.Document)
        );
        return true;
    }

    private static bool NeedsBenchRun(
        IReadOnlyList<string> sourcePaths,
        IReadOnlyList<VerifyInput> inputs,
        out string reason
    )
    {
        if (inputs.Count == 0)
        {
            reason = "no verification artifacts were resolved";
            return true;
        }

        if (
            !TryGetNewestSourceDependency(
                sourcePaths,
                out var newestSourcePath,
                out var newestSourceWriteTimeUtc,
                out reason
            )
        )
        {
            return true;
        }

        foreach (var input in inputs)
        {
            if (!File.Exists(input.Path))
            {
                reason = $"{InputKindLabel(input.Kind)} file '{input.Path}' does not exist";
                return true;
            }

            if (newestSourceWriteTimeUtc > File.GetLastWriteTimeUtc(input.Path))
            {
                reason =
                    $"{InputKindLabel(input.Kind)} file '{input.Path}' is older than source dependency '{newestSourcePath}'";
                return true;
            }
        }
        reason = string.Empty;
        return false;
    }

    private static bool TryGetNewestSourceDependency(
        IReadOnlyList<string> sourcePaths,
        out string newestSourcePath,
        out DateTime newestSourceWriteTimeUtc,
        out string reason
    )
    {
        newestSourcePath = string.Empty;
        newestSourceWriteTimeUtc = default;
        if (sourcePaths.Count == 0)
        {
            reason = "no Cascode source dependencies were resolved";
            return false;
        }

        var foundSource = false;
        foreach (var sourcePath in sourcePaths)
        {
            var fullPath = Path.GetFullPath(sourcePath);
            if (!File.Exists(fullPath))
            {
                reason = $"Cascode source dependency '{fullPath}' does not exist";
                return false;
            }

            var writeTimeUtc = File.GetLastWriteTimeUtc(fullPath);
            if (!foundSource || writeTimeUtc > newestSourceWriteTimeUtc)
            {
                newestSourcePath = fullPath;
                newestSourceWriteTimeUtc = writeTimeUtc;
                foundSource = true;
            }
        }

        reason = string.Empty;
        return true;
    }

    private static string InputKindLabel(VerifyInputKind kind) =>
        kind == VerifyInputKind.Trace ? "Trace" : "Results";

    private static bool TryResolveVerifyInputs(
        ParsedVerifyArgs parsed,
        VerifyRunContext runContext,
        string? preferredDirectory,
        out IReadOnlyList<VerifyInput> inputs,
        out string resolutionNote
    )
    {
        if (
            TryResolveExplicitInput(
                parsed.ResultsPath,
                parsed.TracePath,
                runContext.VerifiableCircuits,
                out inputs,
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
                TryResolveCanonicalFromRootDirectory(
                    preferredFull,
                    runContext.VerifiableCircuits,
                    explicitDirectory: true,
                    out inputs,
                    out var preferredResolution
                )
            )
            {
                resolutionNote = preferredResolution;
                return true;
            }
        }

        return TryResolveDefaultInput(runContext, out inputs, out resolutionNote);
    }

    private static bool TryResolveExplicitInput(
        string? resultsPath,
        string? tracePath,
        IReadOnlyList<Circuit> verifiableCircuits,
        out IReadOnlyList<VerifyInput> inputs,
        out string resolutionNote
    )
    {
        inputs = Array.Empty<VerifyInput>();
        resolutionNote = string.Empty;
        if (!string.IsNullOrWhiteSpace(tracePath))
        {
            inputs = new[] { new VerifyInput(VerifyInputKind.Trace, Path.GetFullPath(tracePath)) };
            return true;
        }

        if (string.IsNullOrWhiteSpace(resultsPath))
        {
            return false;
        }

        var full = Path.GetFullPath(resultsPath);
        if (Directory.Exists(full) || LooksLikeDirectory(full))
        {
            return TryResolveCanonicalFromRootDirectory(
                full,
                verifiableCircuits,
                explicitDirectory: true,
                out inputs,
                out resolutionNote
            );
        }

        inputs = new[] { new VerifyInput(VerifyInputKind.Results, full) };
        return true;
    }

    private static bool TryResolveDefaultInput(
        VerifyRunContext runContext,
        out IReadOnlyList<VerifyInput> inputs,
        out string resolutionNote
    )
    {
        var sourceDir =
            Path.GetDirectoryName(Path.GetFullPath(runContext.InputPath))
            ?? Directory.GetCurrentDirectory();
        var benchRoot = Path.Combine(sourceDir, "build", "bench");
        return TryResolveCanonicalFromRootDirectory(
            benchRoot,
            runContext.VerifiableCircuits,
            explicitDirectory: false,
            out inputs,
            out resolutionNote
        );
    }

    private static bool TryResolveCanonicalFromRootDirectory(
        string rootDirectory,
        IReadOnlyList<Circuit> circuits,
        bool explicitDirectory,
        out IReadOnlyList<VerifyInput> inputs,
        out string resolutionNote
    )
    {
        inputs = Array.Empty<VerifyInput>();
        resolutionNote = string.Empty;
        if (circuits.Count == 0)
        {
            resolutionNote =
                "No EL-level circuits in the Cascode document produced constraint-driven bench invocations.";
            return false;
        }

        if (!Directory.Exists(rootDirectory))
        {
            resolutionNote = explicitDirectory
                ? $"Results directory '{rootDirectory}' does not exist."
                : $"No verification artifacts found under '{rootDirectory}'.";
            return false;
        }

        var resolved = new List<(string CircuitName, string Path)>();
        var missingCircuits = new List<string>();
        var ambiguousCircuits = new List<string>();
        foreach (
            var circuitName in circuits
                .Select(c => c.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        )
        {
            var candidates = ResolveCanonicalCandidates(rootDirectory, circuitName);
            if (candidates.Count == 0)
            {
                missingCircuits.Add(circuitName);
                continue;
            }

            if (candidates.Count > 1)
            {
                ambiguousCircuits.Add($"{circuitName} ({string.Join(", ", candidates)})");
                continue;
            }

            resolved.Add((circuitName, candidates[0]));
        }

        if (ambiguousCircuits.Count > 0)
        {
            resolutionNote =
                $"Multiple canonical results files were found for one or more circuits under '{rootDirectory}': {string.Join("; ", ambiguousCircuits)}. Provide an explicit results file path.";
            return false;
        }

        if (missingCircuits.Count > 0)
        {
            resolutionNote =
                $"Missing canonical results files for circuit(s): {string.Join(", ", missingCircuits)} under '{rootDirectory}'. Expected '<circuit>_results.json'.";
            return false;
        }

        inputs = resolved
            .OrderBy(x => x.CircuitName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .Select(x => new VerifyInput(VerifyInputKind.Results, x.Path))
            .ToArray();
        resolutionNote =
            $"Discovered {inputs.Count} canonical results artifact(s) in '{rootDirectory}'.";
        return true;
    }

    private static IReadOnlyList<string> ResolveCanonicalCandidates(
        string rootDirectory,
        string circuitName
    )
    {
        var candidates = new List<string>(3);
        AddCandidate(candidates, Path.Combine(rootDirectory, $"{circuitName}_results.json"));
        AddCandidate(
            candidates,
            Path.Combine(rootDirectory, "multi", $"{circuitName}_results.json")
        );
        AddCandidate(
            candidates,
            Path.Combine(rootDirectory, circuitName, $"{circuitName}_results.json")
        );
        return candidates;
    }

    private static void AddCandidate(List<string> candidates, string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
        {
            return;
        }

        if (
            candidates.Any(existing =>
                string.Equals(existing, full, StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            return;
        }

        candidates.Add(full);
    }
}
