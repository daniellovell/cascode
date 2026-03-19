using System.Collections.Generic;

namespace Cascode.Language;

public enum SchematicRailKind
{
    Supply,
    Ground,
}

public enum RenderEntityField
{
    Place,
    Orientation,
    Side,
    Route,
    Segments,
    ZIndex,
}

public sealed class RenderEntityPatch
{
    public RenderPlacement? Place { get; init; }
    public RenderOrientation? Orientation { get; init; }
    public RenderPortSide? Side { get; init; }
    public RenderRoute? Route { get; init; }
    public IReadOnlyList<RenderSegment>? Segments { get; init; }
    public int? ZIndex { get; init; }
    public IReadOnlySet<RenderEntityField>? ClearFields { get; init; }
}

public abstract record SchematicSourceOperation;

public sealed record SetRenderModeSourceOperation(RenderLayoutMode Mode) : SchematicSourceOperation;

public sealed record PatchRenderEntitySourceOperation(string Name, RenderEntityPatch Patch)
    : SchematicSourceOperation;

public sealed record ApplyRenderSnapshotSourceOperation(
    RenderLayoutMode Mode,
    IReadOnlyList<RenderEntity> Entities
) : SchematicSourceOperation;

public sealed record RemoveRenderEntitiesSourceOperation(IReadOnlyList<string> Names)
    : SchematicSourceOperation;

public sealed record SetDeviceParamSourceOperation(string DeviceId, string Param, string Value)
    : SchematicSourceOperation;

public sealed record InsertRailSourceOperation(SchematicRailKind Kind, string Name)
    : SchematicSourceOperation;

public sealed record RemoveRailSourceOperation(SchematicRailKind Kind, string Name)
    : SchematicSourceOperation;

public sealed record DeleteDeviceSourceOperation(string DeviceId) : SchematicSourceOperation;

public sealed record ConnectEndpointsSourceOperation(string From, string To)
    : SchematicSourceOperation;

public sealed record DisconnectEndpointsSourceOperation(string From, string To)
    : SchematicSourceOperation;

public sealed class SchematicSourceRewriteResult
{
    public required string SourceText { get; init; }
    public required string LineEnding { get; init; }
}
