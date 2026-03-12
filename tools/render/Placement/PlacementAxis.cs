namespace Cascode.Render.Placement;

internal static class PlacementAxis
{
    public static double GetAxisPosition(int columnCount)
    {
        return columnCount <= 1 ? 0 : (columnCount - 1) / 2.0;
    }

    public static bool IsLeftOfAxis(CoarseGridResult placement, int column)
    {
        return column < GetAxisPosition(placement.ColumnCount);
    }

    public static bool IsRightOfAxis(CoarseGridResult placement, int column)
    {
        return column > GetAxisPosition(placement.ColumnCount);
    }
}
