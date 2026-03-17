using Cascode.Language;

namespace Cascode.Native;

internal static class ManualRenderSnapshotService
{
    public static void EnsureManualRender(DocumentState state)
    {
        var circuit = FindCircuit(state);
        if (circuit.Render?.Mode == RenderLayoutMode.Manual)
        {
            return;
        }

        circuit.Render = ManualRenderSnapshotBuilder.Build(state, circuit);
    }

    public static void RefreshManualRender(DocumentState state)
    {
        var circuit = FindCircuit(state);
        if (circuit.Render?.Mode != RenderLayoutMode.Manual)
        {
            return;
        }

        circuit.Render = ManualRenderSnapshotBuilder.BuildWithExactPlacementRouting(state, circuit);
    }

    public static RenderBlock Build(DocumentState state, Circuit circuit)
    {
        return ManualRenderSnapshotBuilder.Build(state, circuit);
    }

    private static Circuit FindCircuit(DocumentState state)
    {
        var circuit = state.Document.Circuits.FirstOrDefault(c => c.Name == state.CircuitName);
        if (circuit is null)
        {
            throw new ApiException(
                "CASAPI-INVALID-REQUEST",
                $"Circuit '{state.CircuitName}' was not found in document '{state.DocumentId}'."
            );
        }

        return circuit;
    }
}
