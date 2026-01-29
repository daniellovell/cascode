using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<CascodeDocument>();

        void Visit(string path)
        {
            path = Path.GetFullPath(path);
            if (!visited.Add(path))
            {
                return;
            }

            using var reader = File.OpenText(path);
            var read = CascodeReader.TryRead(reader, path);
            diagnostics.AddRange(read.Diagnostics);

            if (!read.Success || read.Document is null)
            {
                return;
            }

            foreach (var inc in read.Document.Includes)
            {
                var targets = ResolveIncludeTargets(inc.Name, path, workspaceRoot);
                if (targets.Count == 0)
                {
                    diagnostics.Add(
                        new Diagnostic(
                            $"CAS1008: Unresolved include '{inc.Name}' in '{path}'.",
                            DiagnosticSeverity.Error,
                            path,
                            1,
                            1
                        )
                    );
                    continue;
                }

                foreach (var resolved in targets)
                {
                    Visit(resolved);
                }
            }

            ordered.Add(read.Document);
        }

        Visit(entryPath);

        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return new CascodeLinkResult(false, null, null, diagnostics);
        }

        var merged = MergeDocuments(ordered, diagnostics, logger);
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return new CascodeLinkResult(false, null, null, diagnostics);
        }

        Directory.CreateDirectory(outputDir);

        var suffix = DetermineHighestLevelSuffix(merged);
        var baseName = GetLinkBaseName(entryPath);
        var linkedPath = Path.Combine(outputDir, $"{baseName}.{suffix}.cas");

        // Extract synth blocks into sidecar and remove from the linked output.
        var (linkedWithoutSynth, synthYaml) = ExtractSynthToYaml(merged);
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

    private static IReadOnlyList<string> ResolveIncludeTargets(
        string includeName,
        string includingFilePath,
        string workspaceRoot
    )
    {
        // Directory-based include: "lib.std" -> <workspaceRoot>/lib/std/**.cas
        if (includeName.Contains('.', StringComparison.Ordinal))
        {
            var dir = Path.Combine(
                workspaceRoot,
                includeName.Replace('.', Path.DirectorySeparatorChar)
            );
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
                return new[] { file };
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
