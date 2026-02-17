using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language;

/// <summary>
/// Applies circuit-level <c>extend &lt;binding&gt; { ... }</c> blocks by merging their statements into
/// the resolved bench bindings (inherited + circuit overrides).
/// </summary>
public static class BenchBindingExtender
{
    public static CascodeDocument Apply(CascodeDocument document, List<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (document.Circuits.All(c => c.BenchBindingExtensions.Count == 0))
        {
            return document;
        }

        var interfacesByName = document.Traits.ToDictionary(
            t => t.Name,
            StringComparer.OrdinalIgnoreCase
        );

        var updatedCircuits = new List<Circuit>(document.Circuits.Count);
        foreach (var circuit in document.Circuits)
        {
            if (circuit.BenchBindingExtensions.Count == 0)
            {
                updatedCircuits.Add(circuit);
                continue;
            }

            updatedCircuits.Add(BuildUpdatedCircuit(circuit, interfacesByName, diagnostics));
        }

        return new CascodeDocument
        {
            VersionMajor = document.VersionMajor,
            VersionMinor = document.VersionMinor,
            Includes = document.Includes,
            FileLibrary = document.FileLibrary,
            Functions = document.Functions,
            BundleTypes = document.BundleTypes,
            Traits = document.Traits,
            BenchDefinitions = document.BenchDefinitions,
            Primitives = document.Primitives,
            Circuits = updatedCircuits,
        };
    }

    private static Circuit BuildUpdatedCircuit(
        Circuit circuit,
        IReadOnlyDictionary<string, TraitDefinition> interfacesByName,
        List<Diagnostic> diagnostics
    )
    {
        var merged = ResolveBaseBenchBindings(circuit, interfacesByName, diagnostics);
        ApplyExtensionsToBindings(merged, circuit.BenchBindingExtensions, circuit, diagnostics);
        return new Circuit
        {
            Name = circuit.Name,
            Traits = circuit.Traits,
            Level = circuit.Level,
            Inline = circuit.Inline,
            Package = circuit.Package,
            Parameters = circuit.Parameters,
            Sizes = circuit.Sizes,
            Supplies = circuit.Supplies,
            Grounds = circuit.Grounds,
            Ports = circuit.Ports,
            Slot = circuit.Slot,
            Fill = circuit.Fill,
            Constraints = circuit.Constraints,
            Harness = circuit.Harness,
            Env = circuit.Env,
            Render = circuit.Render,
            BenchBindings = merged
                .Values.OrderBy(b => b.BindingName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            BenchBindingExtensions = new List<BenchBindingExtension>(),
            Synth = circuit.Synth,
            Provenance = circuit.Provenance,
        };
    }

    private static void ApplyExtensionsToBindings(
        IDictionary<string, BenchBinding> merged,
        IEnumerable<BenchBindingExtension> extensions,
        Circuit circuit,
        List<Diagnostic> diagnostics
    )
    {
        foreach (var ext in extensions)
        {
            if (!merged.TryGetValue(ext.BindingName, out var binding))
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS3010: Bench binding extension targets unknown binding '{ext.BindingName}' in circuit '{circuit.Name}'.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
                continue;
            }

            binding.Statements.AddRange(ext.Statements);
        }
    }

    private static Dictionary<string, BenchBinding> ResolveBaseBenchBindings(
        Circuit circuit,
        IReadOnlyDictionary<string, TraitDefinition> interfacesByName,
        List<Diagnostic> diagnostics
    )
    {
        var merged = new Dictionary<string, BenchBinding>(StringComparer.OrdinalIgnoreCase);

        if (circuit.Traits is { Count: > 0 })
        {
            foreach (var interfaceName in circuit.Traits)
            {
                if (!interfacesByName.TryGetValue(interfaceName, out var interfaceDef))
                {
                    continue;
                }

                foreach (var binding in interfaceDef.BenchBindings)
                {
                    if (!merged.TryAdd(binding.BindingName, Clone(binding)))
                    {
                        diagnostics.Add(
                            new Diagnostic(
                                $"CAS3011: Duplicate inherited bench binding name '{binding.BindingName}' on circuit '{circuit.Name}'.",
                                DiagnosticSeverity.Error,
                                "<bench>",
                                1,
                                1
                            )
                        );
                    }
                }
            }
        }

        foreach (var binding in circuit.BenchBindings)
        {
            merged[binding.BindingName] = Clone(binding);
        }

        return merged;
    }

    private static BenchBinding Clone(BenchBinding binding)
    {
        return new BenchBinding
        {
            BenchName = binding.BenchName,
            BindingName = binding.BindingName,
            Statements = new List<BenchBindingStatement>(binding.Statements),
        };
    }
}
