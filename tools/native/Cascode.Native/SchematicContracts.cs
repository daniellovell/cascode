namespace Cascode.Native;

internal enum RenderSchematicMode
{
    RespectDocument,
    Auto,
    Manual,
}

internal sealed class SchematicDocumentResponse
{
    public string Schema { get; init; } = "cascode.schematic/1.0";
    public required string DocumentId { get; init; }
    public required int Revision { get; init; }
    public required string Circuit { get; init; }
    public required RenderSourceInfo RenderSource { get; init; }
    public required StructuralInfo Structural { get; init; }
    public required LayoutInfo Layout { get; init; }
    public required RenderCacheInfo RenderCache { get; init; }
    public required IReadOnlyDictionary<string, SymbolCatalogEntry> SymbolCatalog { get; init; }
    public required IReadOnlyList<ApiDiagnostic> Diagnostics { get; init; }
}

internal sealed class RoutePreviewResponse
{
    public string Schema { get; init; } = "cascode.routePreview/1.0";
    public required bool Valid { get; init; }
    public required IReadOnlyList<SegmentValue> Segments { get; init; }
    public required IReadOnlyList<RoutePreviewNet> Nets { get; init; }
    public string? Diagnostic { get; init; }
}

internal sealed class RoutePreviewNet
{
    public required string Name { get; init; }
    public required IReadOnlyList<SegmentValue> Segments { get; init; }
}

internal sealed class RenderSourceInfo
{
    public required bool HasRenderBlock { get; init; }
    public required string Mode { get; init; }
}

internal sealed class StructuralInfo
{
    public required IReadOnlyList<StructuralDevice> Devices { get; init; }
    public required IReadOnlyList<StructuralPort> Ports { get; init; }
    public required IReadOnlyList<StructuralNet> Nets { get; init; }
    public required IReadOnlyList<string> Supplies { get; init; }
    public required IReadOnlyList<string> Grounds { get; init; }
}

internal sealed class StructuralDevice
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required IReadOnlyList<string> Terminals { get; init; }
    public required string Primitive { get; init; }
    public required IReadOnlyDictionary<string, string> Size { get; init; }
}

internal sealed class StructuralPort
{
    public required string Name { get; init; }
    public required string Direction { get; init; }
    public required string Type { get; init; }
}

internal sealed class StructuralNet
{
    public required string Name { get; init; }
    public required IReadOnlyList<string[]> Connections { get; init; }
}

internal sealed class LayoutInfo
{
    public required IReadOnlyList<LayoutDevice> Devices { get; init; }
    public required IReadOnlyList<LayoutPort> Ports { get; init; }
    public required IReadOnlyList<LayoutNet> Nets { get; init; }
}

internal sealed class LayoutDevice
{
    public required string Id { get; init; }
    public required PointValue Position { get; init; }
    public required OrientationValue Orientation { get; init; }
    public required BboxValue Bbox { get; init; }
}

internal sealed class OrientationValue
{
    public required int Rotate { get; init; }
    public required bool MirrorX { get; init; }
}

internal sealed class LayoutPort
{
    public required string Name { get; init; }
    public required PointValue Position { get; init; }
    public required string Side { get; init; }
    public required OrientationValue Orientation { get; init; }
}

internal sealed class LayoutNet
{
    public required string Name { get; init; }
    public required IReadOnlyList<SegmentValue> Segments { get; init; }
    public required IReadOnlyList<PointValue> Junctions { get; init; }
}

internal sealed class SegmentValue
{
    public required PointValue From { get; init; }
    public required PointValue To { get; init; }
}

internal sealed class PointValue
{
    public required double X { get; init; }
    public required double Y { get; init; }
}

internal sealed class BboxValue
{
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
}

internal sealed class RenderCacheInfo
{
    public required IReadOnlyDictionary<
        string,
        IReadOnlyDictionary<string, PointValue>
    > TerminalPoints { get; init; }

    public required IReadOnlyDictionary<string, BboxValue> ComputedBboxes { get; init; }
}

internal sealed class ApiDiagnostic
{
    public required string Severity { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public ApiDiagnosticEntityRefs? EntityRefs { get; init; }
    public ApiDiagnosticGeometry? Geometry { get; init; }
}

internal sealed class ApiDiagnosticEntityRefs
{
    public string? DeviceId { get; init; }
    public string? PortName { get; init; }
    public string? NetName { get; init; }
    public int? SegmentIndex { get; init; }
}

internal sealed class ApiDiagnosticGeometry
{
    public PointValue? Point { get; init; }
    public SegmentValue? Segment { get; init; }
    public BboxValue? Bbox { get; init; }
}

internal sealed class SymbolCatalogEntry
{
    public required double[] ViewBox { get; init; }
    public required IReadOnlyList<SymbolPathEntry> Paths { get; init; }
    public required IReadOnlyDictionary<string, SymbolTerminalEntry> Terminals { get; init; }
}

internal sealed class SymbolPathEntry
{
    public required string D { get; init; }
    public required string Style { get; init; }
}

internal sealed class SymbolTerminalEntry
{
    public required double X { get; init; }
    public required double Y { get; init; }
}

internal sealed class RenderComputationState
{
    public required Cascode.Render.Analysis.CircuitGraph Graph { get; init; }
    public required Cascode.Render.Placement.CoarseGridResult Placement { get; init; }
    public required Cascode.Render.Routing.RoutingResult Routing { get; init; }
    public required IReadOnlyList<Cascode.Render.Layout.RenderDiagnostic> Diagnostics { get; init; }
}
