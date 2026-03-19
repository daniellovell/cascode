namespace Cascode.Render.Layout;

public enum RenderDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public sealed class RenderDiagnostic
{
    public required RenderDiagnosticSeverity Severity { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public RenderDiagnosticEntityRefs? EntityRefs { get; init; }
    public RenderDiagnosticGeometry? Geometry { get; init; }
}

public sealed class RenderDiagnosticEntityRefs
{
    public string? DeviceId { get; init; }
    public string? PortName { get; init; }
    public string? NetName { get; init; }
    public int? SegmentIndex { get; init; }
}

public sealed class RenderDiagnosticGeometry
{
    public RenderDiagnosticPoint? Point { get; init; }
    public RenderDiagnosticSegment? Segment { get; init; }
    public RenderDiagnosticBbox? Bbox { get; init; }
}

public readonly record struct RenderDiagnosticPoint(double X, double Y);

public readonly record struct RenderDiagnosticSegment(
    RenderDiagnosticPoint From,
    RenderDiagnosticPoint To
);

public readonly record struct RenderDiagnosticBbox(double X, double Y, double Width, double Height);
