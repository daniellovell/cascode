using System.Collections.Generic;

namespace Cascode.Language;

/// <summary>
/// Render-intent block containing sparse schematic overrides.
/// </summary>
public sealed class RenderBlock
{
    public List<RenderEntity> Entities { get; init; } = new();
}

/// <summary>
/// Unified render entity entry resolved to device, port, or net by validation.
/// </summary>
public sealed class RenderEntity
{
    public string Name { get; init; } = string.Empty;
    public RenderEntityKind Kind { get; set; } = RenderEntityKind.Unknown;
    public int? SourceLine { get; set; }
    public int? SourceColumn { get; set; }
    public RenderPlacement? Place { get; set; }
    public RenderOrientation? Orientation { get; set; }
    public int? ZIndex { get; set; }
    public RenderPortSide? Side { get; set; }
    public RenderRoute? Route { get; set; }
    public List<RenderPointExpression> Waypoints { get; init; } = new();
}

/// <summary>
/// Entity kind resolved during semantic validation.
/// </summary>
public enum RenderEntityKind
{
    Unknown,
    Device,
    Port,
    Net,
}

/// <summary>
/// Placement override.
/// </summary>
public sealed class RenderPlacement
{
    public required RenderPointExpression Point { get; init; }
    public RenderConstraintStrength? Strength { get; init; }
}

/// <summary>
/// Orientation override for symbol rendering.
/// </summary>
public sealed class RenderOrientation
{
    public int Rotate { get; init; }
    public bool MirrorX { get; init; }
}

/// <summary>
/// Route strategy override for a net.
/// </summary>
public sealed class RenderRoute
{
    public RenderRouteMode Mode { get; init; } = RenderRouteMode.Auto;
    public RenderConstraintStrength? Strength { get; init; }
}

public enum RenderConstraintStrength
{
    Hard,
    Soft,
    Hint,
}

public enum RenderRouteMode
{
    Auto,
    Ortho,
}

public enum RenderPortSide
{
    Left,
    Right,
    Top,
    Bottom,
    Auto,
}

/// <summary>
/// Base type for point expressions in render intent.
/// </summary>
public abstract record RenderPointExpression;

/// <summary>
/// Absolute point in render units.
/// </summary>
public sealed record RenderAbsPoint(int X, int Y) : RenderPointExpression;

/// <summary>
/// Point relative to an anchor in render units.
/// </summary>
public sealed record RenderRefPoint(string Anchor, int Dx, int Dy) : RenderPointExpression;

/// <summary>
/// Point relative to the previous waypoint in render units.
/// </summary>
public sealed record RenderRelPoint(int Dx, int Dy) : RenderPointExpression;
