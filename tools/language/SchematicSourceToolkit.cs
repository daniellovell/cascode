using System.Collections.Generic;

namespace Cascode.Language;

public static class SchematicSourceToolkit
{
    public static SchematicSourceRewriteResult Rewrite(
        string path,
        string sourceText,
        IReadOnlyList<SchematicSourceOperation> operations,
        string? circuitName = null
    )
    {
        var rewritten = sourceText;
        foreach (var operation in operations)
        {
            var parsed = SchematicSourceParser.Parse(path, rewritten, circuitName);
            rewritten = Apply(parsed, operation);
        }

        return new SchematicSourceRewriteResult
        {
            SourceText = rewritten,
            LineEnding = SchematicSourceText.DetectLineEnding(rewritten),
        };
    }

    private static string Apply(ParsedSchematicSource parsed, SchematicSourceOperation operation)
    {
        return operation switch
        {
            SetRenderModeSourceOperation mode => SchematicRenderRewriter.SetRenderMode(
                parsed,
                mode.Mode
            ),
            PatchRenderEntitySourceOperation patch => SchematicRenderRewriter.PatchEntity(
                parsed,
                patch.Name,
                patch.Patch
            ),
            ApplyRenderSnapshotSourceOperation snapshot => SchematicRenderRewriter.ApplySnapshot(
                parsed,
                snapshot.Mode,
                snapshot.Entities
            ),
            RemoveRenderEntitiesSourceOperation remove => SchematicRenderRewriter.RemoveEntities(
                parsed,
                remove.Names
            ),
            SetDeviceParamSourceOperation setParam => SchematicStructuralRewriter.SetDeviceParam(
                parsed,
                setParam.DeviceId,
                setParam.Param,
                setParam.Value
            ),
            InsertRailSourceOperation insertRail => SchematicStructuralRewriter.InsertRail(
                parsed,
                insertRail.Kind,
                insertRail.Name
            ),
            RemoveRailSourceOperation removeRail => SchematicStructuralRewriter.RemoveRail(
                parsed,
                removeRail.Kind,
                removeRail.Name
            ),
            DeleteDeviceSourceOperation deleteDevice => SchematicStructuralRewriter.DeleteDevice(
                parsed,
                deleteDevice.DeviceId
            ),
            ConnectEndpointsSourceOperation connect => SchematicStructuralRewriter.Connect(
                parsed,
                connect.From,
                connect.To
            ),
            DisconnectEndpointsSourceOperation disconnect => SchematicStructuralRewriter.Disconnect(
                parsed,
                disconnect.From,
                disconnect.To
            ),
            _ => parsed.Text,
        };
    }
}
