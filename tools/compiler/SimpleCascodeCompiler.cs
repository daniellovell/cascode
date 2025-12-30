using System;
using System.Collections.Generic;
using System.Linq;
using Cascode.ACIR;
using Cascode.Parser;

namespace Cascode.Compiler;

/// <summary>
/// Minimal reference implementation of <see cref="ICascodeCompiler"/> that handles a single motif per file.
/// </summary>
/// <remarks>
/// The v0 pipeline targets structural netlisting for regression fixtures rather than full language coverage.
/// </remarks>
public sealed class SimpleCascodeCompiler : ICascodeCompiler
{
    /// <summary>
    /// Parses, elaborates, and lowers the provided sources into an ACIR document.
    /// </summary>
    /// <param name="sources">Compilation units to process. Only the first unit is consumed in v0.</param>
    /// <param name="options">Compilation options controlling the target ACIR level.</param>
    /// <returns>Compilation result containing either ACIR output or error diagnostics.</returns>
    public CompileResult CompileToACIR(IReadOnlyList<SourceUnit> sources, CompileOptions options)
    {
        ArgumentNullException.ThrowIfNull(sources, nameof(sources));
        ArgumentNullException.ThrowIfNull(options, nameof(options));

        if (sources.Count == 0)
        {
            throw new ArgumentException("No sources provided", nameof(sources));
        }

        // For v0, assume a single file and a single motif.
        var source = sources[0];
        var tree = CascodeParserFacade.Parse(source.Path, source.Text);

        var diagnostics = tree.Diagnostics;
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return new CompileResult { ACIR = null, Diagnostics = diagnostics };
        }

        var motif = tree.Root.Members.OfType<MotifDeclarationSyntax>().FirstOrDefault();
        if (motif is null)
        {
            var compilerDiagnostics = new List<Diagnostic>(diagnostics)
            {
                new Diagnostic(
                    "CAS0001: No motif declaration found",
                    DiagnosticSeverity.Error,
                    tree.Root.FilePath,
                    tree.Root.Line,
                    tree.Root.Column
                ),
            };
            return new CompileResult { ACIR = null, Diagnostics = compilerDiagnostics };
        }

        var elaborationDiagnostics = new List<Diagnostic>(diagnostics);
        var design = ElaborateMotif(motif, elaborationDiagnostics);

        if (elaborationDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return new CompileResult { ACIR = null, Diagnostics = elaborationDiagnostics };
        }

        var acir = LowerToACIR(design, options, motif, source.Path);

        return new CompileResult { ACIR = acir, Diagnostics = elaborationDiagnostics };
    }

    /// <summary>
    /// Extracts the package name from a file path.
    /// </summary>
    private static string? ExtractPackage(string filePath)
    {
        // Convert path like "C:\...\lib\std\prim\DiffPair.cas" to "lib.std.prim"
        var normalized = filePath.Replace('\\', '/');
        var libIndex = normalized.LastIndexOf("/lib/", StringComparison.OrdinalIgnoreCase);
        if (libIndex < 0)
        {
            libIndex = normalized.IndexOf("lib/", StringComparison.OrdinalIgnoreCase);
            if (libIndex != 0)
            {
                return null;
            }
        }
        else
        {
            libIndex++; // Skip the leading /
        }

        var pathPart = normalized[libIndex..];
        var lastSlash = pathPart.LastIndexOf('/');
        if (lastSlash > 0)
        {
            pathPart = pathPart[..lastSlash];
        }

        return pathPart.Replace('/', '.');
    }

    /// <summary>
    /// Builds a structural design from the parsed motif and records semantic diagnostics.
    /// </summary>
    /// <param name="motif">Motif syntax node to elaborate.</param>
    /// <param name="diagnostics">Diagnostic sink that receives compiler errors.</param>
    /// <returns>Structural design with nets, bundles, and instances.</returns>
    private static StructuralDesign ElaborateMotif(
        MotifDeclarationSyntax motif,
        ICollection<Diagnostic> diagnostics
    )
    {
        var design = new StructuralDesign();

        // Top-level ports become nets/bundles.
        foreach (var port in motif.Ports)
        {
            if (string.Equals(port.Kind, "Diff", StringComparison.OrdinalIgnoreCase))
            {
                var pNet = port.Name + "_P";
                var nNet = port.Name + "_N";
                design.Nets[pNet] = new NetInfo { Id = pNet, Domain = "analog" };
                design.Nets[nNet] = new NetInfo { Id = nNet, Domain = "analog" };
                design.Bundles[port.Name] = new BundleInfo
                {
                    Id = port.Name,
                    PNet = pNet,
                    NNet = nNet,
                };
            }
            else
            {
                design.Nets[port.Name] = new NetInfo
                {
                    Id = port.Name,
                    Domain = MapKindToDomain(port.Kind),
                };
            }
        }

        // Supplies.
        foreach (var supply in motif.Supplies)
        {
            design.Nets[supply.Name] = new NetInfo
            {
                Id = supply.Name,
                Domain = "supply",
                Rail = supply.Name,
            };
        }

        foreach (var ground in motif.Grounds)
        {
            design.Nets[ground.Name] = new NetInfo
            {
                Id = ground.Name,
                Domain = "ground",
                Rail = ground.Name,
            };
        }

        if (motif.UseBlock is null)
        {
            return design;
        }

        // Build instances and connections from use { }.
        foreach (var stmt in motif.UseBlock.Statements)
        {
            switch (stmt)
            {
                case InstanceDeclarationSyntax instance:
                    var instInfo = new InstanceInfo
                    {
                        Id = instance.InstanceName,
                        Type = instance.TypeName,
                    };
                    design.Instances[instance.InstanceName] = instInfo;

                    // Elaborate inline bindings as port connections
                    foreach (var binding in instance.Bindings)
                    {
                        var toNet = ResolveBindingTarget(design, binding.ToPin);
                        if (toNet is null)
                        {
                            diagnostics.Add(
                                new Diagnostic(
                                    $"CAS0005: Binding target '{binding.ToPin}' is not a declared net, port, or bundle field.",
                                    DiagnosticSeverity.Error,
                                    binding.FilePath,
                                    binding.Line,
                                    binding.Column
                                )
                            );
                            continue;
                        }

                        instInfo.Ports[binding.FromPin] = toNet;
                    }

                    break;
                case ConnectStatementSyntax connect:
                    BindConnect(design, connect, diagnostics);
                    break;
                case AttachStatementSyntax attach:
                    // v0: bare attach recorded but not elaborated; OTA test focuses on explicit connect.
                    _ = attach;
                    break;
            }
        }

        return design;
    }

    /// <summary>
    /// Maps a port kind token to the ACIR domain string used during lowering.
    /// </summary>
    /// <param name="kind">Case-sensitive kind value from the syntax tree.</param>
    /// <returns>ACIR domain name.</returns>
    private static string MapKindToDomain(string kind)
    {
        return kind switch
        {
            "supply" => "supply",
            "ground" => "ground",
            "bias" => "bias",
            "analog" => "analog",
            "digital" => "digital",
            "mixed" => "mixed",
            "signal" => "signal",
            "rf" => "rf",
            "clock" => "clock",
            _ => "signal",
        };
    }

    /// <summary>
    /// Resolves a binding target to a net identifier, handling bundle field references.
    /// </summary>
    /// <param name="design">Structural design containing nets and bundles.</param>
    /// <param name="target">Target identifier (e.g., "GND", "IN.P").</param>
    /// <returns>The resolved net identifier, or null if not found.</returns>
    private static string? ResolveBindingTarget(StructuralDesign design, string target)
    {
        // Direct net lookup
        if (design.Nets.ContainsKey(target))
        {
            return target;
        }

        // Check for bundle field reference (e.g., IN.P -> IN_P)
        var parts = target.Split('.');
        if (parts.Length == 2)
        {
            var bundleName = parts[0];
            var fieldName = parts[1];

            if (design.Bundles.TryGetValue(bundleName, out var bundle))
            {
                // Map P/N fields to the corresponding net
                return fieldName.ToUpperInvariant() switch
                {
                    "P" => bundle.PNet,
                    "N" => bundle.NNet,
                    _ => null,
                };
            }
        }

        return null;
    }

    /// <summary>
    /// Records a port-to-net connection on an instance, validating both endpoints.
    /// </summary>
    /// <param name="design">Accumulated structural design being elaborated.</param>
    /// <param name="connect">Connect statement syntax.</param>
    /// <param name="diagnostics">Diagnostic sink that receives compiler errors.</param>
    private static void BindConnect(
        StructuralDesign design,
        ConnectStatementSyntax connect,
        ICollection<Diagnostic> diagnostics
    )
    {
        // v0: support only dp.OUT.N -> OUT, where left is instance pin and right is top-level net.
        var from = connect.FromPin;
        var to = connect.ToPin;

        var parts = from.Split('.');
        if (parts.Length < 2)
        {
            diagnostics.Add(
                new Diagnostic(
                    $"CAS0002: Invalid connection source format '{from}'. Expected 'instance.pin'.",
                    DiagnosticSeverity.Error,
                    connect.FilePath,
                    connect.Line,
                    connect.Column
                )
            );
            return;
        }

        var instanceId = parts[0];
        if (!design.Instances.TryGetValue(instanceId, out var instance))
        {
            diagnostics.Add(
                new Diagnostic(
                    $"CAS0003: Instance '{instanceId}' not found in design.",
                    DiagnosticSeverity.Error,
                    connect.FilePath,
                    connect.Line,
                    connect.Column
                )
            );
            return;
        }

        var pinPath = string.Join('.', parts.Skip(1));

        if (!design.Nets.ContainsKey(to))
        {
            diagnostics.Add(
                new Diagnostic(
                    $"CAS0004: Connection target '{to}' is not a declared net or port.",
                    DiagnosticSeverity.Error,
                    connect.FilePath,
                    connect.Line,
                    connect.Column
                )
            );
            return;
        }

        instance.Ports[pinPath] = to;
    }

    /// <summary>
    /// Converts the elaborated structural design into an ACIR document.
    /// </summary>
    /// <param name="design">Structural design produced during elaboration.</param>
    /// <param name="options">Compilation options that supply the target ACIR level.</param>
    /// <param name="motif">Original motif syntax for extracting package and traits.</param>
    /// <param name="sourcePath">Source file path for package extraction.</param>
    /// <returns>ACIR document ready for serialization or further passes.</returns>
    private static ACIRDocument LowerToACIR(
        StructuralDesign design,
        CompileOptions options,
        MotifDeclarationSyntax motif,
        string sourcePath
    )
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
        };

        // Extract bundle types (Diff is built-in, but we can declare it explicitly)
        var hasDiffBundle = design.Bundles.Values.Any(b =>
            b.Id == "Diff" || design.Bundles.Count > 0
        );
        if (hasDiffBundle)
        {
            doc.BundleTypes.Add(
                new BundleType
                {
                    Name = "Diff",
                    Fields = new Dictionary<string, string>
                    {
                        { "P", "analog" },
                        { "N", "analog" },
                    },
                }
            );
        }

        var fill = BuildFillBlock(design, motif, options);

        // Create circuit with all properties in initializer
        var circuit = new Circuit
        {
            Name = motif.Name,
            Level = options.Level,
            Package = ExtractPackage(sourcePath),
            Traits = motif.Implements.Count > 0 ? motif.Implements.ToList() : null,
            Supplies = motif.Supplies.Select(s => s.Name).ToList(),
            Grounds = motif.Grounds.Select(g => g.Name).ToList(),
            Ports = motif
                .Ports.Select(p => new PortDeclaration { Name = p.Name, Type = p.Kind })
                .ToList(),
            Fill = fill,
        };

        doc.Circuits.Add(circuit);
        return doc;
    }

    /// <summary>
    /// Constructs the fill block containing internal nets and instances for ML and EL ACIR levels.
    /// </summary>
    /// <param name="design">Structural design with nets, bundles, and instances.</param>
    /// <param name="motif">Motif syntax for extracting port, supply, and ground declarations.</param>
    /// <param name="options">Compilation options that determine the target ACIR level.</param>
    /// <returns>Fill block with nets and instances, or null for HL level.</returns>
    private static FillBlock? BuildFillBlock(
        StructuralDesign design,
        MotifDeclarationSyntax motif,
        CompileOptions options
    )
    {
        if (options.Level == ACIRLevel.HL)
        {
            return null;
        }

        // Internal nets (exclude ports, supplies, grounds which are implicit)
        var portNets = new HashSet<string>();
        foreach (var port in motif.Ports)
        {
            if (string.Equals(port.Kind, "Diff", StringComparison.OrdinalIgnoreCase))
            {
                portNets.Add(port.Name + "_P");
                portNets.Add(port.Name + "_N");
            }
            else
            {
                portNets.Add(port.Name);
            }
        }
        foreach (var supply in motif.Supplies)
        {
            portNets.Add(supply.Name);
        }
        foreach (var ground in motif.Grounds)
        {
            portNets.Add(ground.Name);
        }

        var nets = design
            .Nets.Values.OrderBy(n => n.Id, StringComparer.Ordinal)
            .Where(n => !portNets.Contains(n.Id))
            .Select(n => new NetDeclaration { Id = n.Id, Domain = n.Domain })
            .ToList();

        var instances = design
            .Instances.Values.OrderBy(i => i.Id, StringComparer.Ordinal)
            .Select(i => new InstanceDeclaration
            {
                Id = i.Id,
                Type = i.Type,
                Bindings = new Dictionary<string, string>(i.Ports),
            })
            .ToList();

        return new FillBlock { Nets = nets, Instances = instances };
    }
}
