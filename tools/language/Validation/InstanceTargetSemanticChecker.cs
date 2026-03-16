using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language.Validation;

internal static class InstanceTargetSemanticChecker
{
    public static void Check(CascodeDocument document, List<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var circuitsByName = new Dictionary<string, Circuit>(StringComparer.Ordinal);
        foreach (var circuit in document.Circuits)
        {
            circuitsByName.TryAdd(circuit.Name, circuit);
        }

        var partsByName = new Dictionary<string, PartDefinition>(StringComparer.Ordinal);
        foreach (var part in document.Parts)
        {
            partsByName.TryAdd(part.Name, part);
        }

        var primitivesByName = new Dictionary<string, PrimitiveDefinition>(StringComparer.Ordinal);
        foreach (var primitive in document.Primitives)
        {
            primitivesByName.TryAdd(primitive.Name, primitive);
        }

        foreach (var circuit in document.Circuits)
        {
            if (circuit.Fill?.Instances is null)
            {
                continue;
            }

            foreach (var instance in circuit.Fill.Instances)
            {
                if (instance.IsSomeRequest)
                {
                    continue;
                }

                if (
                    InstanceTargetResolver.TryResolveConcreteTarget(
                        instance.Type,
                        instance.DeclaredType,
                        circuitsByName,
                        partsByName,
                        primitivesByName,
                        out _,
                        out var error
                    )
                )
                {
                    continue;
                }

                if (error == InstanceTargetResolutionError.Unresolved)
                {
                    continue;
                }

                var location = $"circuit {circuit.Name}, instance {instance.Id}";
                switch (error)
                {
                    case InstanceTargetResolutionError.IncompatibleDeclaredType:
                        diagnostics.Add(
                            new Diagnostic(
                                $"INST-001: Instance '{instance.Id}' declared type '{instance.DeclaredType}' is incompatible with constructor target '{instance.Type}'.",
                                DiagnosticSeverity.Error,
                                "<semantic>",
                                1,
                                1
                            )
                        );
                        break;
                    case InstanceTargetResolutionError.Ambiguous:
                        diagnostics.Add(
                            new Diagnostic(
                                $"INST-002: Instance '{instance.Id}' constructor target '{instance.Type}' is ambiguous in {location}. Use a less ambiguous declared type or unambiguous identifier.",
                                DiagnosticSeverity.Error,
                                "<semantic>",
                                1,
                                1
                            )
                        );
                        break;
                }
            }
        }
    }
}
