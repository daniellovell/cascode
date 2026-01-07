using System;
using System.Collections.Generic;
using System.Linq;
using Cascode.Parser;

namespace Cascode.ACIR;

/// <summary>
/// Resolves attach statements to determine net connectivity using union-find.
/// </summary>
public sealed partial class AttachResolver
{
    private const string PowerDomain = "power";
    private const string GroundDomain = "ground";
    private const string DefaultDomain = "analog";

    private readonly ACIRDocument _document;
    private readonly Dictionary<string, TraitDefinition> _traitsByName;
    private readonly Dictionary<string, Circuit> _circuitsByName;
    private readonly Dictionary<string, BundleType> _bundleTypesByName;

    /// <summary>
    /// Initializes a new AttachResolver for the given document.
    /// </summary>
    public AttachResolver(ACIRDocument document)
    {
        _document = document;
        _traitsByName = document.Traits.ToDictionary(t => t.Name, StringComparer.Ordinal);
        _circuitsByName = document.Circuits.ToDictionary(c => c.Name, StringComparer.Ordinal);
        _bundleTypesByName = document.BundleTypes.ToDictionary(b => b.Name, StringComparer.Ordinal);
    }

    /// <summary>
    /// Resolves all attach statements in the document, returning the resolved
    /// net connectivity map and any diagnostics.
    /// </summary>
    public AttachResolutionResult Resolve()
    {
        var result = new AttachResolutionResult();

        foreach (var circuit in _document.Circuits)
        {
            if (circuit.Fill is null)
            {
                continue;
            }

            var circuitResult = ResolveCircuit(circuit);
            result.CircuitResults[circuit.Name] = circuitResult;
            result.Diagnostics.AddRange(circuitResult.Diagnostics);
        }

        return result;
    }

    /// <summary>
    /// Resolves attach statements for a single circuit.
    /// </summary>
    private CircuitResolutionResult ResolveCircuit(Circuit circuit)
    {
        var result = new CircuitResolutionResult();
        if (circuit.Fill is null)
        {
            return result;
        }

        var context = new ResolutionContext();
        InitializeNetAtoms(circuit, context);
        InitializeInstanceEndpoints(circuit, context);
        ApplyDeviceBindings(circuit, context);
        ApplyInstanceBindings(circuit, context, result.Diagnostics);
        ApplyConnectStatements(circuit, context, result.Diagnostics);
        ApplyAttachStatements(circuit, context, result.Diagnostics);
        FinalizeResolution(context, result);
        PopulateAttachBindings(circuit, context, result);

        return result;
    }
}
