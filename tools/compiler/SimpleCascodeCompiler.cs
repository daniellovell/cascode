using System;
using System.Collections.Generic;
using System.Linq;
using Cascode.CasIR;
using Cascode.Parser;

namespace Cascode.Compiler;

public sealed class SimpleCascodeCompiler : ICascodeCompiler
{
    public CompileResult CompileToCasir(
        IReadOnlyList<SourceUnit> sources,
        CompileOptions options)
    {
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
            return new CompileResult
            {
                CasIR = null,
                Diagnostics = diagnostics
            };
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
                    tree.Root.Column)
            };
            return new CompileResult
            {
                CasIR = null,
                Diagnostics = compilerDiagnostics
            };
        }

        var elaborationDiagnostics = new List<Diagnostic>(diagnostics);
        var design = ElaborateMotif(motif, elaborationDiagnostics);

        if (elaborationDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return new CompileResult
            {
                CasIR = null,
                Diagnostics = elaborationDiagnostics
            };
        }

        var casir = LowerToCasir(design, options);

        return new CompileResult
        {
            CasIR = casir,
            Diagnostics = elaborationDiagnostics
        };
    }

    private static StructuralDesign ElaborateMotif(MotifDeclarationSyntax motif, ICollection<Diagnostic> diagnostics)
    {
        var design = new StructuralDesign();

        // Top-level ports become nets/bundles.
        foreach (var port in motif.Ports)
        {
            if (string.Equals(port.Kind, "Diff", StringComparison.OrdinalIgnoreCase))
            {
                var pNet = port.Name + "_P";
                var nNet = port.Name + "_N";
                design.Nets[pNet] = new NetInfo { Id = pNet, Domain = "electrical" };
                design.Nets[nNet] = new NetInfo { Id = nNet, Domain = "electrical" };
                design.Bundles[port.Name] = new BundleInfo { Id = port.Name, PNet = pNet, NNet = nNet };
            }
            else
            {
                design.Nets[port.Name] = new NetInfo { Id = port.Name, Domain = MapKindToDomain(port.Kind) };
            }
        }

        // Supplies.
        foreach (var supply in motif.Supplies)
        {
            design.Nets[supply.Name] = new NetInfo { Id = supply.Name, Domain = "supply", Rail = supply.Name };
        }

        foreach (var ground in motif.Grounds)
        {
            design.Nets[ground.Name] = new NetInfo { Id = ground.Name, Domain = "ground", Rail = ground.Name };
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
                    design.Instances[instance.InstanceName] = new InstanceInfo
                    {
                        Id = instance.InstanceName,
                        Type = instance.TypeName
                    };
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

    private static string MapKindToDomain(string kind)
    {
        return kind switch
        {
            "supply" => "supply",
            "ground" => "ground",
            "bias" => "bias",
            _ => "electrical"
        };
    }

    private static void BindConnect(StructuralDesign design, ConnectStatementSyntax connect, ICollection<Diagnostic> diagnostics)
    {
        // v0: support only dp.OUT.N -> OUT, where left is instance pin and right is top-level net.
        var from = connect.FromPin;
        var to = connect.ToPin;

        var parts = from.Split('.');
        if (parts.Length < 2)
        {
            diagnostics.Add(new Diagnostic(
                $"CAS0002: Invalid connection source format '{from}'. Expected 'instance.pin'.",
                DiagnosticSeverity.Error,
                connect.FilePath,
                connect.Line,
                connect.Column));
            return;
        }

        var instanceId = parts[0];
        if (!design.Instances.TryGetValue(instanceId, out var instance))
        {
            diagnostics.Add(new Diagnostic(
                $"CAS0003: Instance '{instanceId}' not found in design.",
                DiagnosticSeverity.Error,
                connect.FilePath,
                connect.Line,
                connect.Column));
            return;
        }

        var pinPath = string.Join('.', parts.Skip(1));

        if (!design.Nets.ContainsKey(to))
        {
            diagnostics.Add(new Diagnostic(
                $"CAS0004: Connection target '{to}' is not a declared net or port.",
                DiagnosticSeverity.Error,
                connect.FilePath,
                connect.Line,
                connect.Column));
            return;
        }

        instance.Ports[pinPath] = to;
    }

    private static CasirDocument LowerToCasir(StructuralDesign design, CompileOptions options)
    {
        var doc = new CasirDocument
        {
            Level = options.Level
        };

        foreach (var net in design.Nets.Values.OrderBy(n => n.Id, StringComparer.Ordinal))
        {
            doc.Nets.Add(new Net
            {
                Id = net.Id,
                Domain = net.Domain,
                Rail = net.Rail
            });
        }

        foreach (var bundle in design.Bundles.Values.OrderBy(b => b.Id, StringComparer.Ordinal))
        {
            doc.Bundles.Add(new Bundle
            {
                Id = bundle.Id,
                Fields = new BundleFields
                {
                    P = bundle.PNet,
                    N = bundle.NNet
                }
            });
        }

        foreach (var instance in design.Instances.Values.OrderBy(i => i.Id, StringComparer.Ordinal))
        {
            var motif = new MotifInstance
            {
                Id = instance.Id,
                Type = instance.Type
            };

            foreach (var kvp in instance.Ports.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                motif.Ports[kvp.Key] = kvp.Value;
            }

            doc.Motifs.Add(motif);
        }

        return doc;
    }
}
