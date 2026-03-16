using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language.Validation;

public static class LevelStructureValidator
{
    public static void Check(CascodeDocument document, List<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(diagnostics);

        foreach (var circuit in document.Circuits)
        {
            var hasSlot = circuit.Slot is not null;
            var hasSlotContent =
                circuit.Slot is { } slot
                && (slot.Nets.Count > 0 || slot.Instances.Count > 0 || slot.Connections.Count > 0);
            var hasFill = circuit.Fill is not null;
            var hasSomeFillInstances =
                circuit.Fill?.Instances.Any(instance => instance.IsSomeRequest) == true;

            switch (circuit.Level)
            {
                case CascodeLevel.HL:
                    if (hasFill)
                    {
                        diagnostics.Add(
                            Diagnostic(
                                circuit,
                                "CAS3033",
                                "HL circuits must not define a fill block."
                            )
                        );
                    }

                    if (hasSlotContent)
                    {
                        diagnostics.Add(
                            Diagnostic(
                                circuit,
                                "CAS3034",
                                "HL circuits may only use a bare slot. Structural composition belongs in ML fill blocks."
                            )
                        );
                    }
                    break;

                case CascodeLevel.ML:
                    if (hasSlot)
                    {
                        diagnostics.Add(
                            Diagnostic(
                                circuit,
                                "CAS3035",
                                "ML circuits must not define slot. Use fill and `Some <Interface> <name>` requests for unresolved children."
                            )
                        );
                    }
                    break;

                case CascodeLevel.EL:
                    if (hasSlot)
                    {
                        diagnostics.Add(
                            Diagnostic(
                                circuit,
                                "CAS3035",
                                "EL circuits must not define slot. Use fill with concrete children and devices."
                            )
                        );
                    }

                    if (hasSomeFillInstances)
                    {
                        diagnostics.Add(
                            Diagnostic(
                                circuit,
                                "CAS3036",
                                "EL circuits must not contain `Some` child requests. Resolve all children concretely before EL."
                            )
                        );
                    }
                    break;
            }
        }
    }

    private static Diagnostic Diagnostic(Circuit circuit, string code, string message)
    {
        return new Diagnostic(
            $"{code}: {message} Circuit '{circuit.Name}' is declared {circuit.Level}.",
            DiagnosticSeverity.Error,
            "<level>",
            1,
            1
        );
    }
}
