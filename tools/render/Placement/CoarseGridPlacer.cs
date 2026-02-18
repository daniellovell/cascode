namespace Cascode.Render.Placement;

using Cascode.Render.Analysis;
using Cascode.Render.Layout;
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

    /// <summary>
    /// Device IDs that are placed as horizontal passives (interior fill columns).
    /// </summary>
    public required IReadOnlySet<string> HorizontalPassiveIds { get; init; }

    /// <summary>
    /// Optional SAT-derived Y positions for signal ports.
    /// Empty when the solver does not provide hints.
    /// </summary>
    public IReadOnlyDictionary<string, int> PortYHints { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);
}

/// <summary>
/// Places devices on a coarse grid using topology analysis and SAT constraints.
/// Row assignment uses topology with SAT optimization for horizontal passives.
/// Column assignment uses SAT optimization with terminal-aware wire length.
/// </summary>
public static class CoarseGridPlacer
{
    private const double MaxSolveTimeSeconds = 2.0;
    private const int StraightLaneWeight = 3;
    private const int HorizontalRowAnchorWeight = 25;
    private const int VerticalRowAnchorWeight = 2;
    private const int RailTerminalWeight = 2;

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
                HorizontalPassiveIds = new HashSet<string>(),
            };
        }

        // Identify horizontal passives for special handling
        var horizontalPassiveIds = new HashSet<string>(
            topology
                .PassiveOrientations.Where(kv => kv.Value == PassiveOrientation.Horizontal)
                .Select(kv => kv.Key)
        );
        var railConnectedVerticalPassiveIds = new HashSet<string>(
            topology
                .PassiveOrientations.Where(kv => kv.Value == PassiveOrientation.Vertical)
                .Select(kv => kv.Key)
                .Where(deviceId => IsPassiveConnectedToRail(graph, deviceId))
        );

        // Detect symmetric passive pairs (e.g., CMFB resistors)
        var symmetricPassivePairs = TopologyAnalyzer.DetectSymmetricPassivePairs(graph, topology);

        var deviceIds = topology.DeviceRows.Keys.ToList();
        var symmetricGroups = topology.SymmetricGroups;

        var maxSymmetricGroupSize =
            symmetricGroups.Count > 0 ? symmetricGroups.Max(g => g.DeviceIds.Count) : 0;

        // Calculate columns: a 5-column layout (0,1,2,3,4 with axis at 2) naturally has
        // edge columns (0,4) for MOSFETs and fill columns (1,3) for horizontal passives.
        // No extra slack needed - the interior columns exist in the standard layout.
        var estimatedColumns = Math.Max(3, maxSymmetricGroupSize * 2 + 1);
        var symmetryAxis = estimatedColumns / 2;

        // Compute row offsets for fill rows
        // Find where horizontal passives need to be placed (between which topology rows)
        var fillRowsAfterTopoRow = ComputeFillRowPositions(horizontalPassiveIds, topology, graph);

        // Calculate cumulative fill row offset for each topology row
        var maxTopoRow = topology.DeviceRows.Values.DefaultIfEmpty(0).Max();
        var fillRowOffset = new int[maxTopoRow + 2];
        var cumulativeOffset = 0;
        for (var r = 0; r <= maxTopoRow; r++)
        {
            fillRowOffset[r] = cumulativeOffset;
            if (fillRowsAfterTopoRow.Contains(r))
            {
                cumulativeOffset++;
            }
        }
        fillRowOffset[maxTopoRow + 1] = cumulativeOffset;

        var totalRows =
            topology.RowCount
            + cumulativeOffset
            + (railConnectedVerticalPassiveIds.Count > 0 ? 2 : 0);
        var canvasHeight = totalRows * DeviceGeometry.CellHeight + 2 * DeviceGeometry.RailMargin;
        var signalPorts = GetSignalPorts(graph);

        var model = new CpModel();

        // Create column variables for all devices
        var deviceColumn = new Dictionary<string, IntVar>();
        foreach (var deviceId in deviceIds)
        {
            deviceColumn[deviceId] = model.NewIntVar(0, estimatedColumns - 1, $"col_{deviceId}");
        }

        // Create row variables
        // Vertical-path devices get offset rows; horizontal passives are optimized
        var deviceRow = new Dictionary<string, IntVar>();
        var rowAnchorTargets = new Dictionary<string, (int TargetRow, int Weight)>(
            StringComparer.Ordinal
        );
        foreach (var deviceId in deviceIds)
        {
            var topoRow = topology.DeviceRows.GetValueOrDefault(deviceId, 0);
            var offsetRow = topoRow + fillRowOffset[topoRow];
            if (horizontalPassiveIds.Contains(deviceId))
            {
                // Horizontal passive rows are SAT variables - can be any row
                deviceRow[deviceId] = model.NewIntVar(0, totalRows - 1, $"row_{deviceId}");
                rowAnchorTargets[deviceId] = (offsetRow, HorizontalRowAnchorWeight);
            }
            else if (railConnectedVerticalPassiveIds.Contains(deviceId))
            {
                deviceRow[deviceId] = model.NewIntVar(0, totalRows - 1, $"row_{deviceId}");
                rowAnchorTargets[deviceId] = (offsetRow, VerticalRowAnchorWeight);
            }
            else
            {
                // Vertical-path devices get row = topoRow + offset for fill rows before it
                deviceRow[deviceId] = model.NewConstant(offsetRow);
            }
        }

        var portYVariables = CreatePortYVariables(model, signalPorts, canvasHeight);
        AddRailSideOrderingConstraints(model, deviceRow, railConnectedVerticalPassiveIds, graph);

        AddNoOverlapConstraints(model, deviceColumn, deviceRow, deviceIds);
        AddSymmetryConstraints(model, deviceColumn, symmetricGroups, symmetryAxis, graph);
        AddHorizontalPassiveSymmetryConstraints(
            model,
            deviceColumn,
            deviceRow,
            symmetricPassivePairs,
            symmetryAxis,
            fillRowsAfterTopoRow,
            fillRowOffset,
            graph,
            topology,
            horizontalPassiveIds
        );

        // Constrain center devices (not in any symmetric group) to the symmetry axis
        AddCenterDeviceConstraints(
            model,
            deviceColumn,
            deviceIds,
            symmetricGroups,
            horizontalPassiveIds,
            symmetryAxis
        );

        // Add constraints for column placement based on device type
        // These ensure MOSFETs go to edge columns and passives go to interior columns
        if (horizontalPassiveIds.Count > 0)
        {
            // Push horizontal passives to fill columns (distance 1 from axis)
            AddHorizontalPassiveColumnConstraints(
                model,
                deviceColumn,
                horizontalPassiveIds,
                symmetryAxis
            );

            // Push symmetric MOSFETs to edge columns (distance 2 from axis)
            AddMosfetEdgeColumnConstraints(
                model,
                deviceColumn,
                symmetricGroups,
                horizontalPassiveIds,
                symmetryAxis
            );
        }

        var objectives = new List<LinearExpr>();
        AddTerminalAwareWireLengthObjective(
            model,
            deviceColumn,
            deviceRow,
            graph,
            horizontalPassiveIds,
            objectives
        );
        AddPortStraightnessObjectives(
            model,
            deviceRow,
            graph,
            horizontalPassiveIds,
            portYVariables,
            canvasHeight,
            objectives
        );
        AddRowAnchorObjectives(model, deviceRow, rowAnchorTargets, objectives);
        AddRailProximityObjectives(
            model,
            deviceRow,
            railConnectedVerticalPassiveIds,
            graph,
            horizontalPassiveIds,
            canvasHeight,
            objectives
        );
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
            return FallbackPlacement(
                topology,
                estimatedColumns,
                symmetryAxis,
                horizontalPassiveIds
            );
        }

        var placements = ExtractPlacements(
            solver,
            deviceColumn,
            deviceRow,
            topology,
            symmetryAxis,
            graph,
            horizontalPassiveIds
        );
        var portYHints = ExtractPortYHints(solver, portYVariables, graph, deviceRow);
        var actualColumnCount = placements.Count > 0 ? placements.Values.Max(c => c.Column) + 1 : 1;
        var actualRowCount = placements.Count > 0 ? placements.Values.Max(c => c.Row) + 1 : 1;

        return new CoarseGridResult
        {
            RowCount = Math.Max(actualRowCount, totalRows),
            ColumnCount = Math.Max(actualColumnCount, estimatedColumns),
            DevicePlacements = placements,
            SymmetryAxis = symmetryAxis,
            HorizontalPassiveIds = horizontalPassiveIds,
            PortYHints = portYHints,
        };
    }

    /// <summary>
    /// Ensures no two devices occupy the same cell.
    /// Uses reified constraints when rows are variables.
    /// </summary>
    private static void AddNoOverlapConstraints(
        CpModel model,
        Dictionary<string, IntVar> deviceColumn,
        Dictionary<string, IntVar> deviceRow,
        List<string> deviceIds
    )
    {
        for (var i = 0; i < deviceIds.Count; i++)
        {
            for (var j = i + 1; j < deviceIds.Count; j++)
            {
                var d1 = deviceIds[i];
                var d2 = deviceIds[j];

                // If both devices are in the same row, they must be in different columns
                // Using reified constraint: (row1 == row2) => (col1 != col2)
                var sameRow = model.NewBoolVar($"sameRow_{d1}_{d2}");
                model.Add(deviceRow[d1] == deviceRow[d2]).OnlyEnforceIf(sameRow);
                model.Add(deviceRow[d1] != deviceRow[d2]).OnlyEnforceIf(sameRow.Not());

                // If same row, columns must differ
                model.Add(deviceColumn[d1] != deviceColumn[d2]).OnlyEnforceIf(sameRow);
            }
        }
    }

    /// <summary>
    /// Constrains symmetric groups to be placed symmetrically about the axis.
    /// For diff pairs, uses input port connections (IN_P on left, IN_N on right).
    /// For other groups, uses naming convention.
    /// </summary>
    private static void AddSymmetryConstraints(
        CpModel model,
        Dictionary<string, IntVar> deviceColumn,
        IReadOnlyList<SymmetricGroup> groups,
        int symmetryAxis,
        CircuitGraph graph
    )
    {
        foreach (var group in groups)
        {
            var deviceIds = group.DeviceIds.Where(id => deviceColumn.ContainsKey(id)).ToList();

            if (deviceIds.Count == 2)
            {
                // Determine left/right - for diff pairs, use input port; otherwise naming
                var (left, right) =
                    group.Type == SymmetryType.DiffPair
                        ? DetermineLeftRightByInputPort(deviceIds[0], deviceIds[1], graph)
                        : DetermineLeftRightByNaming(deviceIds[0], deviceIds[1]);

                // Left device should be at col < axis, right at col > axis
                model.Add(deviceColumn[left] < symmetryAxis);
                model.Add(deviceColumn[right] > symmetryAxis);

                // Symmetric about axis: col[left] + col[right] = 2 * axis
                model.Add(deviceColumn[left] + deviceColumn[right] == 2 * symmetryAxis);
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
    /// Determines left/right assignment for diff pairs based on which input port the gate connects to.
    /// The device with gate connected to an input ending in _P (positive input) goes on the left.
    /// The device with gate connected to an input ending in _N (negative input) goes on the right.
    /// </summary>
    private static (string Left, string Right) DetermineLeftRightByInputPort(
        string d1,
        string d2,
        CircuitGraph graph
    )
    {
        var d1GateNet = graph.GetNetForTerminal(d1, "G");
        var d2GateNet = graph.GetNetForTerminal(d2, "G");

        // Check if either gate connects to a "positive" input port
        var d1HasPositiveInput =
            d1GateNet != null
            && graph.InputPorts.Contains(d1GateNet)
            && IsPositiveInputNaming(d1GateNet);

        var d2HasPositiveInput =
            d2GateNet != null
            && graph.InputPorts.Contains(d2GateNet)
            && IsPositiveInputNaming(d2GateNet);

        if (d1HasPositiveInput && !d2HasPositiveInput)
        {
            return (d1, d2); // d1 has positive input, goes on left
        }

        if (d2HasPositiveInput && !d1HasPositiveInput)
        {
            return (d2, d1); // d2 has positive input, goes on left
        }

        // Fallback to naming convention
        return DetermineLeftRightByNaming(d1, d2);
    }

    /// <summary>
    /// Checks if an input port name indicates the positive input.
    /// </summary>
    private static bool IsPositiveInputNaming(string name)
    {
        return name.EndsWith("_P", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("P", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("+", StringComparison.Ordinal)
            || name.EndsWith("_PLUS", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines left/right assignment based on device naming convention.
    /// Devices with _P, P, or + suffix go on the left (negative column from axis).
    /// Devices with _N, N, or - suffix go on the right (positive column from axis).
    /// </summary>
    private static (string Left, string Right) DetermineLeftRightByNaming(string d1, string d2)
    {
        // Check for P/N suffix patterns
        var d1IsLeft = IsLeftSideNaming(d1);
        var d2IsLeft = IsLeftSideNaming(d2);

        if (d1IsLeft && !d2IsLeft)
        {
            return (d1, d2);
        }

        if (d2IsLeft && !d1IsLeft)
        {
            return (d2, d1);
        }

        // If naming doesn't determine it, use alphabetical order
        return string.Compare(d1, d2, StringComparison.Ordinal) < 0 ? (d1, d2) : (d2, d1);
    }

    /// <summary>
    /// Checks if a device name indicates it should be on the left side.
    /// </summary>
    private static bool IsLeftSideNaming(string name)
    {
        return name.EndsWith("_P", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".P", StringComparison.OrdinalIgnoreCase)
            || (
                name.EndsWith("P", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith("TAP", StringComparison.OrdinalIgnoreCase)
            )
            || name.EndsWith("+", StringComparison.Ordinal)
            || name.Contains("_P.", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Constrains symmetric pairs of horizontal passives to be placed symmetrically.
    /// Both column and row symmetry are enforced. Also enforces fill row placement.
    /// </summary>
    private static void AddHorizontalPassiveSymmetryConstraints(
        CpModel model,
        Dictionary<string, IntVar> deviceColumn,
        Dictionary<string, IntVar> deviceRow,
        IReadOnlyList<(string Left, string Right, string PivotNet)> passivePairs,
        int symmetryAxis,
        IReadOnlySet<int> fillRowsAfterTopoRow,
        int[] fillRowOffset,
        CircuitGraph graph,
        TopologyResult topology,
        IReadOnlySet<string> horizontalPassiveIds
    )
    {
        foreach (var (left, right, _) in passivePairs)
        {
            if (!deviceColumn.ContainsKey(left) || !deviceColumn.ContainsKey(right))
            {
                continue;
            }

            // Left device at col < axis, right at col > axis
            model.Add(deviceColumn[left] < symmetryAxis);
            model.Add(deviceColumn[right] > symmetryAxis);

            // Column symmetry: col[left] + col[right] = 2 * symmetryAxis
            model.Add(deviceColumn[left] + deviceColumn[right] == 2 * symmetryAxis);

            // Row symmetry: both in the same row
            model.Add(deviceRow[left] == deviceRow[right]);

            // Force to valid fill rows based on connected device rows
            var validFillRows = ComputeValidFillRowsForPassive(
                left,
                fillRowsAfterTopoRow,
                fillRowOffset,
                graph,
                topology
            );

            if (validFillRows.Count == 1)
            {
                // Single valid fill row - constrain directly
                model.Add(deviceRow[left] == validFillRows[0]);
            }
            else if (validFillRows.Count > 1)
            {
                // Multiple valid fill rows - create disjunction of equality constraints
                var rowOptions = new List<ILiteral>();
                foreach (var row in validFillRows)
                {
                    var isThisRow = model.NewBoolVar($"row_{left}_is_{row}");
                    model.Add(deviceRow[left] == row).OnlyEnforceIf(isThisRow);
                    rowOptions.Add(isThisRow);
                }
                // Exactly one must be true
                model.AddExactlyOne(rowOptions);
            }
        }
    }

    /// <summary>
    /// Computes valid fill row positions for a horizontal passive based on its connections.
    /// </summary>
    private static List<int> ComputeValidFillRowsForPassive(
        string passiveId,
        IReadOnlySet<int> fillRowsAfterTopoRow,
        int[] fillRowOffset,
        CircuitGraph graph,
        TopologyResult topology
    )
    {
        var validRows = new List<int>();
        var connectedTopoRows = GetConnectedDeviceRows(passiveId, graph, topology);

        if (connectedTopoRows.Count >= 2)
        {
            // Place in fill row after minimum connected topology row
            var minTopoRow = connectedTopoRows.Min();
            if (fillRowsAfterTopoRow.Contains(minTopoRow) && minTopoRow < fillRowOffset.Length - 1)
            {
                // Fill row actual position = topoRow + offset + 1
                var fillRowActual = minTopoRow + fillRowOffset[minTopoRow] + 1;
                validRows.Add(fillRowActual);
            }
        }

        // Fallback: allow any fill row position
        if (validRows.Count == 0)
        {
            foreach (var topoRow in fillRowsAfterTopoRow)
            {
                if (topoRow < fillRowOffset.Length - 1)
                {
                    var fillRowActual = topoRow + fillRowOffset[topoRow] + 1;
                    validRows.Add(fillRowActual);
                }
            }
        }

        return validRows.Distinct().ToList();
    }

    /// <summary>
    /// Constrains horizontal passives to be in interior fill columns, not edge columns.
    /// This ensures symmetric pairs end up in columns like 1 and 3 (for axis 2) rather than 0 and 4.
    /// </summary>
    private static void AddHorizontalPassiveColumnConstraints(
        CpModel model,
        Dictionary<string, IntVar> deviceColumn,
        IReadOnlySet<string> horizontalPassiveIds,
        int symmetryAxis
    )
    {
        foreach (var deviceId in horizontalPassiveIds)
        {
            if (!deviceColumn.TryGetValue(deviceId, out var colVar))
            {
                continue;
            }

            // For a typical 5-column layout with axis=2:
            // Columns: 0 (edge), 1 (fill), 2 (axis), 3 (fill), 4 (edge)
            // We want passives at columns 1 and 3, not 0, 2, or 4

            // Constraint: column must be exactly at distance 1 from axis (fill columns)
            // This means column = axis - 1 or column = axis + 1
            // For symmetric pairs, the symmetry constraint col_L + col_R = 2*axis
            // means if one is at axis-1, the other is at axis+1

            // Force passives to fill columns: distance from axis must be 1
            var distFromAxis = model.NewIntVar(0, symmetryAxis, $"dist_{deviceId}");
            model.AddAbsEquality(distFromAxis, colVar - symmetryAxis);
            model.Add(distFromAxis == 1); // Exactly 1 column away from axis
        }
    }

    /// <summary>
    /// Constrains symmetric MOSFET groups to edge columns when horizontal passives are present.
    /// This ensures loads and diff pairs are at distance 2 from axis (columns 0 and 4 for axis=2).
    /// </summary>
    private static void AddMosfetEdgeColumnConstraints(
        CpModel model,
        Dictionary<string, IntVar> deviceColumn,
        IReadOnlyList<SymmetricGroup> symmetricGroups,
        IReadOnlySet<string> horizontalPassiveIds,
        int symmetryAxis
    )
    {
        foreach (var group in symmetricGroups)
        {
            // Only constrain MOSFET groups, not passive groups
            var mosfetDevices = group
                .DeviceIds.Where(id =>
                    deviceColumn.ContainsKey(id) && !horizontalPassiveIds.Contains(id)
                )
                .ToList();

            if (mosfetDevices.Count < 2)
            {
                continue;
            }

            // For a 5-column layout with axis=2:
            // Edge columns are 0 and 4 (distance 2 from axis)
            // Force symmetric MOSFET pairs to edge columns
            foreach (var deviceId in mosfetDevices)
            {
                if (!deviceColumn.TryGetValue(deviceId, out var colVar))
                {
                    continue;
                }

                var distFromAxis = model.NewIntVar(0, symmetryAxis, $"mosdist_{deviceId}");
                model.AddAbsEquality(distFromAxis, colVar - symmetryAxis);
                model.Add(distFromAxis == symmetryAxis); // Distance equals symmetryAxis (edge columns)
            }
        }
    }

    /// <summary>
    /// Constrains devices not in any symmetric group to be placed on the symmetry axis.
    /// These "center" devices (like tail transistors) should be centered in the layout.
    /// </summary>
    private static void AddCenterDeviceConstraints(
        CpModel model,
        Dictionary<string, IntVar> deviceColumn,
        List<string> deviceIds,
        IReadOnlyList<SymmetricGroup> symmetricGroups,
        IReadOnlySet<string> horizontalPassiveIds,
        int symmetryAxis
    )
    {
        // Collect all devices that are in symmetric groups
        var devicesInSymmetricGroups = new HashSet<string>();
        foreach (var group in symmetricGroups)
        {
            foreach (var deviceId in group.DeviceIds)
            {
                devicesInSymmetricGroups.Add(deviceId);
            }
        }

        // Constrain center devices to the symmetry axis
        foreach (var deviceId in deviceIds)
        {
            if (devicesInSymmetricGroups.Contains(deviceId))
            {
                continue;
            }

            if (horizontalPassiveIds.Contains(deviceId))
            {
                continue;
            }

            if (!deviceColumn.TryGetValue(deviceId, out var colVar))
            {
                continue;
            }

            // Center device must be on the symmetry axis
            model.Add(colVar == symmetryAxis);
        }
    }

    /// <summary>
    /// Minimizes terminal-aware wire length.
    /// For horizontal passives, accounts for terminal positions (P toward outer, N toward center).
    /// This causes the solver to naturally place horizontal passives in interior columns
    /// where their N terminals are closer to each other.
    /// </summary>
    private static void AddTerminalAwareWireLengthObjective(
        CpModel model,
        Dictionary<string, IntVar> deviceColumn,
        Dictionary<string, IntVar> deviceRow,
        CircuitGraph graph,
        IReadOnlySet<string> horizontalPassiveIds,
        List<LinearExpr> objectives
    )
    {
        foreach (var (netName, connections) in graph.NetConnections)
        {
            if (graph.IsSupplyOrGround(netName))
            {
                continue;
            }

            // Get unique terminal connections on this net
            var terminalConnections = connections
                .Where(c => deviceColumn.ContainsKey(c.DeviceId))
                .ToList();

            for (var i = 0; i < terminalConnections.Count; i++)
            {
                for (var j = i + 1; j < terminalConnections.Count; j++)
                {
                    var conn1 = terminalConnections[i];
                    var conn2 = terminalConnections[j];

                    var device1 = graph.Devices.GetValueOrDefault(conn1.DeviceId);
                    var device2 = graph.Devices.GetValueOrDefault(conn2.DeviceId);
                    if (device1 == null || device2 == null)
                    {
                        continue;
                    }

                    var offset1 = GetTerminalOffsetInCells(
                        device1.DeviceType,
                        conn1.Terminal,
                        conn1.DeviceId,
                        horizontalPassiveIds
                    );
                    var offset2 = GetTerminalOffsetInCells(
                        device2.DeviceType,
                        conn2.Terminal,
                        conn2.DeviceId,
                        horizontalPassiveIds
                    );

                    var colOffsetPixels = (int)
                        Math.Round(
                            (offset1.DeltaCol - offset2.DeltaCol) * DeviceGeometry.CellWidth,
                            MidpointRounding.AwayFromZero
                        );
                    var rowOffsetCells = (int)
                        Math.Round(
                            offset1.DeltaRow - offset2.DeltaRow,
                            MidpointRounding.AwayFromZero
                        );

                    var colDiffPixels = model.NewIntVar(0, 5000, $"coldiff_{netName}_{i}_{j}");
                    model.AddAbsEquality(
                        colDiffPixels,
                        deviceColumn[conn1.DeviceId] * DeviceGeometry.CellWidth
                            - deviceColumn[conn2.DeviceId] * DeviceGeometry.CellWidth
                            + colOffsetPixels
                    );

                    var rowDiffCells = model.NewIntVar(0, 200, $"rowdiff_{netName}_{i}_{j}");
                    model.AddAbsEquality(
                        rowDiffCells,
                        deviceRow[conn1.DeviceId] - deviceRow[conn2.DeviceId] + rowOffsetCells
                    );

                    objectives.Add(colDiffPixels + rowDiffCells * DeviceGeometry.RoutingPitch);
                }
            }
        }
    }

    /// <summary>
    /// Gets terminal offsets in cell units using the shared geometry model.
    /// </summary>
    private static (double DeltaCol, double DeltaRow) GetTerminalOffsetInCells(
        string deviceType,
        string terminal,
        string deviceId,
        IReadOnlySet<string> horizontalPassiveIds
    )
    {
        var isHorizontalPassive = horizontalPassiveIds.Contains(deviceId);
        var isLeftOfAxis = !IsRightNamedDevice(deviceId);
        return DeviceGeometry.GetTerminalOffset(
            deviceType,
            terminal,
            mirrorX: false,
            isHorizontalPassive,
            isLeftOfAxis
        );
    }

    private static bool IsRightNamedDevice(string deviceId)
    {
        return deviceId.EndsWith("R", StringComparison.OrdinalIgnoreCase)
            || deviceId.Contains("_R", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> GetSignalPorts(CircuitGraph graph)
    {
        return graph
            .InputPorts.Concat(graph.BiasPorts)
            .Concat(graph.OutputPorts)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static Dictionary<string, IntVar> CreatePortYVariables(
        CpModel model,
        IEnumerable<string> signalPorts,
        int canvasHeight
    )
    {
        var variables = new Dictionary<string, IntVar>(StringComparer.Ordinal);
        foreach (var port in signalPorts)
        {
            variables[port] = model.NewIntVar(0, canvasHeight, $"portY_{ToVarToken(port)}");
        }

        return variables;
    }

    private static void AddPortStraightnessObjectives(
        CpModel model,
        Dictionary<string, IntVar> deviceRow,
        CircuitGraph graph,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlyDictionary<string, IntVar> portYVariables,
        int canvasHeight,
        List<LinearExpr> objectives
    )
    {
        var baseTerminalY = DeviceGeometry.RailMargin + DeviceGeometry.CellHeight / 2;

        foreach (var (portName, portYVar) in portYVariables)
        {
            if (!graph.NetConnections.TryGetValue(portName, out var connections))
            {
                continue;
            }

            var connIndex = 0;
            foreach (var conn in connections)
            {
                if (!deviceRow.ContainsKey(conn.DeviceId))
                {
                    continue;
                }

                if (!graph.Devices.TryGetValue(conn.DeviceId, out var device))
                {
                    continue;
                }

                var offsets = GetTerminalOffsetInCells(
                    device.DeviceType,
                    conn.Terminal,
                    conn.DeviceId,
                    horizontalPassiveIds
                );
                var terminalYOffset = (int)
                    Math.Round(
                        offsets.DeltaRow * DeviceGeometry.CellHeight,
                        MidpointRounding.AwayFromZero
                    );
                var terminalYExpr =
                    deviceRow[conn.DeviceId] * DeviceGeometry.CellHeight
                    + baseTerminalY
                    + terminalYOffset;

                var diffVar = model.NewIntVar(
                    0,
                    canvasHeight,
                    $"portdiff_{ToVarToken(portName)}_{ToVarToken(conn.DeviceId)}_{ToVarToken(conn.Terminal)}_{connIndex}"
                );
                model.AddAbsEquality(diffVar, portYVar - terminalYExpr);
                objectives.Add(diffVar);
                connIndex++;
            }
        }

        foreach (var (leftPort, rightPort) in DetectPassiveFeedthroughPortPairs(graph))
        {
            if (
                !portYVariables.TryGetValue(leftPort, out var leftY)
                || !portYVariables.TryGetValue(rightPort, out var rightY)
            )
            {
                continue;
            }

            var laneDiff = model.NewIntVar(
                0,
                canvasHeight,
                $"lanediff_{ToVarToken(leftPort)}_{ToVarToken(rightPort)}"
            );
            model.AddAbsEquality(laneDiff, leftY - rightY);
            objectives.Add(laneDiff * StraightLaneWeight);
        }
    }

    private static IReadOnlyCollection<(
        string LeftPort,
        string RightPort
    )> DetectPassiveFeedthroughPortPairs(CircuitGraph graph)
    {
        var pairs = new HashSet<(string LeftPort, string RightPort)>();

        foreach (var (_, device) in graph.Devices)
        {
            var type = device.DeviceType.ToLowerInvariant();
            if (type is not ("resistor" or "capacitor"))
            {
                continue;
            }

            if (
                !device.Bindings.TryGetValue("P", out var pNet)
                || !device.Bindings.TryGetValue("N", out var nNet)
            )
            {
                continue;
            }

            TryAddFeedthroughPair(pNet, nNet);
            TryAddFeedthroughPair(nNet, pNet);
        }

        return pairs;

        void TryAddFeedthroughPair(string leftCandidate, string rightCandidate)
        {
            var isLeftPort =
                graph.InputPorts.Contains(leftCandidate) || graph.BiasPorts.Contains(leftCandidate);
            var isRightPort = graph.OutputPorts.Contains(rightCandidate);
            if (isLeftPort && isRightPort)
            {
                pairs.Add((leftCandidate, rightCandidate));
            }
        }
    }

    private static void AddRailSideOrderingConstraints(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> deviceRow,
        IReadOnlySet<string> railConnectedVerticalPassiveIds,
        CircuitGraph graph
    )
    {
        foreach (var passiveId in railConnectedVerticalPassiveIds)
        {
            if (!deviceRow.TryGetValue(passiveId, out var passiveRow))
            {
                continue;
            }

            var pNet = graph.GetNetForTerminal(passiveId, "P");
            var nNet = graph.GetNetForTerminal(passiveId, "N");
            var leansToGround =
                (pNet != null && graph.Grounds.Contains(pNet))
                || (nNet != null && graph.Grounds.Contains(nNet));
            var leansToSupply =
                (pNet != null && graph.Supplies.Contains(pNet))
                || (nNet != null && graph.Supplies.Contains(nNet));
            if (!leansToGround && !leansToSupply)
            {
                continue;
            }

            foreach (var net in new[] { pNet, nNet })
            {
                if (net == null || graph.IsSupplyOrGround(net))
                {
                    continue;
                }

                if (!graph.NetConnections.TryGetValue(net, out var connections))
                {
                    continue;
                }

                foreach (var conn in connections)
                {
                    if (
                        conn.DeviceId == passiveId
                        || !deviceRow.TryGetValue(conn.DeviceId, out var otherRow)
                    )
                    {
                        continue;
                    }

                    if (leansToGround)
                    {
                        model.Add(passiveRow >= otherRow + 1);
                    }
                    else if (leansToSupply)
                    {
                        model.Add(passiveRow <= otherRow - 1);
                    }
                }
            }
        }
    }

    private static IReadOnlyDictionary<string, int> ExtractPortYHints(
        CpSolver solver,
        IReadOnlyDictionary<string, IntVar> portYVariables,
        CircuitGraph graph,
        IReadOnlyDictionary<string, IntVar> deviceRow
    )
    {
        var hints = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (portName, varRef) in portYVariables)
        {
            if (!graph.NetConnections.TryGetValue(portName, out var connections))
            {
                continue;
            }

            var hasDeviceTerminal = connections.Any(c => deviceRow.ContainsKey(c.DeviceId));
            if (!hasDeviceTerminal)
            {
                continue;
            }

            hints[portName] = (int)solver.Value(varRef);
        }

        return hints;
    }

    private static void AddRowAnchorObjectives(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> deviceRow,
        IReadOnlyDictionary<string, (int TargetRow, int Weight)> rowAnchorTargets,
        List<LinearExpr> objectives
    )
    {
        foreach (var (deviceId, anchor) in rowAnchorTargets)
        {
            if (!deviceRow.TryGetValue(deviceId, out var rowVar))
            {
                continue;
            }

            var anchorDiff = model.NewIntVar(0, 200, $"rowanchor_{ToVarToken(deviceId)}");
            model.AddAbsEquality(anchorDiff, rowVar - anchor.TargetRow);
            objectives.Add(anchorDiff * anchor.Weight);
        }
    }

    private static void AddRailProximityObjectives(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> deviceRow,
        IReadOnlySet<string> railConnectedVerticalPassiveIds,
        CircuitGraph graph,
        IReadOnlySet<string> horizontalPassiveIds,
        int canvasHeight,
        List<LinearExpr> objectives
    )
    {
        var baseTerminalY = DeviceGeometry.RailMargin + DeviceGeometry.CellHeight / 2;

        foreach (var deviceId in railConnectedVerticalPassiveIds)
        {
            if (!deviceRow.TryGetValue(deviceId, out var rowVar))
            {
                continue;
            }

            if (!graph.Devices.TryGetValue(deviceId, out var device))
            {
                continue;
            }

            foreach (var (terminal, netName) in device.Bindings)
            {
                var isSupply = graph.Supplies.Contains(netName);
                var isGround = graph.Grounds.Contains(netName);
                if (!isSupply && !isGround)
                {
                    continue;
                }

                var offsets = GetTerminalOffsetInCells(
                    device.DeviceType,
                    terminal,
                    deviceId,
                    horizontalPassiveIds
                );
                var terminalYOffset = (int)
                    Math.Round(
                        offsets.DeltaRow * DeviceGeometry.CellHeight,
                        MidpointRounding.AwayFromZero
                    );
                var terminalYExpr =
                    rowVar * DeviceGeometry.CellHeight + baseTerminalY + terminalYOffset;
                var railY = isSupply
                    ? DeviceGeometry.RailMargin / 2
                    : canvasHeight - DeviceGeometry.RailMargin / 2;

                var railDiff = model.NewIntVar(
                    0,
                    canvasHeight,
                    $"raildiff_{ToVarToken(deviceId)}_{ToVarToken(terminal)}"
                );
                model.AddAbsEquality(railDiff, terminalYExpr - railY);
                objectives.Add(railDiff * RailTerminalWeight);
            }
        }
    }

    private static bool IsPassiveConnectedToRail(CircuitGraph graph, string deviceId)
    {
        if (!graph.Devices.TryGetValue(deviceId, out var device))
        {
            return false;
        }

        var type = device.DeviceType.ToLowerInvariant();
        if (type is not ("resistor" or "capacitor"))
        {
            return false;
        }

        if (
            !device.Bindings.TryGetValue("P", out var pNet)
            || !device.Bindings.TryGetValue("N", out var nNet)
        )
        {
            return false;
        }

        return graph.IsSupplyOrGround(pNet) || graph.IsSupplyOrGround(nNet);
    }

    private static string ToVarToken(string value)
    {
        var chars = value.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        return new string(chars);
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
        int symmetryAxis,
        IReadOnlySet<string> horizontalPassiveIds
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
                var mirrorX = col > symmetryAxis;
                placements[devices[i]] = new GridCell(row, col, mirrorX);
            }
        }

        return new CoarseGridResult
        {
            RowCount = topology.RowCount,
            ColumnCount = columnCount,
            DevicePlacements = placements,
            SymmetryAxis = symmetryAxis,
            HorizontalPassiveIds = horizontalPassiveIds,
        };
    }

    /// <summary>
    /// Extracts placements from the solved model.
    /// For input differential pairs, gates face outward (away from axis).
    /// For other symmetric groups (current mirrors, load pairs), gates face inward (toward axis).
    /// For horizontal passives, MirrorX indicates terminal orientation.
    /// </summary>
    private static Dictionary<string, GridCell> ExtractPlacements(
        CpSolver solver,
        Dictionary<string, IntVar> deviceColumn,
        Dictionary<string, IntVar> deviceRow,
        TopologyResult topology,
        int symmetryAxis,
        CircuitGraph graph,
        IReadOnlySet<string> horizontalPassiveIds
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
            var row = (int)solver.Value(deviceRow[deviceId]);

            bool mirrorX;
            if (horizontalPassiveIds.Contains(deviceId))
            {
                // Horizontal passive: MirrorX indicates if right of axis
                // Right of axis means P terminal should face right, N toward center (left)
                mirrorX = col > symmetryAxis;
            }
            else if (inputDiffPairDevices.Contains(deviceId))
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

    /// <summary>
    /// Collects topology rows of devices connected to a passive's P and N terminals.
    /// Excludes supply/ground nets and the passive device itself.
    /// </summary>
    private static List<int> GetConnectedDeviceRows(
        string passiveId,
        CircuitGraph graph,
        TopologyResult topology
    )
    {
        var connectedRows = new List<int>();
        var pNet = graph.GetNetForTerminal(passiveId, "P");
        var nNet = graph.GetNetForTerminal(passiveId, "N");

        foreach (var net in new[] { pNet, nNet })
        {
            if (net == null || graph.IsSupplyOrGround(net))
            {
                continue;
            }

            if (!graph.NetConnections.TryGetValue(net, out var connections))
            {
                continue;
            }

            foreach (var conn in connections)
            {
                if (
                    conn.DeviceId != passiveId
                    && topology.DeviceRows.TryGetValue(conn.DeviceId, out var row)
                )
                {
                    connectedRows.Add(row);
                }
            }
        }

        return connectedRows;
    }

    /// <summary>
    /// Determines which topology rows need a fill row after them for horizontal passives.
    /// Returns a set of topology row indices after which a fill row should be inserted.
    /// </summary>
    private static HashSet<int> ComputeFillRowPositions(
        IReadOnlySet<string> horizontalPassiveIds,
        TopologyResult topology,
        CircuitGraph graph
    )
    {
        var fillRowsAfter = new HashSet<int>();

        foreach (var passiveId in horizontalPassiveIds)
        {
            var connectedRows = GetConnectedDeviceRows(passiveId, graph, topology);

            if (connectedRows.Count >= 2)
            {
                // Place fill row after the minimum connected row (between the rows)
                var minRow = connectedRows.Min();
                fillRowsAfter.Add(minRow);
            }
            else if (connectedRows.Count == 1)
            {
                // Single connection - place fill row after that row
                fillRowsAfter.Add(connectedRows[0]);
            }
        }

        return fillRowsAfter;
    }
}
