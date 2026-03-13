using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Cascode.Language.Validation;
using Microsoft.Extensions.Logging;

namespace Cascode.Language;

public sealed record CascodeLinkResult(
    bool Success,
    string? LinkedCasPath,
    string? SynthYamlPath,
    IReadOnlyList<Diagnostic> Diagnostics
);

public static class CascodeLinker
{
    private static readonly HashSet<string> BuiltinMeasurementFunctions = new(
        new[]
        {
            "transfer",
            "voltage",
            "current",
            "sparam",
            "db20",
            "db10",
            "noise",
            "input_referred_noise",
            "quiescent_power",
            "abs",
            "sqrt",
            "period",
            "op_param",
            "duration",
            "mean",
            "harmonic_power",
            "thd",
        },
        StringComparer.Ordinal
    );

    public static CascodeLinkResult LinkFile(
        string entryPath,
        string outputDir,
        string workspaceRoot,
        ILogger? logger = null
    ) =>
        LinkFile(entryPath, outputDir, new[] { workspaceRoot }, CascodeLinkOptions.Default, logger);

    public static CascodeLinkResult LinkFile(
        string entryPath,
        string outputDir,
        string workspaceRoot,
        CascodeLinkOptions options,
        ILogger? logger = null
    ) => LinkFile(entryPath, outputDir, new[] { workspaceRoot }, options, logger);

    public static CascodeLinkResult LinkFile(
        string entryPath,
        string outputDir,
        IReadOnlyList<string> searchRoots,
        CascodeLinkOptions options,
        ILogger? logger = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDir);
        ArgumentNullException.ThrowIfNull(searchRoots);
        ArgumentNullException.ThrowIfNull(options);

        entryPath = Path.GetFullPath(entryPath);
        outputDir = Path.GetFullPath(outputDir);
        var resolvedRoots = searchRoots
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(Path.GetFullPath)
            .ToList();
        var workspaceRoot = resolvedRoots.Count > 0 ? resolvedRoots[0] : entryPath;

        if (!File.Exists(entryPath))
        {
            return new CascodeLinkResult(
                false,
                null,
                null,
                new[]
                {
                    new Diagnostic(
                        $"CAS1001: Link input file not found: {entryPath}",
                        DiagnosticSeverity.Error,
                        entryPath,
                        1,
                        1
                    ),
                }
            );
        }

        var diagnostics = new List<Diagnostic>();

        // Link is dependency-driven:
        // - Includes declare library search roots
        // - We only parse and merge files needed to satisfy referenced symbols.
        //
        // This is required to keep `include lib.std` usable even if that library contains
        // sources that are not relevant to the current design (or are legacy syntax).
        //
        // Includes are resolved by file-level "library ..." headers (not by directory alone).
        // This enables namespace inheritance and avoids parsing unrelated files.
        var libraryIndex = CascodeLibraryIndex.Build(resolvedRoots);
        var includedDocs = new List<LinkedDocument>();
        var parsedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new Dictionary<string, CandidateSelection>(
            StringComparer.OrdinalIgnoreCase
        );
        var readCache = new Dictionary<string, CascodeReadResult>(StringComparer.OrdinalIgnoreCase);

        var required = new RequiredSymbols();

        CascodeReadResult? TryRead(string path)
        {
            path = Path.GetFullPath(path);

            if (readCache.TryGetValue(path, out var cached))
            {
                return cached;
            }

            try
            {
                using var reader = File.OpenText(path);
                // Linker must be able to parse candidate files before the full dependency graph is
                // known; defer bundle desugaring and bench validation until after merge.
                var read = CascodeReader.TryRead(reader, CascodeParseOptions.SyntaxOnly, path);
                readCache[path] = read;
                return read;
            }
            catch
            {
                return null;
            }
        }

        bool TryAddDoc(string path)
        {
            path = Path.GetFullPath(path);
            if (!parsedPaths.Add(path))
            {
                return false;
            }

            var read = TryRead(path);
            if (read is null || !read.Success || read.Document is null)
            {
                // Only surface diagnostics for the entry path; other candidate failures are
                // only relevant if we end up unable to resolve a required symbol.
                return false;
            }

            diagnostics.AddRange(read.Diagnostics);
            var selectedDoc = ApplyIncludeSelection(read.Document, path, candidates);
            includedDocs.Add(
                new LinkedDocument
                {
                    Path = path,
                    Document = selectedDoc,
                    SourceDocument = read.Document,
                }
            );
            AddIncludeCandidates(
                selectedDoc,
                path,
                workspaceRoot,
                libraryIndex,
                candidates,
                diagnostics,
                options.IncludePolicy
            );
            CollectRequiredSymbols(selectedDoc, required);
            return true;
        }

        // Always parse the entry file and surface any diagnostics.
        var entryFullPath = Path.GetFullPath(entryPath);
        if (!parsedPaths.Add(entryFullPath))
        {
            // Should be impossible, but keep behavior predictable.
            parsedPaths.Clear();
            parsedPaths.Add(entryFullPath);
        }

        var entryRead = TryRead(entryFullPath);
        if (entryRead is null)
        {
            diagnostics.Add(
                new Diagnostic(
                    $"CAS1001: Link input file not found: {entryFullPath}",
                    DiagnosticSeverity.Error,
                    entryFullPath,
                    1,
                    1
                )
            );
            return new CascodeLinkResult(false, null, null, diagnostics);
        }

        diagnostics.AddRange(entryRead.Diagnostics);
        if (!entryRead.Success || entryRead.Document is null)
        {
            return new CascodeLinkResult(false, null, null, diagnostics);
        }

        includedDocs.Add(
            new LinkedDocument
            {
                Path = entryFullPath,
                Document = entryRead.Document,
                SourceDocument = entryRead.Document,
            }
        );
        AddIncludeCandidates(
            entryRead.Document,
            entryFullPath,
            workspaceRoot,
            libraryIndex,
            candidates,
            diagnostics,
            options.IncludePolicy
        );
        CollectRequiredSymbols(entryRead.Document, required);
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return new CascodeLinkResult(false, null, null, diagnostics);
        }

        // Iteratively resolve references using include-provided candidates.
        // This avoids eagerly parsing every file in large libraries.
        var progress = true;
        while (progress)
        {
            progress = false;

            progress |= ResolveMissing(
                "bundle",
                required.Bundles,
                name => includedDocs.Any(d => d.Document.BundleTypes.Any(b => b.Name == name)),
                (content, name) => CascodeSymbolUtils.ContainsKeywordDecl(content, "bundle", name),
                TryAddDoc,
                TryRead,
                candidates,
                parsedPaths
            );

            progress |= ResolveMissing(
                "interface",
                required.Traits,
                name => includedDocs.Any(d => d.Document.Traits.Any(t => t.Name == name)),
                (content, name) =>
                    CascodeSymbolUtils.ContainsKeywordDecl(content, "interface", name),
                TryAddDoc,
                TryRead,
                candidates,
                parsedPaths
            );

            progress |= ResolveMissing(
                "bench",
                required.Benches,
                name => includedDocs.Any(d => d.Document.BenchDefinitions.Any(b => b.Name == name)),
                (content, name) => CascodeSymbolUtils.ContainsKeywordDecl(content, "bench", name),
                TryAddDoc,
                TryRead,
                candidates,
                parsedPaths
            );

            progress |= ResolveMissing(
                "function",
                required.Functions,
                name => includedDocs.Any(d => d.Document.Functions.Any(f => f.Name == name)),
                (content, name) =>
                    CascodeSymbolUtils.ContainsKeywordDecl(content, "function", name),
                TryAddDoc,
                TryRead,
                candidates,
                parsedPaths
            );

            progress |= ResolveMissing(
                "primitive",
                required.Primitives,
                name => includedDocs.Any(d => d.Document.Primitives.Any(p => p.Name == name)),
                CascodeSymbolUtils.ContainsPrimitiveDecl,
                TryAddDoc,
                TryRead,
                candidates,
                parsedPaths
            );

            progress |= ResolveMissing(
                "circuit",
                required.Circuits,
                name => includedDocs.Any(d => d.Document.Circuits.Any(c => c.Name == name)),
                (content, name) => CascodeSymbolUtils.ContainsKeywordDecl(content, "circuit", name),
                TryAddDoc,
                TryRead,
                candidates,
                parsedPaths
            );
        }

        // Any remaining missing symbols are link errors.
        AddUnresolvedDiagnostics(
            required,
            includedDocs,
            entryPath,
            diagnostics,
            libraryIndex,
            options.IncludePolicy
        );
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return new CascodeLinkResult(false, null, null, diagnostics);
        }

        var validatedMerged = BuildValidatedMergedDocument(
            includedDocs,
            diagnostics,
            logger,
            out var mergedDocument
        );
        if (!validatedMerged)
        {
            return new CascodeLinkResult(false, null, null, diagnostics);
        }

        CascodeDocument linked =
            options.BenchMode == LinkBenchMode.None
                ? BuildIncludePrunedDocument(
                    entryRead.Document,
                    includedDocs,
                    workspaceRoot,
                    diagnostics
                )
                : mergedDocument;

        Directory.CreateDirectory(outputDir);

        var suffix = DetermineHighestLevelSuffix(linked);
        var baseName = GetLinkBaseName(entryPath);
        var linkedPath = Path.Combine(outputDir, $"{baseName}.{suffix}.cai");

        // Extract synth blocks into sidecar and remove from the linked output.
        var (linkedWithoutSynth, synthYaml) = ExtractSynthToYaml(linked);
        var synthPath = synthYaml is null
            ? null
            : Path.Combine(outputDir, $"{baseName}.synth.yaml");

        using (var writer = File.CreateText(linkedPath))
        {
            CascodeWriter.Write(linkedWithoutSynth, writer);
        }

        if (synthYaml is not null && synthPath is not null)
        {
            File.WriteAllText(synthPath, synthYaml);
        }

        logger?.LogInformation("Linked '{Entry}' -> '{Out}'", entryPath, linkedPath);

        return new CascodeLinkResult(true, linkedPath, synthPath, diagnostics);
    }

    private sealed class RequiredSymbols
    {
        public HashSet<string> Bundles { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Traits { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Benches { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Functions { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Primitives { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Circuits { get; } = new(StringComparer.Ordinal);
    }

    private sealed class LinkedDocument
    {
        public required string Path { get; init; }
        public required CascodeDocument Document { get; init; }
        public required CascodeDocument SourceDocument { get; init; }
    }

    private sealed class CandidateSelection
    {
        public bool AllowAll { get; set; } = true;
        public HashSet<string> AllowedSymbols { get; } = new(StringComparer.Ordinal);
    }

    private sealed record IncludeTarget(string Path, string? SymbolName);

    private enum SymbolKind
    {
        Bundle,
        Trait,
        Bench,
        Function,
        Primitive,
        Circuit,
    }

    private static void AddIncludeCandidates(
        CascodeDocument doc,
        string parsedFilePath,
        string workspaceRoot,
        CascodeLibraryIndex libraryIndex,
        Dictionary<string, CandidateSelection> candidates,
        List<Diagnostic> diagnostics,
        LinkIncludePolicy includePolicy
    )
    {
        if (includePolicy != LinkIncludePolicy.ExplicitOnly)
        {
            // Namespace inheritance: a file in "lib.std.bench" can see "lib.std" and "lib" automatically.
            AddNamespaceInheritanceCandidates(doc.FileLibrary, libraryIndex, candidates);
        }

        foreach (var inc in doc.Includes)
        {
            var targets = ResolveIncludeTargets(
                inc.Name,
                parsedFilePath,
                workspaceRoot,
                libraryIndex
            );
            if (targets.Count == 0)
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS1008: Unresolved include '{inc.Name}' in '{parsedFilePath}'.",
                        DiagnosticSeverity.Error,
                        parsedFilePath,
                        1,
                        1
                    )
                );
                continue;
            }

            foreach (var t in targets)
            {
                AddCandidate(candidates, t.Path, t.SymbolName);
            }
        }
    }

    private static void AddCandidate(
        Dictionary<string, CandidateSelection> candidates,
        string path,
        string? symbolName
    )
    {
        var fullPath = Path.GetFullPath(path);
        if (!candidates.TryGetValue(fullPath, out var selection))
        {
            selection = new CandidateSelection();
            candidates[fullPath] = selection;
        }

        if (string.IsNullOrWhiteSpace(symbolName))
        {
            selection.AllowAll = true;
            selection.AllowedSymbols.Clear();
            return;
        }

        if (selection.AllowAll && selection.AllowedSymbols.Count == 0)
        {
            selection.AllowAll = false;
        }

        if (!selection.AllowAll)
        {
            selection.AllowedSymbols.Add(symbolName);
        }
    }

    private static CascodeDocument ApplyIncludeSelection(
        CascodeDocument source,
        string path,
        IReadOnlyDictionary<string, CandidateSelection> candidates
    )
    {
        path = Path.GetFullPath(path);
        if (!candidates.TryGetValue(path, out var selection) || selection.AllowAll)
        {
            return source;
        }

        var allowed = selection.AllowedSymbols;
        return new CascodeDocument
        {
            VersionMajor = source.VersionMajor,
            VersionMinor = source.VersionMinor,
            Includes = source.Includes,
            FileLibrary = source.FileLibrary,
            Functions = source.Functions,
            BundleTypes = source.BundleTypes.Where(b => allowed.Contains(b.Name)).ToList(),
            Traits = source.Traits.Where(t => allowed.Contains(t.Name)).ToList(),
            BenchDefinitions = source
                .BenchDefinitions.Where(b => allowed.Contains(b.Name))
                .ToList(),
            Primitives = source.Primitives.Where(p => allowed.Contains(p.Name)).ToList(),
            Circuits = source.Circuits.Where(c => allowed.Contains(c.Name)).ToList(),
        };
    }

    private static void AddNamespaceInheritanceCandidates(
        string? fileLibrary,
        CascodeLibraryIndex libraryIndex,
        Dictionary<string, CandidateSelection> candidates
    )
    {
        if (string.IsNullOrWhiteSpace(fileLibrary))
        {
            return;
        }

        var normalized = CascodeLibraryIndex.NormalizeLibraryName(fileLibrary);
        var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return;
        }

        // Same-library files are also visible (for split libraries like lib.std.bench/*).
        foreach (var p in libraryIndex.FindExact(normalized))
        {
            AddCandidate(candidates, p, symbolName: null);
        }

        // Ancestors only (no descendants): lib.std.bench inherits lib.std and lib.
        for (var i = parts.Length - 1; i >= 1; i--)
        {
            var ancestor = string.Join('.', parts.Take(i));
            foreach (var p in libraryIndex.FindExact(ancestor))
            {
                AddCandidate(candidates, p, symbolName: null);
            }
        }
    }

    private static void CollectRequiredSymbols(CascodeDocument doc, RequiredSymbols required)
    {
        // Ports/terminals referencing bundle types
        foreach (var c in doc.Circuits)
        {
            if (c.Traits is not null)
            {
                foreach (var t in c.Traits)
                {
                    required.Traits.Add(t);
                }
            }

            foreach (var p in c.Ports)
            {
                AddBundleIfNeeded(p.Type, required);
            }

            foreach (var binding in c.BenchBindings)
            {
                required.Benches.Add(binding.BenchName);
            }

            if (c.Slot is not null)
            {
                foreach (var inst in c.Slot.Instances)
                {
                    required.Circuits.Add(inst.Type);
                }
            }

            if (c.Fill is not null)
            {
                foreach (var inst in c.Fill.Instances)
                {
                    foreach (var param in inst.Params.Values)
                    {
                        CollectFunctionReferencesFromParamValue(param, required);
                    }
                }

                foreach (var dev in c.Fill.Devices)
                {
                    required.Primitives.Add(dev.Primitive);
                }

                foreach (var inst in c.Fill.Instances)
                {
                    required.Circuits.Add(inst.Type);
                }

                foreach (var attach in c.Fill.Attaches)
                {
                    // Via: "Iface::TargetIface"
                    var parts = attach.Via.Split(new[] { "::" }, StringSplitOptions.None);
                    if (parts.Length == 2)
                    {
                        required.Traits.Add(parts[0]);
                        required.Traits.Add(parts[1]);
                    }
                }
            }
        }

        foreach (var t in doc.Traits)
        {
            foreach (var p in t.Ports)
            {
                AddBundleIfNeeded(p.Type, required);
            }

            foreach (var conn in t.Connectors)
            {
                required.Traits.Add(conn.TargetTrait);
            }

            foreach (var binding in t.BenchBindings)
            {
                required.Benches.Add(binding.BenchName);
            }
        }

        foreach (var b in doc.BenchDefinitions)
        {
            if (!string.IsNullOrWhiteSpace(b.BaseBench))
            {
                required.Benches.Add(b.BaseBench);
            }

            CollectBenchFunctionRequirements(b, required);

            foreach (var term in b.Terminals)
            {
                if (term.Type is not null)
                {
                    AddBundleIfNeeded(term.Type, required);
                }
            }

            // Fill blocks in benches may instantiate harness primitives; don't treat those as circuit deps.
            if (b.Fill is not null)
            {
                foreach (var inst in b.Fill.Instances)
                {
                    if (!IsHarnessPrimitive(inst.Type))
                    {
                        required.Circuits.Add(inst.Type);
                    }
                }

                foreach (var dev in b.Fill.Devices)
                {
                    required.Primitives.Add(dev.Primitive);
                }
            }
        }

        foreach (var fn in doc.Functions)
        {
            CollectFunctionReferencesFromStatements(fn.Body, required);
        }
    }

    private static void CollectBenchFunctionRequirements(
        BenchDefinition bench,
        RequiredSymbols required
    )
    {
        foreach (var parameter in bench.Parameters)
        {
            if (parameter.Default is not null)
            {
                CollectFunctionReferencesFromExpr(parameter.Default, required);
            }
        }

        foreach (var analysis in bench.Analyses)
        {
            foreach (var value in analysis.Parameters.Values)
            {
                CollectFunctionReferencesFromExpr(value, required);
            }
        }

        var benchLocalNames = CollectBenchLocalNames(bench);
        foreach (var fn in bench.Functions)
        {
            CollectFunctionReferencesFromStatements(fn.Body, required, benchLocalNames);
        }

        foreach (var measurement in bench.Measurements)
        {
            // Bench-local functions and sibling measurements stay inside the bench scope, so
            // exclude them from global function requirements.
            CollectFunctionReferencesFromStatements(measurement.Body, required, benchLocalNames);
        }

        if (bench.Fill is null)
        {
            return;
        }

        foreach (var inst in bench.Fill.Instances)
        {
            foreach (var param in inst.Params.Values)
            {
                CollectFunctionReferencesFromParamValue(param, required, benchLocalNames);
            }
        }
    }

    private static HashSet<string> CollectBenchLocalNames(BenchDefinition bench)
    {
        var benchMeasurementNames = bench
            .Measurements.Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);
        var benchFunctionNames = bench
            .Functions.Select(fn => fn.Name)
            .ToHashSet(StringComparer.Ordinal);
        var benchLocalNames = new HashSet<string>(benchMeasurementNames, StringComparer.Ordinal);
        benchLocalNames.UnionWith(benchFunctionNames);
        return benchLocalNames;
    }

    private static void CollectFunctionReferencesFromParamValue(
        ParamValue paramValue,
        RequiredSymbols required,
        ISet<string>? excludedNames = null
    )
    {
        if (
            string.IsNullOrWhiteSpace(paramValue.Symbolic)
            || !CascodeAstBuilder.TryParseMeasurementExprText(
                paramValue.Symbolic,
                out var parsed,
                out _
            )
            || parsed is null
        )
        {
            return;
        }

        CollectFunctionReferencesFromExpr(parsed, required, excludedNames);
    }

    private static void CollectFunctionReferencesFromStatements(
        IEnumerable<BenchStatement> statements,
        RequiredSymbols required,
        ISet<string>? excludedNames = null
    )
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case BenchVarDecl decl:
                    CollectFunctionReferencesFromExpr(decl.Expr, required, excludedNames);
                    break;
                case BenchReturn ret:
                    CollectFunctionReferencesFromExpr(ret.Expr, required, excludedNames);
                    break;
                case BenchIf bif:
                    CollectFunctionReferencesFromBoolExpr(bif.Condition, required, excludedNames);
                    CollectFunctionReferencesFromStatements(bif.ThenBody, required, excludedNames);
                    if (bif.ElseBody is not null)
                    {
                        CollectFunctionReferencesFromStatements(
                            bif.ElseBody,
                            required,
                            excludedNames
                        );
                    }
                    break;
            }
        }
    }

    private static void CollectFunctionReferencesFromBoolExpr(
        BoolExpr expr,
        RequiredSymbols required,
        ISet<string>? excludedNames = null
    )
    {
        switch (expr)
        {
            case BoolTruthy truthy:
                CollectFunctionReferencesFromExpr(truthy.Expr, required, excludedNames);
                break;
            case BoolCompare cmp:
                CollectFunctionReferencesFromExpr(cmp.Left, required, excludedNames);
                CollectFunctionReferencesFromExpr(cmp.Right, required, excludedNames);
                break;
        }
    }

    private static void CollectFunctionReferencesFromExpr(
        MeasurementExpr expr,
        RequiredSymbols required,
        ISet<string>? excludedNames = null
    )
    {
        switch (expr)
        {
            case MeasurementCall call:
                if (
                    !IsBuiltinMeasurementFunction(call.Name)
                    && (excludedNames is null || !excludedNames.Contains(call.Name))
                )
                {
                    required.Functions.Add(call.Name);
                }
                foreach (var arg in call.Args)
                {
                    CollectFunctionReferencesFromExpr(arg.Value, required, excludedNames);
                }
                break;
            case MeasurementMethodCall methodCall:
                CollectFunctionReferencesFromExpr(methodCall.Receiver, required, excludedNames);
                foreach (var arg in methodCall.Args)
                {
                    CollectFunctionReferencesFromExpr(arg.Value, required, excludedNames);
                }
                break;
            case MeasurementConditional conditional:
                CollectFunctionReferencesFromBoolExpr(
                    conditional.Condition,
                    required,
                    excludedNames
                );
                CollectFunctionReferencesFromExpr(conditional.ThenExpr, required, excludedNames);
                CollectFunctionReferencesFromExpr(conditional.ElseExpr, required, excludedNames);
                break;
            case MeasurementBinary binary:
                CollectFunctionReferencesFromExpr(binary.Left, required, excludedNames);
                CollectFunctionReferencesFromExpr(binary.Right, required, excludedNames);
                break;
            case MeasurementUnary unary:
                CollectFunctionReferencesFromExpr(unary.Operand, required, excludedNames);
                break;
            case MeasurementBenchMeasurementRef benchRef:
                foreach (var arg in benchRef.Args)
                {
                    CollectFunctionReferencesFromExpr(arg.Expr, required, excludedNames);
                }
                break;
        }
    }

    private static bool IsBuiltinMeasurementFunction(string name) =>
        BuiltinMeasurementFunctions.Contains(name);

    private static void AddBundleIfNeeded(string typeName, RequiredSymbols required)
    {
        // Built-in domains are not bundle types.
        if (
            typeName.Equals("analog", StringComparison.OrdinalIgnoreCase)
            || typeName.Equals("digital", StringComparison.OrdinalIgnoreCase)
            || typeName.Equals("mixed", StringComparison.OrdinalIgnoreCase)
            || typeName.Equals("clock", StringComparison.OrdinalIgnoreCase)
            || typeName.Equals("rf", StringComparison.OrdinalIgnoreCase)
            || typeName.Equals("bias", StringComparison.OrdinalIgnoreCase)
            || typeName.Equals("supply", StringComparison.OrdinalIgnoreCase)
            || typeName.Equals("ground", StringComparison.OrdinalIgnoreCase)
        )
        {
            return;
        }

        required.Bundles.Add(typeName);
    }

    private static bool IsHarnessPrimitive(string typeName)
    {
        // Keep in sync with the subset supported by BenchRuntime.
        return typeName.Equals("GND", StringComparison.OrdinalIgnoreCase)
            || typeName.Equals("VDC", StringComparison.OrdinalIgnoreCase)
            || typeName.Equals("VAC", StringComparison.OrdinalIgnoreCase)
            || typeName.Equals("VSIN", StringComparison.OrdinalIgnoreCase)
            || typeName.Equals("Port", StringComparison.OrdinalIgnoreCase)
            || typeName.Equals("Impedance", StringComparison.OrdinalIgnoreCase)
            || typeName.Equals("Impedor", StringComparison.OrdinalIgnoreCase)
            || typeName.Equals("Kick", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ResolveMissing(
        string kind,
        HashSet<string> required,
        Func<string, bool> isResolved,
        Func<string, string, bool> mightDefine,
        Func<string, bool> tryAddDoc,
        Func<string, CascodeReadResult?> tryRead,
        IReadOnlyDictionary<string, CandidateSelection> candidates,
        HashSet<string> parsedPaths
    )
    {
        // Work on a snapshot to allow `tryAddDoc` to extend candidates.
        var missing = required
            .Where(name => !isResolved(name))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        foreach (var name in missing)
        {
            foreach (var path in candidates.Keys.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var full = Path.GetFullPath(path);
                if (parsedPaths.Contains(full))
                {
                    continue;
                }

                string content;
                try
                {
                    content = File.ReadAllText(full);
                }
                catch
                {
                    continue;
                }

                if (!mightDefine(content, name))
                {
                    continue;
                }

                var read = tryRead(full);
                if (read is null || !read.Success || read.Document is null)
                {
                    continue;
                }

                // Verify the definition exists in this document before accepting it.
                var defines = kind switch
                {
                    "bundle" => read.Document.BundleTypes.Any(b => b.Name == name),
                    "interface" => read.Document.Traits.Any(t => t.Name == name),
                    "bench" => read.Document.BenchDefinitions.Any(b => b.Name == name),
                    "function" => read.Document.Functions.Any(f => f.Name == name),
                    "primitive" => read.Document.Primitives.Any(p => p.Name == name),
                    "circuit" => read.Document.Circuits.Any(c => c.Name == name),
                    _ => false,
                };

                if (!defines)
                {
                    continue;
                }

                if (tryAddDoc(full))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void AddUnresolvedDiagnostics(
        RequiredSymbols required,
        IReadOnlyList<LinkedDocument> includedDocs,
        string entryPath,
        List<Diagnostic> diagnostics,
        CascodeLibraryIndex libraryIndex,
        LinkIncludePolicy includePolicy
    )
    {
        void AddMissing(string kind, IEnumerable<string> names, Func<string, bool> resolved)
        {
            foreach (
                var name in names.Where(n => !resolved(n)).OrderBy(n => n, StringComparer.Ordinal)
            )
            {
                var suggestion =
                    includePolicy == LinkIncludePolicy.ExplicitOnly
                    && TryBuildIncludeSuggestion(name, libraryIndex, out var includeHint)
                        ? $" Add include {includeHint}."
                        : string.Empty;
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS1009: Unresolved {kind} reference '{name}' while linking '{entryPath}'.{suggestion}",
                        DiagnosticSeverity.Error,
                        entryPath,
                        1,
                        1
                    )
                );
            }
        }

        AddMissing(
            "bundle",
            required.Bundles,
            name => includedDocs.Any(d => d.Document.BundleTypes.Any(b => b.Name == name))
        );
        AddMissing(
            "interface",
            required.Traits,
            name => includedDocs.Any(d => d.Document.Traits.Any(t => t.Name == name))
        );
        AddMissing(
            "bench",
            required.Benches,
            name => includedDocs.Any(d => d.Document.BenchDefinitions.Any(b => b.Name == name))
        );
        AddMissing(
            "function",
            required.Functions,
            name => includedDocs.Any(d => d.Document.Functions.Any(f => f.Name == name))
        );
        AddMissing(
            "primitive",
            required.Primitives,
            name => includedDocs.Any(d => d.Document.Primitives.Any(p => p.Name == name))
        );
        AddMissing(
            "circuit",
            required.Circuits,
            name => includedDocs.Any(d => d.Document.Circuits.Any(c => c.Name == name))
        );
    }

    private static bool TryBuildIncludeSuggestion(
        string symbolName,
        CascodeLibraryIndex libraryIndex,
        out string includeHint
    )
    {
        includeHint = string.Empty;
        var candidates = libraryIndex.FindSymbolIncludeCandidates(symbolName);
        if (candidates.Count == 0)
        {
            return false;
        }

        includeHint = candidates[0];
        return true;
    }

    private static IReadOnlyList<IncludeTarget> ResolveIncludeTargets(
        string includeName,
        string includingFilePath,
        string workspaceRoot,
        CascodeLibraryIndex libraryIndex
    ) =>
        ResolveIncludeTargets(
            includeName,
            includingFilePath,
            new[] { workspaceRoot },
            libraryIndex
        );

    private static IReadOnlyList<IncludeTarget> ResolveIncludeTargets(
        string includeName,
        string includingFilePath,
        IReadOnlyList<string> searchRoots,
        CascodeLibraryIndex libraryIndex
    )
    {
        var normalized = includeName.Trim();
        if (normalized.Contains('.', StringComparison.Ordinal))
        {
            var byLibrary = libraryIndex.FindByPrefix(normalized);
            if (byLibrary.Count > 0)
            {
                return byLibrary.Select(p => new IncludeTarget(p, null)).ToList();
            }

            var symbolTargets = TryResolveSymbolInclude(normalized, libraryIndex);
            if (symbolTargets.Count > 0)
            {
                return symbolTargets;
            }
        }

        // Directory-based include: iterate roots in order.
        if (
            includeName.Contains('.', StringComparison.Ordinal)
            || includeName.Contains('_', StringComparison.Ordinal)
        )
        {
            foreach (var root in searchRoots)
            {
                var targets = TryResolveIncludeAsDirectoryOrFile(root, includeName);
                if (targets.Count > 0)
                    return targets;

                var alt = TryResolveIncludeWithLastSegmentRewrite(
                    root,
                    includeName,
                    fromLast: "benches",
                    toLast: "bench"
                );
                if (alt.Count > 0)
                    return alt;
            }
        }
        else
        {
            var local = Path.Combine(
                Path.GetDirectoryName(includingFilePath)!,
                includeName + ".cas"
            );
            if (File.Exists(local))
            {
                return new[] { new IncludeTarget(local, null) };
            }

            foreach (var root in searchRoots)
            {
                var candidate = Path.Combine(root, includeName + ".cas");
                if (File.Exists(candidate))
                {
                    return new[] { new IncludeTarget(candidate, null) };
                }
            }
        }

        return Array.Empty<IncludeTarget>();
    }

    private static List<IncludeTarget> TryResolveIncludeAsDirectoryOrFile(
        string workspaceRoot,
        string name
    )
    {
        var normalized = name.Replace('.', Path.DirectorySeparatorChar)
            .Replace('_', Path.DirectorySeparatorChar);
        var dir = Path.Combine(workspaceRoot, normalized);
        if (Directory.Exists(dir))
        {
            return Directory
                .GetFiles(dir, "*.cas", SearchOption.AllDirectories)
                .OrderBy(Path.GetFullPath, StringComparer.OrdinalIgnoreCase)
                .Select(path => new IncludeTarget(path, null))
                .ToList();
        }

        var file = dir + ".cas";
        if (File.Exists(file))
        {
            return new List<IncludeTarget> { new(file, null) };
        }

        return new List<IncludeTarget>();
    }

    private static List<IncludeTarget> TryResolveIncludeWithLastSegmentRewrite(
        string workspaceRoot,
        string includeName,
        string fromLast,
        string toLast
    )
    {
        // Split on both '.' and '_' while preserving overall intent.
        var parts = includeName.Split(new[] { '.', '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return new List<IncludeTarget>();

        if (!parts[^1].Equals(fromLast, StringComparison.OrdinalIgnoreCase))
            return new List<IncludeTarget>();

        parts[^1] = toLast;
        var rewritten = string.Join(Path.DirectorySeparatorChar, parts);
        return TryResolveIncludeAsDirectoryOrFile(workspaceRoot, rewritten);
    }

    private static List<IncludeTarget> TryResolveSymbolInclude(
        string normalizedIncludeName,
        CascodeLibraryIndex libraryIndex
    )
    {
        var parts = normalizedIncludeName.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return new List<IncludeTarget>();
        }

        for (var split = parts.Length - 1; split >= 1; split--)
        {
            if (parts.Length - split != 1)
            {
                continue;
            }

            var libraryName = string.Join('.', parts.Take(split));
            var symbolName = parts[split];

            var files = libraryIndex.FindExact(libraryName);
            if (files.Count == 0)
            {
                continue;
            }

            var matches = new List<IncludeTarget>();
            foreach (var path in files)
            {
                string content;
                try
                {
                    content = File.ReadAllText(path);
                }
                catch
                {
                    continue;
                }

                if (!CascodeSymbolUtils.MightDefineAnySymbol(content, symbolName))
                {
                    continue;
                }

                matches.Add(new IncludeTarget(path, symbolName));
            }

            if (matches.Count > 0)
            {
                return matches.OrderBy(m => m.Path, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        return new List<IncludeTarget>();
    }

    private sealed record SymbolSource<T>(T Definition, string IncludePath)
        where T : class;

    private sealed class LocalSymbols
    {
        public HashSet<string> Bundles { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Traits { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Benches { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Functions { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Primitives { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Circuits { get; } = new(StringComparer.Ordinal);
    }

    private static bool BuildValidatedMergedDocument(
        IReadOnlyList<LinkedDocument> includedDocs,
        List<Diagnostic> diagnostics,
        ILogger? logger,
        out CascodeDocument mergedDocument
    )
    {
        mergedDocument = MergeDocuments(
            includedDocs.Select(d => d.Document).ToList(),
            diagnostics,
            logger
        );
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return false;
        }

        mergedDocument = BundleDesugarer.Desugar(mergedDocument);
        mergedDocument = BenchInheritanceResolver.Resolve(mergedDocument, diagnostics);
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return false;
        }

        mergedDocument = BenchBindingExtender.Apply(mergedDocument, diagnostics);
        BenchSemanticChecker.Check(mergedDocument, diagnostics);
        CompleteDocumentSemanticValidator.Check(mergedDocument, diagnostics);
        return !diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    }

    private static CascodeDocument BuildIncludePrunedDocument(
        CascodeDocument entryDoc,
        IReadOnlyList<LinkedDocument> includedDocs,
        string workspaceRoot,
        List<Diagnostic> diagnostics
    )
    {
        var bundleSources = BuildSymbolSources(
            includedDocs,
            workspaceRoot,
            d => d.Document.BundleTypes,
            d => d.Name
        );
        var traitSources = BuildSymbolSources(
            includedDocs,
            workspaceRoot,
            d => d.Document.Traits,
            d => d.Name
        );
        var benchSources = BuildSymbolSources(
            includedDocs,
            workspaceRoot,
            d => d.Document.BenchDefinitions,
            d => d.Name
        );
        var functionSources = BuildSymbolSources(
            includedDocs,
            workspaceRoot,
            d => d.Document.Functions,
            d => d.Name
        );
        var primitiveSources = BuildSymbolSources(
            includedDocs,
            workspaceRoot,
            d => d.Document.Primitives,
            d => d.Name
        );
        var circuitSources = BuildSymbolSources(
            includedDocs,
            workspaceRoot,
            d => d.Document.Circuits,
            d => d.Name
        );

        var localSymbols = CollectLocalSymbols(entryDoc);
        var seed = new RequiredSymbols();
        CollectRequiredSymbols(entryDoc, seed);
        RestrictBenchRequirementsToConstraintReachability(
            entryDoc,
            traitSources,
            benchSources,
            seed
        );
        RestrictFunctionRequirementsToConstraintReachability(entryDoc, seed);

        var queue = new Queue<(SymbolKind Kind, string Name)>();
        EnqueueRequired(seed, queue);

        var external = new HashSet<(SymbolKind Kind, string Name)>();
        while (queue.Count > 0)
        {
            var symbol = queue.Dequeue();
            if (IsLocalSymbol(symbol.Kind, symbol.Name, localSymbols))
            {
                continue;
            }

            if (!external.Add(symbol))
            {
                continue;
            }

            var deps = CollectSymbolDependencies(
                symbol.Kind,
                symbol.Name,
                traitSources,
                benchSources,
                functionSources,
                circuitSources
            );
            if (symbol.Kind is SymbolKind.Trait or SymbolKind.Circuit)
            {
                deps.Benches.Clear();
            }
            EnqueueRequired(deps, queue);
        }

        var includes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (
            var symbol in external.OrderBy(s => s.Kind).ThenBy(s => s.Name, StringComparer.Ordinal)
        )
        {
            if (
                TryGetIncludePath(
                    symbol.Kind,
                    symbol.Name,
                    bundleSources,
                    traitSources,
                    benchSources,
                    functionSources,
                    primitiveSources,
                    circuitSources,
                    out var includePath
                )
            )
            {
                includes.Add(includePath);
            }
            else
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS1010: Could not determine include path for {symbol.Kind.ToString().ToLowerInvariant()} '{symbol.Name}'.",
                        DiagnosticSeverity.Warning,
                        "<link>",
                        1,
                        1
                    )
                );
            }
        }

        return new CascodeDocument
        {
            VersionMajor = entryDoc.VersionMajor,
            VersionMinor = entryDoc.VersionMinor,
            Includes = includes.Select(name => new IncludeDirective(name)).ToList(),
            FileLibrary = entryDoc.FileLibrary,
            Functions = entryDoc.Functions,
            BundleTypes = entryDoc.BundleTypes,
            Traits = entryDoc.Traits,
            BenchDefinitions = new List<BenchDefinition>(),
            Primitives = entryDoc.Primitives,
            Circuits = entryDoc.Circuits,
        };
    }

    private static void RestrictBenchRequirementsToConstraintReachability(
        CascodeDocument entryDoc,
        IReadOnlyDictionary<string, SymbolSource<TraitDefinition>> traitSources,
        IReadOnlyDictionary<string, SymbolSource<BenchDefinition>> benchSources,
        RequiredSymbols required
    )
    {
        var planningDoc = new CascodeDocument
        {
            Traits = traitSources.Values.Select(s => s.Definition).ToList(),
            BenchDefinitions = benchSources.Values.Select(s => s.Definition).ToList(),
        };

        required.Benches.Clear();
        foreach (var circuit in entryDoc.Circuits)
        {
            foreach (
                var invocation in BenchRuntime.BenchInvocationPlanner.CollectInvocations(
                    planningDoc,
                    circuit
                )
            )
            {
                required.Benches.Add(invocation.Binding.BenchName);
            }
        }
    }

    private static void RestrictFunctionRequirementsToConstraintReachability(
        CascodeDocument entryDoc,
        RequiredSymbols required
    )
    {
        var reachableBenches = required.Benches.ToHashSet(StringComparer.Ordinal);
        required.Functions.Clear();

        foreach (var circuit in entryDoc.Circuits)
        {
            if (circuit.Fill is null)
            {
                continue;
            }

            foreach (var inst in circuit.Fill.Instances)
            {
                foreach (var param in inst.Params.Values)
                {
                    CollectFunctionReferencesFromParamValue(param, required);
                }
            }
        }

        foreach (var bench in entryDoc.BenchDefinitions)
        {
            if (!reachableBenches.Contains(bench.Name))
            {
                continue;
            }

            CollectBenchFunctionRequirements(bench, required);
        }

        foreach (var function in entryDoc.Functions)
        {
            CollectFunctionReferencesFromStatements(function.Body, required);
        }
    }

    private static Dictionary<string, SymbolSource<T>> BuildSymbolSources<T>(
        IReadOnlyList<LinkedDocument> docs,
        string workspaceRoot,
        Func<LinkedDocument, IEnumerable<T>> selectSymbols,
        Func<T, string> selectName
    )
        where T : class
    {
        var map = new Dictionary<string, SymbolSource<T>>(StringComparer.Ordinal);
        foreach (var doc in docs)
        {
            foreach (var symbol in selectSymbols(doc))
            {
                var name = selectName(symbol);
                if (map.ContainsKey(name))
                {
                    continue;
                }

                map[name] = new SymbolSource<T>(
                    symbol,
                    BuildSymbolIncludePath(doc, workspaceRoot, name)
                );
            }
        }

        return map;
    }

    private static string BuildSymbolIncludePath(
        LinkedDocument document,
        string workspaceRoot,
        string symbolName
    )
    {
        if (!string.IsNullOrWhiteSpace(document.SourceDocument.FileLibrary))
        {
            var library = CascodeLibraryIndex.NormalizeLibraryName(
                document.SourceDocument.FileLibrary
            );
            return $"{library}.{symbolName}";
        }

        var relativePath = Path.GetRelativePath(workspaceRoot, document.Path);
        var withoutExtension = relativePath.EndsWith(".cas", StringComparison.OrdinalIgnoreCase)
            ? relativePath[..^".cas".Length]
            : relativePath;
        return withoutExtension
            .Replace(Path.DirectorySeparatorChar, '.')
            .Replace(Path.AltDirectorySeparatorChar, '.');
    }

    private static LocalSymbols CollectLocalSymbols(CascodeDocument document)
    {
        var symbols = new LocalSymbols();
        symbols.Bundles.UnionWith(document.BundleTypes.Select(b => b.Name));
        symbols.Traits.UnionWith(document.Traits.Select(t => t.Name));
        symbols.Benches.UnionWith(document.BenchDefinitions.Select(b => b.Name));
        symbols.Functions.UnionWith(document.Functions.Select(f => f.Name));
        symbols.Primitives.UnionWith(document.Primitives.Select(p => p.Name));
        symbols.Circuits.UnionWith(document.Circuits.Select(c => c.Name));
        return symbols;
    }

    private static bool IsLocalSymbol(SymbolKind kind, string name, LocalSymbols symbols) =>
        kind switch
        {
            SymbolKind.Bundle => symbols.Bundles.Contains(name),
            SymbolKind.Trait => symbols.Traits.Contains(name),
            SymbolKind.Bench => symbols.Benches.Contains(name),
            SymbolKind.Function => symbols.Functions.Contains(name),
            SymbolKind.Primitive => symbols.Primitives.Contains(name),
            SymbolKind.Circuit => symbols.Circuits.Contains(name),
            _ => false,
        };

    private static void EnqueueRequired(
        RequiredSymbols required,
        Queue<(SymbolKind Kind, string Name)> queue
    )
    {
        foreach (var name in required.Bundles)
        {
            queue.Enqueue((SymbolKind.Bundle, name));
        }

        foreach (var name in required.Traits)
        {
            queue.Enqueue((SymbolKind.Trait, name));
        }

        foreach (var name in required.Benches)
        {
            queue.Enqueue((SymbolKind.Bench, name));
        }

        foreach (var name in required.Functions)
        {
            queue.Enqueue((SymbolKind.Function, name));
        }

        foreach (var name in required.Primitives)
        {
            queue.Enqueue((SymbolKind.Primitive, name));
        }

        foreach (var name in required.Circuits)
        {
            queue.Enqueue((SymbolKind.Circuit, name));
        }
    }

    private static RequiredSymbols CollectSymbolDependencies(
        SymbolKind kind,
        string name,
        IReadOnlyDictionary<string, SymbolSource<TraitDefinition>> traitSources,
        IReadOnlyDictionary<string, SymbolSource<BenchDefinition>> benchSources,
        IReadOnlyDictionary<string, SymbolSource<FunctionDefinition>> functionSources,
        IReadOnlyDictionary<string, SymbolSource<Circuit>> circuitSources
    )
    {
        var temp = new CascodeDocument();
        switch (kind)
        {
            case SymbolKind.Trait:
                if (traitSources.TryGetValue(name, out var trait))
                {
                    temp.Traits.Add(trait.Definition);
                }
                break;
            case SymbolKind.Bench:
                if (benchSources.TryGetValue(name, out var bench))
                {
                    temp.BenchDefinitions.Add(bench.Definition);
                }
                break;
            case SymbolKind.Function:
                if (functionSources.TryGetValue(name, out var function))
                {
                    temp.Functions.Add(function.Definition);
                }
                break;
            case SymbolKind.Circuit:
                if (circuitSources.TryGetValue(name, out var circuit))
                {
                    temp.Circuits.Add(circuit.Definition);
                }
                break;
        }

        var required = new RequiredSymbols();
        CollectRequiredSymbols(temp, required);
        return required;
    }

    private static bool TryGetIncludePath(
        SymbolKind kind,
        string name,
        IReadOnlyDictionary<string, SymbolSource<BundleType>> bundleSources,
        IReadOnlyDictionary<string, SymbolSource<TraitDefinition>> traitSources,
        IReadOnlyDictionary<string, SymbolSource<BenchDefinition>> benchSources,
        IReadOnlyDictionary<string, SymbolSource<FunctionDefinition>> functionSources,
        IReadOnlyDictionary<string, SymbolSource<PrimitiveDefinition>> primitiveSources,
        IReadOnlyDictionary<string, SymbolSource<Circuit>> circuitSources,
        out string includePath
    )
    {
        includePath = string.Empty;
        switch (kind)
        {
            case SymbolKind.Bundle when bundleSources.TryGetValue(name, out var bundle):
                includePath = bundle.IncludePath;
                return true;
            case SymbolKind.Trait when traitSources.TryGetValue(name, out var trait):
                includePath = trait.IncludePath;
                return true;
            case SymbolKind.Bench when benchSources.TryGetValue(name, out var bench):
                includePath = bench.IncludePath;
                return true;
            case SymbolKind.Function when functionSources.TryGetValue(name, out var function):
                includePath = function.IncludePath;
                return true;
            case SymbolKind.Primitive when primitiveSources.TryGetValue(name, out var primitive):
                includePath = primitive.IncludePath;
                return true;
            case SymbolKind.Circuit when circuitSources.TryGetValue(name, out var circuit):
                includePath = circuit.IncludePath;
                return true;
            default:
                return false;
        }
    }

    private static CascodeDocument MergeDocuments(
        IReadOnlyList<CascodeDocument> docs,
        List<Diagnostic> diagnostics,
        ILogger? logger
    )
    {
        var versionMajor = docs.Count > 0 ? docs[0].VersionMajor : CascodeVersion.Major;
        var versionMinor = docs.Count > 0 ? docs[0].VersionMinor : CascodeVersion.Minor;

        var bundles = new Dictionary<string, BundleType>(StringComparer.Ordinal);
        var traits = new Dictionary<string, TraitDefinition>(StringComparer.Ordinal);
        var benches = new Dictionary<string, BenchDefinition>(StringComparer.Ordinal);
        var functions = new Dictionary<string, FunctionDefinition>(StringComparer.Ordinal);
        var primitives = new Dictionary<string, PrimitiveDefinition>(StringComparer.Ordinal);
        var circuits = new Dictionary<string, Circuit>(StringComparer.Ordinal);

        foreach (var doc in docs)
        {
            foreach (var b in doc.BundleTypes)
            {
                if (!bundles.TryAdd(b.Name, b))
                {
                    diagnostics.Add(
                        new Diagnostic(
                            $"CAS1002: Duplicate bundle type '{b.Name}' while linking.",
                            DiagnosticSeverity.Error,
                            "<link>",
                            1,
                            1
                        )
                    );
                }
            }

            foreach (var t in doc.Traits)
            {
                if (!traits.TryAdd(t.Name, t))
                {
                    diagnostics.Add(
                        new Diagnostic(
                            $"CAS1003: Duplicate interface '{t.Name}' while linking.",
                            DiagnosticSeverity.Error,
                            "<link>",
                            1,
                            1
                        )
                    );
                }
            }

            foreach (var b in doc.BenchDefinitions)
            {
                if (!benches.TryAdd(b.Name, b))
                {
                    diagnostics.Add(
                        new Diagnostic(
                            $"CAS1004: Duplicate bench '{b.Name}' while linking.",
                            DiagnosticSeverity.Error,
                            "<link>",
                            1,
                            1
                        )
                    );
                }
            }

            foreach (var f in doc.Functions)
            {
                if (!functions.TryAdd(f.Name, f))
                {
                    diagnostics.Add(
                        new Diagnostic(
                            $"CAS1005: Duplicate function '{f.Name}' while linking.",
                            DiagnosticSeverity.Error,
                            "<link>",
                            1,
                            1
                        )
                    );
                }
            }

            foreach (var p in doc.Primitives)
            {
                if (!primitives.TryAdd(p.Name, p))
                {
                    diagnostics.Add(
                        new Diagnostic(
                            $"CAS1006: Duplicate primitive '{p.Name}' while linking.",
                            DiagnosticSeverity.Error,
                            "<link>",
                            1,
                            1
                        )
                    );
                }
            }

            foreach (var c in doc.Circuits)
            {
                if (!circuits.TryAdd(c.Name, c))
                {
                    diagnostics.Add(
                        new Diagnostic(
                            $"CAS1007: Duplicate circuit '{c.Name}' while linking.",
                            DiagnosticSeverity.Error,
                            "<link>",
                            1,
                            1
                        )
                    );
                }
            }
        }

        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            logger?.LogError("Link failed due to merge errors.");
        }

        return new CascodeDocument
        {
            VersionMajor = versionMajor,
            VersionMinor = versionMinor,
            Includes = new List<IncludeDirective>(),
            Functions = functions.Values.OrderBy(f => f.Name, StringComparer.Ordinal).ToList(),
            BundleTypes = bundles.Values.OrderBy(b => b.Name, StringComparer.Ordinal).ToList(),
            Traits = traits.Values.OrderBy(t => t.Name, StringComparer.Ordinal).ToList(),
            BenchDefinitions = benches.Values.OrderBy(b => b.Name, StringComparer.Ordinal).ToList(),
            Primitives = primitives.Values.OrderBy(p => p.Name, StringComparer.Ordinal).ToList(),
            Circuits = circuits.Values.OrderBy(c => c.Name, StringComparer.Ordinal).ToList(),
        };
    }

    private static string DetermineHighestLevelSuffix(CascodeDocument doc)
    {
        if (doc.Circuits.Any(c => c.Level == CascodeLevel.HL))
        {
            return "hl";
        }
        if (doc.Circuits.Any(c => c.Level == CascodeLevel.ML))
        {
            return "ml";
        }
        return "el";
    }

    private static string GetLinkBaseName(string entryPath)
    {
        var name = Path.GetFileName(entryPath);
        if (name.EndsWith(".hl.cai", StringComparison.OrdinalIgnoreCase))
        {
            return name[..^".hl.cai".Length];
        }
        if (name.EndsWith(".ml.cai", StringComparison.OrdinalIgnoreCase))
        {
            return name[..^".ml.cai".Length];
        }
        if (name.EndsWith(".el.cai", StringComparison.OrdinalIgnoreCase))
        {
            return name[..^".el.cai".Length];
        }
        if (name.EndsWith(".hl.cas", StringComparison.OrdinalIgnoreCase))
        {
            return name[..^".hl.cas".Length];
        }
        if (name.EndsWith(".ml.cas", StringComparison.OrdinalIgnoreCase))
        {
            return name[..^".ml.cas".Length];
        }
        if (name.EndsWith(".el.cas", StringComparison.OrdinalIgnoreCase))
        {
            return name[..^".el.cas".Length];
        }
        return Path.GetFileNameWithoutExtension(entryPath);
    }

    /// <summary>
    /// Extracts synth entries from circuits into a YAML sidecar and produces a document with those synths removed.
    /// </summary>
    /// <param name="doc">The source document to scan for circuit synth entries.</param>
    /// <returns>
    /// A tuple where the first element is the updated document with all circuit <c>Synth</c> fields cleared,
    /// and the second element is the YAML string containing extracted synth data, or <c>null</c> if no synths were found.
    /// </returns>
    private static (CascodeDocument LinkedDoc, string? SynthYaml) ExtractSynthToYaml(
        CascodeDocument doc
    )
    {
        var circuitsWithSynth = doc
            .Circuits.Where(c => c.Synth is not null && c.Synth.Entries.Count > 0)
            .ToList();

        if (circuitsWithSynth.Count == 0)
        {
            return (doc, null);
        }

        var yaml = new StringBuilder();
        yaml.AppendLine("circuits:");
        foreach (var circuit in circuitsWithSynth.OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            yaml.AppendLine($"  {circuit.Name}:");
            foreach (
                var entry in circuit.Synth!.Entries.OrderBy(e => e.Key, StringComparer.Ordinal)
            )
            {
                yaml.AppendLine($"    {entry.Key}: {QuoteYamlScalar(entry.Value)}");
            }
        }

        var updatedCircuits = doc
            .Circuits.Select(c =>
                c.Synth is null
                    ? c
                    : new Circuit
                    {
                        Name = c.Name,
                        Traits = c.Traits,
                        Level = c.Level,
                        Inline = c.Inline,
                        Package = c.Package,
                        Parameters = c.Parameters,
                        Sizes = c.Sizes,
                        Supplies = c.Supplies,
                        Grounds = c.Grounds,
                        Ports = c.Ports,
                        Slot = c.Slot,
                        Fill = c.Fill,
                        Constraints = c.Constraints,
                        Harness = c.Harness,
                        Env = c.Env,
                        Render = c.Render,
                        BenchBindings = c.BenchBindings,
                        BenchBindingExtensions = c.BenchBindingExtensions,
                        Synth = null,
                        Provenance = c.Provenance,
                    }
            )
            .ToList();

        var updated = new CascodeDocument
        {
            VersionMajor = doc.VersionMajor,
            VersionMinor = doc.VersionMinor,
            Includes = doc.Includes,
            FileLibrary = doc.FileLibrary,
            Functions = doc.Functions,
            BundleTypes = doc.BundleTypes,
            Traits = doc.Traits,
            BenchDefinitions = doc.BenchDefinitions,
            Primitives = doc.Primitives,
            Circuits = updatedCircuits,
        };

        return (updated, yaml.ToString());
    }

    private static string QuoteYamlScalar(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "\"\"";
        }

        // Keep already-quoted strings as-is.
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
        {
            return raw;
        }

        if (raw.Any(ch => char.IsWhiteSpace(ch) || ch is ':' or '#' or '{' or '}' or '[' or ']'))
        {
            return "\"" + raw.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        }

        return raw;
    }
}
