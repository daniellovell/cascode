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
    /// <summary>
    /// Merges per-circuit bench binding extensions into their resolved bench bindings and returns a document containing the updated circuits.
    /// </summary>
    /// <param name="document">The CascodeDocument to process.</param>
    /// <param name="diagnostics">A list to which any diagnostics produced during processing will be appended.</param>
    /// <returns>A new CascodeDocument where circuits that had bench binding extensions have those extensions applied and their BenchBindingExtensions cleared; circuits without extensions are preserved unchanged.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="document"/> or <paramref name="diagnostics"/> is null.</exception>
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

    /// <summary>
    /// Produce a new Circuit whose bench bindings are the merged result of trait-inherited bindings, circuit-level overrides, and applied bench-binding extensions.
    /// </summary>
    /// <param name="circuit">The source circuit to update.</param>
    /// <param name="interfacesByName">Case-insensitive map of trait names to their definitions used to inherit bench bindings.</param>
    /// <param name="diagnostics">List that will receive diagnostics for issues encountered while merging or applying extensions.</param>
    /// <returns>
    /// A new Circuit with BenchBindings set to the merged bindings (ordered by BindingName, case-insensitive), BenchBindingExtensions cleared, and all other circuit properties preserved from the input.
    /// </returns>
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

    /// <summary>
    /// Appends each extension's statements to the corresponding bench binding and records an error diagnostic for any extension that targets a non-existent binding.
    /// </summary>
    /// <param name="merged">Dictionary mapping binding names to their current <see cref="BenchBinding"/> instances; target bindings are looked up by <c>BindingName</c>.</param>
    /// <param name="extensions">Bench binding extensions whose <c>Statements</c> will be appended to the matching bindings identified by each extension's <c>BindingName</c>.</param>
    /// <param name="circuit">The circuit containing the bindings; its name is used when emitting diagnostics for unknown targets.</param>
    /// <param name="diagnostics">List to which an error diagnostic (CAS3010) is added for each extension that targets a missing binding.</param>
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

    /// <summary>
    /// Build a case-insensitive map of bench bindings for a circuit by inheriting bindings from its traits and then applying the circuit's own bindings as overrides.
    /// </summary>
    /// <param name="circuit">The circuit whose bench bindings are being resolved.</param>
    /// <param name="interfacesByName">A lookup of trait (interface) definitions by name, used to inherit bench bindings.</param>
    /// <param name="diagnostics">A list to which diagnostics are appended; a CAS3011 error is added when two inherited traits define the same binding name.</param>
    /// <returns>
    /// A dictionary keyed by binding name (case-insensitive) containing cloned BenchBinding instances where trait bindings are inherited first and circuit-level bindings overwrite inherited ones.
    /// </returns>
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