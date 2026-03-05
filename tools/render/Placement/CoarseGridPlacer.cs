namespace Cascode.Render.Placement;

using Cascode.Language;
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
    private const int RailTerminalWeight = 2;
    private const int CascodeStackWeight = 500;

    /// <summary>
    /// Places devices on a coarse grid based on topology analysis.
    /// <summary>
    /// Computes a coarse grid placement for devices described by topology and circuit graph, producing device row/column assignments and symmetry information.
    /// </summary>
    /// <param name="topology">Topology rows, device-to-row mapping, symmetric groups, and passive orientations used to determine valid grid positions and fill rows.</param>
    /// <param name="graph">Circuit connectivity used for symmetry decisions and terminal-aware wire-length evaluation.</param>
    /// <param name="constraints">Optional render placement constraints (hard, soft, hints) that fix or bias device positions; may be null to disable constraint-based placement.</param>
    /// <returns>A CoarseGridResult containing row and column counts, per-device GridCell placements, symmetry axis index, and the set of horizontal passive device IDs.</returns>
    /// <exception cref="RenderConstraintUnsatException">Thrown when provided hard render placement constraints cannot be satisfied and constraint relaxation is not allowed.</exception>
    public static CoarseGridResult Place(
        TopologyResult topology,
        CircuitGraph graph,
        PlacementConstraintSet? constraints = null
    )
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
        var cascodePairs = DetectCascodeVerticalPairs(graph);
        var cascodeDeviceIds = new HashSet<string>(
            cascodePairs.SelectMany(pair => new[] { pair.UpperDeviceId, pair.LowerDeviceId }),
            StringComparer.Ordinal
        );

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
        var hardConstraintEntities = new List<string>();

        // Create column variables for all devices
        var deviceColumn = new Dictionary<string, IntVar>();
        var deviceTransforms = new Dictionary<string, IntVar>();
        foreach (var deviceId in deviceIds)
        {
            deviceColumn[deviceId] = model.NewIntVar(0, estimatedColumns - 1, $"col_{deviceId}");
            deviceTransforms[deviceId] = model.NewIntVar(0, 15, $"xfm_{deviceId}");
        }

        // Create row variables
        // Vertical-path devices get offset rows; horizontal passives are optimized
        var deviceRow = new Dictionary<string, IntVar>();
        foreach (var deviceId in deviceIds)
        {
            var topoRow = topology.DeviceRows.GetValueOrDefault(deviceId, 0);
            var offsetRow = topoRow + fillRowOffset[topoRow];
            if (horizontalPassiveIds.Contains(deviceId))
            {
                // Horizontal passive rows are SAT variables - can be any row
                deviceRow[deviceId] = model.NewIntVar(0, totalRows - 1, $"row_{deviceId}");
            }
            else if (railConnectedVerticalPassiveIds.Contains(deviceId))
            {
                deviceRow[deviceId] = model.NewIntVar(0, totalRows - 1, $"row_{deviceId}");
            }
            else if (cascodeDeviceIds.Contains(deviceId))
            {
                // Cascode devices need movable rows so vertical stack constraints can be satisfied.
                deviceRow[deviceId] = model.NewIntVar(0, totalRows - 1, $"row_{deviceId}");
            }
            else
            {
                // Vertical-path devices get row = topoRow + offset for fill rows before it
                deviceRow[deviceId] = model.NewConstant(offsetRow);
            }
        }

        // Collect devices with hard render placement constraints so that
        // structural layout constraints do not override the user's explicit position.
        var hardPlacedDeviceIds = new HashSet<string>(StringComparer.Ordinal);
        if (constraints is not null)
        {
            foreach (var entry in constraints.DevicePlacements)
            {
                if (entry.Strength == RenderConstraintStrength.Hard)
                {
                    hardPlacedDeviceIds.Add(entry.DeviceId);
                }
            }
        }

        var portYVariables = CreatePortYVariables(model, signalPorts, canvasHeight);
        AddRailSideOrderingConstraints(
            model,
            deviceRow,
            railConnectedVerticalPassiveIds,
            graph,
            hardPlacedDeviceIds
        );

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
            hardPlacedDeviceIds,
            symmetryAxis
        );

        var hasHardPlacementConstraints = AddRenderPlacementConstraints(
            model,
            deviceRow,
            deviceColumn,
            totalRows,
            estimatedColumns,
            constraints,
            hardConstraintEntities
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
                hardPlacedDeviceIds,
                symmetryAxis
            );

            // Push symmetric MOSFETs to edge columns (distance 2 from axis)
            AddMosfetEdgeColumnConstraints(
                model,
                deviceColumn,
                symmetricGroups,
                horizontalPassiveIds,
                hardPlacedDeviceIds,
                symmetryAxis
            );
        }

        var wireLengthObjectives = new List<LinearExpr>();
        AddTerminalAwareWireLengthObjective(
            model,
            deviceColumn,
            deviceRow,
            deviceTransforms,
            graph,
            estimatedColumns,
            totalRows,
            wireLengthObjectives
        );
        AddCascodeVerticalStackObjectives(
            model,
            deviceColumn,
            deviceRow,
            cascodePairs,
            wireLengthObjectives
        );
        AddRailTerminalProximityObjectives(
            model,
            deviceRow,
            deviceTransforms,
            graph,
            totalRows * DeviceGeometry.CellHeight + 2 * DeviceGeometry.RailMargin,
            wireLengthObjectives
        );

        if (wireLengthObjectives.Count > 0)
        {
            model.Minimize(LinearExpr.Sum(wireLengthObjectives));
        }

        var solver = new CpSolver();
        solver.StringParameters = OrToolsSolverDefaults.BuildSolverParameters(MaxSolveTimeSeconds);
        var status = solver.Solve(model);

        if (status != CpSolverStatus.Optimal && status != CpSolverStatus.Feasible)
        {
            if (
                hasHardPlacementConstraints
                && constraints is { AllowConstraintRelaxation: false }
                && hardConstraintEntities.Count > 0
            )
            {
                throw new RenderConstraintUnsatException(
                    "Hard render placement constraints are unsatisfiable.",
                    hardConstraintEntities
                );
            }

            return FallbackPlacement(
                topology,
                estimatedColumns,
                symmetryAxis,
                horizontalPassiveIds,
                cascodePairs
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
        ApplyCascodeStackAdjustments(placements, cascodePairs);
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
    /// Applies hard render-placement constraints from the given PlacementConstraintSet to the CP-SAT model.
    /// </summary>
    /// <param name="model">The CP-SAT model to modify.</param>
    /// <param name="deviceRow">Map from device ID to row IntVar used by the model.</param>
    /// <param name="deviceColumn">Map from device ID to column IntVar used by the model.</param>
    /// <param name="totalRows">Number of rows in the placement grid (used to clamp and bound penalties).</param>
    /// <param name="totalColumns">Number of columns in the placement grid (used to clamp and bound penalties).</param>
    /// <param name="constraints">Optional placement constraints containing device render coordinates and strengths; if null or empty no constraints are applied.</param>
    /// <param name="hardConstraintEntities">List to which device IDs fixed by hard constraints will be appended.</param>
    /// <returns>`true` if at least one hard render-placement constraint was applied, `false` otherwise.</returns>
    private static bool AddRenderPlacementConstraints(
        CpModel model,
        Dictionary<string, IntVar> deviceRow,
        Dictionary<string, IntVar> deviceColumn,
        int totalRows,
        int totalColumns,
        PlacementConstraintSet? constraints,
        List<string> hardConstraintEntities
    )
    {
        if (constraints is null || constraints.DevicePlacements.Count == 0)
        {
            return false;
        }

        var hasHardConstraints = false;
        foreach (var entry in constraints.DevicePlacements)
        {
            if (
                !deviceRow.TryGetValue(entry.DeviceId, out var rowVar)
                || !deviceColumn.TryGetValue(entry.DeviceId, out var colVar)
            )
            {
                continue;
            }

            var (targetRow, targetCol) = RenderCoordinateMapper.MapRenderUnitsToCell(
                entry.XRu,
                entry.YRu
            );
            targetRow = Math.Clamp(targetRow, 0, totalRows - 1);
            targetCol = Math.Clamp(targetCol, 0, totalColumns - 1);

            switch (entry.Strength)
            {
                case RenderConstraintStrength.Hard:
                    model.Add(rowVar == targetRow);
                    model.Add(colVar == targetCol);
                    hasHardConstraints = true;
                    hardConstraintEntities.Add(entry.DeviceId);
                    break;

                case RenderConstraintStrength.Soft:
                    break;

                case RenderConstraintStrength.Hint:
                    break;
            }
        }

        return hasHardConstraints;
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
        IReadOnlySet<string> hardPlacedDeviceIds,
        int symmetryAxis
    )
    {
        foreach (var deviceId in horizontalPassiveIds)
        {
            if (hardPlacedDeviceIds.Contains(deviceId))
            {
                continue;
            }

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
        IReadOnlySet<string> hardPlacedDeviceIds,
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
                if (hardPlacedDeviceIds.Contains(deviceId))
                {
                    continue;
                }

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
    /// Devices with hard render placement constraints are excluded — the user's explicit
    /// position takes precedence over the centering heuristic.
    /// </summary>
    private static void AddCenterDeviceConstraints(
        CpModel model,
        Dictionary<string, IntVar> deviceColumn,
        List<string> deviceIds,
        IReadOnlyList<SymmetricGroup> symmetricGroups,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlySet<string> hardPlacedDeviceIds,
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

            if (hardPlacedDeviceIds.Contains(deviceId))
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
        IReadOnlyDictionary<string, IntVar> deviceTransforms,
        CircuitGraph graph,
        int columnCount,
        int rowCount,
        List<LinearExpr> objectives
    )
    {
        var maxPinDeltaPixels = (int)
            Math.Ceiling(Math.Max(DeviceGeometry.MosfetHeight, DeviceGeometry.PassiveWidth));
        var maxColDiffPixels = columnCount * DeviceGeometry.CellWidth + 2 * maxPinDeltaPixels;
        var maxRowDiffPixels = rowCount * DeviceGeometry.CellHeight + 2 * maxPinDeltaPixels;

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

                    if (
                        !deviceTransforms.TryGetValue(conn1.DeviceId, out var transform1)
                        || !deviceTransforms.TryGetValue(conn2.DeviceId, out var transform2)
                    )
                    {
                        continue;
                    }

                    var options1 = GetTerminalOffsetOptionsInPixels(
                        device1.DeviceType,
                        conn1.Terminal
                    );
                    var options2 = GetTerminalOffsetOptionsInPixels(
                        device2.DeviceType,
                        conn2.Terminal
                    );
                    var xOptions1 = options1.Select(o => o.DeltaX).ToArray();
                    var yOptions1 = options1.Select(o => o.DeltaY).ToArray();
                    var xOptions2 = options2.Select(o => o.DeltaX).ToArray();
                    var yOptions2 = options2.Select(o => o.DeltaY).ToArray();

                    var xOffset1 = model.NewIntVar(
                        xOptions1.Min(),
                        xOptions1.Max(),
                        $"xoff_{ToVarToken(conn1.DeviceId)}_{ToVarToken(conn1.Terminal)}_{ToVarToken(netName)}_{i}_{j}"
                    );
                    var yOffset1 = model.NewIntVar(
                        yOptions1.Min(),
                        yOptions1.Max(),
                        $"yoff_{ToVarToken(conn1.DeviceId)}_{ToVarToken(conn1.Terminal)}_{ToVarToken(netName)}_{i}_{j}"
                    );
                    var xOffset2 = model.NewIntVar(
                        xOptions2.Min(),
                        xOptions2.Max(),
                        $"xoff_{ToVarToken(conn2.DeviceId)}_{ToVarToken(conn2.Terminal)}_{ToVarToken(netName)}_{i}_{j}"
                    );
                    var yOffset2 = model.NewIntVar(
                        yOptions2.Min(),
                        yOptions2.Max(),
                        $"yoff_{ToVarToken(conn2.DeviceId)}_{ToVarToken(conn2.Terminal)}_{ToVarToken(netName)}_{i}_{j}"
                    );

                    model.AddElement(transform1, xOptions1, xOffset1);
                    model.AddElement(transform1, yOptions1, yOffset1);
                    model.AddElement(transform2, xOptions2, xOffset2);
                    model.AddElement(transform2, yOptions2, yOffset2);

                    var colDiffPixels = model.NewIntVar(
                        0,
                        maxColDiffPixels,
                        $"coldiff_{netName}_{i}_{j}"
                    );
                    model.AddAbsEquality(
                        colDiffPixels,
                        deviceColumn[conn1.DeviceId] * DeviceGeometry.CellWidth
                            - deviceColumn[conn2.DeviceId] * DeviceGeometry.CellWidth
                            + xOffset1
                            - xOffset2
                    );

                    var rowDiffPixels = model.NewIntVar(
                        0,
                        maxRowDiffPixels,
                        $"rowdiff_{netName}_{i}_{j}"
                    );
                    model.AddAbsEquality(
                        rowDiffPixels,
                        deviceRow[conn1.DeviceId] * DeviceGeometry.CellHeight
                            - deviceRow[conn2.DeviceId] * DeviceGeometry.CellHeight
                            + yOffset1
                            - yOffset2
                    );

                    objectives.Add(colDiffPixels + rowDiffPixels);
                }
            }
        }
    }

    private static void AddCascodeVerticalStackObjectives(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> deviceColumn,
        IReadOnlyDictionary<string, IntVar> deviceRow,
        IReadOnlyCollection<(string UpperDeviceId, string LowerDeviceId)> cascodePairs,
        List<LinearExpr> objectives
    )
    {
        foreach (var (upperDeviceId, lowerDeviceId) in cascodePairs)
        {
            if (
                !deviceColumn.TryGetValue(upperDeviceId, out var upperCol)
                || !deviceColumn.TryGetValue(lowerDeviceId, out var lowerCol)
                || !deviceRow.TryGetValue(upperDeviceId, out var upperRow)
                || !deviceRow.TryGetValue(lowerDeviceId, out var lowerRow)
            )
            {
                continue;
            }

            var colDiff = model.NewIntVar(
                0,
                200,
                $"cascode_col_diff_{ToVarToken(upperDeviceId)}_{ToVarToken(lowerDeviceId)}"
            );
            model.AddAbsEquality(colDiff, upperCol - lowerCol);
            objectives.Add(colDiff * CascodeStackWeight);

            var stackGap = model.NewIntVar(
                0,
                200,
                $"cascode_row_gap_{ToVarToken(upperDeviceId)}_{ToVarToken(lowerDeviceId)}"
            );
            model.AddAbsEquality(stackGap, lowerRow - upperRow - 1);
            objectives.Add(stackGap * CascodeStackWeight);
        }
    }

    private static IReadOnlyCollection<(
        string UpperDeviceId,
        string LowerDeviceId
    )> DetectCascodeVerticalPairs(CircuitGraph graph)
    {
        var pairs = new HashSet<(string UpperDeviceId, string LowerDeviceId)>();

        foreach (var (netName, connections) in graph.NetConnections)
        {
            if (graph.IsSupplyOrGround(netName) || graph.IsPort(netName))
            {
                continue;
            }

            var terminalRefs = connections
                .Where(c =>
                    graph.Devices.TryGetValue(c.DeviceId, out var d)
                    && IsMosType(d.DeviceType.ToLowerInvariant())
                    && (IsDrainTerminal(c.Terminal) || IsSourceTerminal(c.Terminal))
                )
                .ToList();
            if (terminalRefs.Count != 2)
            {
                continue;
            }

            var c1 = terminalRefs[0];
            var c2 = terminalRefs[1];
            if (
                !graph.Devices.TryGetValue(c1.DeviceId, out var d1)
                || !graph.Devices.TryGetValue(c2.DeviceId, out var d2)
            )
            {
                continue;
            }

            var t1 = d1.DeviceType.ToLowerInvariant();
            var t2 = d2.DeviceType.ToLowerInvariant();
            var bothNfet = IsNfetLike(t1) && IsNfetLike(t2);
            var bothPfet = IsPfetLike(t1) && IsPfetLike(t2);
            if (!bothNfet && !bothPfet)
            {
                continue;
            }

            var aIsDrain = IsDrainTerminal(c1.Terminal);
            var aIsSource = IsSourceTerminal(c1.Terminal);
            var bIsDrain = IsDrainTerminal(c2.Terminal);
            var bIsSource = IsSourceTerminal(c2.Terminal);
            var isCascodeLink = (aIsDrain && bIsSource) || (aIsSource && bIsDrain);
            if (!isCascodeLink)
            {
                continue;
            }

            if (bothNfet)
            {
                if (aIsSource)
                {
                    pairs.Add((c1.DeviceId, c2.DeviceId));
                }
                else
                {
                    pairs.Add((c2.DeviceId, c1.DeviceId));
                }
            }
            else
            {
                if (aIsDrain)
                {
                    pairs.Add((c1.DeviceId, c2.DeviceId));
                }
                else
                {
                    pairs.Add((c2.DeviceId, c1.DeviceId));
                }
            }
        }

        return pairs;
    }

    private static bool IsMosType(string normalizedType) =>
        IsNfetLike(normalizedType) || IsPfetLike(normalizedType);

    private static bool IsNfetLike(string normalizedType) =>
        normalizedType.Contains("nfet", StringComparison.OrdinalIgnoreCase)
        || normalizedType.Equals("nmos", StringComparison.OrdinalIgnoreCase);

    private static bool IsPfetLike(string normalizedType) =>
        normalizedType.Contains("pfet", StringComparison.OrdinalIgnoreCase)
        || normalizedType.Equals("pmos", StringComparison.OrdinalIgnoreCase);

    private static bool IsDrainTerminal(string terminal) =>
        terminal.Equals("D", StringComparison.OrdinalIgnoreCase)
        || terminal.Contains("DRAIN", StringComparison.OrdinalIgnoreCase)
        || terminal.StartsWith("D", StringComparison.OrdinalIgnoreCase);

    private static bool IsSourceTerminal(string terminal) =>
        terminal.Equals("S", StringComparison.OrdinalIgnoreCase)
        || terminal.Contains("SOURCE", StringComparison.OrdinalIgnoreCase)
        || terminal.StartsWith("S", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Enumerates terminal offsets in pixels for all supported transforms.
    /// Transform index layout: rotation (0,90,180,270) x mirrorX (0,1) x mirrorY (0,1).
    /// </summary>
    private static IReadOnlyList<(int DeltaX, int DeltaY)> GetTerminalOffsetOptionsInPixels(
        string deviceType,
        string terminal
    )
    {
        var (baseX, baseY) = GetBaseTerminalOffset(deviceType, terminal);
        var options = new List<(int DeltaX, int DeltaY)>(16);
        foreach (var rotation in new[] { 0, 90, 180, 270 })
        {
            for (var mirrorX = 0; mirrorX <= 1; mirrorX++)
            {
                for (var mirrorY = 0; mirrorY <= 1; mirrorY++)
                {
                    var x = baseX;
                    var y = baseY;
                    if (mirrorX == 1)
                    {
                        x = -x;
                    }

                    if (mirrorY == 1)
                    {
                        y = -y;
                    }

                    var transformed = rotation switch
                    {
                        0 => (x, y),
                        90 => (y, -x),
                        180 => (-x, -y),
                        270 => (-y, x),
                        _ => (x, y),
                    };
                    options.Add(transformed);
                }
            }
        }

        return options;
    }

    private static (int DeltaX, int DeltaY) GetBaseTerminalOffset(
        string deviceType,
        string terminal
    )
    {
        var type = deviceType.ToLowerInvariant();
        var pin = terminal.ToUpperInvariant();

        if (IsNfetLike(type))
        {
            return pin switch
            {
                "G" => (-(int)Math.Round(DeviceGeometry.MosfetWidth / 2.0), 0),
                "D" => (0, -(int)Math.Round(DeviceGeometry.MosfetHeight / 2.0)),
                "S" => (0, (int)Math.Round(DeviceGeometry.MosfetHeight / 2.0)),
                _ => (0, 0),
            };
        }

        if (IsPfetLike(type))
        {
            return pin switch
            {
                "G" => (-(int)Math.Round(DeviceGeometry.MosfetWidth / 2.0), 0),
                "D" => (0, (int)Math.Round(DeviceGeometry.MosfetHeight / 2.0)),
                "S" => (0, -(int)Math.Round(DeviceGeometry.MosfetHeight / 2.0)),
                _ => (0, 0),
            };
        }

        if (type is "resistor" or "capacitor")
        {
            return pin switch
            {
                "P" => (0, -(int)Math.Round(DeviceGeometry.PassiveWidth / 2.0)),
                "N" => (0, (int)Math.Round(DeviceGeometry.PassiveWidth / 2.0)),
                _ => (0, 0),
            };
        }

        return (0, 0);
    }

    private static (double DeltaCol, double DeltaRow) GetTerminalOffsetInCells(
        string deviceType,
        string terminal,
        string deviceId,
        IReadOnlySet<string> horizontalPassiveIds
    )
    {
        var (deltaX, deltaY) = GetBaseTerminalOffset(deviceType, terminal);
        return (
            deltaX / (double)DeviceGeometry.CellWidth,
            deltaY / (double)DeviceGeometry.CellHeight
        );
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
        CircuitGraph graph,
        IReadOnlySet<string> hardPlacedDeviceIds
    )
    {
        foreach (var passiveId in railConnectedVerticalPassiveIds)
        {
            if (hardPlacedDeviceIds.Contains(passiveId))
            {
                continue;
            }

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
                        || railConnectedVerticalPassiveIds.Contains(conn.DeviceId)
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

    private static void AddRailTerminalProximityObjectives(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> deviceRow,
        IReadOnlyDictionary<string, IntVar> deviceTransforms,
        CircuitGraph graph,
        int canvasHeight,
        List<LinearExpr> objectives
    )
    {
        var baseTerminalY = DeviceGeometry.RailMargin + DeviceGeometry.CellHeight / 2;

        foreach (var (deviceId, rowVar) in deviceRow)
        {
            if (!graph.Devices.TryGetValue(deviceId, out var device))
            {
                continue;
            }

            if (!deviceTransforms.TryGetValue(deviceId, out var transformVar))
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

                if (IsBodyOrShieldTerminal(terminal))
                {
                    continue;
                }

                var yOptions = GetTerminalOffsetOptionsInPixels(device.DeviceType, terminal)
                    .Select(o => o.DeltaY)
                    .ToArray();
                var terminalYOffset = model.NewIntVar(
                    yOptions.Min(),
                    yOptions.Max(),
                    $"ryoff_{ToVarToken(deviceId)}_{ToVarToken(terminal)}"
                );
                model.AddElement(transformVar, yOptions, terminalYOffset);

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

    private static bool IsBodyOrShieldTerminal(string terminal)
    {
        var t = terminal.Trim().ToUpperInvariant();
        return t is "B" or "BULK" or "BODY" or "SH" or "SHIELD";
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
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlyCollection<(string UpperDeviceId, string LowerDeviceId)> cascodePairs
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
        ApplyCascodeStackAdjustments(placements, cascodePairs);

        return new CoarseGridResult
        {
            RowCount = topology.RowCount,
            ColumnCount = columnCount,
            DevicePlacements = placements,
            SymmetryAxis = symmetryAxis,
            HorizontalPassiveIds = horizontalPassiveIds,
        };
    }

    private static void ApplyCascodeStackAdjustments(
        IDictionary<string, GridCell> placements,
        IReadOnlyCollection<(string UpperDeviceId, string LowerDeviceId)> cascodePairs
    )
    {
        foreach (var (upperDeviceId, lowerDeviceId) in cascodePairs)
        {
            if (
                !placements.TryGetValue(upperDeviceId, out var upper)
                || !placements.TryGetValue(lowerDeviceId, out var lower)
            )
            {
                continue;
            }

            var stackedCol = upper.Column;
            var upperRow = Math.Min(upper.Row, lower.Row);
            var lowerRow = upperRow + 1;

            placements[upperDeviceId] = new GridCell(upperRow, stackedCol, upper.MirrorX);
            placements[lowerDeviceId] = new GridCell(lowerRow, stackedCol, lower.MirrorX);
        }
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
