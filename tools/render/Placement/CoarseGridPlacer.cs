namespace Cascode.Render.Placement;

using Cascode.Render.Analysis;
using Cascode.Render.OrTools;
using Google.OrTools.Sat;

/// <summary>
/// A cell in the coarse placement grid.
/// </summary>
public sealed record GridCell(int Row, int Column, bool MirrorX = false);

/// <summary>
/// Complete coarse grid placement result.
/// </summary>
public sealed class CoarseGridResult
{
    public required int RowCount { get; init; }
    public required int ColumnCount { get; init; }
    public required IReadOnlyDictionary<string, GridCell> DevicePlacements { get; init; }
    public required int SymmetryAxis { get; init; }
}

/// <summary>
/// Places devices on a coarse grid using topology analysis and SAT constraints.
/// Row assignment is deterministic from topology; column assignment uses SAT optimization.
/// </summary>
public static class CoarseGridPlacer
{
    private const double MaxSolveTimeSeconds = 2.0;

    /// <summary>
    /// Places devices on a coarse grid based on topology analysis.
    /// </summary>
    public static CoarseGridResult Place(TopologyResult topology, CircuitGraph graph)
    {
        if (topology.DeviceRows.Count == 0)
        {
            return new CoarseGridResult
            {
                RowCount = 1,
                ColumnCount = 1,
                DevicePlacements = new Dictionary<string, GridCell>(),
                SymmetryAxis = 0,
            };
        }

        var deviceIds = topology.DeviceRows.Keys.ToList();
        var symmetricGroups = topology.SymmetricGroups;
        var floatingPassives = topology.FloatingPassives;

        var maxSymmetricGroupSize =
            symmetricGroups.Count > 0 ? symmetricGroups.Max(g => g.DeviceIds.Count) : 0;
        var estimatedColumns = Math.Max(3, maxSymmetricGroupSize * 2 + 1);
        var symmetryAxis = estimatedColumns / 2;

        var model = new CpModel();

        var deviceColumn = new Dictionary<string, IntVar>();
        foreach (var deviceId in deviceIds)
        {
            deviceColumn[deviceId] = model.NewIntVar(0, estimatedColumns - 1, $"col_{deviceId}");
        }

        AddNoOverlapConstraints(model, deviceColumn, topology.DeviceRows);
        AddSymmetryConstraints(model, deviceColumn, symmetricGroups, symmetryAxis);

        var objectives = new List<LinearExpr>();
        AddWireLengthObjective(model, deviceColumn, topology.DeviceRows, graph, objectives);
        AddCompactnessObjective(model, deviceColumn, objectives);

        if (objectives.Count > 0)
        {
            model.Minimize(LinearExpr.Sum(objectives));
        }

        var solver = new CpSolver();
        solver.StringParameters = OrToolsSolverDefaults.BuildSolverParameters(MaxSolveTimeSeconds);
        var status = solver.Solve(model);

        if (status != CpSolverStatus.Optimal && status != CpSolverStatus.Feasible)
        {
            return FallbackPlacement(topology, estimatedColumns, symmetryAxis);
        }

        var placements = ExtractPlacements(solver, deviceColumn, topology, symmetryAxis, graph);
        var actualColumnCount = placements.Values.Max(c => c.Column) + 1;

        return new CoarseGridResult
        {
            RowCount = topology.RowCount,
            ColumnCount = Math.Max(actualColumnCount, estimatedColumns),
            DevicePlacements = placements,
            SymmetryAxis = symmetryAxis,
        };
    }

    /// <summary>
    /// Ensures no two devices occupy the same cell.
    /// </summary>
    private static void AddNoOverlapConstraints(
        CpModel model,
        Dictionary<string, IntVar> deviceColumn,
        IReadOnlyDictionary<string, int> deviceRows
    )
    {
        var byRow = new Dictionary<int, List<string>>();
        foreach (var (deviceId, row) in deviceRows)
        {
            if (!byRow.TryGetValue(row, out var list))
            {
                list = new List<string>();
                byRow[row] = list;
            }
            list.Add(deviceId);
        }

        foreach (var (_, devicesInRow) in byRow)
        {
            for (var i = 0; i < devicesInRow.Count; i++)
            {
                for (var j = i + 1; j < devicesInRow.Count; j++)
                {
                    var d1 = devicesInRow[i];
                    var d2 = devicesInRow[j];
                    model.Add(deviceColumn[d1] != deviceColumn[d2]);
                }
            }
        }
    }

    /// <summary>
    /// Constrains symmetric groups to be placed symmetrically about the axis.
    /// </summary>
    private static void AddSymmetryConstraints(
        CpModel model,
        Dictionary<string, IntVar> deviceColumn,
        IReadOnlyList<SymmetricGroup> groups,
        int symmetryAxis
    )
    {
        foreach (var group in groups)
        {
            var deviceIds = group.DeviceIds.Where(id => deviceColumn.ContainsKey(id)).ToList();

            if (deviceIds.Count == 2)
            {
                var d1 = deviceIds[0];
                var d2 = deviceIds[1];

                var dist1 = model.NewIntVar(-100, 100, $"dist1_{d1}");
                var dist2 = model.NewIntVar(-100, 100, $"dist2_{d2}");

                model.Add(dist1 == deviceColumn[d1] - symmetryAxis);
                model.Add(dist2 == deviceColumn[d2] - symmetryAxis);

                model.Add(dist1 + dist2 == 0);
            }
            else if (deviceIds.Count > 2)
            {
                var sorted = deviceIds.ToList();

                for (var i = 0; i < sorted.Count / 2; i++)
                {
                    var left = sorted[i];
                    var right = sorted[sorted.Count - 1 - i];

                    var distLeft = model.NewIntVar(-100, 100, $"distL_{left}");
                    var distRight = model.NewIntVar(-100, 100, $"distR_{right}");

                    model.Add(distLeft == deviceColumn[left] - symmetryAxis);
                    model.Add(distRight == deviceColumn[right] - symmetryAxis);
                    model.Add(distLeft + distRight == 0);
                }

                if (sorted.Count % 2 == 1)
                {
                    var middle = sorted[sorted.Count / 2];
                    model.Add(deviceColumn[middle] == symmetryAxis);
                }
            }
        }
    }

    /// <summary>
    /// Minimizes wire length by keeping connected devices in nearby columns.
    /// </summary>
    private static void AddWireLengthObjective(
        CpModel model,
        Dictionary<string, IntVar> deviceColumn,
        IReadOnlyDictionary<string, int> deviceRows,
        CircuitGraph graph,
        List<LinearExpr> objectives
    )
    {
        foreach (var (netName, connections) in graph.NetConnections)
        {
            if (graph.IsSupplyOrGround(netName))
            {
                continue;
            }

            var deviceIds = connections
                .Select(c => c.DeviceId)
                .Where(id => deviceColumn.ContainsKey(id))
                .Distinct()
                .ToList();

            for (var i = 0; i < deviceIds.Count; i++)
            {
                for (var j = i + 1; j < deviceIds.Count; j++)
                {
                    var d1 = deviceIds[i];
                    var d2 = deviceIds[j];

                    var colDiff = model.NewIntVar(0, 100, $"coldiff_{netName}_{i}_{j}");
                    model.AddAbsEquality(colDiff, deviceColumn[d1] - deviceColumn[d2]);

                    var row1 = deviceRows.GetValueOrDefault(d1, 0);
                    var row2 = deviceRows.GetValueOrDefault(d2, 0);
                    var rowDiff = Math.Abs(row1 - row2);

                    objectives.Add(colDiff + rowDiff);
                }
            }
        }
    }

    /// <summary>
    /// Encourages compact layouts by minimizing column span.
    /// </summary>
    private static void AddCompactnessObjective(
        CpModel model,
        Dictionary<string, IntVar> deviceColumn,
        List<LinearExpr> objectives
    )
    {
        foreach (var (_, col) in deviceColumn)
        {
            objectives.Add(col);
        }
    }

    /// <summary>
    /// Fallback placement when SAT solver fails.
    /// </summary>
    private static CoarseGridResult FallbackPlacement(
        TopologyResult topology,
        int columnCount,
        int symmetryAxis
    )
    {
        var placements = new Dictionary<string, GridCell>();
        var byRow = new Dictionary<int, List<string>>();

        foreach (var (deviceId, row) in topology.DeviceRows)
        {
            if (!byRow.TryGetValue(row, out var list))
            {
                list = new List<string>();
                byRow[row] = list;
            }
            list.Add(deviceId);
        }

        foreach (var (row, devices) in byRow)
        {
            var startCol = Math.Max(0, symmetryAxis - devices.Count / 2);
            for (var i = 0; i < devices.Count; i++)
            {
                var col = startCol + i;
                placements[devices[i]] = new GridCell(row, col);
            }
        }

        return new CoarseGridResult
        {
            RowCount = topology.RowCount,
            ColumnCount = columnCount,
            DevicePlacements = placements,
            SymmetryAxis = symmetryAxis,
        };
    }

    /// <summary>
    /// Extracts placements from the solved model.
    /// For input differential pairs, gates face outward (away from axis).
    /// For other symmetric groups (current mirrors, load pairs), gates face inward (toward axis).
    /// </summary>
    private static Dictionary<string, GridCell> ExtractPlacements(
        CpSolver solver,
        Dictionary<string, IntVar> deviceColumn,
        TopologyResult topology,
        int symmetryAxis,
        CircuitGraph graph
    )
    {
        var placements = new Dictionary<string, GridCell>();

        // Build a set of devices that are part of input differential pairs
        var inputDiffPairDevices = new HashSet<string>();
        var otherSymmetricDevices = new HashSet<string>();

        foreach (var group in topology.SymmetricGroups)
        {
            if (group.Type == SymmetryType.DiffPair)
            {
                // Check if this diff pair has gates connected to input ports
                var hasInputGate = group.DeviceIds.Any(deviceId =>
                {
                    var gateNet = graph.GetNetForTerminal(deviceId, "G");
                    return gateNet != null && graph.InputPorts.Contains(gateNet);
                });

                if (hasInputGate)
                {
                    foreach (var deviceId in group.DeviceIds)
                    {
                        inputDiffPairDevices.Add(deviceId);
                    }
                }
                else
                {
                    foreach (var deviceId in group.DeviceIds)
                    {
                        otherSymmetricDevices.Add(deviceId);
                    }
                }
            }
            else
            {
                // Current mirrors and load pairs: gates face inward
                foreach (var deviceId in group.DeviceIds)
                {
                    otherSymmetricDevices.Add(deviceId);
                }
            }
        }

        foreach (var (deviceId, colVar) in deviceColumn)
        {
            var col = (int)solver.Value(colVar);
            var row = topology.DeviceRows.GetValueOrDefault(deviceId, 0);

            bool mirrorX;
            if (inputDiffPairDevices.Contains(deviceId))
            {
                // Input diff pair: gates face outward (away from axis)
                // Left of axis: gate left (mirrorX=false), right of axis: gate right (mirrorX=true)
                mirrorX = col > symmetryAxis;
            }
            else if (otherSymmetricDevices.Contains(deviceId))
            {
                // Other symmetric groups: gates face inward (toward axis)
                // Left of axis: gate right (mirrorX=true), right of axis: gate left (mirrorX=false)
                mirrorX = col < symmetryAxis;
            }
            else
            {
                // Non-symmetric devices: default to gates facing left
                mirrorX = false;
            }

            placements[deviceId] = new GridCell(row, col, mirrorX);
        }

        return placements;
    }
}
