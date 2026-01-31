using System;
using System.IO;
using Cascode.Bench;
using Cascode.Language;
using Microsoft.Extensions.Logging;

namespace Cascode.Cli.Services;

internal static class CascodeEmitPipeline
{
    public static ValidatedEmitResult ValidateAndEmit(
        CascodeDocument doc,
        string outputDir,
        BenchBackendType backend,
        string workspaceRoot,
        string? pdkRoot,
        ILogger logger
    )
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(outputDir);
        ArgumentNullException.ThrowIfNull(workspaceRoot);
        ArgumentNullException.ThrowIfNull(logger);

        Directory.CreateDirectory(outputDir);

        var includeRoot = string.IsNullOrWhiteSpace(pdkRoot) ? workspaceRoot : pdkRoot;
        var includeResolver = PdkBenchIncludeResolver.Create(includeRoot, logger);
        return SpiceEmitter.ValidateAndEmit(
            doc,
            outputDir,
            backend,
            workspaceRoot,
            includeResolver
        );
    }
}
