namespace Cascode.Render.Placement;

using Cascode.Language;
using Cascode.Render.Analysis;
using Cascode.Render.Layout;
using Cascode.Render.OrTools;
using Google.OrTools.Sat;

internal static class CoarseGridPlacementSolver
{
    private const double MaxSolveTimeSeconds = 2.0;
    private const int SoftConstraintWeight = 40;
    private const int HintConstraintWeight = 8;
    private const int HorizontalRowAnchorWeight = 25;
    private const int VerticalRowAnchorWeight = 6;
    private const int CenterAxisWeight = 6;
    private const int SymmetryWeight = 12;
    private const int CompactnessWeight = 16;
    private const int PortBiasWeight = 5;
    private const int PortLaneWeight = 3;
    private const int RailTerminalWeight = 2;

    public static CoarseGridResult Solve(
        TopologyResult topology,
        CircuitGraph graph,
        PlacementConstraintSet? constraints
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
                HorizontalPassiveIds = new HashSet<string>(StringComparer.Ordinal),
            };
        }

        var horizontalPassiveIds = topology
            .PassiveOrientations.Where(kv => kv.Value == PassiveOrientation.Horizontal)
            .Select(kv => kv.Key)
            .ToHashSet(StringComparer.Ordinal);
        var railConnectedVerticalPassiveIds = topology
            .PassiveOrientations.Where(kv => kv.Value == PassiveOrientation.Vertical)
            .Select(kv => kv.Key)
            .Where(deviceId => IsPassiveConnectedToRail(graph, deviceId))
            .ToHashSet(StringComparer.Ordinal);
        var symmetricPassivePairs = TopologyAnalyzer.DetectSymmetricPassivePairs(graph, topology);
        var hardTargets = GetConstraintTargets(constraints, RenderConstraintStrength.Hard);
        var deviceIds = topology.DeviceRows.Keys.OrderBy(id => id, StringComparer.Ordinal).ToList();
        var estimatedColumns = Math.Max(
            3,
            Math.Max(
                deviceIds.Count,
                topology.SymmetricGroups.Count == 0
                    ? 0
                    : topology.SymmetricGroups.Max(group => group.DeviceIds.Count) * 2 + 1
            )
        );
        var estimatedAxis = estimatedColumns / 2;
        var fillRowsAfterTopoRow = ComputeFillRowPositions(horizontalPassiveIds, topology, graph);
        var fillRowOffsets = BuildFillRowOffsets(topology.DeviceRows.Values, fillRowsAfterTopoRow);
        var totalRows = topology.RowCount + fillRowsAfterTopoRow.Count + 2;
        var canvasHeight = totalRows * DeviceGeometry.CellHeight + 2 * DeviceGeometry.RailMargin;

        var model = new CpModel();
        var rowVars = new Dictionary<string, IntVar>(StringComparer.Ordinal);
        var columnVars = new Dictionary<string, IntVar>(StringComparer.Ordinal);
        var hardPlacedIds = hardTargets.Keys.ToHashSet(StringComparer.Ordinal);
        var rowAnchorTargets = new Dictionary<string, (int TargetRow, int Weight)>(
            StringComparer.Ordinal
        );
        foreach (var deviceId in deviceIds)
        {
            var topoRow = topology.DeviceRows.GetValueOrDefault(deviceId, 0);
            var anchorRow = topoRow + fillRowOffsets[topoRow];
            var isFlexibleRow =
                hardTargets.ContainsKey(deviceId)
                || horizontalPassiveIds.Contains(deviceId)
                || railConnectedVerticalPassiveIds.Contains(deviceId);
            columnVars[deviceId] = model.NewIntVar(0, estimatedColumns - 1, $"col_{deviceId}");
            rowVars[deviceId] = isFlexibleRow
                ? model.NewIntVar(0, totalRows - 1, $"row_{deviceId}")
                : model.NewConstant(anchorRow);
            var anchorWeight =
                horizontalPassiveIds.Contains(deviceId) ? HorizontalRowAnchorWeight
                : railConnectedVerticalPassiveIds.Contains(deviceId) ? VerticalRowAnchorWeight
                : 0;
            if (anchorWeight > 0)
            {
                rowAnchorTargets[deviceId] = (anchorRow, anchorWeight);
            }
        }

        var hardConstraintEntities = new List<string>();
        var objectives = new List<LinearExpr>();
        var portYVars = CreatePortYVariables(model, graph, canvasHeight);

        AddNoOverlapConstraints(model, rowVars, columnVars, deviceIds);
        AddTopologyOrderingConstraints(model, rowVars, topology, hardTargets.Keys);
        AddRailEdgeClearanceConstraints(model, rowVars, columnVars, graph, horizontalPassiveIds);
        AddSymmetryLayoutConstraints(
            model,
            columnVars,
            topology.SymmetricGroups,
            estimatedAxis,
            graph
        );
        AddGroupConstraints(model, rowVars, columnVars, topology, symmetricPassivePairs, graph);
        AddHorizontalPassiveRowConstraints(
            model,
            rowVars,
            horizontalPassiveIds,
            fillRowsAfterTopoRow,
            fillRowOffsets,
            graph,
            topology,
            hardPlacedIds
        );
        if (horizontalPassiveIds.Count > 0)
        {
            AddHorizontalPassiveColumnConstraints(
                model,
                columnVars,
                horizontalPassiveIds,
                hardPlacedIds,
                estimatedAxis
            );
            AddMosfetEdgeColumnConstraints(
                model,
                columnVars,
                topology.SymmetricGroups,
                horizontalPassiveIds,
                hardPlacedIds,
                estimatedAxis
            );
        }
        AddCenterDeviceConstraints(
            model,
            columnVars,
            deviceIds,
            topology.SymmetricGroups,
            horizontalPassiveIds,
            hardPlacedIds,
            estimatedAxis
        );
        AddRailSideOrderingConstraints(
            model,
            rowVars,
            railConnectedVerticalPassiveIds,
            graph,
            hardPlacedIds
        );

        var hasHardConstraints = AddRenderPlacementConstraints(
            model,
            rowVars,
            columnVars,
            totalRows,
            estimatedColumns,
            constraints,
            hardConstraintEntities,
            objectives
        );

        AddWireLengthObjectives(
            model,
            rowVars,
            columnVars,
            graph,
            horizontalPassiveIds,
            estimatedColumns,
            totalRows,
            objectives
        );
        AddRowAnchorObjectives(model, rowVars, rowAnchorTargets, objectives);
        AddPortLaneObjectives(
            model,
            rowVars,
            graph,
            horizontalPassiveIds,
            portYVars,
            canvasHeight,
            objectives
        );
        AddPortBiasObjectives(model, columnVars, graph, estimatedColumns, objectives);
        AddSymmetryObjectives(
            model,
            columnVars,
            deviceIds,
            topology,
            symmetricPassivePairs,
            estimatedAxis,
            horizontalPassiveIds,
            hardTargets.Keys.ToHashSet(StringComparer.Ordinal),
            objectives
        );
        AddRailProximityObjectives(
            model,
            rowVars,
            graph,
            railConnectedVerticalPassiveIds,
            horizontalPassiveIds,
            canvasHeight,
            objectives
        );
        AddCompactnessObjective(model, columnVars, estimatedColumns, objectives);

        if (objectives.Count > 0)
        {
            model.Minimize(LinearExpr.Sum(objectives));
        }

        var solver = new CpSolver();
        solver.StringParameters = OrToolsSolverDefaults.BuildSolverParameters(MaxSolveTimeSeconds);
        var status = solver.Solve(model);
        if (status is not (CpSolverStatus.Feasible or CpSolverStatus.Optimal))
        {
            if (
                hasHardConstraints
                && constraints is { AllowConstraintRelaxation: false }
                && hardConstraintEntities.Count > 0
            )
            {
                throw new RenderConstraintUnsatException(
                    "Hard render placement constraints are unsatisfiable.",
                    hardConstraintEntities
                );
            }

            return BuildFallbackPlacement(topology, graph, horizontalPassiveIds);
        }

        var rawPlacements = deviceIds.ToDictionary(
            deviceId => deviceId,
            deviceId => new GridCell(
                row: (int)solver.Value(rowVars[deviceId]),
                column: (int)solver.Value(columnVars[deviceId])
            ),
            StringComparer.Ordinal
        );
        var solvedPortYHints = ExtractSolvedPortYHints(solver, portYVars, graph);
        var repaired = RepairHardConstraintViolations(
            Compact(rawPlacements),
            graph,
            topology,
            horizontalPassiveIds,
            symmetricPassivePairs
        );
        var oriented = ApplyOrientationRules(
            repaired,
            graph,
            topology,
            horizontalPassiveIds,
            symmetricPassivePairs
        );
        var rowCount = Math.Max(1, oriented.Values.Select(cell => cell.Row).Distinct().Count());
        var columnCount = Math.Max(
            1,
            oriented.Values.Select(cell => cell.Column).Distinct().Count()
        );
        var symmetryAxis = Math.Max(0, columnCount / 2);
        return new CoarseGridResult
        {
            RowCount = rowCount,
            ColumnCount = columnCount,
            DevicePlacements = oriented,
            SymmetryAxis = symmetryAxis,
            HorizontalPassiveIds = horizontalPassiveIds,
            PortYHints = ApplyFeedthroughPortHints(
                solvedPortYHints,
                new CoarseGridResult
                {
                    RowCount = rowCount,
                    ColumnCount = columnCount,
                    DevicePlacements = oriented,
                    SymmetryAxis = symmetryAxis,
                    HorizontalPassiveIds = horizontalPassiveIds,
                },
                graph
            ),
        };
    }

    private static Dictionary<string, (int Row, int Column)> GetConstraintTargets(
        PlacementConstraintSet? constraints,
        RenderConstraintStrength strength
    )
    {
        if (constraints is null)
        {
            return new Dictionary<string, (int Row, int Column)>(StringComparer.Ordinal);
        }

        return constraints
            .DevicePlacements.Where(entry => entry.Strength == strength)
            .ToDictionary(
                entry => entry.DeviceId,
                entry => RenderCoordinateMapper.MapRenderUnitsToCell(entry.XRu, entry.YRu),
                StringComparer.Ordinal
            );
    }

    private static int[] BuildFillRowOffsets(
        IEnumerable<int> rows,
        IReadOnlySet<int> fillRowsAfterTopoRow
    )
    {
        var maxTopoRow = rows.DefaultIfEmpty(0).Max();
        var offsets = new int[maxTopoRow + 2];
        var running = 0;
        for (var row = 0; row <= maxTopoRow; row++)
        {
            offsets[row] = running;
            if (fillRowsAfterTopoRow.Contains(row))
            {
                running++;
            }
        }

        offsets[maxTopoRow + 1] = running;
        return offsets;
    }

    private static void AddNoOverlapConstraints(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> rowVars,
        IReadOnlyDictionary<string, IntVar> columnVars,
        IReadOnlyList<string> deviceIds
    )
    {
        for (var i = 0; i < deviceIds.Count; i++)
        {
            for (var j = i + 1; j < deviceIds.Count; j++)
            {
                var sameRow = model.NewBoolVar($"sameRow_{i}_{j}");
                model.Add(rowVars[deviceIds[i]] == rowVars[deviceIds[j]]).OnlyEnforceIf(sameRow);
                model
                    .Add(rowVars[deviceIds[i]] != rowVars[deviceIds[j]])
                    .OnlyEnforceIf(sameRow.Not());
                model
                    .Add(columnVars[deviceIds[i]] != columnVars[deviceIds[j]])
                    .OnlyEnforceIf(sameRow);
            }
        }
    }

    private static void AddTopologyOrderingConstraints(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> rowVars,
        TopologyResult topology,
        IEnumerable<string> hardPlacedDeviceIds
    )
    {
        var hardPlaced = hardPlacedDeviceIds.ToHashSet(StringComparer.Ordinal);
        var devices = topology.DeviceRows.Keys.OrderBy(id => id, StringComparer.Ordinal).ToList();
        for (var i = 0; i < devices.Count; i++)
        {
            for (var j = i + 1; j < devices.Count; j++)
            {
                var left = devices[i];
                var right = devices[j];
                if (hardPlaced.Contains(left) || hardPlaced.Contains(right))
                {
                    continue;
                }

                var leftRow = topology.DeviceRows[left];
                var rightRow = topology.DeviceRows[right];
                if (leftRow < rightRow)
                {
                    model.Add(rowVars[left] <= rowVars[right]);
                }
                else if (rightRow < leftRow)
                {
                    model.Add(rowVars[right] <= rowVars[left]);
                }
            }
        }
    }

    private static void AddGroupConstraints(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> rowVars,
        IReadOnlyDictionary<string, IntVar> columnVars,
        TopologyResult topology,
        IReadOnlyList<(string Left, string Right, string PivotNet)> symmetricPassivePairs,
        CircuitGraph graph
    )
    {
        foreach (var group in topology.SymmetricGroups)
        {
            var members = group.DeviceIds.Where(rowVars.ContainsKey).ToList();
            if (members.Count < 2)
            {
                continue;
            }

            for (var i = 1; i < members.Count; i++)
            {
                model.Add(rowVars[members[0]] == rowVars[members[i]]);
            }

            foreach (var pair in members.Zip(members.Skip(1)))
            {
                model.Add(columnVars[pair.First] != columnVars[pair.Second]);
            }

            if (group.Type == SymmetryType.DiffPair && members.Count == 2)
            {
                var (left, right) = DetermineLeftRightByInputPort(members[0], members[1], graph);
                model.Add(columnVars[left] < columnVars[right]);
            }
        }

        foreach (var (left, right, _) in symmetricPassivePairs)
        {
            if (!rowVars.ContainsKey(left) || !rowVars.ContainsKey(right))
            {
                continue;
            }

            model.Add(rowVars[left] == rowVars[right]);
            model.Add(columnVars[left] < columnVars[right]);
        }
    }

    private static void AddSymmetryLayoutConstraints(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> columnVars,
        IReadOnlyList<SymmetricGroup> groups,
        int symmetryAxis,
        CircuitGraph graph
    )
    {
        foreach (var group in groups)
        {
            var members = group.DeviceIds.Where(columnVars.ContainsKey).ToList();
            if (members.Count == 2)
            {
                var (left, right) =
                    group.Type == SymmetryType.DiffPair
                        ? DetermineLeftRightByInputPort(members[0], members[1], graph)
                        : DetermineLeftRightByNaming(members[0], members[1]);
                model.Add(columnVars[left] < symmetryAxis);
                model.Add(columnVars[right] > symmetryAxis);
                model.Add(columnVars[left] + columnVars[right] == 2 * symmetryAxis);
            }
            else if (members.Count > 2)
            {
                var sorted = members.OrderBy(id => id, StringComparer.Ordinal).ToList();
                for (var i = 0; i < sorted.Count / 2; i++)
                {
                    model.Add(
                        columnVars[sorted[i]] + columnVars[sorted[sorted.Count - 1 - i]]
                            == 2 * symmetryAxis
                    );
                }

                if (sorted.Count % 2 == 1)
                {
                    model.Add(columnVars[sorted[sorted.Count / 2]] == symmetryAxis);
                }
            }
        }
    }

    private static void AddHorizontalPassiveRowConstraints(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> rowVars,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlySet<int> fillRowsAfterTopoRow,
        int[] fillRowOffsets,
        CircuitGraph graph,
        TopologyResult topology,
        IReadOnlySet<string> hardPlacedDeviceIds
    )
    {
        foreach (var passiveId in horizontalPassiveIds)
        {
            if (hardPlacedDeviceIds.Contains(passiveId) || !rowVars.ContainsKey(passiveId))
            {
                continue;
            }

            var validRows = ComputeValidFillRowsForPassive(
                passiveId,
                fillRowsAfterTopoRow,
                fillRowOffsets,
                graph,
                topology
            );
            if (validRows.Count == 1)
            {
                model.Add(rowVars[passiveId] == validRows[0]);
            }
        }
    }

    private static bool AddRenderPlacementConstraints(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> rowVars,
        IReadOnlyDictionary<string, IntVar> columnVars,
        int totalRows,
        int totalColumns,
        PlacementConstraintSet? constraints,
        List<string> hardConstraintEntities,
        List<LinearExpr> objectives
    )
    {
        if (constraints is null || constraints.DevicePlacements.Count == 0)
        {
            return false;
        }

        var hasHardConstraints = false;
        foreach (var entry in constraints.DevicePlacements)
        {
            if (!rowVars.ContainsKey(entry.DeviceId) || !columnVars.ContainsKey(entry.DeviceId))
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
                    model.Add(rowVars[entry.DeviceId] == targetRow);
                    model.Add(columnVars[entry.DeviceId] == targetCol);
                    hardConstraintEntities.Add(entry.DeviceId);
                    hasHardConstraints = true;
                    break;
                case RenderConstraintStrength.Soft:
                    AddDistancePenalty(
                        model,
                        rowVars[entry.DeviceId],
                        columnVars[entry.DeviceId],
                        targetRow,
                        targetCol,
                        totalRows + totalColumns,
                        SoftConstraintWeight,
                        objectives,
                        $"soft_{entry.DeviceId}"
                    );
                    break;
                case RenderConstraintStrength.Hint:
                    AddDistancePenalty(
                        model,
                        rowVars[entry.DeviceId],
                        columnVars[entry.DeviceId],
                        targetRow,
                        targetCol,
                        totalRows + totalColumns,
                        HintConstraintWeight,
                        objectives,
                        $"hint_{entry.DeviceId}"
                    );
                    break;
            }
        }

        return hasHardConstraints;
    }

    private static void AddDistancePenalty(
        CpModel model,
        IntVar rowVar,
        IntVar columnVar,
        int targetRow,
        int targetColumn,
        int maxValue,
        int weight,
        List<LinearExpr> objectives,
        string token
    )
    {
        var rowPenalty = model.NewIntVar(0, maxValue, $"row_{token}");
        var colPenalty = model.NewIntVar(0, maxValue, $"col_{token}");
        model.AddAbsEquality(rowPenalty, rowVar - targetRow);
        model.AddAbsEquality(colPenalty, columnVar - targetColumn);
        objectives.Add((rowPenalty + colPenalty) * weight);
    }

    private static void AddRowAnchorObjectives(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> rowVars,
        IReadOnlyDictionary<string, (int TargetRow, int Weight)> anchors,
        List<LinearExpr> objectives
    )
    {
        foreach (var (deviceId, anchor) in anchors)
        {
            var penalty = model.NewIntVar(0, 100, $"rowAnchor_{deviceId}");
            model.AddAbsEquality(penalty, rowVars[deviceId] - anchor.TargetRow);
            objectives.Add(penalty * anchor.Weight);
        }
    }

    private static void AddWireLengthObjectives(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> rowVars,
        IReadOnlyDictionary<string, IntVar> columnVars,
        CircuitGraph graph,
        IReadOnlySet<string> horizontalPassiveIds,
        int columnCount,
        int rowCount,
        List<LinearExpr> objectives
    )
    {
        var maxColDiffPixels = columnCount * DeviceGeometry.CellWidth;
        var maxRowDiffCells = rowCount;
        foreach (var (netName, connections) in graph.NetConnections)
        {
            var terminals = connections
                .Where(conn => columnVars.ContainsKey(conn.DeviceId))
                .ToList();
            var weight = graph.IsSupplyOrGround(netName) ? 1 : 4;
            for (var i = 0; i < terminals.Count; i++)
            {
                for (var j = i + 1; j < terminals.Count; j++)
                {
                    var offsetA = GetTerminalOffset(terminals[i], graph, horizontalPassiveIds);
                    var offsetB = GetTerminalOffset(terminals[j], graph, horizontalPassiveIds);
                    var colOffsetPixels = (int)
                        Math.Round(
                            (offsetA.DeltaCol - offsetB.DeltaCol) * DeviceGeometry.CellWidth,
                            MidpointRounding.AwayFromZero
                        );
                    var rowOffsetCells = (int)
                        Math.Round(
                            offsetA.DeltaRow - offsetB.DeltaRow,
                            MidpointRounding.AwayFromZero
                        );
                    var colDiff = model.NewIntVar(0, maxColDiffPixels, $"netX_{netName}_{i}_{j}");
                    var rowDiff = model.NewIntVar(0, maxRowDiffCells, $"netY_{netName}_{i}_{j}");
                    model.AddAbsEquality(
                        colDiff,
                        columnVars[terminals[i].DeviceId] * DeviceGeometry.CellWidth
                            - columnVars[terminals[j].DeviceId] * DeviceGeometry.CellWidth
                            + colOffsetPixels
                    );
                    model.AddAbsEquality(
                        rowDiff,
                        rowVars[terminals[i].DeviceId]
                            - rowVars[terminals[j].DeviceId]
                            + rowOffsetCells
                    );
                    objectives.Add((colDiff + rowDiff * DeviceGeometry.CellHeight) * weight);
                }
            }
        }
    }

    private static (double DeltaCol, double DeltaRow) GetTerminalOffset(
        TerminalRef terminal,
        CircuitGraph graph,
        IReadOnlySet<string> horizontalPassiveIds
    )
    {
        var device = graph.Devices[terminal.DeviceId];
        var isHorizontalPassive = horizontalPassiveIds.Contains(terminal.DeviceId);
        var isLeftOfAxis = !terminal.DeviceId.EndsWith("_R", StringComparison.OrdinalIgnoreCase);
        return DeviceGeometry.GetTerminalOffset(
            device.DeviceType,
            terminal.Terminal,
            mirrorX: false,
            isHorizontalPassive,
            isLeftOfAxis
        );
    }

    private static Dictionary<string, IntVar> CreatePortYVariables(
        CpModel model,
        CircuitGraph graph,
        int canvasHeight
    )
    {
        return graph
            .InputPorts.Concat(graph.BiasPorts)
            .Concat(graph.OutputPorts)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                port => port,
                port => model.NewIntVar(0, canvasHeight, $"portY_{Sanitize(port)}"),
                StringComparer.Ordinal
            );
    }

    private static IReadOnlyDictionary<string, int> ExtractSolvedPortYHints(
        CpSolver solver,
        IReadOnlyDictionary<string, IntVar> portYVars,
        CircuitGraph graph
    )
    {
        var hints = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (portName, variable) in portYVars)
        {
            if (
                graph.NetConnections.TryGetValue(portName, out var connections)
                && connections.Any(connection => graph.Devices.ContainsKey(connection.DeviceId))
            )
            {
                hints[portName] = (int)solver.Value(variable);
            }
        }

        return hints;
    }

    private static void AddPortLaneObjectives(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> rowVars,
        CircuitGraph graph,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlyDictionary<string, IntVar> portYVars,
        int canvasHeight,
        List<LinearExpr> objectives
    )
    {
        var baseTerminalY = DeviceGeometry.RailMargin + DeviceGeometry.CellHeight / 2;
        foreach (var (portName, portYVar) in portYVars)
        {
            if (!graph.NetConnections.TryGetValue(portName, out var connections))
            {
                continue;
            }

            var index = 0;
            foreach (var conn in connections.Where(conn => rowVars.ContainsKey(conn.DeviceId)))
            {
                var offset = GetTerminalOffset(conn, graph, horizontalPassiveIds);
                var terminalYExpr =
                    rowVars[conn.DeviceId] * DeviceGeometry.CellHeight
                    + baseTerminalY
                    + (int)
                        Math.Round(
                            offset.DeltaRow * DeviceGeometry.CellHeight,
                            MidpointRounding.AwayFromZero
                        );
                var penalty = model.NewIntVar(
                    0,
                    canvasHeight,
                    $"portLane_{Sanitize(portName)}_{index}"
                );
                model.AddAbsEquality(penalty, portYVar - terminalYExpr);
                objectives.Add(penalty * PortLaneWeight);
                index++;
            }
        }
    }

    private static void AddPortBiasObjectives(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> columnVars,
        CircuitGraph graph,
        int columnCount,
        List<LinearExpr> objectives
    )
    {
        var (inputDistance, outputDistance) = ComputePortDistances(graph);
        foreach (var deviceId in columnVars.Keys)
        {
            var input = inputDistance.GetValueOrDefault(deviceId, int.MaxValue / 2);
            var output = outputDistance.GetValueOrDefault(deviceId, int.MaxValue / 2);
            if (input < output)
            {
                objectives.Add(columnVars[deviceId] * PortBiasWeight);
            }
            else if (output < input)
            {
                objectives.Add((columnCount - 1 - columnVars[deviceId]) * PortBiasWeight);
            }
        }
    }

    private static void AddSymmetryObjectives(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> columnVars,
        IReadOnlyList<string> deviceIds,
        TopologyResult topology,
        IReadOnlyList<(string Left, string Right, string PivotNet)> symmetricPassivePairs,
        int axis,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlySet<string> hardPlacedDeviceIds,
        List<LinearExpr> objectives
    )
    {
        var inSymmetricGroup = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in topology.SymmetricGroups)
        {
            foreach (var deviceId in group.DeviceIds)
            {
                inSymmetricGroup.Add(deviceId);
            }

            if (
                group.DeviceIds.Count != 2
                || !columnVars.ContainsKey(group.DeviceIds[0])
                || !columnVars.ContainsKey(group.DeviceIds[1])
            )
            {
                continue;
            }

            var penalty = model.NewIntVar(
                0,
                axis * 4 + 4,
                $"sym_{group.DeviceIds[0]}_{group.DeviceIds[1]}"
            );
            model.AddAbsEquality(
                penalty,
                columnVars[group.DeviceIds[0]] + columnVars[group.DeviceIds[1]] - 2 * axis
            );
            objectives.Add(penalty * SymmetryWeight);
        }

        foreach (var (left, right, _) in symmetricPassivePairs)
        {
            if (!columnVars.ContainsKey(left) || !columnVars.ContainsKey(right))
            {
                continue;
            }

            var penalty = model.NewIntVar(0, axis * 4 + 4, $"sympassive_{left}_{right}");
            model.AddAbsEquality(penalty, columnVars[left] + columnVars[right] - 2 * axis);
            objectives.Add(penalty * SymmetryWeight);
        }

        foreach (var deviceId in deviceIds)
        {
            if (
                inSymmetricGroup.Contains(deviceId)
                || horizontalPassiveIds.Contains(deviceId)
                || hardPlacedDeviceIds.Contains(deviceId)
            )
            {
                continue;
            }

            var penalty = model.NewIntVar(0, axis + 2, $"center_{deviceId}");
            model.AddAbsEquality(penalty, columnVars[deviceId] - axis);
            objectives.Add(penalty * CenterAxisWeight);
        }
    }

    private static void AddHorizontalPassiveColumnConstraints(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> columnVars,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlySet<string> hardPlacedDeviceIds,
        int symmetryAxis
    )
    {
        foreach (var deviceId in horizontalPassiveIds)
        {
            if (hardPlacedDeviceIds.Contains(deviceId) || !columnVars.ContainsKey(deviceId))
            {
                continue;
            }

            var distance = model.NewIntVar(0, symmetryAxis, $"passiveDist_{deviceId}");
            model.AddAbsEquality(distance, columnVars[deviceId] - symmetryAxis);
            model.Add(distance == 1);
        }
    }

    private static void AddMosfetEdgeColumnConstraints(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> columnVars,
        IReadOnlyList<SymmetricGroup> groups,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlySet<string> hardPlacedDeviceIds,
        int symmetryAxis
    )
    {
        foreach (var deviceId in groups.SelectMany(group => group.DeviceIds).Distinct())
        {
            if (
                horizontalPassiveIds.Contains(deviceId)
                || hardPlacedDeviceIds.Contains(deviceId)
                || !columnVars.ContainsKey(deviceId)
            )
            {
                continue;
            }

            var distance = model.NewIntVar(0, symmetryAxis, $"mosDist_{deviceId}");
            model.AddAbsEquality(distance, columnVars[deviceId] - symmetryAxis);
            model.Add(distance == symmetryAxis);
        }
    }

    private static void AddRailEdgeClearanceConstraints(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> rowVars,
        IReadOnlyDictionary<string, IntVar> columnVars,
        CircuitGraph graph,
        IReadOnlySet<string> horizontalPassiveIds
    )
    {
        var deviceIds = rowVars.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        foreach (var deviceId in deviceIds)
        {
            if (!graph.Devices.TryGetValue(deviceId, out var device))
            {
                continue;
            }

            foreach (var (terminal, netName) in device.Bindings)
            {
                if (!graph.IsSupplyOrGround(netName))
                {
                    continue;
                }

                var offset = GetTerminalOffset(
                    new TerminalRef(deviceId, terminal),
                    graph,
                    horizontalPassiveIds
                );
                var blocksAbove = graph.Supplies.Contains(netName) && offset.DeltaRow < 0;
                var blocksBelow = graph.Grounds.Contains(netName) && offset.DeltaRow > 0;
                if (!blocksAbove && !blocksBelow)
                {
                    continue;
                }

                foreach (var otherDeviceId in deviceIds.Where(other => other != deviceId))
                {
                    var sameColumn = model.NewBoolVar(
                        $"sameCol_{Sanitize(deviceId)}_{Sanitize(terminal)}_{Sanitize(otherDeviceId)}"
                    );
                    model
                        .Add(columnVars[deviceId] == columnVars[otherDeviceId])
                        .OnlyEnforceIf(sameColumn);
                    model
                        .Add(columnVars[deviceId] != columnVars[otherDeviceId])
                        .OnlyEnforceIf(sameColumn.Not());

                    if (blocksAbove)
                    {
                        model
                            .Add(rowVars[otherDeviceId] >= rowVars[deviceId])
                            .OnlyEnforceIf(sameColumn);
                    }

                    if (blocksBelow)
                    {
                        model
                            .Add(rowVars[otherDeviceId] <= rowVars[deviceId])
                            .OnlyEnforceIf(sameColumn);
                    }
                }
            }
        }
    }

    private static void AddCenterDeviceConstraints(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> columnVars,
        IReadOnlyList<string> deviceIds,
        IReadOnlyList<SymmetricGroup> groups,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlySet<string> hardPlacedDeviceIds,
        int symmetryAxis
    )
    {
        var groupedDevices = groups
            .SelectMany(group => group.DeviceIds)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var deviceId in deviceIds)
        {
            if (
                groupedDevices.Contains(deviceId)
                || horizontalPassiveIds.Contains(deviceId)
                || hardPlacedDeviceIds.Contains(deviceId)
            )
            {
                continue;
            }

            model.Add(columnVars[deviceId] == symmetryAxis);
        }
    }

    private static void AddRailProximityObjectives(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> rowVars,
        CircuitGraph graph,
        IReadOnlySet<string> railConnectedVerticalPassiveIds,
        IReadOnlySet<string> horizontalPassiveIds,
        int canvasHeight,
        List<LinearExpr> objectives
    )
    {
        var baseTerminalY = DeviceGeometry.RailMargin + DeviceGeometry.CellHeight / 2;
        foreach (var deviceId in railConnectedVerticalPassiveIds)
        {
            var device = graph.Devices[deviceId];
            foreach (var (terminal, netName) in device.Bindings)
            {
                if (!graph.IsSupplyOrGround(netName))
                {
                    continue;
                }

                var offset = GetTerminalOffset(
                    new TerminalRef(deviceId, terminal),
                    graph,
                    horizontalPassiveIds
                );
                var terminalY =
                    rowVars[deviceId] * DeviceGeometry.CellHeight
                    + baseTerminalY
                    + (int)
                        Math.Round(
                            offset.DeltaRow * DeviceGeometry.CellHeight,
                            MidpointRounding.AwayFromZero
                        );
                var railY = graph.Supplies.Contains(netName)
                    ? DeviceGeometry.RailMargin / 2
                    : canvasHeight - DeviceGeometry.RailMargin / 2;
                var penalty = model.NewIntVar(0, canvasHeight, $"rail_{deviceId}_{terminal}");
                model.AddAbsEquality(penalty, terminalY - railY);
                objectives.Add(penalty * RailTerminalWeight);
            }
        }
    }

    private static void AddCompactnessObjective(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> columnVars,
        int columnCount,
        List<LinearExpr> objectives
    )
    {
        var maxColumn = model.NewIntVar(0, columnCount - 1, "compact_max");
        var minColumn = model.NewIntVar(0, columnCount - 1, "compact_min");
        model.AddMaxEquality(maxColumn, columnVars.Values);
        model.AddMinEquality(minColumn, columnVars.Values);
        objectives.Add((maxColumn - minColumn) * CompactnessWeight);
    }

    private static Dictionary<string, GridCell> Compact(
        IReadOnlyDictionary<string, GridCell> placements
    )
    {
        var rowMap = placements
            .Values.Select(cell => cell.Row)
            .Distinct()
            .Order()
            .Select((row, index) => (row, index))
            .ToDictionary(pair => pair.row, pair => pair.index);
        var columnMap = placements
            .Values.Select(cell => cell.Column)
            .Distinct()
            .Order()
            .Select((column, index) => (column, index))
            .ToDictionary(pair => pair.column, pair => pair.index);
        return placements.ToDictionary(
            kv => kv.Key,
            kv => new GridCell(
                rowMap[kv.Value.Row],
                columnMap[kv.Value.Column],
                kv.Value.RotationQuarterTurns,
                kv.Value.MirrorX,
                kv.Value.MirrorY
            ),
            StringComparer.Ordinal
        );
    }

    private static Dictionary<string, GridCell> ApplyOrientationRules(
        IReadOnlyDictionary<string, GridCell> placements,
        CircuitGraph graph,
        TopologyResult topology,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlyList<(string Left, string Right, string PivotNet)> symmetricPassivePairs
    )
    {
        var result = new Dictionary<string, GridCell>(StringComparer.Ordinal);
        var columnCount = placements.Values.Select(cell => cell.Column).Distinct().Count();
        var axisPlacement = new CoarseGridResult
        {
            RowCount = 1,
            ColumnCount = columnCount,
            DevicePlacements = placements,
            SymmetryAxis = Math.Max(0, columnCount / 2),
            HorizontalPassiveIds = horizontalPassiveIds,
        };
        var diffPairRoles = topology
            .SymmetricGroups.Where(group =>
                group.Type == SymmetryType.DiffPair && group.DeviceIds.Count == 2
            )
            .Select(group =>
                DetermineLeftRightByInputPort(group.DeviceIds[0], group.DeviceIds[1], graph)
            )
            .ToList();
        var inwardFacingGroups = topology
            .SymmetricGroups.Where(group =>
                group.Type is SymmetryType.CurrentMirror or SymmetryType.LoadPair
            )
            .SelectMany(group => group.DeviceIds)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (deviceId, placement) in placements)
        {
            if (horizontalPassiveIds.Contains(deviceId))
            {
                var passiveMirrorX = DetermineHorizontalPassiveMirrorX(
                    deviceId,
                    placement,
                    axisPlacement,
                    graph
                );
                result[deviceId] = new GridCell(
                    placement.Row,
                    placement.Column,
                    rotationQuarterTurns: 1,
                    mirrorX: passiveMirrorX
                );
                continue;
            }

            if (!IsMosDevice(graph.Devices[deviceId].DeviceType))
            {
                result[deviceId] = placement;
                continue;
            }

            var mirrorX = DetermineGateFacing(
                deviceId,
                placement,
                placements,
                graph,
                diffPairRoles,
                inwardFacingGroups
            );
            result[deviceId] = new GridCell(placement.Row, placement.Column, mirrorX);
        }

        return result;
    }

    private static bool DetermineHorizontalPassiveMirrorX(
        string deviceId,
        GridCell placement,
        CoarseGridResult axisPlacement,
        CircuitGraph graph
    )
    {
        var pNet = graph.GetNetForTerminal(deviceId, "P");
        var nNet = graph.GetNetForTerminal(deviceId, "N");
        var pPrefersLeft = pNet != null && NetPrefersLeft(pNet, graph);
        var nPrefersLeft = nNet != null && NetPrefersLeft(nNet, graph);
        if (pPrefersLeft && !nPrefersLeft)
        {
            return false;
        }

        if (nPrefersLeft && !pPrefersLeft)
        {
            return true;
        }

        return !PlacementAxis.IsLeftOfAxis(axisPlacement, placement.Column);
    }

    private static bool DetermineGateFacing(
        string deviceId,
        GridCell placement,
        IReadOnlyDictionary<string, GridCell> placements,
        CircuitGraph graph,
        IReadOnlyList<(string Left, string Right)> diffPairRoles,
        IReadOnlySet<string> inwardFacingGroups
    )
    {
        foreach (var (left, right) in diffPairRoles)
        {
            if (deviceId == left)
            {
                return false;
            }

            if (deviceId == right)
            {
                return true;
            }
        }

        var gateNet = graph.GetNetForTerminal(deviceId, "G");
        if (
            gateNet != null
            && (graph.InputPorts.Contains(gateNet) || graph.BiasPorts.Contains(gateNet))
        )
        {
            return false;
        }

        if (gateNet != null && graph.NetConnections.TryGetValue(gateNet, out var connections))
        {
            var filtered = connections
                .Where(conn => !IsIgnoredPlacementTerminal(conn.Terminal))
                .ToList();
            if (
                filtered.Count == 2
                && filtered.Count(conn =>
                    conn.DeviceId == deviceId
                    && conn.Terminal.Equals("G", StringComparison.OrdinalIgnoreCase)
                ) == 1
            )
            {
                var other = filtered.Single(conn =>
                    conn.DeviceId != deviceId
                    || !conn.Terminal.Equals("G", StringComparison.OrdinalIgnoreCase)
                );
                if (
                    other.DeviceId != deviceId
                    && placements.TryGetValue(other.DeviceId, out var otherPlacement)
                )
                {
                    if (otherPlacement.Column > placement.Column)
                    {
                        return true;
                    }

                    if (otherPlacement.Column < placement.Column)
                    {
                        return false;
                    }
                }
            }
        }

        if (inwardFacingGroups.Contains(deviceId))
        {
            var axisPosition = PlacementAxis.GetAxisPosition(
                placements.Values.Select(cell => cell.Column).Distinct().Count()
            );
            return placement.Column < axisPosition;
        }

        return false;
    }

    private static bool IsIgnoredPlacementTerminal(string terminal)
    {
        return terminal.ToUpperInvariant() is "B" or "BULK" or "BODY" or "SH" or "SHIELD" or "TAP";
    }

    private static (
        Dictionary<string, int> Input,
        Dictionary<string, int> Output
    ) ComputePortDistances(CircuitGraph graph)
    {
        return (
            BfsPortDistances(graph, graph.InputPorts.Concat(graph.BiasPorts)),
            BfsPortDistances(graph, graph.OutputPorts)
        );
    }

    private static Dictionary<string, int> BfsPortDistances(
        CircuitGraph graph,
        IEnumerable<string> startNets
    )
    {
        var queue = new Queue<(string Net, int Depth)>();
        var seenNets = new HashSet<string>(StringComparer.Ordinal);
        var seenDevices = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var net in startNets)
        {
            if (seenNets.Add(net))
            {
                queue.Enqueue((net, 0));
            }
        }

        while (queue.Count > 0)
        {
            var (net, depth) = queue.Dequeue();
            if (!graph.NetConnections.TryGetValue(net, out var connections))
            {
                continue;
            }

            foreach (var connection in connections)
            {
                if (
                    !seenDevices.TryGetValue(connection.DeviceId, out var current)
                    || depth < current
                )
                {
                    seenDevices[connection.DeviceId] = depth;
                }

                var device = graph.Devices[connection.DeviceId];
                foreach (var nextNet in device.Bindings.Values)
                {
                    if (graph.IsSupplyOrGround(nextNet) || !seenNets.Add(nextNet))
                    {
                        continue;
                    }

                    queue.Enqueue((nextNet, depth + 1));
                }
            }
        }

        return seenDevices;
    }

    private static CoarseGridResult BuildFallbackPlacement(
        TopologyResult topology,
        CircuitGraph graph,
        IReadOnlySet<string> horizontalPassiveIds
    )
    {
        var byRow = topology.DeviceRows.GroupBy(kv => kv.Value).OrderBy(group => group.Key);
        var placements = new Dictionary<string, GridCell>(StringComparer.Ordinal);
        foreach (var (rowIndex, row) in byRow.Select((group, rowIndex) => (rowIndex, group)))
        {
            foreach (
                var (columnIndex, device) in row.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select((kv, columnIndex) => (columnIndex, kv))
            )
            {
                placements[device.Key] = new GridCell(rowIndex, columnIndex);
            }
        }

        var repaired = RepairHardConstraintViolations(
            Compact(placements),
            graph,
            topology,
            horizontalPassiveIds,
            Array.Empty<(string Left, string Right, string PivotNet)>()
        );
        var compacted = ApplyOrientationRules(
            repaired,
            graph,
            topology,
            horizontalPassiveIds,
            Array.Empty<(string Left, string Right, string PivotNet)>()
        );
        var rowCount = Math.Max(1, compacted.Values.Select(cell => cell.Row).Distinct().Count());
        var columnCount = Math.Max(
            1,
            compacted.Values.Select(cell => cell.Column).Distinct().Count()
        );
        var symmetryAxis = Math.Max(0, columnCount / 2);
        return new CoarseGridResult
        {
            RowCount = rowCount,
            ColumnCount = columnCount,
            DevicePlacements = compacted,
            SymmetryAxis = symmetryAxis,
            HorizontalPassiveIds = horizontalPassiveIds,
            PortYHints = ComputePortYHints(
                new CoarseGridResult
                {
                    RowCount = rowCount,
                    ColumnCount = columnCount,
                    DevicePlacements = compacted,
                    SymmetryAxis = symmetryAxis,
                    HorizontalPassiveIds = horizontalPassiveIds,
                },
                graph
            ),
        };
    }

    private static Dictionary<string, GridCell> RepairHardConstraintViolations(
        IReadOnlyDictionary<string, GridCell> placements,
        CircuitGraph graph,
        TopologyResult topology,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlyList<(string Left, string Right, string PivotNet)> symmetricPassivePairs
    )
    {
        var repaired = placements.ToDictionary(
            kv => kv.Key,
            kv => new GridCell(kv.Value.Row, kv.Value.Column),
            StringComparer.Ordinal
        );
        var groupMembers = topology
            .SymmetricGroups.SelectMany(group => group.DeviceIds)
            .ToHashSet(StringComparer.Ordinal);

        for (var iteration = 0; iteration < placements.Count * 4; iteration++)
        {
            repaired = Compact(repaired);
            var oriented = ApplyOrientationRules(
                repaired,
                graph,
                topology,
                horizontalPassiveIds,
                symmetricPassivePairs
            );

            if (TryMoveSplitSymmetricGroupToFreshRow(repaired, topology))
            {
                continue;
            }

            if (
                TryMoveRailEdgeViolatorToFreshColumn(
                    repaired,
                    graph,
                    horizontalPassiveIds,
                    groupMembers
                )
            )
            {
                continue;
            }

            return repaired;
        }

        return Compact(repaired);
    }

    private static bool TryMoveSplitSymmetricGroupToFreshRow(
        Dictionary<string, GridCell> placements,
        TopologyResult topology
    )
    {
        foreach (var group in topology.SymmetricGroups)
        {
            var members = group.DeviceIds.Where(placements.ContainsKey).ToList();
            if (members.Count < 2)
            {
                continue;
            }

            var distinctRows = members.Select(member => placements[member].Row).Distinct().ToList();
            if (distinctRows.Count == 1)
            {
                continue;
            }

            var freshRow = placements.Values.Max(existing => existing.Row) + 1;
            foreach (var member in members)
            {
                placements[member] = new GridCell(freshRow, placements[member].Column);
            }

            return true;
        }

        return false;
    }

    private static bool TryMoveRailEdgeViolatorToFreshColumn(
        Dictionary<string, GridCell> placements,
        CircuitGraph graph,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlySet<string> groupMembers
    )
    {
        foreach (
            var (deviceId, cell) in placements
                .OrderBy(kv => kv.Value.Row)
                .ThenBy(kv => kv.Value.Column)
        )
        {
            if (
                groupMembers.Contains(deviceId)
                || !graph.Devices.TryGetValue(deviceId, out var device)
            )
            {
                continue;
            }

            foreach (var (terminal, netName) in device.Bindings)
            {
                if (!graph.IsSupplyOrGround(netName))
                {
                    continue;
                }

                var offset = GetTerminalOffset(
                    new TerminalRef(deviceId, terminal),
                    graph,
                    horizontalPassiveIds
                );
                var blocksAbove = graph.Supplies.Contains(netName) && offset.DeltaRow < 0;
                var blocksBelow = graph.Grounds.Contains(netName) && offset.DeltaRow > 0;
                if (!blocksAbove && !blocksBelow)
                {
                    continue;
                }

                var violates = blocksAbove
                    ? placements.Any(kv =>
                        kv.Key != deviceId
                        && kv.Value.Column == cell.Column
                        && kv.Value.Row < cell.Row
                    )
                    : placements.Any(kv =>
                        kv.Key != deviceId
                        && kv.Value.Column == cell.Column
                        && kv.Value.Row > cell.Row
                    );
                if (!violates)
                {
                    continue;
                }

                var freshColumn = placements.Values.Max(existing => existing.Column) + 1;
                placements[deviceId] = new GridCell(cell.Row, freshColumn);
                return true;
            }
        }

        return false;
    }

    private static void AddRailSideOrderingConstraints(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> rowVars,
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

            var pNet = graph.GetNetForTerminal(passiveId, "P");
            var nNet = graph.GetNetForTerminal(passiveId, "N");
            var leansToGround =
                (pNet != null && graph.Grounds.Contains(pNet))
                || (nNet != null && graph.Grounds.Contains(nNet));
            var leansToSupply =
                (pNet != null && graph.Supplies.Contains(pNet))
                || (nNet != null && graph.Supplies.Contains(nNet));
            foreach (var net in new[] { pNet, nNet })
            {
                if (
                    net == null
                    || graph.IsSupplyOrGround(net)
                    || !graph.NetConnections.TryGetValue(net, out var connections)
                )
                {
                    continue;
                }

                foreach (
                    var conn in connections.Where(conn =>
                        conn.DeviceId != passiveId && rowVars.ContainsKey(conn.DeviceId)
                    )
                )
                {
                    if (leansToGround)
                    {
                        model.Add(rowVars[passiveId] >= rowVars[conn.DeviceId] + 1);
                    }
                    else if (leansToSupply)
                    {
                        model.Add(rowVars[passiveId] <= rowVars[conn.DeviceId] - 1);
                    }
                }
            }
        }
    }

    private static bool IsPassiveConnectedToRail(CircuitGraph graph, string deviceId)
    {
        var device = graph.Devices.GetValueOrDefault(deviceId);
        if (device == null || !IsPassive(device.DeviceType))
        {
            return false;
        }

        return device.Bindings.Values.Any(graph.IsSupplyOrGround);
    }

    private static bool IsPassive(string deviceType)
    {
        var normalized = deviceType.ToLowerInvariant();
        return normalized is "resistor" or "capacitor" or "inductor";
    }

    private static bool IsMosDevice(string deviceType)
    {
        var normalized = deviceType.ToLowerInvariant();
        return normalized is "nmos" or "nfet" or "pmos" or "pfet";
    }

    private static (string Left, string Right) DetermineLeftRightByInputPort(
        string first,
        string second,
        CircuitGraph graph
    )
    {
        var firstGate = graph.GetNetForTerminal(first, "G");
        var secondGate = graph.GetNetForTerminal(second, "G");
        var firstPositive =
            firstGate != null && graph.InputPorts.Contains(firstGate) && IsPositiveInput(firstGate);
        var secondPositive =
            secondGate != null
            && graph.InputPorts.Contains(secondGate)
            && IsPositiveInput(secondGate);
        if (firstPositive && !secondPositive)
        {
            return (first, second);
        }

        if (secondPositive && !firstPositive)
        {
            return (second, first);
        }

        return string.Compare(first, second, StringComparison.Ordinal) <= 0
            ? (first, second)
            : (second, first);
    }

    private static bool IsPositiveInput(string netName)
    {
        return netName.EndsWith("_P", StringComparison.OrdinalIgnoreCase)
            || netName.EndsWith(".P", StringComparison.OrdinalIgnoreCase)
            || netName.EndsWith("+", StringComparison.Ordinal);
    }

    private static (string Left, string Right) DetermineLeftRightByNaming(
        string first,
        string second
    )
    {
        var firstIsLeft = IsLeftSideName(first);
        var secondIsLeft = IsLeftSideName(second);
        if (firstIsLeft && !secondIsLeft)
        {
            return (first, second);
        }

        if (secondIsLeft && !firstIsLeft)
        {
            return (second, first);
        }

        return string.Compare(first, second, StringComparison.Ordinal) <= 0
            ? (first, second)
            : (second, first);
    }

    private static bool IsLeftSideName(string deviceId)
    {
        return deviceId.EndsWith("_P", StringComparison.OrdinalIgnoreCase)
            || deviceId.EndsWith(".P", StringComparison.OrdinalIgnoreCase)
            || deviceId.Contains("_P.", StringComparison.OrdinalIgnoreCase)
            || deviceId.EndsWith("+", StringComparison.Ordinal);
    }

    private static string Sanitize(string value)
    {
        return new string(value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
    }

    private static List<int> GetConnectedDeviceRows(
        string passiveId,
        CircuitGraph graph,
        TopologyResult topology
    )
    {
        var rows = new List<int>();
        foreach (
            var terminalNet in new[]
            {
                graph.GetNetForTerminal(passiveId, "P"),
                graph.GetNetForTerminal(passiveId, "N"),
            }
        )
        {
            if (
                terminalNet == null
                || graph.IsSupplyOrGround(terminalNet)
                || !graph.NetConnections.TryGetValue(terminalNet, out var connections)
            )
            {
                continue;
            }

            foreach (var connection in connections)
            {
                if (
                    connection.DeviceId != passiveId
                    && topology.DeviceRows.TryGetValue(connection.DeviceId, out var row)
                )
                {
                    rows.Add(row);
                }
            }
        }

        return rows;
    }

    private static List<int> ComputeValidFillRowsForPassive(
        string passiveId,
        IReadOnlySet<int> fillRowsAfterTopoRow,
        int[] fillRowOffsets,
        CircuitGraph graph,
        TopologyResult topology
    )
    {
        var validRows = new List<int>();
        var connectedRows = GetConnectedDeviceRows(passiveId, graph, topology);
        if (connectedRows.Count >= 2)
        {
            var minRow = connectedRows.Min();
            if (fillRowsAfterTopoRow.Contains(minRow) && minRow < fillRowOffsets.Length - 1)
            {
                validRows.Add(minRow + fillRowOffsets[minRow] + 1);
            }
        }
        else if (connectedRows.Count == 1)
        {
            var row = connectedRows[0];
            if (fillRowsAfterTopoRow.Contains(row) && row < fillRowOffsets.Length - 1)
            {
                validRows.Add(row + fillRowOffsets[row] + 1);
            }
        }

        if (validRows.Count == 0)
        {
            foreach (var topoRow in fillRowsAfterTopoRow)
            {
                if (topoRow < fillRowOffsets.Length - 1)
                {
                    validRows.Add(topoRow + fillRowOffsets[topoRow] + 1);
                }
            }
        }

        return validRows.Distinct().ToList();
    }

    private static HashSet<int> ComputeFillRowPositions(
        IReadOnlySet<string> horizontalPassiveIds,
        TopologyResult topology,
        CircuitGraph graph
    )
    {
        var fillRows = new HashSet<int>();
        foreach (var passiveId in horizontalPassiveIds)
        {
            var connectedRows = GetConnectedDeviceRows(passiveId, graph, topology);
            if (connectedRows.Count == 0)
            {
                continue;
            }

            fillRows.Add(connectedRows.Min());
        }

        return fillRows;
    }

    private static IReadOnlyDictionary<string, int> ComputePortYHints(
        CoarseGridResult placement,
        CircuitGraph graph
    )
    {
        var terminalYByNet = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var (deviceId, cell) in placement.DevicePlacements)
        {
            if (!graph.Devices.TryGetValue(deviceId, out var device))
            {
                continue;
            }

            if (IsMosDevice(device.DeviceType))
            {
                var mos = DeviceGeometry.GetMosfetPlacement(cell.Row, cell.Column, cell.MirrorX);
                var isPmos =
                    device.DeviceType.Equals("pmos", StringComparison.OrdinalIgnoreCase)
                    || device.DeviceType.Equals("pfet", StringComparison.OrdinalIgnoreCase);
                AddTerminalY(deviceId, "G", mos.GateY);
                AddTerminalY(deviceId, "D", isPmos ? mos.SourceY : mos.DrainY);
                AddTerminalY(deviceId, "S", isPmos ? mos.DrainY : mos.SourceY);
                continue;
            }

            if (!IsPassive(device.DeviceType))
            {
                continue;
            }

            if (placement.HorizontalPassiveIds.Contains(deviceId))
            {
                var passive = DeviceGeometry.GetHorizontalPassivePlacement(
                    cell.Row,
                    cell.Column,
                    placement.ColumnCount,
                    PlacementAxis.IsLeftOfAxis(placement, cell.Column)
                );
                AddTerminalY(deviceId, "P", passive.PY);
                AddTerminalY(deviceId, "N", passive.NY);
            }
            else
            {
                var passive = DeviceGeometry.GetPassivePlacement(cell.Row, cell.Column);
                AddTerminalY(deviceId, "P", passive.PY);
                AddTerminalY(deviceId, "N", passive.NY);
            }
        }

        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        var usedYs = new List<int>();
        foreach (var port in graph.InputPorts.Concat(graph.BiasPorts).Concat(graph.OutputPorts))
        {
            if (!terminalYByNet.TryGetValue(port, out var ys) || ys.Count == 0)
            {
                continue;
            }

            var y = (int)ys.Average();
            while (usedYs.Any(existing => Math.Abs(existing - y) < 15))
            {
                y += 15;
            }

            result[port] = y;
            usedYs.Add(y);
        }

        return result;

        void AddTerminalY(string deviceId, string terminal, int y)
        {
            var net = graph.GetNetForTerminal(deviceId, terminal);
            if (net == null)
            {
                return;
            }

            if (!terminalYByNet.TryGetValue(net, out var list))
            {
                list = new List<int>();
                terminalYByNet[net] = list;
            }

            list.Add(y);
        }
    }

    private static IReadOnlyDictionary<string, int> ApplyFeedthroughPortHints(
        IReadOnlyDictionary<string, int> baseHints,
        CoarseGridResult placement,
        CircuitGraph graph
    )
    {
        var result = new Dictionary<string, int>(baseHints, StringComparer.Ordinal);
        var feedthroughPairs = new List<(string LeftPort, string RightPort, int BaseY)>();
        foreach (var (deviceId, device) in graph.Devices)
        {
            if (
                !IsPassive(device.DeviceType)
                || !placement.DevicePlacements.TryGetValue(deviceId, out var cell)
            )
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

            var pIsLeft = graph.InputPorts.Contains(pNet) || graph.BiasPorts.Contains(pNet);
            var nIsLeft = graph.InputPorts.Contains(nNet) || graph.BiasPorts.Contains(nNet);
            var pIsRight = graph.OutputPorts.Contains(pNet);
            var nIsRight = graph.OutputPorts.Contains(nNet);
            if (!(pIsLeft && nIsRight) && !(nIsLeft && pIsRight))
            {
                continue;
            }

            var y = placement.HorizontalPassiveIds.Contains(deviceId)
                ? DeviceGeometry
                    .GetHorizontalPassivePlacement(
                        cell.Row,
                        cell.Column,
                        placement.ColumnCount,
                        PlacementAxis.IsLeftOfAxis(placement, cell.Column)
                    )
                    .PY
                : DeviceGeometry.GetPassivePlacement(cell.Row, cell.Column).PY;
            feedthroughPairs.Add((pIsLeft ? pNet : nNet, pIsRight ? pNet : nNet, y));
        }

        var usedYs = new List<int>();
        foreach (
            var (leftPort, rightPort, baseY) in feedthroughPairs
                .OrderBy(pair => pair.BaseY)
                .ThenBy(pair => IsPositivePortName(pair.LeftPort) ? 0 : 1)
                .ThenBy(pair => pair.LeftPort, StringComparer.Ordinal)
        )
        {
            var y = baseY;
            while (usedYs.Any(existing => Math.Abs(existing - y) < 15))
            {
                y += 15;
            }

            result[leftPort] = y;
            result[rightPort] = y;
            usedYs.Add(y);
        }

        return result;
    }

    private static bool IsPositivePortName(string portName)
    {
        return portName.EndsWith(".P", StringComparison.OrdinalIgnoreCase)
            || portName.EndsWith("_P", StringComparison.OrdinalIgnoreCase)
            || portName.EndsWith("+", StringComparison.Ordinal);
    }

    private static bool NetPrefersLeft(string netName, CircuitGraph graph)
    {
        return graph.InputPorts.Contains(netName)
            || graph.BiasPorts.Contains(netName)
            || IsPositivePortName(netName);
    }
}
