namespace Cascode.Render.Placement;

using Cascode.Render.Analysis;

public sealed record GridCell
{
    public GridCell(int row, int column, bool MirrorX = false)
        : this(row, column, rotationQuarterTurns: 0, MirrorX, mirrorY: false) { }

    public GridCell(
        int row,
        int column,
        int rotationQuarterTurns,
        bool mirrorX = false,
        bool mirrorY = false
    )
    {
        Row = row;
        Column = column;
        RotationQuarterTurns = ((rotationQuarterTurns % 4) + 4) % 4;
        MirrorX = mirrorX;
        MirrorY = mirrorY;
    }

    public int Row { get; init; }
    public int Column { get; init; }
    public int RotationQuarterTurns { get; init; }
    public bool MirrorX { get; init; }
    public bool MirrorY { get; init; }
}

public sealed class CoarseGridResult
{
    public required int RowCount { get; init; }
    public required int ColumnCount { get; init; }
    public required IReadOnlyDictionary<string, GridCell> DevicePlacements { get; init; }
    public required int SymmetryAxis { get; init; }
    public required IReadOnlySet<string> HorizontalPassiveIds { get; init; }
    public IReadOnlyDictionary<string, int> PortYHints { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);
}

public static class CoarseGridPlacer
{
    public static CoarseGridResult Place(
        TopologyResult topology,
        CircuitGraph graph,
        PlacementConstraintSet? constraints = null
    ) => CoarseGridPlacementSolver.Solve(topology, graph, constraints);
}
