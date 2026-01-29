using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language;

/// <summary>
/// Resolves attach statements to determine net connectivity using union-find.
/// </summary>
public sealed partial class AttachResolver
{
    private const string PowerDomain = "power";
    private const string GroundDomain = "ground";
    private const string DefaultDomain = "analog";

    private readonly CascodeDocument _document;
    private readonly Dictionary<string, TraitDefinition> _traitsByName;
    private readonly Dictionary<string, Circuit> _circuitsByName;
    private readonly Dictionary<string, BundleType> _bundleTypesByName;
    private readonly List<Diagnostic> _constructorDiagnostics = new();

    /// <summary>
    /// Initializes a new AttachResolver for the given document.
    /// </summary>
    public AttachResolver(CascodeDocument document)
    {
        _document = document;
        _traitsByName = BuildLookup(document.Traits, t => t.Name, "trait", _constructorDiagnostics);
        _circuitsByName = BuildLookup(
            document.Circuits,
            c => c.Name,
            "circuit",
            _constructorDiagnostics
        );
        _bundleTypesByName = BuildLookup(
            document.BundleTypes,
            b => b.Name,
            "bundle type",
            _constructorDiagnostics
        );
    }

    /// <summary>
    /// Resolves all attach statements in the document, returning the resolved
    /// net connectivity map and any diagnostics.
    /// </summary>
    public AttachResolutionResult Resolve()
    {
        var result = new AttachResolutionResult();
        result._diagnostics.AddRange(_constructorDiagnostics);

        foreach (var circuit in _document.Circuits)
        {
            if (circuit.Fill is null)
            {
                continue;
            }

            var circuitResult = ResolveCircuit(circuit);
            result._circuitResults[circuit.Name] = circuitResult;
            result._diagnostics.AddRange(circuitResult.Diagnostics);
        }

        return result;
    }

    /// <summary>
    /// Resolves attach statements for a single circuit.
    /// </summary>
    private CircuitResolutionResult ResolveCircuit(Circuit circuit)
    {
        var result = new CircuitResolutionResult();
        // Defensive: Resolve() pre-filters null Fill, but guard here for direct calls.
        if (circuit.Fill is null)
        {
            return result;
        }

        var context = new ResolutionContext();
        InitializeNetAtoms(circuit, context);
        InitializeInstanceEndpoints(circuit, context);
        ApplyDeviceBindings(circuit, context);
        ApplyInstanceBindings(circuit, context, result._diagnostics);
        ApplyConnectStatements(circuit, context, result._diagnostics);
        ApplyAttachStatements(circuit, context, result._diagnostics);
        FinalizeResolution(context, result);
        PopulateAttachBindings(circuit, context, result);

        return result;
    }

    internal static List<string> BuildInstanceChain(AttachStatement attach)
    {
        var chain = new List<string>(1 + attach.TargetInstances.Count) { attach.SourceInstance };
        chain.AddRange(attach.TargetInstances);
        return chain;
    }

    private static Dictionary<string, T> BuildLookup<T>(
        IEnumerable<T> items,
        Func<T, string> nameSelector,
        string itemKind,
        List<Diagnostic> diagnostics
    )
    {
        var lookup = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var name = nameSelector(item);
            if (!lookup.TryAdd(name, item))
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS0026: Duplicate {itemKind} name '{name}'; keeping first definition",
                        DiagnosticSeverity.Warning,
                        "<document>",
                        1,
                        1
                    )
                );
            }
        }

        return lookup;
    }
}
