using System;
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
        CascodeDocument Document
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

        if (
            !resolvedPath.EndsWith(".cas", StringComparison.OrdinalIgnoreCase)
            || doc.Includes.Count == 0
        )
        {
            loaded = new LoadedCascode(resolvedPath, resolvedPath, workspaceRoot, doc);
            return true;
        }

        // Source files with includes must be linked to a self-contained .cai before emission/simulation.
        // Prefer a caller-provided artifacts directory; otherwise use build/link.
        var outDir = string.IsNullOrWhiteSpace(linkArtifactsDir)
            ? Path.Combine(
                Path.GetDirectoryName(resolvedPath) ?? Directory.GetCurrentDirectory(),
                "build",
                "link"
            )
            : Path.GetFullPath(linkArtifactsDir);
        Directory.CreateDirectory(outDir);

        var link = CascodeLinker.LinkFile(resolvedPath, outDir, workspaceRoot, logger);
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

        loaded = new LoadedCascode(resolvedPath, linkedPath, workspaceRoot, linkedDoc);
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
