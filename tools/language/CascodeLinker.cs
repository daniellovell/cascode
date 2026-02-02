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
    public static CascodeLinkResult LinkFile(
        string entryPath,
        string outputDir,
        string workspaceRoot,
        ILogger? logger = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        entryPath = Path.GetFullPath(entryPath);
        outputDir = Path.GetFullPath(outputDir);
        workspaceRoot = Path.GetFullPath(workspaceRoot);

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
        var libraryIndex = CascodeLibraryIndex.Build(workspaceRoot);
        var includedDocs = new List<CascodeDocument>();
        var parsedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
            includedDocs.Add(read.Document);
            AddIncludeCandidates(
                read.Document,
                path,
                workspaceRoot,
                libraryIndex,
                candidates,
                diagnostics
            );
            CollectRequiredSymbols(read.Document, required);
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

        includedDocs.Add(entryRead.Document);
        AddIncludeCandidates(
            entryRead.Document,
            entryFullPath,
            workspaceRoot,
            libraryIndex,
            candidates,
            diagnostics
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
                name => includedDocs.Any(d => d.BundleTypes.Any(b => b.Name == name)),
                MightDefineBundle,
                TryAddDoc,
                TryRead,
                candidates,
                parsedPaths
            );

            progress |= ResolveMissing(
                "interface",
                required.Traits,
                name => includedDocs.Any(d => d.Traits.Any(t => t.Name == name)),
                MightDefineTrait,
                TryAddDoc,
                TryRead,
                candidates,
                parsedPaths
            );

            progress |= ResolveMissing(
                "bench",
                required.Benches,
                name => includedDocs.Any(d => d.BenchDefinitions.Any(b => b.Name == name)),
                MightDefineBench,
                TryAddDoc,
                TryRead,
                candidates,
                parsedPaths
            );

            progress |= ResolveMissing(
                "function",
                required.Functions,
                name => includedDocs.Any(d => d.Functions.Any(f => f.Name == name)),
                MightDefineFunction,
                TryAddDoc,
                TryRead,
                candidates,
                parsedPaths
            );

            progress |= ResolveMissing(
                "primitive",
                required.Primitives,
                name => includedDocs.Any(d => d.Primitives.Any(p => p.Name == name)),
                MightDefinePrimitive,
                TryAddDoc,
                TryRead,
                candidates,
                parsedPaths
            );

            progress |= ResolveMissing(
                "circuit",
                required.Circuits,
                name => includedDocs.Any(d => d.Circuits.Any(c => c.Name == name)),
                MightDefineCircuit,
                TryAddDoc,
                TryRead,
                candidates,
                parsedPaths
            );
        }

        // Any remaining missing symbols are link errors.
        AddUnresolvedDiagnostics(required, includedDocs, entryPath, diagnostics);
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return new CascodeLinkResult(false, null, null, diagnostics);
        }

        var merged = MergeDocuments(includedDocs, diagnostics, logger);
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return new CascodeLinkResult(false, null, null, diagnostics);
        }

        // Now that the document is self-contained (no includes), run bundle expansion and bench validation.
        // This is the earliest point where bundle types and bench/interface bindings are resolvable.
        var linked = BundleDesugarer.Desugar(merged);
        linked = BenchBindingExtender.Apply(linked, diagnostics);
        BenchSemanticChecker.Check(linked, diagnostics);
        BenchBindingChecker.Check(linked, diagnostics);
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return new CascodeLinkResult(false, null, null, diagnostics);
        }

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

    private static void AddIncludeCandidates(
        CascodeDocument doc,
        string parsedFilePath,
        string workspaceRoot,
        CascodeLibraryIndex libraryIndex,
        HashSet<string> candidates,
        List<Diagnostic> diagnostics
    )
    {
        // Namespace inheritance: a file in "lib.std.bench" can see "lib.std" and "lib" automatically.
        AddNamespaceInheritanceCandidates(doc.FileLibrary, libraryIndex, candidates);

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
                candidates.Add(Path.GetFullPath(t));
            }
        }
    }

    private static void AddNamespaceInheritanceCandidates(
        string? fileLibrary,
        CascodeLibraryIndex libraryIndex,
        HashSet<string> candidates
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

        // Ancestors only (no descendants): lib.std.bench inherits lib.std and lib.
        for (var i = parts.Length - 1; i >= 1; i--)
        {
            var ancestor = string.Join('.', parts.Take(i));
            foreach (var p in libraryIndex.FindExact(ancestor))
            {
                candidates.Add(Path.GetFullPath(p));
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

            if (c.Fill is not null)
            {
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
            foreach (var term in b.Terminals)
            {
                AddBundleIfNeeded(term.Type, required);
            }

            foreach (var fn in b.Functions)
            {
                // Not a reference; but measurements can call other measurements/functions.
                // Cross-file function resolution is currently best-effort: only bring in
                // file-level functions that are explicitly referenced elsewhere.
                _ = fn;
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
    }

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
            || typeName.Equals("Impedance", StringComparison.OrdinalIgnoreCase)
            || typeName.Equals("Impedor", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ResolveMissing(
        string kind,
        HashSet<string> required,
        Func<string, bool> isResolved,
        Func<string, string, bool> mightDefine,
        Func<string, bool> tryAddDoc,
        Func<string, CascodeReadResult?> tryRead,
        HashSet<string> candidates,
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
            foreach (var path in candidates.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
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

    private static bool MightDefineBundle(string content, string name) =>
        ContainsKeywordDecl(content, "bundle", name);

    private static bool MightDefineTrait(string content, string name) =>
        ContainsKeywordDecl(content, "interface", name);

    private static bool MightDefineBench(string content, string name) =>
        ContainsKeywordDecl(content, "bench", name);

    private static bool MightDefineFunction(string content, string name) =>
        ContainsKeywordDecl(content, "function", name);

    private static bool MightDefineCircuit(string content, string name) =>
        ContainsKeywordDecl(content, "circuit", name);

    private static bool MightDefinePrimitive(string content, string name)
    {
        // primitives are "primitive <DeviceType> <Name>(...)"
        return content.Contains("primitive", StringComparison.OrdinalIgnoreCase)
            && content.Contains(name, StringComparison.Ordinal);
    }

    private static bool ContainsKeywordDecl(string content, string keyword, string name)
    {
        // Quick-and-dirty text check to avoid parsing irrelevant files.
        // We only require that the token sequence appears somewhere; the parser will validate.
        return content.Contains(keyword + " " + name, StringComparison.Ordinal)
            || content.Contains(keyword + "\t" + name, StringComparison.Ordinal)
            || content.Contains(keyword + "\r\n" + name, StringComparison.Ordinal)
            || content.Contains(keyword + "\n" + name, StringComparison.Ordinal);
    }

    private static void AddUnresolvedDiagnostics(
        RequiredSymbols required,
        IReadOnlyList<CascodeDocument> includedDocs,
        string entryPath,
        List<Diagnostic> diagnostics
    )
    {
        void AddMissing(string kind, IEnumerable<string> names, Func<string, bool> resolved)
        {
            foreach (
                var name in names.Where(n => !resolved(n)).OrderBy(n => n, StringComparer.Ordinal)
            )
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS1009: Unresolved {kind} reference '{name}' while linking '{entryPath}'.",
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
            name => includedDocs.Any(d => d.BundleTypes.Any(b => b.Name == name))
        );
        AddMissing(
            "interface",
            required.Traits,
            name => includedDocs.Any(d => d.Traits.Any(t => t.Name == name))
        );
        AddMissing(
            "bench",
            required.Benches,
            name => includedDocs.Any(d => d.BenchDefinitions.Any(b => b.Name == name))
        );
        AddMissing(
            "function",
            required.Functions,
            name => includedDocs.Any(d => d.Functions.Any(f => f.Name == name))
        );
        AddMissing(
            "primitive",
            required.Primitives,
            name => includedDocs.Any(d => d.Primitives.Any(p => p.Name == name))
        );
        AddMissing(
            "circuit",
            required.Circuits,
            name => includedDocs.Any(d => d.Circuits.Any(c => c.Name == name))
        );
    }

    private static IReadOnlyList<string> ResolveIncludeTargets(
        string includeName,
        string includingFilePath,
        string workspaceRoot,
        CascodeLibraryIndex libraryIndex
    )
    {
        // Library-based include:
        // include lib.std -> all files with library lib.std.* (prefix match).
        //
        // This mirrors the historical directory-based behavior (lib/std/**) while decoupling
        // resolution from folder structure and avoiding full parses of unrelated files.
        var normalized = CascodeLibraryIndex.NormalizeLibraryName(includeName);
        if (normalized.Contains('.', StringComparison.Ordinal))
        {
            var byLibrary = libraryIndex.FindByPrefix(normalized);
            if (byLibrary.Count > 0)
            {
                return byLibrary;
            }
        }

        // Directory-based include:
        // - "lib.std" -> <workspaceRoot>/lib/std/**.cas
        // - "lib_std" -> <workspaceRoot>/lib/std/**.cas (legacy separator)
        if (
            includeName.Contains('.', StringComparison.Ordinal)
            || includeName.Contains('_', StringComparison.Ordinal)
        )
        {
            var targets = TryResolveIncludeAsDirectoryOrFile(workspaceRoot, includeName);
            if (targets.Count > 0)
                return targets;

            // Compatibility shim:
            // Some library package names don't exactly match the on-disk folder name
            // (e.g., "lib.std.bench" vs "lib/std/bench").
            var alt = TryResolveIncludeWithLastSegmentRewrite(
                workspaceRoot,
                includeName,
                fromLast: "benches",
                toLast: "bench"
            );
            if (alt.Count > 0)
                return alt;
        }
        else
        {
            var local = Path.Combine(
                Path.GetDirectoryName(includingFilePath)!,
                includeName + ".cas"
            );
            if (File.Exists(local))
            {
                return new[] { local };
            }

            var root = Path.Combine(workspaceRoot, includeName + ".cas");
            if (File.Exists(root))
            {
                return new[] { root };
            }
        }

        // Missing include is reported as a link-time error.
        return Array.Empty<string>();
    }

    private static List<string> TryResolveIncludeAsDirectoryOrFile(
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
                .ToList();
        }

        var file = dir + ".cas";
        if (File.Exists(file))
        {
            return new List<string> { file };
        }

        return new List<string>();
    }

    private static List<string> TryResolveIncludeWithLastSegmentRewrite(
        string workspaceRoot,
        string includeName,
        string fromLast,
        string toLast
    )
    {
        // Split on both '.' and '_' while preserving overall intent.
        var parts = includeName.Split(new[] { '.', '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return new List<string>();

        if (!parts[^1].Equals(fromLast, StringComparison.OrdinalIgnoreCase))
            return new List<string>();

        parts[^1] = toLast;
        var rewritten = string.Join(Path.DirectorySeparatorChar, parts);
        return TryResolveIncludeAsDirectoryOrFile(workspaceRoot, rewritten);
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
                        Slots = c.Slots,
                        Fill = c.Fill,
                        Constraints = c.Constraints,
                        Harness = c.Harness,
                        Env = c.Env,
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
