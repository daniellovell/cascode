using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language.Validation;

public static class InterfaceContractValidator
{
    public static void Check(CascodeDocument document, List<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var interfacesByName = document.Traits.ToDictionary(
            trait => trait.Name,
            StringComparer.OrdinalIgnoreCase
        );

        foreach (var circuit in document.Circuits)
        {
            if (circuit.Traits is not { Count: > 0 })
            {
                continue;
            }

            var circuitTerminals = TerminalContractModel.ForCircuit(circuit);
            foreach (var interfaceName in circuit.Traits)
            {
                if (!interfacesByName.TryGetValue(interfaceName, out var interfaceDef))
                {
                    diagnostics.Add(
                        new Diagnostic(
                            $"CAS3028: Circuit '{circuit.Name}' implements unknown interface '{interfaceName}' in a complete document.",
                            DiagnosticSeverity.Error,
                            "<interface>",
                            1,
                            1
                        )
                    );
                    continue;
                }

                CheckInterfaceImplementation(circuit, interfaceDef, circuitTerminals, diagnostics);
            }
        }
    }

    private static void CheckInterfaceImplementation(
        Circuit circuit,
        TraitDefinition interfaceDef,
        IReadOnlyDictionary<string, TerminalContract> circuitTerminals,
        List<Diagnostic> diagnostics
    )
    {
        var interfaceTerminals = TerminalContractModel.ForInterface(interfaceDef);
        foreach (var expected in interfaceTerminals.Values)
        {
            if (!circuitTerminals.TryGetValue(expected.Name, out var actual))
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS3029: Circuit '{circuit.Name}' does not provide required terminal '{expected.Name}' from interface '{interfaceDef.Name}'.",
                        DiagnosticSeverity.Error,
                        "<interface>",
                        1,
                        1
                    )
                );
                continue;
            }

            if (RequiresDirectionMatch(expected, actual) && expected.Direction != actual.Direction)
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS3030: Circuit '{circuit.Name}' terminal '{expected.Name}' direction '{actual.Direction!.Value.ToCascodeString()}' does not match interface '{interfaceDef.Name}' direction '{expected.Direction!.Value.ToCascodeString()}'.",
                        DiagnosticSeverity.Error,
                        "<interface>",
                        1,
                        1
                    )
                );
            }

            if (!HasMatchingShape(expected, actual))
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS3031: Circuit '{circuit.Name}' terminal '{expected.Name}' has incompatible shape for interface '{interfaceDef.Name}' ({actual.Leaves.Count} vs {expected.Leaves.Count}).",
                        DiagnosticSeverity.Error,
                        "<interface>",
                        1,
                        1
                    )
                );
                continue;
            }

            for (var i = 0; i < expected.Leaves.Count; i++)
            {
                if (
                    string.Equals(
                        expected.Leaves[i].LeafType,
                        actual.Leaves[i].LeafType,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    continue;
                }

                diagnostics.Add(
                    new Diagnostic(
                        $"CAS3032: Circuit '{circuit.Name}' terminal '{expected.Name}' leaf '{actual.Leaves[i].Path}' type '{actual.Leaves[i].LeafType}' does not match interface '{interfaceDef.Name}' leaf '{expected.Leaves[i].Path}' type '{expected.Leaves[i].LeafType}'.",
                        DiagnosticSeverity.Error,
                        "<interface>",
                        1,
                        1
                    )
                );
            }
        }
    }

    private static bool RequiresDirectionMatch(TerminalContract expected, TerminalContract actual)
    {
        if (expected.Direction is null || actual.Direction is null)
        {
            return false;
        }

        return !IsRail(expected.Type) && !IsRail(actual.Type);
    }

    private static bool HasMatchingShape(TerminalContract expected, TerminalContract actual)
    {
        if (expected.Leaves.Count != actual.Leaves.Count)
        {
            return false;
        }

        for (var i = 0; i < expected.Leaves.Count; i++)
        {
            if (
                !string.Equals(
                    expected.Leaves[i].RelativePath,
                    actual.Leaves[i].RelativePath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsRail(string type) =>
        type.Equals("supply", StringComparison.OrdinalIgnoreCase)
        || type.Equals("ground", StringComparison.OrdinalIgnoreCase);
}
