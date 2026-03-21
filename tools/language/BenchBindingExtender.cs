using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language;

/// <summary>
/// Applies circuit-level <c>extend &lt;binding&gt; { ... }</c> blocks by merging their statements into
/// the resolved bench bindings (inherited + circuit overrides).
/// </summary>
/// <remarks>
/// The actual inheritance/override/extension ordering lives in
/// <see cref="BenchBindingResolver"/> so this pass, the validator, and the runtime planner
/// all elaborate the same effective binding set. This file is responsible only for
/// materializing that resolved view back onto circuits after the resolver has decided what
/// the circuit means.
/// </remarks>
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
        // Resolve first so the persisted post-link circuit uses the exact same binding view
        // that validation and planning will consume.
        var resolution = BenchBindingResolver.ResolveForCircuit(circuit, interfacesByName);
        foreach (var bindingName in resolution.DuplicateInheritedBindingNames)
        {
            diagnostics.Add(
                new Diagnostic(
                    $"CAS3011: Duplicate inherited bench binding name '{bindingName}' on circuit '{circuit.Name}'.",
                    DiagnosticSeverity.Error,
                    "<bench>",
                    1,
                    1
                )
            );
        }
        foreach (var bindingName in resolution.UnknownExtensionTargets)
        {
            diagnostics.Add(
                new Diagnostic(
                    $"CAS3010: Bench binding extension targets unknown binding '{bindingName}' in circuit '{circuit.Name}'.",
                    DiagnosticSeverity.Error,
                    "<bench>",
                    1,
                    1
                )
            );
        }

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
            BenchBindings = resolution
                .Bindings.Values.Select(binding => binding.Binding)
                .OrderBy(binding => binding.BindingName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            BenchBindingExtensions = new List<BenchBindingExtension>(),
            Synth = circuit.Synth,
            Provenance = circuit.Provenance,
        };
    }
}
