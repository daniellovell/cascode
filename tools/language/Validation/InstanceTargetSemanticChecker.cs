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

        var circuitsByName = document.Circuits.ToDictionary(c => c.Name, StringComparer.Ordinal);
        var partsByName = document.Parts.ToDictionary(p => p.Name, StringComparer.Ordinal);
        var primitivesByName = document.Primitives.ToDictionary(
            p => p.Name,
            StringComparer.Ordinal
        );

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
                                $"INST-002: Instance '{instance.Id}' constructor target '{instance.Type}' is ambiguous in {location}. Use a less ambiguous declared type or a fully-qualified target.",
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
