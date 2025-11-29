using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cascode.CasIR;
using Cascode.Parser;

namespace Cascode.Compiler;

/// <summary>
/// Resolves import statements to source files and collects motif definitions.
/// </summary>
internal sealed class ImportResolver
{
    private readonly IReadOnlyList<string> _libraryRoots;
    private readonly Dictionary<string, MotifDeclarationSyntax> _resolvedMotifs = new();
    private readonly HashSet<string> _parsedFiles = new();

    public ImportResolver(IReadOnlyList<string>? libraryRoots)
    {
        _libraryRoots = libraryRoots ?? Array.Empty<string>();
    }

    /// <summary>
    /// Resolves imports from a compilation unit and collects all referenced motif definitions.
    /// </summary>
    /// <param name="compilationUnit">The parsed compilation unit with imports.</param>
    /// <param name="referencedTypes">Set of motif type names referenced in the unit.</param>
    /// <param name="diagnostics">Diagnostic collection for errors.</param>
    /// <returns>Dictionary of motif type name to its syntax declaration.</returns>
    public IReadOnlyDictionary<string, MotifDeclarationSyntax> ResolveImports(
        CompilationUnitSyntax compilationUnit,
        ISet<string> referencedTypes,
        ICollection<Diagnostic> diagnostics)
    {
        if (_libraryRoots.Count == 0)
        {
            return _resolvedMotifs;
        }

        // Process each import statement
        foreach (var import in compilationUnit.Imports)
        {
            ResolveImport(import, diagnostics);
        }

        // Filter to only return definitions for referenced types
        return _resolvedMotifs
            .Where(kvp => referencedTypes.Contains(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    private void ResolveImport(ImportDeclarationSyntax import, ICollection<Diagnostic> diagnostics)
    {
        var importPath = import.Name;

        if (import.IsWildcard)
        {
            // Wildcard import: lib.std.prim.* → look for all .cas files in lib/std/prim/
            var dirPath = importPath.Replace('.', Path.DirectorySeparatorChar);

            foreach (var root in _libraryRoots)
            {
                var fullDir = Path.Combine(root, dirPath);
                if (Directory.Exists(fullDir))
                {
                    foreach (var casFile in Directory.GetFiles(fullDir, "*.cas"))
                    {
                        ParseAndCollectMotifs(casFile, diagnostics);
                    }
                }
            }
        }
        else
        {
            // Specific import: lib.std.prim.DiffPair → lib/std/prim/DiffPair.cas
            var parts = importPath.Split('.');
            var fileName = parts[^1] + ".cas";
            var dirPath = string.Join(Path.DirectorySeparatorChar.ToString(), parts[..^1]);

            foreach (var root in _libraryRoots)
            {
                var fullPath = Path.Combine(root, dirPath, fileName);
                if (File.Exists(fullPath))
                {
                    ParseAndCollectMotifs(fullPath, diagnostics);
                    return;
                }
            }

            // File not found - not an error, motif may be defined elsewhere
        }
    }

    private void ParseAndCollectMotifs(string filePath, ICollection<Diagnostic> diagnostics)
    {
        var normalizedPath = Path.GetFullPath(filePath);
        if (_parsedFiles.Contains(normalizedPath))
        {
            return;
        }

        _parsedFiles.Add(normalizedPath);

        try
        {
            var sourceText = File.ReadAllText(filePath);
            var tree = CascodeParserFacade.Parse(filePath, sourceText);

            // Add any parse errors
            foreach (var diag in tree.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
            {
                diagnostics.Add(diag);
            }

            // Collect motif declarations
            foreach (var motif in tree.Root.Members.OfType<MotifDeclarationSyntax>())
            {
                if (!_resolvedMotifs.ContainsKey(motif.Name))
                {
                    _resolvedMotifs[motif.Name] = motif;
                }
            }
        }
        catch (IOException ex)
        {
            diagnostics.Add(new Diagnostic(
                $"CAS0006: Failed to read import file '{filePath}': {ex.Message}",
                DiagnosticSeverity.Warning,
                filePath,
                1,
                1));
        }
    }

    /// <summary>
    /// Converts a motif syntax declaration to a CasIR definition.
    /// </summary>
    public static MotifDefinition ToDefinition(MotifDeclarationSyntax motif, string? package)
    {
        return new MotifDefinition
        {
            Name = motif.Name,
            Package = package,
            Implements = motif.Implements.Count > 0 ? motif.Implements.ToList() : null,
            Params = ExtractParams(motif),
            Ports = motif.Ports.Select(p => new PortDeclaration
            {
                Name = p.Name,
                Kind = p.Kind
            }).ToList(),
            Supplies = motif.Supplies.Count > 0 ? motif.Supplies.Select(s => s.Name).ToList() : null,
            Grounds = motif.Grounds.Count > 0 ? motif.Grounds.Select(g => g.Name).ToList() : null,
            Instances = ExtractInstances(motif)
        };
    }

    private static List<ParamDeclaration>? ExtractParams(MotifDeclarationSyntax motif)
    {
        // Parameters are captured in InstanceParameterSyntax format during parsing
        // For definitions, we need to extract from the params block if present
        // For now, we extract from instance parameters as a placeholder
        // TODO: Add proper params block parsing to the grammar/AST
        return null;
    }

    private static List<MotifInstance>? ExtractInstances(MotifDeclarationSyntax motif)
    {
        if (motif.UseBlock is null)
        {
            return null;
        }

        var instances = new List<MotifInstance>();
        foreach (var stmt in motif.UseBlock.Statements)
        {
            if (stmt is InstanceDeclarationSyntax inst)
            {
                // Skip instances with empty type names (malformed due to parser limitations)
                if (string.IsNullOrEmpty(inst.TypeName))
                {
                    continue;
                }

                var ports = new Dictionary<string, string>();
                foreach (var binding in inst.Bindings)
                {
                    ports[binding.FromPin] = binding.ToPin;
                }

                instances.Add(new MotifInstance
                {
                    Id = inst.InstanceName,
                    Type = inst.TypeName,
                    Ports = ports
                });
            }
        }

        return instances.Count > 0 ? instances : null;
    }
}

