using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language;

/// <summary>
/// Identifies which declaration site supplied the base body for a resolved bench binding.
/// </summary>
internal enum BenchBindingOriginKind
{
    Interface,
    Circuit,
}

/// <summary>
/// Names the owner that contributed the resolved binding body before any circuit-local
/// extension statements were appended.
/// </summary>
internal sealed record BenchBindingOrigin(BenchBindingOriginKind Kind, string OwnerName);

/// <summary>
/// Resolution-time metadata for a bench binding.
/// </summary>
/// <remarks>
/// Bench bindings are source-shaped AST nodes, but validation/planning need to answer
/// semantic questions that the raw AST cannot represent directly:
/// - Did this binding come from an interface or from the circuit?
/// - Did the circuit override an inherited binding?
/// - Did a circuit <c>extend</c> append statements onto an inherited binding?
///
/// Keeping this metadata in a separate resolved layer lets validation and runtime share a
/// single interpretation of bench inheritance without turning source serialization into a
/// semantic dump of linker state.
/// </remarks>
internal sealed record BenchBindingResolutionInfo(
    BenchBindingOrigin Origin,
    bool OverridesInheritedBinding,
    int ExtensionStatementCount
)
{
    public bool HasExtensions => ExtensionStatementCount > 0;

    public bool RequiresCircuitValidation =>
        Origin.Kind == BenchBindingOriginKind.Circuit || HasExtensions;
}

/// <summary>
/// Couples a cloned <see cref="BenchBinding"/> with the semantic context that produced it.
/// </summary>
internal sealed record ResolvedBenchBinding(
    BenchBinding Binding,
    BenchBindingResolutionInfo Resolution
);

/// <summary>
/// Result of resolving the effective bench bindings for a circuit.
/// </summary>
/// <remarks>
/// The resolver returns both the usable binding map and any resolution anomalies that
/// downstream stages must surface with stage-specific diagnostic codes. This keeps the
/// actual resolution algorithm centralized while allowing validation and extension passes
/// to preserve their existing error contracts.
/// </remarks>
internal sealed record BenchBindingResolutionResult(
    IReadOnlyDictionary<string, ResolvedBenchBinding> Bindings,
    IReadOnlyList<string> DuplicateInheritedBindingNames,
    IReadOnlyList<string> UnknownExtensionTargets
);

/// <summary>
/// Builds the effective bench-binding view for a circuit.
/// </summary>
/// <remarks>
/// This is the single source of truth for bench-binding resolution order:
/// interface inheritance first, then circuit overrides, then circuit <c>extend</c> blocks.
/// Validation, extension folding, and invocation planning all consume this helper so they
/// stay aligned on what the circuit actually means.
/// </remarks>
internal static class BenchBindingResolver
{
    /// <summary>
    /// Resolves the bench bindings visible to <paramref name="circuit"/>.
    /// </summary>
    /// <param name="circuit">The circuit whose effective bench bindings are being computed.</param>
    /// <param name="interfacesByName">Lookup used to inherit interface-declared bindings.</param>
    /// <returns>
    /// A resolved binding map keyed by binding alias, plus duplicate-inheritance and
    /// unknown-extension information for callers that need to emit diagnostics.
    /// </returns>
    public static BenchBindingResolutionResult ResolveForCircuit(
        Circuit circuit,
        IReadOnlyDictionary<string, TraitDefinition> interfacesByName
    )
    {
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(interfacesByName);

        var resolved = new Dictionary<string, ResolvedBenchBinding>(
            StringComparer.OrdinalIgnoreCase
        );
        var duplicateInheritedBindingNames = new List<string>();
        var unknownExtensionTargets = new List<string>();

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
                    var resolution = new BenchBindingResolutionInfo(
                        new BenchBindingOrigin(BenchBindingOriginKind.Interface, interfaceDef.Name),
                        OverridesInheritedBinding: false,
                        ExtensionStatementCount: 0
                    );
                    if (
                        !resolved.TryAdd(
                            binding.BindingName,
                            CreateResolvedBinding(binding, resolution)
                        )
                    )
                    {
                        duplicateInheritedBindingNames.Add(binding.BindingName);
                    }
                }
            }
        }

        foreach (var binding in circuit.BenchBindings)
        {
            var resolution =
                binding.Resolution
                ?? new BenchBindingResolutionInfo(
                    new BenchBindingOrigin(BenchBindingOriginKind.Circuit, circuit.Name),
                    OverridesInheritedBinding: resolved.ContainsKey(binding.BindingName),
                    ExtensionStatementCount: 0
                );
            resolved[binding.BindingName] = CreateResolvedBinding(binding, resolution);
        }

        foreach (var extension in circuit.BenchBindingExtensions)
        {
            if (!resolved.TryGetValue(extension.BindingName, out var existing))
            {
                unknownExtensionTargets.Add(extension.BindingName);
                continue;
            }

            existing.Binding.Statements.AddRange(extension.Statements);
            var updatedResolution = existing.Resolution with
            {
                ExtensionStatementCount =
                    existing.Resolution.ExtensionStatementCount + extension.Statements.Count,
            };
            existing.Binding.Resolution = updatedResolution;
            resolved[extension.BindingName] = existing with { Resolution = updatedResolution };
        }

        return new BenchBindingResolutionResult(
            resolved,
            duplicateInheritedBindingNames,
            unknownExtensionTargets
        );
    }

    /// <summary>
    /// Clones a source binding into the resolved layer so callers can safely append
    /// extension statements without mutating parsed interface definitions.
    /// </summary>
    private static ResolvedBenchBinding CreateResolvedBinding(
        BenchBinding binding,
        BenchBindingResolutionInfo resolution
    )
    {
        var clone = new BenchBinding
        {
            BenchName = binding.BenchName,
            BindingName = binding.BindingName,
            Statements = new List<BenchBindingStatement>(binding.Statements),
            Resolution = resolution,
        };
        return new ResolvedBenchBinding(clone, resolution);
    }
}
