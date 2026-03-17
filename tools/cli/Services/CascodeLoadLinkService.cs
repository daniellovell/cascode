using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cascode.Language;
using Microsoft.Extensions.Logging;

namespace Cascode.Cli.Services;

internal static class CascodeLoadLinkService
{
    internal sealed record LoadedCascode(
        string InputPath,
        string ResolvedPath,
        string WorkspaceRoot,
        CascodeDocument Document,
        IReadOnlyList<string> SourcePaths
    );

    public static LoadedCascode LoadAndLinkIfNeeded(
        string inputPath,
        string workspaceRootHint,
        string? linkArtifactsDir,
        ILogger logger
    )
    {
        if (
            !TryLoadAndLinkIfNeeded(
                inputPath,
                workspaceRootHint,
                linkArtifactsDir,
                logger,
                out var loaded,
                out var diagnostics
            )
        )
        {
            var msg =
                diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error)?.Message
                ?? "Load/link failed.";
            throw new InvalidOperationException(msg);
        }

        return loaded;
    }

    public static bool TryLoadAndLinkIfNeeded(
        string inputPath,
        string workspaceRootHint,
        string? linkArtifactsDir,
        ILogger logger,
        out LoadedCascode loaded,
        out IReadOnlyList<Diagnostic> diagnostics
    )
    {
        loaded = null!;
        diagnostics = Array.Empty<Diagnostic>();

        if (string.IsNullOrWhiteSpace(inputPath))
        {
            diagnostics = new[]
            {
                new Diagnostic(
                    "Input path is required.",
                    DiagnosticSeverity.Error,
                    string.Empty,
                    line: 1,
                    column: 1,
                    code: "CASCLI-LOAD"
                ),
            };
            return false;
        }

        var resolvedPath = Path.GetFullPath(inputPath);
        if (!TryReadCascode(resolvedPath, out var doc, out diagnostics))
        {
            return false;
        }

        var workspaceRoot = BenchRunHelpers.ResolveWorkspaceRoot(resolvedPath, workspaceRootHint);

        if (doc.Includes.Count == 0)
        {
            loaded = new LoadedCascode(
                resolvedPath,
                resolvedPath,
                workspaceRoot,
                doc,
                new[] { resolvedPath }
            );
            return true;
        }

        if (resolvedPath.EndsWith(".cai", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Input '{InputPath}' uses .cai and still contains include directives; treating it as an intermediate artifact and re-linking.",
                resolvedPath
            );
        }

        // Any document with includes must be linked before emission/simulation.
        // Prefer a caller-provided artifacts directory; otherwise use build/link.
        var outDir = string.IsNullOrWhiteSpace(linkArtifactsDir)
            ? Path.Combine(
                Path.GetDirectoryName(resolvedPath) ?? Directory.GetCurrentDirectory(),
                "build",
                "link"
            )
            : Path.GetFullPath(linkArtifactsDir);
        Directory.CreateDirectory(outDir);

        var searchRoots = BenchRunHelpers.BuildSearchRoots(workspaceRoot);
        var link = CascodeLinker.LinkFile(
            resolvedPath,
            outDir,
            searchRoots,
            CascodeLinkOptions.Default,
            logger
        );
        if (!link.Success || string.IsNullOrWhiteSpace(link.LinkedCasPath))
        {
            diagnostics = link.Diagnostics;
            return false;
        }

        var linkedPath = link.LinkedCasPath!;
        if (!TryReadCascode(linkedPath, out var linkedDoc, out diagnostics))
        {
            return false;
        }

        var sourcePaths = link.SourcePaths.Count > 0 ? link.SourcePaths : new[] { resolvedPath };
        loaded = new LoadedCascode(resolvedPath, linkedPath, workspaceRoot, linkedDoc, sourcePaths);
        return true;
    }

    private static bool TryReadCascode(
        string cascodePath,
        out CascodeDocument document,
        out IReadOnlyList<Diagnostic> diagnostics
    )
    {
        document = null!;
        diagnostics = Array.Empty<Diagnostic>();

        CascodeReadResult readResult;
        using (var reader = File.OpenText(cascodePath))
        {
            readResult = CascodeReader.TryRead(reader, cascodePath);
        }

        if (!readResult.Success)
        {
            diagnostics = readResult.Diagnostics;
            return false;
        }

        document = readResult.Document!;
        return true;
    }
}
