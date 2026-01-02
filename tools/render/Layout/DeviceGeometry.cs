namespace Cascode.Render.Layout;

/// <summary>
/// Centralized geometry for device symbols and routing alignment.
/// </summary>
public static class DeviceGeometry
{
    public const int CellWidth = 60;
    public const int CellHeight = 50;
    public const int RailMargin = 15;
    public const int RoutingPitch = 10;

    public const double MosfetWidth = 17.0;
    public const double MosfetHeight = 26.0;
    public const double PassiveWidth = 26.0;
    public const double PassiveHeight = 9.0;

    public const double PortWidth = 13.5;
    public const double PortHeight = 5.0;
    public const double PortPinX = 13.0;
    public const double PortPinY = 2.5;

    private const double MosfetGateX = 0.5;
    private const double MosfetGateY = 12.5;
    private const double MosfetDrainX = 16.5;
    private const double MosfetDrainY = 0.5;
    private const double MosfetSourceY = 25.5;

    public sealed record MosfetPlacement(
        double X,
        double Y,
        int GateX,
        int GateY,
        int DrainX,
        int DrainY,
        int SourceX,
        int SourceY,
        int AxisX
    );

    public sealed record PassivePlacement(double X, double Y, int PX, int PY, int NX, int NY);

    public static double GetCellCenterX(int col)
    {
        return col * CellWidth + CellWidth / 2.0;
    }

    public static double GetCellCenterY(int row)
    {
        return row * CellHeight + RailMargin + CellHeight / 2.0;
    }

    public static int SnapToRoutingGrid(double value)
    {
        return (int)Math.Round(value / RoutingPitch, MidpointRounding.AwayFromZero) * RoutingPitch;
    }

    public static int RoundToInt(double value)
    {
        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    public static MosfetPlacement GetMosfetPlacement(int row, int col, bool mirrorX)
    {
        var baseX = GetCellCenterX(col);
        var baseY = GetCellCenterY(row);

        var axisX = SnapToRoutingGrid(baseX + MosfetWidth / 2.0);
        var drainRelX = mirrorX ? MosfetWidth - MosfetDrainX : MosfetDrainX;
        var gateRelX = mirrorX ? MosfetWidth - MosfetGateX : MosfetGateX;

        var topLeftX = axisX - drainRelX;

        var drainY = RoundToInt(baseY - (MosfetHeight / 2.0 - MosfetDrainY));
        var topLeftY = drainY - MosfetDrainY;
        var sourceY = drainY + RoundToInt(MosfetSourceY - MosfetDrainY);

        var gateX = RoundToInt(topLeftX + gateRelX);
        var gateY = RoundToInt(topLeftY + MosfetGateY);

        return new MosfetPlacement(
            X: topLeftX,
            Y: topLeftY,
            GateX: gateX,
            GateY: gateY,
            DrainX: axisX,
            DrainY: drainY,
            SourceX: axisX,
            SourceY: sourceY,
            AxisX: axisX
        );
    }

    public static PassivePlacement GetPassivePlacement(int row, int col)
    {
        var baseX = GetCellCenterX(col);
        var baseY = GetCellCenterY(row);
        var axisX = SnapToRoutingGrid(baseX + MosfetWidth / 2.0);

        var topLeftX = axisX - PassiveHeight / 2.0;
        var topLeftY = baseY - PassiveWidth / 2.0;

        var pY = RoundToInt(baseY - PassiveWidth / 2.0);
        var nY = RoundToInt(baseY + PassiveWidth / 2.0);

        return new PassivePlacement(X: topLeftX, Y: topLeftY, PX: axisX, PY: pY, NX: axisX, NY: nY);
    }
}
