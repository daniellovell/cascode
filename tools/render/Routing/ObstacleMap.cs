namespace Cascode.Render.Routing;

using Cascode.Render.Analysis;
using Cascode.Render.Layout;
using Cascode.Render.Placement;

/// <summary>
/// A rectangular obstacle that wires must route around.
/// </summary>
public readonly record struct Obstacle(int MinX, int MinY, int MaxX, int MaxY);

/// <summary>
/// Builds and queries obstacle map from device placements.
/// </summary>
public static class ObstacleMap
{
    private const int Margin = 2;

    /// <summary>
    /// Builds obstacle list from placed devices.
    /// </summary>
    public static IReadOnlyList<Obstacle> FromPlacement(
        CoarseGridResult placement,
        CircuitGraph graph
    )
    {
        var obstacles = new List<Obstacle>();

        foreach (var (deviceId, cell) in placement.DevicePlacements)
        {
            if (!graph.Devices.TryGetValue(deviceId, out var device))
            {
                continue;
            }

            var deviceType = device.DeviceType.ToLowerInvariant();
            var obstacle = ComputeDeviceBounds(deviceType, cell);
            if (obstacle.HasValue)
            {
                obstacles.Add(obstacle.Value);
            }
        }

        return obstacles;
    }

    /// <summary>
    /// Computes bounding box for a device at given cell.
    /// </summary>
    private static Obstacle? ComputeDeviceBounds(string deviceType, GridCell cell)
    {
        if (deviceType is "nmos" or "nfet" or "pmos" or "pfet")
        {
            var p = DeviceGeometry.GetMosfetPlacement(cell.Row, cell.Column, cell.MirrorX);
            return new Obstacle(
                MinX: (int)p.X + Margin,
                MinY: (int)p.Y + Margin,
                MaxX: (int)(p.X + DeviceGeometry.MosfetWidth) - Margin,
                MaxY: (int)(p.Y + DeviceGeometry.MosfetHeight) - Margin
            );
        }

        if (deviceType is "resistor" or "capacitor")
        {
            var p = DeviceGeometry.GetPassivePlacement(cell.Row, cell.Column);
            return new Obstacle(
                MinX: (int)p.X + Margin,
                MinY: (int)p.Y + Margin,
                MaxX: (int)(p.X + DeviceGeometry.PassiveHeight) - Margin,
                MaxY: (int)(p.Y + DeviceGeometry.PassiveWidth) - Margin
            );
        }

        if (deviceType == "instance")
        {
            var cx = DeviceGeometry.GetCellCenterX(cell.Column);
            var cy = DeviceGeometry.GetCellCenterY(cell.Row);
            var x = cx - DeviceGeometry.InstanceBlockWidth / 2.0;
            var y = cy - DeviceGeometry.InstanceBlockHeight / 2.0;
            return new Obstacle(
                MinX: (int)x + Margin,
                MinY: (int)y + Margin,
                MaxX: (int)(x + DeviceGeometry.InstanceBlockWidth) - Margin,
                MaxY: (int)(y + DeviceGeometry.InstanceBlockHeight) - Margin
            );
        }

        return null;
    }

    /// <summary>
    /// Checks if a wire segment passes through an obstacle's interior.
    /// Touching the boundary is allowed (terminals are on edges).
    /// </summary>
    public static bool SegmentIntersectsObstacle(int x1, int y1, int x2, int y2, Obstacle obs)
    {
        // Normalize segment coordinates
        var minX = Math.Min(x1, x2);
        var maxX = Math.Max(x1, x2);
        var minY = Math.Min(y1, y2);
        var maxY = Math.Max(y1, y2);

        // Quick rejection: no overlap at all
        if (maxX < obs.MinX || minX > obs.MaxX || maxY < obs.MinY || minY > obs.MaxY)
        {
            return false;
        }

        // Horizontal segment
        if (y1 == y2)
        {
            var y = y1;
            // Segment passes through obstacle if Y is strictly inside and X range overlaps
            if (y > obs.MinY && y < obs.MaxY && maxX > obs.MinX && minX < obs.MaxX)
            {
                return true;
            }
        }

        // Vertical segment
        if (x1 == x2)
        {
            var x = x1;
            // Segment passes through obstacle if X is strictly inside and Y range overlaps
            if (x > obs.MinX && x < obs.MaxX && maxY > obs.MinY && minY < obs.MaxY)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if segment intersects any obstacle.
    /// </summary>
    public static bool SegmentIntersectsAny(
        int x1,
        int y1,
        int x2,
        int y2,
        IReadOnlyList<Obstacle> obstacles
    )
    {
        foreach (var obs in obstacles)
        {
            if (SegmentIntersectsObstacle(x1, y1, x2, y2, obs))
            {
                return true;
            }
        }
        return false;
    }
}
