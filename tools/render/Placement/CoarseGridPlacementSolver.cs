namespace Cascode.Render.Placement;

using Cascode.Language;
using Cascode.Render.Analysis;
using Cascode.Render.Layout;
using Cascode.Render.OrTools;
using Cascode.Render.Routing;
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
    private const int GateDriverPassiveDistanceWeight = 18;
    private const int GateDriverPassiveSideWeight = 22;
    private const int OutputCouplingPassiveDistanceWeight = 20;
    private const int OutputCouplingPassiveSideWeight = 28;
    private const int BiasPassiveClusterSpanWeight = 36;
    private const int BiasPassiveConsumerWeight = 18;
    private const int InlinePassiveChainShuntSpanWeight = 20;
    private const int DirectGateBiasConsumerWeightMultiplier = 3;
    private const int GateDriverBiasConsumerWeightMultiplier = 2;

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
        AddPointToPointDrainSourceStackConstraints(model, columnVars, graph);
        AddMixedPolarityDrainAlignmentConstraints(model, columnVars, graph);
        AddSupplyLoadAlignmentConstraints(model, columnVars, graph);
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
                estimatedAxis,
                graph
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
        AddGateDriverPassiveObjectives(model, columnVars, graph, estimatedColumns, objectives);
        AddInlinePassiveChainShuntObjectives(
            model,
            columnVars,
            graph,
            estimatedColumns,
            objectives
        );
        AddOutputCouplingPassiveObjectives(
            model,
            columnVars,
            graph,
            estimatedColumns,
            estimatedAxis,
            horizontalPassiveIds,
            objectives
        );
        AddBiasPassiveClusterObjectives(model, columnVars, graph, estimatedColumns, objectives);
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
        var refined = RefineGateDriverPassivePlacements(
            repaired,
            graph,
            topology,
            horizontalPassiveIds,
            symmetricPassivePairs,
            hardPlacedIds
        );
        var inlineRefined = RefineInlinePassiveChainPlacements(
            refined,
            graph,
            topology,
            horizontalPassiveIds,
            symmetricPassivePairs,
            hardPlacedIds
        );
        var biasRefined = RefineBiasPassiveClusterPlacements(
            inlineRefined,
            graph,
            topology,
            horizontalPassiveIds,
            symmetricPassivePairs,
            hardPlacedIds
        );
        var orderedBiasRefined = RefineBiasPassiveClusterOrderingPlacements(
            biasRefined,
            graph,
            topology,
            horizontalPassiveIds,
            symmetricPassivePairs,
            hardPlacedIds
        );
        var biasChainRefined = RefineBiasPassiveChainPlacements(
            orderedBiasRefined,
            graph,
            topology,
            horizontalPassiveIds,
            symmetricPassivePairs,
            hardPlacedIds
        );
        var gateBiasDistributionRefined = RefineGateBiasDistributionPlacements(
            biasChainRefined,
            graph,
            topology,
            horizontalPassiveIds,
            symmetricPassivePairs,
            hardPlacedIds
        );
        var outputCouplingRefined = RefineOutputCouplingPassivePlacements(
            gateBiasDistributionRefined,
            graph,
            topology,
            horizontalPassiveIds,
            symmetricPassivePairs,
            hardPlacedIds
        );
        var oriented = ApplyOrientationRules(
            outputCouplingRefined,
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
        var basePlacement = new CoarseGridResult
        {
            RowCount = rowCount,
            ColumnCount = columnCount,
            DevicePlacements = oriented,
            SymmetryAxis = symmetryAxis,
            HorizontalPassiveIds = horizontalPassiveIds,
        };
        var portYHints = ApplyFeedthroughPortHints(solvedPortYHints, basePlacement, graph);
        return new CoarseGridResult
        {
            RowCount = rowCount,
            ColumnCount = columnCount,
            DevicePlacements = oriented,
            SymmetryAxis = symmetryAxis,
            HorizontalPassiveIds = horizontalPassiveIds,
            PortYHints = AvoidBlockedStraightPortHints(basePlacement, graph, portYHints),
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

    private static void AddPointToPointDrainSourceStackConstraints(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> columnVars,
        CircuitGraph graph
    )
    {
        foreach (var pair in FindPointToPointDrainSourcePairs(graph))
        {
            if (
                !columnVars.ContainsKey(pair.SourceDeviceId)
                || !columnVars.ContainsKey(pair.DrainDeviceId)
            )
            {
                continue;
            }

            model.Add(columnVars[pair.SourceDeviceId] == columnVars[pair.DrainDeviceId]);
        }
    }

    private static void AddMixedPolarityDrainAlignmentConstraints(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> columnVars,
        CircuitGraph graph
    )
    {
        foreach (var pair in FindMixedPolarityDrainPairs(graph))
        {
            if (
                !columnVars.ContainsKey(pair.PmosDeviceId)
                || !columnVars.ContainsKey(pair.NmosDeviceId)
            )
            {
                continue;
            }

            model.Add(columnVars[pair.PmosDeviceId] == columnVars[pair.NmosDeviceId]);
        }
    }

    private static void AddSupplyLoadAlignmentConstraints(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> columnVars,
        CircuitGraph graph
    )
    {
        foreach (var pair in FindSupplyConnectedMosLoadPairs(graph))
        {
            if (
                !columnVars.ContainsKey(pair.LoadDeviceId)
                || !columnVars.ContainsKey(pair.MosDeviceId)
            )
            {
                continue;
            }

            model.Add(columnVars[pair.LoadDeviceId] == columnVars[pair.MosDeviceId]);
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
                        : DetermineLeftRightByPartnerDrainAlignment(
                            members[0],
                            members[1],
                            groups,
                            graph
                        ) ?? DetermineLeftRightByNaming(members[0], members[1]);
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
        int symmetryAxis,
        CircuitGraph graph
    )
    {
        var exemptPassiveIds = GetFlowDirectedGatePassiveIds(graph)
            .ToHashSet(StringComparer.Ordinal);
        exemptPassiveIds.UnionWith(
            FindOutputCouplingPassivePairs(graph).Select(pair => pair.PassiveDeviceId)
        );
        foreach (var deviceId in horizontalPassiveIds)
        {
            if (
                hardPlacedDeviceIds.Contains(deviceId)
                || exemptPassiveIds.Contains(deviceId)
                || !columnVars.ContainsKey(deviceId)
            )
            {
                continue;
            }

            var distance = model.NewIntVar(0, symmetryAxis, $"passiveDist_{deviceId}");
            model.AddAbsEquality(distance, columnVars[deviceId] - symmetryAxis);
            model.Add(distance == 1);
        }
    }

    private static void AddOutputCouplingPassiveObjectives(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> columnVars,
        CircuitGraph graph,
        int columnCount,
        int symmetryAxis,
        IReadOnlySet<string> horizontalPassiveIds,
        List<LinearExpr> objectives
    )
    {
        foreach (var pair in FindOutputCouplingPassivePairs(graph))
        {
            if (
                !columnVars.TryGetValue(pair.PassiveDeviceId, out var passiveColumn)
                || !columnVars.TryGetValue(pair.MosDeviceId, out var mosColumn)
            )
            {
                continue;
            }

            var distancePenalty = model.NewIntVar(
                0,
                columnCount - 1,
                $"outputCouplingDist_{Sanitize(pair.PassiveDeviceId)}_{Sanitize(pair.MosDeviceId)}"
            );
            model.AddAbsEquality(distancePenalty, passiveColumn - mosColumn);
            objectives.Add(distancePenalty * OutputCouplingPassiveDistanceWeight);

            var sidePreference = GetOutputCouplingSidePreference(pair, horizontalPassiveIds);
            if (sidePreference == SignalSidePreference.None)
            {
                continue;
            }

            var sidePenalty = model.NewIntVar(
                0,
                columnCount - 1,
                $"outputCouplingSide_{Sanitize(pair.PassiveDeviceId)}"
            );
            var zero = model.NewConstant(0);
            if (sidePreference == SignalSidePreference.Left)
            {
                model.AddMaxEquality(sidePenalty, [passiveColumn - symmetryAxis, zero]);
            }
            else
            {
                model.AddMaxEquality(sidePenalty, [symmetryAxis - passiveColumn, zero]);
            }

            objectives.Add(sidePenalty * OutputCouplingPassiveSideWeight);
        }
    }

    private static void AddGateDriverPassiveObjectives(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> columnVars,
        CircuitGraph graph,
        int columnCount,
        List<LinearExpr> objectives
    )
    {
        var flowDirectedGatePassiveIds = GetFlowDirectedGatePassiveIds(graph);
        var inputNetDistances = BfsNetDistances(graph, graph.InputPorts.Concat(graph.BiasPorts));
        var outputNetDistances = BfsNetDistances(graph, graph.OutputPorts);
        foreach (var pair in FindGatePassiveLinks(graph))
        {
            if (!flowDirectedGatePassiveIds.Contains(pair.PassiveDeviceId))
            {
                continue;
            }

            if (
                !columnVars.TryGetValue(pair.PassiveDeviceId, out var passiveColumn)
                || !columnVars.TryGetValue(pair.MosDeviceId, out var mosColumn)
            )
            {
                continue;
            }

            var distancePenalty = model.NewIntVar(
                0,
                columnCount - 1,
                $"gatePassiveDist_{Sanitize(pair.PassiveDeviceId)}_{Sanitize(pair.MosDeviceId)}"
            );
            model.AddAbsEquality(distancePenalty, passiveColumn - mosColumn);
            objectives.Add(distancePenalty * GateDriverPassiveDistanceWeight);

            var sidePreference = GetGatePassiveSidePreference(
                pair.PassiveOtherNet,
                graph,
                inputNetDistances,
                outputNetDistances
            );
            if (sidePreference == SignalSidePreference.None)
            {
                continue;
            }

            var sidePenalty = model.NewIntVar(
                0,
                columnCount - 1,
                $"gatePassiveSide_{Sanitize(pair.PassiveDeviceId)}_{Sanitize(pair.MosDeviceId)}"
            );
            var zero = model.NewConstant(0);
            if (sidePreference == SignalSidePreference.Left)
            {
                model.AddMaxEquality(sidePenalty, [passiveColumn - mosColumn, zero]);
            }
            else
            {
                model.AddMaxEquality(sidePenalty, [mosColumn - passiveColumn, zero]);
            }

            objectives.Add(sidePenalty * GateDriverPassiveSideWeight);
        }
    }

    private static void AddBiasPassiveClusterObjectives(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> columnVars,
        CircuitGraph graph,
        int columnCount,
        List<LinearExpr> objectives
    )
    {
        foreach (var cluster in FindRailConnectedPassiveClusters(graph))
        {
            var members = cluster
                .DeviceIds.Where(columnVars.ContainsKey)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            if (members.Length < 2)
            {
                continue;
            }

            var maxColumn = model.NewIntVar(
                0,
                columnCount - 1,
                $"biasClusterMax_{Sanitize(cluster.NetName)}"
            );
            var minColumn = model.NewIntVar(
                0,
                columnCount - 1,
                $"biasClusterMin_{Sanitize(cluster.NetName)}"
            );
            model.AddMaxEquality(maxColumn, members.Select(id => columnVars[id]));
            model.AddMinEquality(minColumn, members.Select(id => columnVars[id]));
            objectives.Add((maxColumn - minColumn) * BiasPassiveClusterSpanWeight);

            foreach (var consumerId in GetBiasClusterConsumerIds(cluster, graph))
            {
                if (!columnVars.TryGetValue(consumerId, out var consumerColumn))
                {
                    continue;
                }

                foreach (var memberId in members)
                {
                    var penalty = model.NewIntVar(
                        0,
                        columnCount - 1,
                        $"biasClusterConsumer_{Sanitize(cluster.NetName)}_{Sanitize(memberId)}_{Sanitize(consumerId)}"
                    );
                    model.AddAbsEquality(penalty, columnVars[memberId] - consumerColumn);
                    objectives.Add(
                        penalty
                            * BiasPassiveConsumerWeight
                            * GetBiasConsumerPriorityWeight([cluster.NetName], consumerId, graph)
                    );
                }
            }
        }
    }

    private static void AddInlinePassiveChainShuntObjectives(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> columnVars,
        CircuitGraph graph,
        int columnCount,
        List<LinearExpr> objectives
    )
    {
        var inputNetDistances = BfsNetDistances(graph, graph.InputPorts.Concat(graph.BiasPorts));
        var outputNetDistances = BfsNetDistances(graph, graph.OutputPorts);
        var zero = model.NewConstant(0);
        foreach (
            var pair in FindInlinePassiveChainPairs(graph, inputNetDistances, outputNetDistances)
        )
        {
            if (
                !columnVars.TryGetValue(pair.UpstreamPassiveId, out var upstreamColumn)
                || !columnVars.TryGetValue(pair.DownstreamPassiveId, out var downstreamColumn)
            )
            {
                continue;
            }

            var minColumn = model.NewIntVar(
                0,
                columnCount - 1,
                $"inlineShuntMin_{Sanitize(pair.JunctionNet)}"
            );
            var maxColumn = model.NewIntVar(
                0,
                columnCount - 1,
                $"inlineShuntMax_{Sanitize(pair.JunctionNet)}"
            );
            model.AddMinEquality(minColumn, [upstreamColumn, downstreamColumn]);
            model.AddMaxEquality(maxColumn, [upstreamColumn, downstreamColumn]);

            foreach (var shuntId in FindRailConnectedPassiveIdsOnNet(pair.JunctionNet, graph))
            {
                if (!columnVars.TryGetValue(shuntId, out var shuntColumn))
                {
                    continue;
                }

                var leftPenalty = model.NewIntVar(
                    0,
                    columnCount - 1,
                    $"inlineShuntLeft_{Sanitize(shuntId)}"
                );
                var rightPenalty = model.NewIntVar(
                    0,
                    columnCount - 1,
                    $"inlineShuntRight_{Sanitize(shuntId)}"
                );
                model.AddMaxEquality(leftPenalty, [minColumn - shuntColumn, zero]);
                model.AddMaxEquality(rightPenalty, [shuntColumn - maxColumn, zero]);
                objectives.Add((leftPenalty + rightPenalty) * InlinePassiveChainShuntSpanWeight);
            }
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
        var outputCouplingOutputTerminals = FindOutputCouplingPassivePairs(graph)
            .ToDictionary(
                pair => pair.PassiveDeviceId,
                pair =>
                    pair.PassiveSignalTerminal.Equals("P", StringComparison.OrdinalIgnoreCase)
                        ? "N"
                        : "P",
                StringComparer.Ordinal
            );
        var inputNetDistances = BfsNetDistances(graph, graph.InputPorts.Concat(graph.BiasPorts));
        var outputNetDistances = BfsNetDistances(graph, graph.OutputPorts);

        foreach (var (deviceId, placement) in placements)
        {
            if (horizontalPassiveIds.Contains(deviceId))
            {
                var passiveMirrorX = DetermineHorizontalPassiveMirrorX(
                    deviceId,
                    placement,
                    axisPlacement,
                    graph,
                    outputCouplingOutputTerminals,
                    inputNetDistances,
                    outputNetDistances
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
        CircuitGraph graph,
        IReadOnlyDictionary<string, string> outputCouplingOutputTerminals,
        IReadOnlyDictionary<string, int> inputNetDistances,
        IReadOnlyDictionary<string, int> outputNetDistances
    )
    {
        var pNet = graph.GetNetForTerminal(deviceId, "P");
        var nNet = graph.GetNetForTerminal(deviceId, "N");
        if (outputCouplingOutputTerminals.TryGetValue(deviceId, out var outputTerminal))
        {
            return outputTerminal.Equals("P", StringComparison.OrdinalIgnoreCase);
        }

        var gatePassiveLink = FindGatePassiveLinks(graph)
            .FirstOrDefault(link =>
                string.Equals(link.PassiveDeviceId, deviceId, StringComparison.Ordinal)
            );
        if (gatePassiveLink is not null)
        {
            var otherTerminal = string.Equals(
                graph.GetNetForTerminal(deviceId, "P"),
                gatePassiveLink.PassiveOtherNet,
                StringComparison.Ordinal
            )
                ? "P"
                : "N";
            var sidePreference = GetGatePassiveSidePreference(
                gatePassiveLink.PassiveOtherNet,
                graph,
                inputNetDistances,
                outputNetDistances
            );
            if (
                IsGateBiasDistributionNet(
                    gatePassiveLink.PassiveOtherNet,
                    gatePassiveLink.PassiveDeviceId,
                    graph
                )
                && axisPlacement.DevicePlacements.TryGetValue(
                    gatePassiveLink.MosDeviceId,
                    out var gatedMosCell
                )
            )
            {
                var gateTerminal = otherTerminal == "P" ? "N" : "P";
                var mosIsOnOrLeftOfPassive = gatedMosCell.Column <= placement.Column;
                return mosIsOnOrLeftOfPassive ? gateTerminal == "N" : gateTerminal == "P";
            }

            if (sidePreference == SignalSidePreference.Left)
            {
                return otherTerminal != "P";
            }

            if (sidePreference == SignalSidePreference.Right)
            {
                return otherTerminal == "P";
            }
        }

        var pSidePreference =
            pNet == null
                ? SignalSidePreference.None
                : GetNetSidePreference(pNet, graph, inputNetDistances, outputNetDistances);
        var nSidePreference =
            nNet == null
                ? SignalSidePreference.None
                : GetNetSidePreference(nNet, graph, inputNetDistances, outputNetDistances);
        if (
            pSidePreference == SignalSidePreference.Left
            && nSidePreference != SignalSidePreference.Left
        )
        {
            return false;
        }

        if (
            nSidePreference == SignalSidePreference.Left
            && pSidePreference != SignalSidePreference.Left
        )
        {
            return true;
        }

        if (
            pSidePreference == SignalSidePreference.Right
            && nSidePreference != SignalSidePreference.Right
        )
        {
            return true;
        }

        if (
            nSidePreference == SignalSidePreference.Right
            && pSidePreference != SignalSidePreference.Right
        )
        {
            return false;
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

    private static Dictionary<string, int> BfsNetDistances(
        CircuitGraph graph,
        IEnumerable<string> startNets
    )
    {
        var queue = new Queue<(string Net, int Depth)>();
        var seenNets = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var net in startNets.Distinct(StringComparer.Ordinal))
        {
            seenNets[net] = 0;
            queue.Enqueue((net, 0));
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
                if (!graph.Devices.TryGetValue(connection.DeviceId, out var device))
                {
                    continue;
                }

                foreach (var nextNet in device.Bindings.Values)
                {
                    if (graph.IsSupplyOrGround(nextNet))
                    {
                        continue;
                    }

                    var nextDepth = depth + 1;
                    if (
                        seenNets.TryGetValue(nextNet, out var currentDepth)
                        && currentDepth <= nextDepth
                    )
                    {
                        continue;
                    }

                    seenNets[nextNet] = nextDepth;
                    queue.Enqueue((nextNet, nextDepth));
                }
            }
        }

        return seenNets;
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
        var refined = RefineGateDriverPassivePlacements(
            repaired,
            graph,
            topology,
            horizontalPassiveIds,
            Array.Empty<(string Left, string Right, string PivotNet)>(),
            new HashSet<string>(StringComparer.Ordinal)
        );
        var inlineRefined = RefineInlinePassiveChainPlacements(
            refined,
            graph,
            topology,
            horizontalPassiveIds,
            Array.Empty<(string Left, string Right, string PivotNet)>(),
            new HashSet<string>(StringComparer.Ordinal)
        );
        var biasRefined = RefineBiasPassiveClusterPlacements(
            inlineRefined,
            graph,
            topology,
            horizontalPassiveIds,
            Array.Empty<(string Left, string Right, string PivotNet)>(),
            new HashSet<string>(StringComparer.Ordinal)
        );
        var orderedBiasRefined = RefineBiasPassiveClusterOrderingPlacements(
            biasRefined,
            graph,
            topology,
            horizontalPassiveIds,
            Array.Empty<(string Left, string Right, string PivotNet)>(),
            new HashSet<string>(StringComparer.Ordinal)
        );
        var biasChainRefined = RefineBiasPassiveChainPlacements(
            orderedBiasRefined,
            graph,
            topology,
            horizontalPassiveIds,
            Array.Empty<(string Left, string Right, string PivotNet)>(),
            new HashSet<string>(StringComparer.Ordinal)
        );
        var gateBiasDistributionRefined = RefineGateBiasDistributionPlacements(
            biasChainRefined,
            graph,
            topology,
            horizontalPassiveIds,
            Array.Empty<(string Left, string Right, string PivotNet)>(),
            new HashSet<string>(StringComparer.Ordinal)
        );
        var outputCouplingRefined = RefineOutputCouplingPassivePlacements(
            gateBiasDistributionRefined,
            graph,
            topology,
            horizontalPassiveIds,
            Array.Empty<(string Left, string Right, string PivotNet)>(),
            new HashSet<string>(StringComparer.Ordinal)
        );
        var compacted = ApplyOrientationRules(
            outputCouplingRefined,
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
        var basePlacement = new CoarseGridResult
        {
            RowCount = rowCount,
            ColumnCount = columnCount,
            DevicePlacements = compacted,
            SymmetryAxis = symmetryAxis,
            HorizontalPassiveIds = horizontalPassiveIds,
        };
        var portYHints = ApplyFeedthroughPortHints(
            ComputePortYHints(basePlacement, graph),
            basePlacement,
            graph
        );
        return new CoarseGridResult
        {
            RowCount = rowCount,
            ColumnCount = columnCount,
            DevicePlacements = compacted,
            SymmetryAxis = symmetryAxis,
            HorizontalPassiveIds = horizontalPassiveIds,
            PortYHints = AvoidBlockedStraightPortHints(basePlacement, graph, portYHints),
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

            if (TryRepairPointToPointDrainSourceStackViolation(repaired, topology, graph))
            {
                continue;
            }

            if (TryRepairMixedPolarityDrainAlignmentViolation(repaired, topology, graph))
            {
                continue;
            }

            if (TryRepairSupplyLoadAlignmentViolation(repaired, topology, graph))
            {
                continue;
            }

            if (
                TryRepairStraightConnectionViolation(
                    repaired,
                    graph,
                    topology,
                    horizontalPassiveIds,
                    symmetricPassivePairs
                )
            )
            {
                continue;
            }

            return repaired;
        }

        return Compact(repaired);
    }

    private static Dictionary<string, GridCell> RefineGateDriverPassivePlacements(
        IReadOnlyDictionary<string, GridCell> placements,
        CircuitGraph graph,
        TopologyResult topology,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlyList<(string Left, string Right, string PivotNet)> symmetricPassivePairs,
        IReadOnlySet<string> hardPlacedDeviceIds
    )
    {
        var refined = placements.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        var flowDirectedGatePassiveIds = GetFlowDirectedGatePassiveIds(graph);
        var inputNetDistances = BfsNetDistances(graph, graph.InputPorts.Concat(graph.BiasPorts));
        var outputNetDistances = BfsNetDistances(graph, graph.OutputPorts);
        var fillRowsAfterTopoRow = ComputeFillRowPositions(horizontalPassiveIds, topology, graph);
        var fillRowOffsets = BuildFillRowOffsets(topology.DeviceRows.Values, fillRowsAfterTopoRow);
        foreach (var pair in FindGatePassiveLinks(graph))
        {
            if (
                hardPlacedDeviceIds.Contains(pair.PassiveDeviceId)
                || !horizontalPassiveIds.Contains(pair.PassiveDeviceId)
                || !flowDirectedGatePassiveIds.Contains(pair.PassiveDeviceId)
            )
            {
                continue;
            }

            var sidePreference = GetGatePassiveSidePreference(
                pair.PassiveOtherNet,
                graph,
                inputNetDistances,
                outputNetDistances
            );
            if (sidePreference == SignalSidePreference.None)
            {
                continue;
            }

            TryRefineGateDriverPassivePlacement(
                refined,
                pair,
                sidePreference,
                graph,
                topology,
                horizontalPassiveIds,
                symmetricPassivePairs,
                fillRowsAfterTopoRow,
                fillRowOffsets
            );
        }

        return Compact(refined);
    }

    private static Dictionary<string, GridCell> RefineInlinePassiveChainPlacements(
        IReadOnlyDictionary<string, GridCell> placements,
        CircuitGraph graph,
        TopologyResult topology,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlyList<(string Left, string Right, string PivotNet)> symmetricPassivePairs,
        IReadOnlySet<string> hardPlacedDeviceIds
    )
    {
        var refined = placements.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        var inputNetDistances = BfsNetDistances(graph, graph.InputPorts.Concat(graph.BiasPorts));
        var outputNetDistances = BfsNetDistances(graph, graph.OutputPorts);
        var fillRowsAfterTopoRow = ComputeFillRowPositions(horizontalPassiveIds, topology, graph);
        var fillRowOffsets = BuildFillRowOffsets(topology.DeviceRows.Values, fillRowsAfterTopoRow);
        foreach (
            var pair in FindInlinePassiveChainPairs(graph, inputNetDistances, outputNetDistances)
        )
        {
            if (
                hardPlacedDeviceIds.Contains(pair.UpstreamPassiveId)
                || !horizontalPassiveIds.Contains(pair.UpstreamPassiveId)
                || !horizontalPassiveIds.Contains(pair.DownstreamPassiveId)
            )
            {
                continue;
            }

            TryRefineInlinePassiveChainPlacement(
                refined,
                pair,
                graph,
                topology,
                horizontalPassiveIds,
                symmetricPassivePairs,
                hardPlacedDeviceIds,
                fillRowsAfterTopoRow,
                fillRowOffsets
            );
        }

        return Compact(refined);
    }

    private static bool TryRefineGateDriverPassivePlacement(
        Dictionary<string, GridCell> placements,
        GatePassiveLink pair,
        SignalSidePreference sidePreference,
        CircuitGraph graph,
        TopologyResult topology,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlyList<(string Left, string Right, string PivotNet)> symmetricPassivePairs,
        IReadOnlySet<int> fillRowsAfterTopoRow,
        int[] fillRowOffsets
    )
    {
        if (
            !placements.TryGetValue(pair.PassiveDeviceId, out var passiveCell)
            || !placements.TryGetValue(pair.MosDeviceId, out var mosCell)
        )
        {
            return false;
        }

        var currentScore = ScoreGateDriverPassivePlacement(
            placements,
            pair,
            sidePreference,
            graph,
            topology,
            horizontalPassiveIds,
            symmetricPassivePairs
        );
        foreach (
            var row in GetGateDriverCandidateRows(
                pair.PassiveDeviceId,
                pair.MosDeviceId,
                placements,
                graph,
                topology,
                horizontalPassiveIds,
                fillRowsAfterTopoRow,
                fillRowOffsets
            )
        )
        {
            foreach (var column in GetGateDriverCandidateColumns(mosCell.Column, sidePreference))
            {
                if (row == passiveCell.Row && column == passiveCell.Column)
                {
                    continue;
                }

                if (
                    placements.Any(kv =>
                        kv.Key != pair.PassiveDeviceId
                        && kv.Value.Row == row
                        && kv.Value.Column == column
                    )
                )
                {
                    continue;
                }

                var candidateCell = new GridCell(
                    row,
                    column,
                    passiveCell.RotationQuarterTurns,
                    passiveCell.MirrorX,
                    passiveCell.MirrorY
                );
                placements[pair.PassiveDeviceId] = candidateCell;
                var candidateScore = ScoreGateDriverPassivePlacement(
                    placements,
                    pair,
                    sidePreference,
                    graph,
                    topology,
                    horizontalPassiveIds,
                    symmetricPassivePairs
                );
                placements[pair.PassiveDeviceId] = passiveCell;
                if (!IsBetterGateDriverPassivePlacement(currentScore, candidateScore))
                {
                    continue;
                }

                placements[pair.PassiveDeviceId] = candidateCell;
                if (
                    IsValidPostRepairPlacement(
                        placements,
                        graph,
                        topology,
                        horizontalPassiveIds,
                        symmetricPassivePairs
                    )
                )
                {
                    return true;
                }

                placements[pair.PassiveDeviceId] = passiveCell;
            }
        }

        return false;
    }

    private static bool TryRefineInlinePassiveChainPlacement(
        Dictionary<string, GridCell> placements,
        InlinePassiveChainPair pair,
        CircuitGraph graph,
        TopologyResult topology,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlyList<(string Left, string Right, string PivotNet)> symmetricPassivePairs,
        IReadOnlySet<string> hardPlacedDeviceIds,
        IReadOnlySet<int> fillRowsAfterTopoRow,
        int[] fillRowOffsets
    )
    {
        if (
            !placements.TryGetValue(pair.UpstreamPassiveId, out var upstreamCell)
            || !placements.TryGetValue(pair.DownstreamPassiveId, out var downstreamCell)
        )
        {
            return false;
        }

        var currentScore = ScoreInlinePassiveChainPlacement(
            upstreamCell,
            downstreamCell,
            pair.UpstreamSidePreference
        );
        foreach (
            var row in GetInlinePassiveChainCandidateRows(
                pair.UpstreamPassiveId,
                upstreamCell,
                downstreamCell,
                graph,
                topology,
                horizontalPassiveIds,
                fillRowsAfterTopoRow,
                fillRowOffsets
            )
        )
        {
            foreach (
                var column in GetInlinePassiveChainCandidateColumns(
                    downstreamCell.Column,
                    pair.UpstreamSidePreference,
                    Math.Max(
                        placements.Values.Max(cell => cell.Column) + 2,
                        downstreamCell.Column + 2
                    )
                )
            )
            {
                if (row == upstreamCell.Row && column == upstreamCell.Column)
                {
                    continue;
                }

                if (
                    placements.Any(kv =>
                        kv.Key != pair.UpstreamPassiveId
                        && kv.Value.Row == row
                        && kv.Value.Column == column
                    )
                )
                {
                    continue;
                }

                var candidateCell = new GridCell(
                    row,
                    column,
                    upstreamCell.RotationQuarterTurns,
                    upstreamCell.MirrorX,
                    upstreamCell.MirrorY
                );
                var candidateScore = ScoreInlinePassiveChainPlacement(
                    candidateCell,
                    downstreamCell,
                    pair.UpstreamSidePreference
                );
                if (!IsBetterInlinePassiveChainPlacement(currentScore, candidateScore))
                {
                    continue;
                }

                if (
                    TryCommitInlinePassiveChainPlacement(
                        placements,
                        pair,
                        graph,
                        topology,
                        horizontalPassiveIds,
                        symmetricPassivePairs,
                        hardPlacedDeviceIds,
                        upstreamCell,
                        candidateCell
                    )
                )
                {
                    return true;
                }
            }
        }

        return TryResolveInlinePassiveChainRailShuntPlacement(
            placements,
            pair,
            graph,
            topology,
            horizontalPassiveIds,
            symmetricPassivePairs,
            hardPlacedDeviceIds,
            upstreamCell
        );
    }

    private static bool TryCommitInlinePassiveChainPlacement(
        Dictionary<string, GridCell> placements,
        InlinePassiveChainPair pair,
        CircuitGraph graph,
        TopologyResult topology,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlyList<(string Left, string Right, string PivotNet)> symmetricPassivePairs,
        IReadOnlySet<string> hardPlacedDeviceIds,
        GridCell upstreamOriginalCell,
        GridCell upstreamCandidateCell
    )
    {
        placements[pair.UpstreamPassiveId] = upstreamCandidateCell;
        if (
            IsValidPostRepairPlacement(
                placements,
                graph,
                topology,
                horizontalPassiveIds,
                symmetricPassivePairs
            )
        )
        {
            TryResolveInlinePassiveChainRailShuntPlacement(
                placements,
                pair,
                graph,
                topology,
                horizontalPassiveIds,
                symmetricPassivePairs,
                hardPlacedDeviceIds,
                upstreamCandidateCell
            );
            return true;
        }

        if (
            TryResolveInlinePassiveChainRailShuntPlacement(
                placements,
                pair,
                graph,
                topology,
                horizontalPassiveIds,
                symmetricPassivePairs,
                hardPlacedDeviceIds,
                upstreamCandidateCell
            )
        )
        {
            return true;
        }

        placements[pair.UpstreamPassiveId] = upstreamOriginalCell;
        return false;
    }

    private static bool TryResolveInlinePassiveChainRailShuntPlacement(
        Dictionary<string, GridCell> placements,
        InlinePassiveChainPair pair,
        CircuitGraph graph,
        TopologyResult topology,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlyList<(string Left, string Right, string PivotNet)> symmetricPassivePairs,
        IReadOnlySet<string> hardPlacedDeviceIds,
        GridCell upstreamCandidateCell
    )
    {
        if (!placements.TryGetValue(pair.DownstreamPassiveId, out var downstreamCell))
        {
            return false;
        }

        var improved = false;
        foreach (
            var shuntId in FindRailConnectedPassiveIdsOnNet(pair.JunctionNet, graph)
                .Where(id =>
                    !hardPlacedDeviceIds.Contains(id)
                    && id != pair.UpstreamPassiveId
                    && id != pair.DownstreamPassiveId
                    && placements.ContainsKey(id)
                )
        )
        {
            var shuntCell = placements[shuntId];
            var railNet = GetRailConnectedNet(shuntId, graph);
            if (railNet == null)
            {
                continue;
            }

            var towardGround = graph.Grounds.Contains(railNet);
            var bestCell = shuntCell;
            var bestScore = ScoreInlinePassiveChainRailPassivePlacement(
                shuntCell,
                upstreamCandidateCell,
                downstreamCell,
                towardGround
            );
            foreach (
                var row in GetInlinePassiveChainRailPassiveCandidateRows(
                    shuntCell,
                    upstreamCandidateCell,
                    downstreamCell,
                    towardGround
                )
            )
            {
                foreach (
                    var column in GetInlinePassiveChainRailPassiveCandidateColumns(
                        shuntCell,
                        upstreamCandidateCell,
                        downstreamCell
                    )
                )
                {
                    if (row == shuntCell.Row && column == shuntCell.Column)
                    {
                        continue;
                    }

                    if (
                        placements.Any(kv =>
                            kv.Key != shuntId && kv.Value.Row == row && kv.Value.Column == column
                        )
                    )
                    {
                        continue;
                    }

                    var candidateCell = new GridCell(
                        row,
                        column,
                        shuntCell.RotationQuarterTurns,
                        shuntCell.MirrorX,
                        shuntCell.MirrorY
                    );
                    placements[shuntId] = candidateCell;
                    if (
                        IsValidPostRepairPlacement(
                            placements,
                            graph,
                            topology,
                            horizontalPassiveIds,
                            symmetricPassivePairs
                        )
                    )
                    {
                        var candidateScore = ScoreInlinePassiveChainRailPassivePlacement(
                            candidateCell,
                            upstreamCandidateCell,
                            downstreamCell,
                            towardGround
                        );
                        if (candidateScore.CompareTo(bestScore) < 0)
                        {
                            bestCell = candidateCell;
                            bestScore = candidateScore;
                        }
                    }

                    placements[shuntId] = shuntCell;
                }
            }

            if (!bestCell.Equals(shuntCell))
            {
                placements[shuntId] = bestCell;
                improved = true;
            }
        }

        return improved;
    }

    private static IReadOnlyList<int> GetGateDriverCandidateRows(
        string passiveId,
        string mosId,
        IReadOnlyDictionary<string, GridCell> placements,
        CircuitGraph graph,
        TopologyResult topology,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlySet<int> fillRowsAfterTopoRow,
        int[] fillRowOffsets
    )
    {
        var rows = new List<int>();
        if (placements.TryGetValue(passiveId, out var passiveCell))
        {
            rows.Add(passiveCell.Row);
        }

        if (
            horizontalPassiveIds.Contains(passiveId)
            && placements.TryGetValue(mosId, out var mosCell)
        )
        {
            rows.AddRange(
                ComputeValidFillRowsForPassive(
                        passiveId,
                        fillRowsAfterTopoRow,
                        fillRowOffsets,
                        graph,
                        topology
                    )
                    .OrderBy(row => Math.Abs(row - mosCell.Row))
            );
        }

        return rows.Distinct().ToList();
    }

    private static IEnumerable<int> GetGateDriverCandidateColumns(
        int mosColumn,
        SignalSidePreference sidePreference
    )
    {
        if (sidePreference == SignalSidePreference.Left)
        {
            for (var column = mosColumn; column >= 0; column--)
            {
                yield return column;
            }

            yield break;
        }

        for (var column = mosColumn; column <= mosColumn + 8; column++)
        {
            yield return column;
        }
    }

    private static IReadOnlyList<int> GetInlinePassiveChainCandidateRows(
        string upstreamPassiveId,
        GridCell upstreamCell,
        GridCell downstreamCell,
        CircuitGraph graph,
        TopologyResult topology,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlySet<int> fillRowsAfterTopoRow,
        int[] fillRowOffsets
    )
    {
        var rows = new List<int> { downstreamCell.Row, upstreamCell.Row };
        if (horizontalPassiveIds.Contains(upstreamPassiveId))
        {
            rows.AddRange(
                ComputeValidFillRowsForPassive(
                        upstreamPassiveId,
                        fillRowsAfterTopoRow,
                        fillRowOffsets,
                        graph,
                        topology
                    )
                    .OrderBy(row => Math.Abs(row - downstreamCell.Row))
            );
        }

        return rows.Distinct().ToList();
    }

    private static IEnumerable<int> GetInlinePassiveChainCandidateColumns(
        int downstreamColumn,
        SignalSidePreference upstreamSidePreference,
        int searchLimit
    )
    {
        if (upstreamSidePreference == SignalSidePreference.Left)
        {
            for (var column = downstreamColumn - 1; column >= 0; column--)
            {
                yield return column;
            }

            yield break;
        }

        if (upstreamSidePreference == SignalSidePreference.Right)
        {
            for (var column = downstreamColumn + 1; column <= searchLimit; column++)
            {
                yield return column;
            }
        }
    }

    private static IReadOnlyList<int> GetInlinePassiveChainRailPassiveCandidateRows(
        GridCell shuntCell,
        GridCell upstreamCell,
        GridCell downstreamCell,
        bool towardGround
    )
    {
        var anchorRow = towardGround
            ? Math.Max(Math.Max(shuntCell.Row, upstreamCell.Row), downstreamCell.Row)
            : Math.Min(Math.Min(shuntCell.Row, upstreamCell.Row), downstreamCell.Row);
        var rows = new List<int> { shuntCell.Row };
        if (towardGround)
        {
            rows.Add(anchorRow + 1);
            rows.Add(anchorRow + 2);
            rows.Add(anchorRow + 3);
        }
        else
        {
            rows.Add(anchorRow - 1);
            rows.Add(anchorRow - 2);
            rows.Add(anchorRow - 3);
        }

        return rows.Distinct().ToList();
    }

    private static IReadOnlyList<int> GetInlinePassiveChainRailPassiveCandidateColumns(
        GridCell shuntCell,
        GridCell upstreamCell,
        GridCell downstreamCell
    )
    {
        var minColumn = Math.Min(upstreamCell.Column, downstreamCell.Column);
        var maxColumn = Math.Max(upstreamCell.Column, downstreamCell.Column);
        var spanCenter = (minColumn + maxColumn) / 2.0;
        return Enumerable
            .Range(minColumn, maxColumn - minColumn + 1)
            .Append(shuntCell.Column)
            .Distinct()
            .OrderBy(column => column < minColumn || column > maxColumn ? 1 : 0)
            .ThenBy(column => Math.Abs(column - spanCenter))
            .ThenBy(column => Math.Abs(column - shuntCell.Column))
            .ToList();
    }

    private static (
        int OutsideSpanDistance,
        int DirectionPenalty,
        int Movement
    ) ScoreInlinePassiveChainRailPassivePlacement(
        GridCell shuntCell,
        GridCell upstreamCell,
        GridCell downstreamCell,
        bool towardGround
    )
    {
        var minColumn = Math.Min(upstreamCell.Column, downstreamCell.Column);
        var maxColumn = Math.Max(upstreamCell.Column, downstreamCell.Column);
        var anchorRow = towardGround
            ? Math.Max(upstreamCell.Row, downstreamCell.Row)
            : Math.Min(upstreamCell.Row, downstreamCell.Row);
        var outsideSpanDistance =
            shuntCell.Column < minColumn ? minColumn - shuntCell.Column
            : shuntCell.Column > maxColumn ? shuntCell.Column - maxColumn
            : 0;
        var directionPenalty = towardGround
            ? Math.Max(0, anchorRow - shuntCell.Row)
            : Math.Max(0, shuntCell.Row - anchorRow);
        var movement =
            Math.Abs(shuntCell.Column - minColumn)
            + Math.Abs(shuntCell.Column - maxColumn)
            + Math.Abs(shuntCell.Row - anchorRow);
        return (outsideSpanDistance, directionPenalty, movement);
    }

    private static (
        int WrongSide,
        int TerminalDistance,
        int RowDistance
    ) ScoreGateDriverPassivePlacement(
        IReadOnlyDictionary<string, GridCell> placements,
        GatePassiveLink pair,
        SignalSidePreference sidePreference,
        CircuitGraph graph,
        TopologyResult topology,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlyList<(string Left, string Right, string PivotNet)> symmetricPassivePairs
    )
    {
        var oriented = ApplyOrientationRules(
            placements,
            graph,
            topology,
            horizontalPassiveIds,
            symmetricPassivePairs
        );
        var snapshot = BuildPlacementSnapshot(oriented, horizontalPassiveIds);
        var passiveCell = placements[pair.PassiveDeviceId];
        var mosCell = placements[pair.MosDeviceId];
        var passiveGateTerminal = string.Equals(
            graph.GetNetForTerminal(pair.PassiveDeviceId, "P"),
            pair.PassiveOtherNet,
            StringComparison.Ordinal
        )
            ? "N"
            : "P";
        var passiveTerminalX = GetTerminalX(
            snapshot,
            graph,
            pair.PassiveDeviceId,
            passiveGateTerminal
        );
        var mosGateX = GetTerminalX(snapshot, graph, pair.MosDeviceId, "G");
        var wrongSide = sidePreference switch
        {
            SignalSidePreference.Left when passiveTerminalX > mosGateX => 1,
            SignalSidePreference.Right when passiveTerminalX < mosGateX => 1,
            _ => 0,
        };
        return (
            wrongSide,
            Math.Abs(passiveTerminalX - mosGateX),
            Math.Abs(passiveCell.Row - mosCell.Row)
        );
    }

    private static bool IsBetterGateDriverPassivePlacement(
        (int WrongSide, int TerminalDistance, int RowDistance) current,
        (int WrongSide, int TerminalDistance, int RowDistance) candidate
    )
    {
        return candidate.WrongSide < current.WrongSide
            || (
                candidate.WrongSide == current.WrongSide
                && (
                    candidate.TerminalDistance < current.TerminalDistance
                    || (
                        candidate.TerminalDistance == current.TerminalDistance
                        && candidate.RowDistance < current.RowDistance
                    )
                )
            );
    }

    private static (
        int WrongSide,
        int RowDistance,
        int ColumnGap,
        int Movement
    ) ScoreInlinePassiveChainPlacement(
        GridCell upstreamCell,
        GridCell downstreamCell,
        SignalSidePreference upstreamSidePreference
    )
    {
        var wrongSide = upstreamSidePreference switch
        {
            SignalSidePreference.Left when upstreamCell.Column >= downstreamCell.Column => 1,
            SignalSidePreference.Right when upstreamCell.Column <= downstreamCell.Column => 1,
            _ => 0,
        };
        var columnGap =
            wrongSide == 0
                ? Math.Max(0, Math.Abs(upstreamCell.Column - downstreamCell.Column) - 1)
                : Math.Abs(upstreamCell.Column - downstreamCell.Column);
        return (
            wrongSide,
            Math.Abs(upstreamCell.Row - downstreamCell.Row),
            columnGap,
            Math.Abs(upstreamCell.Row - downstreamCell.Row)
                + Math.Abs(upstreamCell.Column - downstreamCell.Column)
        );
    }

    private static bool IsBetterInlinePassiveChainPlacement(
        (int WrongSide, int RowDistance, int ColumnGap, int Movement) current,
        (int WrongSide, int RowDistance, int ColumnGap, int Movement) candidate
    )
    {
        return candidate.WrongSide < current.WrongSide
            || (
                candidate.WrongSide == current.WrongSide
                && (
                    candidate.RowDistance < current.RowDistance
                    || (
                        candidate.RowDistance == current.RowDistance
                        && (
                            candidate.ColumnGap < current.ColumnGap
                            || (
                                candidate.ColumnGap == current.ColumnGap
                                && candidate.Movement < current.Movement
                            )
                        )
                    )
                )
            );
    }

    private static SignalSidePreference GetOutputCouplingSidePreference(
        OutputCouplingPassivePair pair,
        IReadOnlySet<string> horizontalPassiveIds
    )
    {
        if (!horizontalPassiveIds.Contains(pair.PassiveDeviceId))
        {
            return SignalSidePreference.None;
        }

        return pair.PassiveSignalTerminal switch
        {
            "P" => SignalSidePreference.Right,
            "N" => SignalSidePreference.Left,
            _ => SignalSidePreference.None,
        };
    }

    private static (
        int Retreat,
        int TerminalDistance,
        int Movement
    ) ScoreOutputCouplingPassivePlacement(
        IReadOnlyDictionary<string, GridCell> placements,
        OutputCouplingPassivePair pair,
        CircuitGraph graph,
        TopologyResult topology,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlyList<(string Left, string Right, string PivotNet)> symmetricPassivePairs,
        GridCell originalPassiveCell
    )
    {
        var compacted = Compact(placements);
        var oriented = ApplyOrientationRules(
            compacted,
            graph,
            topology,
            horizontalPassiveIds,
            symmetricPassivePairs
        );
        var snapshot = BuildPlacementSnapshot(oriented, horizontalPassiveIds);
        var mosX = GetTerminalX(snapshot, graph, pair.MosDeviceId, pair.MosTerminal);
        var signalX = GetTerminalX(
            snapshot,
            graph,
            pair.PassiveDeviceId,
            pair.PassiveSignalTerminal
        );
        var passiveCell = oriented[pair.PassiveDeviceId];
        return (
            Math.Max(0, mosX - signalX),
            Math.Abs(signalX - mosX),
            Math.Abs(passiveCell.Column - originalPassiveCell.Column)
        );
    }

    private static bool IsBetterOutputCouplingPassivePlacement(
        (int Retreat, int TerminalDistance, int Movement) candidate,
        (int Retreat, int TerminalDistance, int Movement) current
    )
    {
        return candidate.Retreat < current.Retreat
            || (
                candidate.Retreat == current.Retreat
                && (
                    candidate.TerminalDistance < current.TerminalDistance
                    || (
                        candidate.TerminalDistance == current.TerminalDistance
                        && candidate.Movement < current.Movement
                    )
                )
            );
    }

    private static int GetTerminalX(
        CoarseGridResult placement,
        CircuitGraph graph,
        string deviceId,
        string terminal
    )
    {
        var cell = placement.DevicePlacements[deviceId];
        var deviceType = graph.Devices[deviceId].DeviceType.ToLowerInvariant();
        if (deviceType is "nmos" or "nfet" or "pmos" or "pfet")
        {
            var mos = DeviceGeometry.GetMosfetPlacement(cell.Row, cell.Column, cell.MirrorX);
            return terminal switch
            {
                "G" => mos.GateX,
                "D" or "S" => mos.AxisX,
                _ => throw new InvalidOperationException(
                    $"Unsupported MOS terminal '{terminal}' for '{deviceId}'."
                ),
            };
        }

        if (deviceType is "resistor" or "capacitor" or "inductor")
        {
            if (placement.HorizontalPassiveIds.Contains(deviceId))
            {
                var horizontal = DeviceGeometry.GetHorizontalPassivePlacement(
                    cell.Row,
                    cell.Column,
                    placement.ColumnCount,
                    pOnLeft: !cell.MirrorX
                );
                return terminal switch
                {
                    "P" => horizontal.PX,
                    "N" => horizontal.NX,
                    _ => throw new InvalidOperationException(
                        $"Unsupported passive terminal '{terminal}' for '{deviceId}'."
                    ),
                };
            }

            var vertical = DeviceGeometry.GetPassivePlacement(cell.Row, cell.Column);
            return terminal switch
            {
                "P" or "N" => vertical.PX,
                _ => throw new InvalidOperationException(
                    $"Unsupported passive terminal '{terminal}' for '{deviceId}'."
                ),
            };
        }

        throw new InvalidOperationException(
            $"Unsupported device type '{deviceType}' for '{deviceId}'."
        );
    }

    private static bool IsValidPostRepairPlacement(
        IReadOnlyDictionary<string, GridCell> placements,
        CircuitGraph graph,
        TopologyResult topology,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlyList<(string Left, string Right, string PivotNet)> symmetricPassivePairs
    )
    {
        var compacted = Compact(placements);
        var oriented = ApplyOrientationRules(
            compacted,
            graph,
            topology,
            horizontalPassiveIds,
            symmetricPassivePairs
        );
        if (HasRailEdgeClearanceViolation(oriented, graph, horizontalPassiveIds))
        {
            return false;
        }

        var snapshot = BuildPlacementSnapshot(oriented, horizontalPassiveIds);
        return FindStraightConnectionViolation(snapshot, graph, portsOnly: false) == null;
    }

    private static bool HasRailEdgeClearanceViolation(
        IReadOnlyDictionary<string, GridCell> placements,
        CircuitGraph graph,
        IReadOnlySet<string> horizontalPassiveIds
    )
    {
        foreach (var (deviceId, cell) in placements)
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
                if (
                    (
                        blocksAbove
                        && placements.Any(kv =>
                            kv.Key != deviceId
                            && kv.Value.Column == cell.Column
                            && kv.Value.Row < cell.Row
                        )
                    )
                    || (
                        blocksBelow
                        && placements.Any(kv =>
                            kv.Key != deviceId
                            && kv.Value.Column == cell.Column
                            && kv.Value.Row > cell.Row
                        )
                    )
                )
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static Dictionary<string, GridCell> RefineBiasPassiveClusterPlacements(
        IReadOnlyDictionary<string, GridCell> placements,
        CircuitGraph graph,
        TopologyResult topology,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlyList<(string Left, string Right, string PivotNet)> symmetricPassivePairs,
        IReadOnlySet<string> hardPlacedDeviceIds
    )
    {
        var refined = placements.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        foreach (var cluster in FindRailConnectedPassiveClusters(graph))
        {
            TryRefineBiasPassiveClusterPlacement(
                refined,
                cluster,
                graph,
                topology,
                horizontalPassiveIds,
                symmetricPassivePairs,
                hardPlacedDeviceIds
            );
        }

        return Compact(refined);
    }

    private static Dictionary<string, GridCell> RefineBiasPassiveChainPlacements(
        IReadOnlyDictionary<string, GridCell> placements,
        CircuitGraph graph,
        TopologyResult topology,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlyList<(string Left, string Right, string PivotNet)> symmetricPassivePairs,
        IReadOnlySet<string> hardPlacedDeviceIds
    )
    {
        var refined = placements.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        foreach (var chain in FindBiasPassiveChains(graph))
        {
            TryRefineBiasPassiveChainPlacement(
                refined,
                chain,
                graph,
                topology,
                horizontalPassiveIds,
                symmetricPassivePairs,
                hardPlacedDeviceIds
            );
        }

        return Compact(refined);
    }

    private static Dictionary<string, GridCell> RefineGateBiasDistributionPlacements(
        IReadOnlyDictionary<string, GridCell> placements,
        CircuitGraph graph,
        TopologyResult topology,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlyList<(string Left, string Right, string PivotNet)> symmetricPassivePairs,
        IReadOnlySet<string> hardPlacedDeviceIds
    )
    {
        var refined = placements.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        foreach (var link in FindGatePassiveLinks(graph))
        {
            if (!IsGateBiasDistributionNet(link.PassiveOtherNet, link.PassiveDeviceId, graph))
            {
                continue;
            }

            TryRefineGateBiasDistributionPlacement(
                refined,
                link,
                graph,
                topology,
                horizontalPassiveIds,
                symmetricPassivePairs,
                hardPlacedDeviceIds
            );
        }

        return Compact(refined);
    }

    private static Dictionary<string, GridCell> RefineBiasPassiveClusterOrderingPlacements(
        IReadOnlyDictionary<string, GridCell> placements,
        CircuitGraph graph,
        TopologyResult topology,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlyList<(string Left, string Right, string PivotNet)> symmetricPassivePairs,
        IReadOnlySet<string> hardPlacedDeviceIds
    )
    {
        var refined = placements.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        var clusters = FindRailConnectedPassiveClusters(graph);
        for (var leftIndex = 0; leftIndex < clusters.Count; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < clusters.Count; rightIndex++)
            {
                TrySwapInvertedBiasClusterWindows(
                    refined,
                    clusters[leftIndex],
                    clusters[rightIndex],
                    graph,
                    topology,
                    horizontalPassiveIds,
                    symmetricPassivePairs,
                    hardPlacedDeviceIds
                );
            }
        }

        return Compact(refined);
    }

    private static Dictionary<string, GridCell> RefineOutputCouplingPassivePlacements(
        IReadOnlyDictionary<string, GridCell> placements,
        CircuitGraph graph,
        TopologyResult topology,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlyList<(string Left, string Right, string PivotNet)> symmetricPassivePairs,
        IReadOnlySet<string> hardPlacedDeviceIds
    )
    {
        var refined = placements.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        foreach (var pair in FindOutputCouplingPassivePairs(graph))
        {
            if (hardPlacedDeviceIds.Contains(pair.PassiveDeviceId))
            {
                continue;
            }

            TryRefineOutputCouplingPassivePlacement(
                refined,
                pair,
                graph,
                topology,
                horizontalPassiveIds,
                symmetricPassivePairs
            );
        }

        return Compact(refined);
    }

    private static bool TryRefineOutputCouplingPassivePlacement(
        Dictionary<string, GridCell> placements,
        OutputCouplingPassivePair pair,
        CircuitGraph graph,
        TopologyResult topology,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlyList<(string Left, string Right, string PivotNet)> symmetricPassivePairs
    )
    {
        if (
            !placements.TryGetValue(pair.PassiveDeviceId, out var passiveCell)
            || !placements.ContainsKey(pair.MosDeviceId)
        )
        {
            return false;
        }

        var bestPlacements = placements.ToDictionary(
            kv => kv.Key,
            kv => kv.Value,
            StringComparer.Ordinal
        );
        var bestScore = ScoreOutputCouplingPassivePlacement(
            bestPlacements,
            pair,
            graph,
            topology,
            horizontalPassiveIds,
            symmetricPassivePairs,
            passiveCell
        );
        var improved = false;
        var searchLimit = placements.Values.Max(cell => cell.Column) + 2;
        for (var column = 0; column <= searchLimit; column++)
        {
            if (column == passiveCell.Column)
            {
                continue;
            }

            if (
                placements.Any(kv =>
                    kv.Key != pair.PassiveDeviceId
                    && kv.Value.Row == passiveCell.Row
                    && kv.Value.Column == column
                )
            )
            {
                continue;
            }

            placements[pair.PassiveDeviceId] = new GridCell(
                passiveCell.Row,
                column,
                passiveCell.RotationQuarterTurns,
                passiveCell.MirrorX,
                passiveCell.MirrorY
            );
            if (
                !IsValidPostRepairPlacement(
                    placements,
                    graph,
                    topology,
                    horizontalPassiveIds,
                    symmetricPassivePairs
                )
            )
            {
                placements[pair.PassiveDeviceId] = passiveCell;
                continue;
            }

            var candidateScore = ScoreOutputCouplingPassivePlacement(
                placements,
                pair,
                graph,
                topology,
                horizontalPassiveIds,
                symmetricPassivePairs,
                passiveCell
            );
            if (IsBetterOutputCouplingPassivePlacement(candidateScore, bestScore))
            {
                bestPlacements = placements.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value,
                    StringComparer.Ordinal
                );
                bestScore = candidateScore;
                improved = true;
            }

            placements[pair.PassiveDeviceId] = passiveCell;
        }

        if (!improved)
        {
            return false;
        }

        placements.Clear();
        foreach (var (deviceId, cell) in bestPlacements)
        {
            placements[deviceId] = cell;
        }

        return true;
    }

    private static bool TryRefineBiasPassiveClusterPlacement(
        Dictionary<string, GridCell> placements,
        RailConnectedPassiveCluster cluster,
        CircuitGraph graph,
        TopologyResult topology,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlyList<(string Left, string Right, string PivotNet)> symmetricPassivePairs,
        IReadOnlySet<string> hardPlacedDeviceIds
    )
    {
        var members = cluster
            .DeviceIds.Where(placements.ContainsKey)
            .OrderBy(id => placements[id].Row)
            .ThenBy(id => placements[id].Column)
            .ToArray();
        if (members.Length < 2)
        {
            return false;
        }

        var currentSpan =
            members.Max(id => placements[id].Column) - members.Min(id => placements[id].Column);
        var minSpan = members.GroupBy(id => placements[id].Row).Max(group => group.Count()) - 1;
        if (currentSpan < minSpan)
        {
            return false;
        }

        var consumerColumns = GetWeightedBiasConsumerIds(
                [cluster.NetName],
                GetBiasClusterConsumerIds(cluster, graph),
                graph
            )
            .Where(placements.ContainsKey)
            .Select(id => placements[id].Column)
            .ToArray();
        var searchLimit = placements.Values.Max(cell => cell.Column) + members.Length;
        var currentScore = ScoreBiasClusterPlacement(
            placements,
            members,
            consumerColumns,
            placements
        );
        Dictionary<string, GridCell>? best = null;
        var bestScore = currentScore;
        for (var span = minSpan; span <= currentSpan; span++)
        {
            for (var start = 0; start + span <= searchLimit; start++)
            {
                var candidate = BuildBiasClusterWindowCandidate(
                    placements,
                    members,
                    hardPlacedDeviceIds,
                    consumerColumns,
                    start,
                    span,
                    graph,
                    topology,
                    horizontalPassiveIds,
                    symmetricPassivePairs
                );
                if (candidate == null)
                {
                    continue;
                }

                var score = ScoreBiasClusterPlacement(
                    candidate,
                    members,
                    consumerColumns,
                    placements
                );
                if (score.CompareTo(bestScore) >= 0)
                {
                    continue;
                }

                best = candidate;
                bestScore = score;
            }
        }

        if (best == null || bestScore.CompareTo(currentScore) >= 0)
        {
            return false;
        }

        foreach (var member in members)
        {
            placements[member] = best[member];
        }

        return true;
    }

    private static bool TryRefineBiasPassiveChainPlacement(
        Dictionary<string, GridCell> placements,
        BiasPassiveChain chain,
        CircuitGraph graph,
        TopologyResult topology,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlyList<(string Left, string Right, string PivotNet)> symmetricPassivePairs,
        IReadOnlySet<string> hardPlacedDeviceIds
    )
    {
        var members = chain
            .DeviceIds.Where(placements.ContainsKey)
            .OrderBy(id => placements[id].Row)
            .ThenBy(id => placements[id].Column)
            .ToArray();
        if (members.Length < 3)
        {
            return false;
        }

        var currentSpan =
            members.Max(id => placements[id].Column) - members.Min(id => placements[id].Column);
        var minSpan = members.GroupBy(id => placements[id].Row).Max(group => group.Count()) - 1;
        if (currentSpan < minSpan)
        {
            return false;
        }

        var consumerColumns = chain
            .ConsumerIds.Where(placements.ContainsKey)
            .Select(id => placements[id].Column)
            .ToArray();
        var searchLimit = placements.Values.Max(cell => cell.Column) + members.Length;
        var currentScore = ScoreBiasClusterPlacement(
            placements,
            members,
            consumerColumns,
            placements
        );
        Dictionary<string, GridCell>? best = null;
        var bestScore = currentScore;
        for (var span = minSpan; span <= currentSpan; span++)
        {
            for (var start = 0; start + span <= searchLimit; start++)
            {
                var candidate = BuildBiasClusterWindowCandidate(
                    placements,
                    members,
                    hardPlacedDeviceIds,
                    consumerColumns,
                    start,
                    span,
                    graph,
                    topology,
                    horizontalPassiveIds,
                    symmetricPassivePairs
                );
                if (candidate == null)
                {
                    continue;
                }

                var score = ScoreBiasClusterPlacement(
                    candidate,
                    members,
                    consumerColumns,
                    placements
                );
                if (score.CompareTo(bestScore) >= 0)
                {
                    continue;
                }

                best = candidate;
                bestScore = score;
            }
        }

        if (best == null || bestScore.CompareTo(currentScore) >= 0)
        {
            return false;
        }

        foreach (var member in members)
        {
            placements[member] = best[member];
        }

        return true;
    }

    private static bool TryRefineGateBiasDistributionPlacement(
        Dictionary<string, GridCell> placements,
        GatePassiveLink link,
        CircuitGraph graph,
        TopologyResult topology,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlyList<(string Left, string Right, string PivotNet)> symmetricPassivePairs,
        IReadOnlySet<string> hardPlacedDeviceIds
    )
    {
        if (!placements.TryGetValue(link.PassiveDeviceId, out var gatePassiveCell))
        {
            return false;
        }

        var members = FindRailConnectedPassiveIdsOnNet(link.PassiveOtherNet, graph)
            .Where(id => placements.ContainsKey(id) && !horizontalPassiveIds.Contains(id))
            .ToArray();
        if (members.Length == 0)
        {
            return false;
        }

        var memberSet = members.ToHashSet(StringComparer.Ordinal);
        var referenceRow = gatePassiveCell.Row;
        var baseline = placements.ToDictionary(
            kv => kv.Key,
            kv => kv.Value,
            StringComparer.Ordinal
        );
        var currentScore = ScoreGateBiasDistributionPlacement(
            placements,
            members,
            referenceRow,
            graph,
            baseline
        );
        var candidate = placements.ToDictionary(
            kv => kv.Key,
            kv => kv.Value,
            StringComparer.Ordinal
        );
        var usedCells = new HashSet<(int Row, int Column)>();
        foreach (var (deviceId, cell) in placements)
        {
            if (!memberSet.Contains(deviceId) || hardPlacedDeviceIds.Contains(deviceId))
            {
                usedCells.Add((cell.Row, cell.Column));
            }
        }

        var movableMembers = members
            .Where(id => !hardPlacedDeviceIds.Contains(id))
            .OrderByDescending(id =>
                GetGateBiasDistributionSidePenalty(
                    placements[id].Row,
                    referenceRow,
                    GetGateBiasPreferredRowDirection(id, graph)
                )
            )
            .ThenByDescending(id => Math.Abs(placements[id].Row - referenceRow))
            .ToArray();
        if (movableMembers.Length == 0)
        {
            return false;
        }

        var searchLimit = Math.Max(
            placements.Values.Max(cell => cell.Row) + movableMembers.Length,
            referenceRow + movableMembers.Length + 1
        );
        Dictionary<string, GridCell>? best = null;
        var bestScore = currentScore;
        if (
            TryAssignGateBiasDistributionRows(
                index: 0,
                movableMembers,
                baseline,
                referenceRow,
                searchLimit,
                graph,
                topology,
                horizontalPassiveIds,
                symmetricPassivePairs,
                usedCells,
                candidate
            )
        )
        {
            var score = ScoreGateBiasDistributionPlacement(
                candidate,
                members,
                referenceRow,
                graph,
                baseline
            );
            if (score.CompareTo(bestScore) < 0)
            {
                best = candidate.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
                bestScore = score;
            }
        }

        if (best == null || bestScore.CompareTo(currentScore) >= 0)
        {
            return false;
        }

        foreach (var member in members)
        {
            placements[member] = best[member];
        }

        return true;
    }

    private static Dictionary<string, GridCell>? BuildBiasClusterWindowCandidate(
        IReadOnlyDictionary<string, GridCell> placements,
        IReadOnlyList<string> members,
        IReadOnlySet<string> hardPlacedDeviceIds,
        IReadOnlyList<int> consumerColumns,
        int startColumn,
        int span,
        CircuitGraph graph,
        TopologyResult topology,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlyList<(string Left, string Right, string PivotNet)> symmetricPassivePairs
    )
    {
        var windowColumns = Enumerable.Range(startColumn, span + 1).ToArray();
        var memberSet = members.ToHashSet(StringComparer.Ordinal);
        var candidate = placements.ToDictionary(
            kv => kv.Key,
            kv => kv.Value,
            StringComparer.Ordinal
        );
        var usedCells = new HashSet<(int Row, int Column)>();
        foreach (var member in members.Where(hardPlacedDeviceIds.Contains))
        {
            var cell = placements[member];
            if (
                cell.Column < startColumn
                || cell.Column > startColumn + span
                || placements.Any(kv =>
                    !memberSet.Contains(kv.Key)
                    && kv.Value.Row == cell.Row
                    && kv.Value.Column == cell.Column
                )
                || !usedCells.Add((cell.Row, cell.Column))
            )
            {
                return null;
            }
        }

        var movableMembers = members
            .Where(id => !hardPlacedDeviceIds.Contains(id))
            .OrderBy(id => GetBiasClusterWindowMemberPriority(id, graph))
            .ThenBy(id => placements[id].Row)
            .ThenBy(id => placements[id].Column)
            .ToArray();
        return TryAssignBiasClusterWindowMembers(
            index: 0,
            movableMembers,
            placements,
            memberSet,
            windowColumns,
            consumerColumns,
            graph,
            topology,
            horizontalPassiveIds,
            symmetricPassivePairs,
            usedCells,
            candidate
        )
            ? candidate
            : null;
    }

    private static int GetBiasClusterWindowMemberPriority(string deviceId, CircuitGraph graph)
    {
        if (
            graph.Devices.TryGetValue(deviceId, out var device)
            && device.DeviceType.Equals("capacitor", StringComparison.OrdinalIgnoreCase)
            && device.Bindings.Values.Any(graph.IsSupplyOrGround)
        )
        {
            return 1;
        }

        return 0;
    }

    private static bool TryAssignBiasClusterWindowMembers(
        int index,
        IReadOnlyList<string> movableMembers,
        IReadOnlyDictionary<string, GridCell> baseline,
        IReadOnlySet<string> memberSet,
        IReadOnlyList<int> windowColumns,
        IReadOnlyList<int> consumerColumns,
        CircuitGraph graph,
        TopologyResult topology,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlyList<(string Left, string Right, string PivotNet)> symmetricPassivePairs,
        HashSet<(int Row, int Column)> usedCells,
        Dictionary<string, GridCell> candidate
    )
    {
        if (index >= movableMembers.Count)
        {
            return IsValidPostRepairPlacement(
                candidate,
                graph,
                topology,
                horizontalPassiveIds,
                symmetricPassivePairs
            );
        }

        var member = movableMembers[index];
        var currentCell = baseline[member];
        var targetColumn =
            consumerColumns.Count == 0
                ? currentCell.Column
                : (int)Math.Round(consumerColumns.Average(), MidpointRounding.AwayFromZero);
        var validColumns = windowColumns
            .Where(candidateColumn =>
                !usedCells.Contains((currentCell.Row, candidateColumn))
                && !baseline.Any(kv =>
                    !memberSet.Contains(kv.Key)
                    && kv.Value.Row == currentCell.Row
                    && kv.Value.Column == candidateColumn
                )
            )
            .OrderBy(candidateColumn => Math.Abs(candidateColumn - targetColumn))
            .ThenBy(candidateColumn => Math.Abs(candidateColumn - currentCell.Column))
            .ToArray();
        foreach (var column in validColumns)
        {
            usedCells.Add((currentCell.Row, column));
            candidate[member] = new GridCell(
                currentCell.Row,
                column,
                currentCell.RotationQuarterTurns,
                currentCell.MirrorX,
                currentCell.MirrorY
            );
            if (
                TryAssignBiasClusterWindowMembers(
                    index + 1,
                    movableMembers,
                    baseline,
                    memberSet,
                    windowColumns,
                    consumerColumns,
                    graph,
                    topology,
                    horizontalPassiveIds,
                    symmetricPassivePairs,
                    usedCells,
                    candidate
                )
            )
            {
                return true;
            }

            candidate[member] = currentCell;
            usedCells.Remove((currentCell.Row, column));
        }

        return false;
    }

    private static bool TryAssignGateBiasDistributionRows(
        int index,
        IReadOnlyList<string> movableMembers,
        IReadOnlyDictionary<string, GridCell> baseline,
        int referenceRow,
        int searchLimit,
        CircuitGraph graph,
        TopologyResult topology,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlyList<(string Left, string Right, string PivotNet)> symmetricPassivePairs,
        HashSet<(int Row, int Column)> usedCells,
        Dictionary<string, GridCell> candidate
    )
    {
        if (index >= movableMembers.Count)
        {
            return IsValidPostRepairPlacement(
                candidate,
                graph,
                topology,
                horizontalPassiveIds,
                symmetricPassivePairs
            );
        }

        var member = movableMembers[index];
        var currentCell = baseline[member];
        var preferredDirection = GetGateBiasPreferredRowDirection(member, graph);
        var candidateRows = Enumerable
            .Range(0, searchLimit + 1)
            .Where(row => !usedCells.Contains((row, currentCell.Column)))
            .OrderBy(row =>
                GetGateBiasDistributionSidePenalty(row, referenceRow, preferredDirection)
            )
            .ThenBy(row => Math.Abs(row - currentCell.Row))
            .ThenBy(row => Math.Abs(row - referenceRow))
            .ToArray();
        foreach (var row in candidateRows)
        {
            usedCells.Add((row, currentCell.Column));
            candidate[member] = new GridCell(
                row,
                currentCell.Column,
                currentCell.RotationQuarterTurns,
                currentCell.MirrorX,
                currentCell.MirrorY
            );
            if (
                TryAssignGateBiasDistributionRows(
                    index + 1,
                    movableMembers,
                    baseline,
                    referenceRow,
                    searchLimit,
                    graph,
                    topology,
                    horizontalPassiveIds,
                    symmetricPassivePairs,
                    usedCells,
                    candidate
                )
            )
            {
                return true;
            }

            candidate[member] = currentCell;
            usedCells.Remove((row, currentCell.Column));
        }

        return false;
    }

    private static (int Span, int ConsumerDistance, int Movement) ScoreBiasClusterPlacement(
        IReadOnlyDictionary<string, GridCell> candidate,
        IReadOnlyList<string> members,
        IReadOnlyList<int> consumerColumns,
        IReadOnlyDictionary<string, GridCell> baseline
    )
    {
        var span =
            members.Max(id => candidate[id].Column) - members.Min(id => candidate[id].Column);
        var consumerDistance = consumerColumns.Sum(consumerColumn =>
            members.Sum(id => Math.Abs(candidate[id].Column - consumerColumn))
        );
        var movement = members.Sum(id => Math.Abs(candidate[id].Column - baseline[id].Column));
        return (span, consumerDistance, movement);
    }

    private static (
        int SidePenalty,
        int Movement,
        int RowSpan,
        int ReferenceDistance
    ) ScoreGateBiasDistributionPlacement(
        IReadOnlyDictionary<string, GridCell> candidate,
        IReadOnlyList<string> members,
        int referenceRow,
        CircuitGraph graph,
        IReadOnlyDictionary<string, GridCell> baseline
    )
    {
        var sidePenalty = members.Sum(id =>
            GetGateBiasDistributionSidePenalty(
                candidate[id].Row,
                referenceRow,
                GetGateBiasPreferredRowDirection(id, graph)
            )
        );
        var movement = members.Sum(id => Math.Abs(candidate[id].Row - baseline[id].Row));
        var rowSpan = members.Max(id => candidate[id].Row) - members.Min(id => candidate[id].Row);
        var referenceDistance = members.Sum(id => Math.Abs(candidate[id].Row - referenceRow));
        return (sidePenalty, movement, rowSpan, referenceDistance);
    }

    private static int GetGateBiasPreferredRowDirection(string passiveId, CircuitGraph graph)
    {
        var railNet = GetRailConnectedNet(passiveId, graph);
        if (railNet == null)
        {
            return 0;
        }

        if (graph.Supplies.Contains(railNet))
        {
            return -1;
        }

        if (graph.Grounds.Contains(railNet))
        {
            return 1;
        }

        return 0;
    }

    private static int GetGateBiasDistributionSidePenalty(
        int row,
        int referenceRow,
        int preferredDirection
    )
    {
        return preferredDirection switch
        {
            < 0 => Math.Max(0, row - referenceRow),
            > 0 => Math.Max(0, referenceRow - row),
            _ => 0,
        };
    }

    private static bool TrySwapInvertedBiasClusterWindows(
        Dictionary<string, GridCell> placements,
        RailConnectedPassiveCluster leftCluster,
        RailConnectedPassiveCluster rightCluster,
        CircuitGraph graph,
        TopologyResult topology,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlyList<(string Left, string Right, string PivotNet)> symmetricPassivePairs,
        IReadOnlySet<string> hardPlacedDeviceIds
    )
    {
        var leftMembers = leftCluster.DeviceIds.Where(placements.ContainsKey).ToArray();
        var rightMembers = rightCluster.DeviceIds.Where(placements.ContainsKey).ToArray();
        if (
            leftMembers.Length < 2
            || rightMembers.Length < 2
            || leftMembers.Any(hardPlacedDeviceIds.Contains)
            || rightMembers.Any(hardPlacedDeviceIds.Contains)
            || leftMembers.Intersect(rightMembers, StringComparer.Ordinal).Any()
        )
        {
            return false;
        }

        var leftConsumerColumns = GetWeightedBiasConsumerIds(
                [leftCluster.NetName],
                GetBiasClusterConsumerIds(leftCluster, graph),
                graph
            )
            .Where(placements.ContainsKey)
            .Select(id => placements[id].Column)
            .ToArray();
        var rightConsumerColumns = GetWeightedBiasConsumerIds(
                [rightCluster.NetName],
                GetBiasClusterConsumerIds(rightCluster, graph),
                graph
            )
            .Where(placements.ContainsKey)
            .Select(id => placements[id].Column)
            .ToArray();
        if (leftConsumerColumns.Length == 0 || rightConsumerColumns.Length == 0)
        {
            return false;
        }

        var leftCurrentCenter = leftMembers.Average(id => placements[id].Column);
        var rightCurrentCenter = rightMembers.Average(id => placements[id].Column);
        var leftConsumerCenter = leftConsumerColumns.Average();
        var rightConsumerCenter = rightConsumerColumns.Average();
        var inverted =
            leftConsumerCenter < rightConsumerCenter
                ? leftCurrentCenter > rightCurrentCenter
                : leftConsumerCenter > rightConsumerCenter
                    && leftCurrentCenter < rightCurrentCenter;
        if (!inverted)
        {
            return false;
        }

        var leftMin = leftMembers.Min(id => placements[id].Column);
        var rightMin = rightMembers.Min(id => placements[id].Column);
        var candidate = placements.ToDictionary(
            kv => kv.Key,
            kv => kv.Value,
            StringComparer.Ordinal
        );
        foreach (var member in leftMembers)
        {
            var cell = placements[member];
            candidate[member] = new GridCell(
                cell.Row,
                rightMin + (cell.Column - leftMin),
                cell.RotationQuarterTurns,
                cell.MirrorX,
                cell.MirrorY
            );
        }

        foreach (var member in rightMembers)
        {
            var cell = placements[member];
            candidate[member] = new GridCell(
                cell.Row,
                leftMin + (cell.Column - rightMin),
                cell.RotationQuarterTurns,
                cell.MirrorX,
                cell.MirrorY
            );
        }

        var candidateCells = leftMembers
            .Concat(rightMembers)
            .Select(member => (candidate[member].Row, candidate[member].Column))
            .ToArray();
        if (candidateCells.Distinct().Count() != candidateCells.Length)
        {
            return false;
        }

        if (
            candidate.Any(kv =>
                !leftMembers.Contains(kv.Key, StringComparer.Ordinal)
                && !rightMembers.Contains(kv.Key, StringComparer.Ordinal)
                && candidateCells.Contains((kv.Value.Row, kv.Value.Column))
            )
            || !IsValidPostRepairPlacement(
                candidate,
                graph,
                topology,
                horizontalPassiveIds,
                symmetricPassivePairs
            )
        )
        {
            return false;
        }

        var currentLeftScore = ScoreBiasClusterPlacement(
            placements,
            leftMembers,
            leftConsumerColumns,
            placements
        );
        var currentRightScore = ScoreBiasClusterPlacement(
            placements,
            rightMembers,
            rightConsumerColumns,
            placements
        );
        var candidateLeftScore = ScoreBiasClusterPlacement(
            candidate,
            leftMembers,
            leftConsumerColumns,
            placements
        );
        var candidateRightScore = ScoreBiasClusterPlacement(
            candidate,
            rightMembers,
            rightConsumerColumns,
            placements
        );
        var currentConsumerDistance =
            currentLeftScore.ConsumerDistance + currentRightScore.ConsumerDistance;
        var candidateConsumerDistance =
            candidateLeftScore.ConsumerDistance + candidateRightScore.ConsumerDistance;
        var currentMovement = currentLeftScore.Movement + currentRightScore.Movement;
        var candidateMovement = candidateLeftScore.Movement + candidateRightScore.Movement;
        if (
            candidateConsumerDistance > currentConsumerDistance
            || (
                candidateConsumerDistance == currentConsumerDistance
                && candidateMovement >= currentMovement
            )
        )
        {
            return false;
        }

        placements.Clear();
        foreach (var (deviceId, cell) in candidate)
        {
            placements[deviceId] = cell;
        }

        return true;
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

    private static bool TryRepairPointToPointDrainSourceStackViolation(
        Dictionary<string, GridCell> placements,
        TopologyResult topology,
        CircuitGraph graph
    )
    {
        var groupedDevices = topology
            .SymmetricGroups.SelectMany(group => group.DeviceIds)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var pair in FindPointToPointDrainSourcePairs(graph))
        {
            if (
                !placements.TryGetValue(pair.SourceDeviceId, out var sourceCell)
                || !placements.TryGetValue(pair.DrainDeviceId, out var drainCell)
                || sourceCell.Column == drainCell.Column
            )
            {
                continue;
            }

            var moveSource =
                groupedDevices.Contains(pair.DrainDeviceId)
                && !groupedDevices.Contains(pair.SourceDeviceId);
            if (moveSource)
            {
                placements[pair.SourceDeviceId] = new GridCell(
                    sourceCell.Row,
                    drainCell.Column,
                    sourceCell.RotationQuarterTurns,
                    sourceCell.MirrorX,
                    sourceCell.MirrorY
                );
            }
            else
            {
                placements[pair.DrainDeviceId] = new GridCell(
                    drainCell.Row,
                    sourceCell.Column,
                    drainCell.RotationQuarterTurns,
                    drainCell.MirrorX,
                    drainCell.MirrorY
                );
            }

            return true;
        }

        return false;
    }

    private static bool TryRepairMixedPolarityDrainAlignmentViolation(
        Dictionary<string, GridCell> placements,
        TopologyResult topology,
        CircuitGraph graph
    )
    {
        var groupedDevices = topology
            .SymmetricGroups.SelectMany(group => group.DeviceIds)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var pair in FindMixedPolarityDrainPairs(graph))
        {
            if (
                !placements.TryGetValue(pair.PmosDeviceId, out var pmosCell)
                || !placements.TryGetValue(pair.NmosDeviceId, out var nmosCell)
                || pmosCell.Column == nmosCell.Column
            )
            {
                continue;
            }

            var movePmos =
                groupedDevices.Contains(pair.NmosDeviceId)
                && !groupedDevices.Contains(pair.PmosDeviceId);
            if (movePmos)
            {
                placements[pair.PmosDeviceId] = new GridCell(
                    pmosCell.Row,
                    nmosCell.Column,
                    pmosCell.RotationQuarterTurns,
                    pmosCell.MirrorX,
                    pmosCell.MirrorY
                );
            }
            else
            {
                placements[pair.NmosDeviceId] = new GridCell(
                    nmosCell.Row,
                    pmosCell.Column,
                    nmosCell.RotationQuarterTurns,
                    nmosCell.MirrorX,
                    nmosCell.MirrorY
                );
            }

            return true;
        }

        return false;
    }

    private static bool TryRepairSupplyLoadAlignmentViolation(
        Dictionary<string, GridCell> placements,
        TopologyResult topology,
        CircuitGraph graph
    )
    {
        var groupedDevices = topology
            .SymmetricGroups.SelectMany(group => group.DeviceIds)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var pair in FindSupplyConnectedMosLoadPairs(graph))
        {
            if (
                !placements.TryGetValue(pair.LoadDeviceId, out var loadCell)
                || !placements.TryGetValue(pair.MosDeviceId, out var mosCell)
                || loadCell.Column == mosCell.Column
            )
            {
                continue;
            }

            if (
                groupedDevices.Contains(pair.LoadDeviceId)
                && !groupedDevices.Contains(pair.MosDeviceId)
            )
            {
                placements[pair.MosDeviceId] = new GridCell(
                    mosCell.Row,
                    loadCell.Column,
                    mosCell.RotationQuarterTurns,
                    mosCell.MirrorX,
                    mosCell.MirrorY
                );
            }
            else
            {
                placements[pair.LoadDeviceId] = new GridCell(
                    loadCell.Row,
                    mosCell.Column,
                    loadCell.RotationQuarterTurns,
                    loadCell.MirrorX,
                    loadCell.MirrorY
                );
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

    private static bool TryRepairStraightConnectionViolation(
        Dictionary<string, GridCell> placements,
        CircuitGraph graph,
        TopologyResult topology,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlyList<(string Left, string Right, string PivotNet)> symmetricPassivePairs
    )
    {
        var oriented = ApplyOrientationRules(
            placements,
            graph,
            topology,
            horizontalPassiveIds,
            symmetricPassivePairs
        );
        var candidate = BuildPlacementSnapshot(oriented, horizontalPassiveIds);
        var violation = FindStraightConnectionViolation(candidate, graph, portsOnly: false);
        if (
            violation == null
            || !placements.TryGetValue(violation.BlockingDeviceId, out var blockingCell)
        )
        {
            return false;
        }

        if (TryMoveStraightConnectionEndpoint(placements, graph, violation))
        {
            return true;
        }

        var isHorizontal = violation.Start.Y == violation.End.Y;
        if (isHorizontal)
        {
            var freshRow = placements.Values.Max(existing => existing.Row) + 1;
            placements[violation.BlockingDeviceId] = new GridCell(freshRow, blockingCell.Column);
            return true;
        }

        var freshColumn = placements.Values.Max(existing => existing.Column) + 1;
        placements[violation.BlockingDeviceId] = new GridCell(blockingCell.Row, freshColumn);
        return true;
    }

    private static bool TryMoveStraightConnectionEndpoint(
        Dictionary<string, GridCell> placements,
        CircuitGraph graph,
        StraightConnectionViolation violation
    )
    {
        var endpointIds = new[] { violation.Start.DeviceId, violation.End.DeviceId };
        foreach (var endpointId in endpointIds)
        {
            if (
                endpointId.StartsWith("PORT_", StringComparison.Ordinal)
                || !placements.TryGetValue(endpointId, out var endpointCell)
                || !graph.Devices.TryGetValue(endpointId, out var endpointDevice)
                || !IsPassive(endpointDevice.DeviceType)
            )
            {
                continue;
            }

            if (violation.Start.Y == violation.End.Y)
            {
                var freshRow = placements.Values.Max(existing => existing.Row) + 1;
                placements[endpointId] = new GridCell(freshRow, endpointCell.Column);
            }
            else
            {
                var freshColumn = placements.Values.Max(existing => existing.Column) + 1;
                placements[endpointId] = new GridCell(endpointCell.Row, freshColumn);
            }

            return true;
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

    private static bool IsNmosDevice(string deviceType)
    {
        var normalized = deviceType.ToLowerInvariant();
        return normalized is "nmos" or "nfet";
    }

    private static bool IsPmosDevice(string deviceType)
    {
        var normalized = deviceType.ToLowerInvariant();
        return normalized is "pmos" or "pfet";
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

    private static (string Left, string Right)? DetermineLeftRightByPartnerDrainAlignment(
        string first,
        string second,
        IReadOnlyList<SymmetricGroup> groups,
        CircuitGraph graph
    )
    {
        var firstPartner = GetUniqueMixedPolarityDrainPartner(first, graph);
        var secondPartner = GetUniqueMixedPolarityDrainPartner(second, graph);
        if (
            firstPartner == null
            || secondPartner == null
            || string.Equals(firstPartner, secondPartner, StringComparison.Ordinal)
        )
        {
            return null;
        }

        var partnerGroup = groups.FirstOrDefault(group =>
            group.DeviceIds.Count == 2
            && group.DeviceIds.Contains(firstPartner, StringComparer.Ordinal)
            && group.DeviceIds.Contains(secondPartner, StringComparer.Ordinal)
        );
        if (partnerGroup == null)
        {
            return null;
        }

        var partnerOrder =
            partnerGroup.Type == SymmetryType.DiffPair
                ? DetermineLeftRightByInputPort(firstPartner, secondPartner, graph)
                : DetermineLeftRightByNaming(firstPartner, secondPartner);
        return string.Equals(partnerOrder.Left, firstPartner, StringComparison.Ordinal)
            ? (first, second)
            : (second, first);
    }

    private static string? GetUniqueMixedPolarityDrainPartner(string deviceId, CircuitGraph graph)
    {
        if (
            !graph.Devices.TryGetValue(deviceId, out var device)
            || !IsMosDevice(device.DeviceType)
            || !device.Bindings.TryGetValue("D", out var drainNet)
            || !graph.NetConnections.TryGetValue(drainNet, out var connections)
        )
        {
            return null;
        }

        var isPmos = IsPmosDevice(device.DeviceType);
        var partnerIds = connections
            .Where(connection =>
                connection.DeviceId != deviceId
                && connection.Terminal.Equals("D", StringComparison.OrdinalIgnoreCase)
                && graph.Devices.TryGetValue(connection.DeviceId, out var partner)
                && IsMosDevice(partner.DeviceType)
                && (isPmos ? IsNmosDevice(partner.DeviceType) : IsPmosDevice(partner.DeviceType))
            )
            .Select(connection => connection.DeviceId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return partnerIds.Length == 1 ? partnerIds[0] : null;
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
                    pOnLeft: !cell.MirrorX
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
                        pOnLeft: !cell.MirrorX
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

    private static IReadOnlyDictionary<string, int> AvoidBlockedStraightPortHints(
        CoarseGridResult placement,
        CircuitGraph graph,
        IReadOnlyDictionary<string, int> baseHints
    )
    {
        var adjustedHints = new Dictionary<string, int>(baseHints, StringComparer.Ordinal);
        var portAdjustments = new Dictionary<string, int>(StringComparer.Ordinal);
        var portCount = graph.InputPorts.Count + graph.BiasPorts.Count + graph.OutputPorts.Count;
        for (var iteration = 0; iteration < Math.Max(1, portCount * 4); iteration++)
        {
            var candidate = BuildPlacementSnapshot(
                placement.DevicePlacements,
                placement.HorizontalPassiveIds,
                adjustedHints
            );
            var violation = FindStraightConnectionViolation(candidate, graph, portsOnly: true);
            if (violation == null)
            {
                break;
            }

            var portTerminal = IsPortTerminal(violation.Start) ? violation.Start : violation.End;
            var otherTerminal = ReferenceEquals(portTerminal, violation.Start)
                ? violation.End
                : violation.Start;
            var portName = portTerminal.DeviceId[5..];
            var attempt = portAdjustments.GetValueOrDefault(portName) + 1;
            portAdjustments[portName] = attempt;
            var direction = attempt % 2 == 1 ? 1 : -1;
            var distance = ((attempt + 1) / 2) * DeviceGeometry.RoutingPitch;
            adjustedHints[portName] = otherTerminal.Y + direction * distance;
        }

        return adjustedHints;
    }

    private static CoarseGridResult BuildPlacementSnapshot(
        IReadOnlyDictionary<string, GridCell> placements,
        IReadOnlySet<string> horizontalPassiveIds,
        IReadOnlyDictionary<string, int>? portYHints = null
    )
    {
        var rowCount = Math.Max(1, placements.Values.Select(cell => cell.Row).Distinct().Count());
        var columnCount = Math.Max(
            1,
            placements.Values.Select(cell => cell.Column).Distinct().Count()
        );
        return new CoarseGridResult
        {
            RowCount = rowCount,
            ColumnCount = columnCount,
            DevicePlacements = placements,
            SymmetryAxis = Math.Max(0, columnCount / 2),
            HorizontalPassiveIds = horizontalPassiveIds,
            PortYHints = portYHints ?? new Dictionary<string, int>(StringComparer.Ordinal),
        };
    }

    private static StraightConnectionViolation? FindStraightConnectionViolation(
        CoarseGridResult placement,
        CircuitGraph graph,
        bool portsOnly
    )
    {
        var routing = MazeRouter.Route(placement, graph);
        var terminalsByDevice = routing
            .TerminalPositions.GroupBy(t => t.DeviceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        foreach (var (netName, terminals) in GroupTerminalsByNet(graph, routing.TerminalPositions))
        {
            if (graph.IsSupplyOrGround(netName) || terminals.Count != 2)
            {
                continue;
            }

            var a = terminals[0];
            var b = terminals[1];
            if ((a.X != b.X && a.Y != b.Y) || (a.X == b.X && a.Y == b.Y))
            {
                continue;
            }

            var endpointIncludesPort = IsPortTerminal(a) || IsPortTerminal(b);
            if (portsOnly != endpointIncludesPort)
            {
                continue;
            }

            foreach (var (deviceId, cell) in placement.DevicePlacements)
            {
                if (
                    deviceId == a.DeviceId
                    || deviceId == b.DeviceId
                    || !IntersectsSegmentInterior(cell, graph.Devices[deviceId].DeviceType, a, b)
                )
                {
                    continue;
                }

                var segment = new WireSegment(
                    new GridPoint(a.X, a.Y),
                    new GridPoint(b.X, b.Y),
                    netName
                );
                var ownsNetOnSegment = terminalsByDevice
                    .GetValueOrDefault(deviceId, [])
                    .Any(t =>
                        GetNetName(graph, t) == netName
                        && IsPointOnSegment(new GridPoint(t.X, t.Y), segment)
                    );
                if (!ownsNetOnSegment)
                {
                    return new StraightConnectionViolation(netName, a, b, deviceId);
                }
            }

            foreach (
                var terminal in routing.TerminalPositions.Where(t =>
                    GetNetName(graph, t) != netName
                )
            )
            {
                if (IsStrictlyOnSegment(new GridPoint(terminal.X, terminal.Y), a, b))
                {
                    return new StraightConnectionViolation(netName, a, b, terminal.DeviceId);
                }
            }
        }

        return null;
    }

    private static IReadOnlyDictionary<string, List<TerminalPosition>> GroupTerminalsByNet(
        CircuitGraph graph,
        IReadOnlyList<TerminalPosition> terminalPositions
    )
    {
        var result = new Dictionary<string, List<TerminalPosition>>(StringComparer.Ordinal);
        foreach (var terminal in terminalPositions)
        {
            var netName = GetNetName(graph, terminal);
            if (netName == null)
            {
                continue;
            }

            if (!result.TryGetValue(netName, out var list))
            {
                list = new List<TerminalPosition>();
                result[netName] = list;
            }

            list.Add(terminal);
        }

        return result;
    }

    private static string? GetNetName(CircuitGraph graph, TerminalPosition terminal)
    {
        return IsPortTerminal(terminal)
            ? terminal.DeviceId[5..]
            : graph.GetNetForTerminal(terminal.DeviceId, terminal.Terminal);
    }

    private static bool IsPortTerminal(TerminalPosition terminal)
    {
        return terminal.DeviceId.StartsWith("PORT_", StringComparison.Ordinal);
    }

    private static bool IntersectsSegmentInterior(
        GridCell cell,
        string deviceType,
        TerminalPosition a,
        TerminalPosition b
    )
    {
        var colMargin = IsMosDevice(deviceType) ? 1 : 0;
        var rowMargin = IsMosDevice(deviceType) ? 1 : 0;
        var minX = (cell.Column - colMargin) * DeviceGeometry.CellWidth;
        var maxX = (cell.Column + colMargin + 1) * DeviceGeometry.CellWidth;
        var minY = DeviceGeometry.RailMargin + (cell.Row - rowMargin) * DeviceGeometry.CellHeight;
        var maxY =
            DeviceGeometry.RailMargin + (cell.Row + rowMargin + 1) * DeviceGeometry.CellHeight;

        if (a.Y == b.Y)
        {
            var y = a.Y;
            var left = Math.Min(a.X, b.X);
            var right = Math.Max(a.X, b.X);
            return y > minY && y < maxY && left < maxX && right > minX;
        }

        var x = a.X;
        var top = Math.Min(a.Y, b.Y);
        var bottom = Math.Max(a.Y, b.Y);
        return x > minX && x < maxX && top < maxY && bottom > minY;
    }

    private static bool IsStrictlyOnSegment(GridPoint point, TerminalPosition a, TerminalPosition b)
    {
        if (point.X == a.X && point.Y == a.Y || point.X == b.X && point.Y == b.Y)
        {
            return false;
        }

        return IsPointOnSegment(
            point,
            new WireSegment(new GridPoint(a.X, a.Y), new GridPoint(b.X, b.Y), string.Empty)
        );
    }

    private static bool IsPointOnSegment(GridPoint point, WireSegment segment)
    {
        if (segment.From.X == segment.To.X)
        {
            if (point.X != segment.From.X)
            {
                return false;
            }

            var minY = Math.Min(segment.From.Y, segment.To.Y);
            var maxY = Math.Max(segment.From.Y, segment.To.Y);
            return point.Y >= minY && point.Y <= maxY;
        }

        if (segment.From.Y == segment.To.Y)
        {
            if (point.Y != segment.From.Y)
            {
                return false;
            }

            var minX = Math.Min(segment.From.X, segment.To.X);
            var maxX = Math.Max(segment.From.X, segment.To.X);
            return point.X >= minX && point.X <= maxX;
        }

        return false;
    }

    private sealed record StraightConnectionViolation(
        string NetName,
        TerminalPosition Start,
        TerminalPosition End,
        string BlockingDeviceId
    );

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

    private static SignalSidePreference GetNetSidePreference(
        string netName,
        CircuitGraph graph,
        IReadOnlyDictionary<string, int> inputNetDistances,
        IReadOnlyDictionary<string, int> outputNetDistances
    )
    {
        if (graph.InputPorts.Contains(netName) || graph.BiasPorts.Contains(netName))
        {
            return SignalSidePreference.Left;
        }

        if (graph.OutputPorts.Contains(netName))
        {
            return SignalSidePreference.Right;
        }

        var inputDistance = inputNetDistances.GetValueOrDefault(netName, int.MaxValue / 2);
        var outputDistance = outputNetDistances.GetValueOrDefault(netName, int.MaxValue / 2);
        if (inputDistance < outputDistance)
        {
            return SignalSidePreference.Left;
        }

        if (outputDistance < inputDistance)
        {
            return SignalSidePreference.Right;
        }

        return SignalSidePreference.None;
    }

    private static SignalSidePreference GetGatePassiveSidePreference(
        string otherNet,
        CircuitGraph graph,
        IReadOnlyDictionary<string, int> inputNetDistances,
        IReadOnlyDictionary<string, int> outputNetDistances
    )
    {
        if (FindRailConnectedPassiveIdsOnNet(otherNet, graph).Count > 0)
        {
            return SignalSidePreference.Left;
        }

        var sidePreference = GetNetSidePreference(
            otherNet,
            graph,
            inputNetDistances,
            outputNetDistances
        );
        return sidePreference;
    }

    private static IReadOnlyList<GatePassiveLink> FindGatePassiveLinks(CircuitGraph graph)
    {
        var result = new List<GatePassiveLink>();
        foreach (var (netName, connections) in graph.NetConnections)
        {
            if (graph.IsSupplyOrGround(netName))
            {
                continue;
            }

            var mosIds = connections
                .Where(connection =>
                    connection.Terminal.Equals("G", StringComparison.OrdinalIgnoreCase)
                    && graph.Devices.TryGetValue(connection.DeviceId, out var device)
                    && IsMosDevice(device.DeviceType)
                )
                .Select(connection => connection.DeviceId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (mosIds.Length != 1)
            {
                continue;
            }

            var mosId = mosIds[0];
            foreach (
                var passive in connections
                    .Where(connection =>
                        !IsIgnoredPlacementTerminal(connection.Terminal)
                        && !string.Equals(connection.DeviceId, mosId, StringComparison.Ordinal)
                        && graph.Devices.TryGetValue(connection.DeviceId, out var device)
                        && IsPassive(device.DeviceType)
                    )
                    .Select(connection =>
                    {
                        var otherNet = GetPassiveOtherNet(
                            graph.Devices[connection.DeviceId],
                            connection.Terminal,
                            netName
                        );
                        return (connection.DeviceId, OtherNet: otherNet);
                    })
                    .Where(candidate =>
                        candidate.OtherNet != null && !graph.IsSupplyOrGround(candidate.OtherNet)
                    )
                    .DistinctBy(candidate => candidate.DeviceId)
            )
            {
                result.Add(new GatePassiveLink(passive.DeviceId, mosId, passive.OtherNet!));
            }
        }

        return result;
    }

    private static bool IsGateBiasDistributionNet(
        string netName,
        string gatePassiveId,
        CircuitGraph graph
    )
    {
        if (!graph.NetConnections.TryGetValue(netName, out var connections))
        {
            return false;
        }

        var sawRailPassive = false;
        foreach (var connection in connections)
        {
            if (string.Equals(connection.DeviceId, gatePassiveId, StringComparison.Ordinal))
            {
                continue;
            }

            if (
                !graph.Devices.TryGetValue(connection.DeviceId, out var device)
                || !IsPassive(device.DeviceType)
            )
            {
                return false;
            }

            if (device.Bindings.Values.Any(graph.IsSupplyOrGround))
            {
                sawRailPassive = true;
                continue;
            }

            return false;
        }

        return sawRailPassive;
    }

    private static IReadOnlySet<string> GetFlowDirectedGatePassiveIds(CircuitGraph graph)
    {
        var inputNetDistances = BfsNetDistances(graph, graph.InputPorts.Concat(graph.BiasPorts));
        var outputNetDistances = BfsNetDistances(graph, graph.OutputPorts);
        return FindGatePassiveLinks(graph)
            .Where(pair =>
                GetGatePassiveSidePreference(
                    pair.PassiveOtherNet,
                    graph,
                    inputNetDistances,
                    outputNetDistances
                ) != SignalSidePreference.None
            )
            .Select(pair => pair.PassiveDeviceId)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IReadOnlyList<InlinePassiveChainPair> FindInlinePassiveChainPairs(
        CircuitGraph graph,
        IReadOnlyDictionary<string, int> inputNetDistances,
        IReadOnlyDictionary<string, int> outputNetDistances
    )
    {
        var result = new List<InlinePassiveChainPair>();
        var flowDirectedGatePassiveIds = GetFlowDirectedGatePassiveIds(graph);
        foreach (var pair in FindGatePassiveLinks(graph))
        {
            if (
                !flowDirectedGatePassiveIds.Contains(pair.PassiveDeviceId)
                || !graph.NetConnections.TryGetValue(pair.PassiveOtherNet, out var connections)
            )
            {
                continue;
            }

            var upstreamCandidates = connections
                .Where(connection =>
                    connection.DeviceId != pair.PassiveDeviceId
                    && !IsIgnoredPlacementTerminal(connection.Terminal)
                    && graph.Devices.TryGetValue(connection.DeviceId, out var device)
                    && IsPassive(device.DeviceType)
                )
                .Select(connection =>
                {
                    var otherNet = GetPassiveOtherNet(
                        graph.Devices[connection.DeviceId],
                        connection.Terminal,
                        pair.PassiveOtherNet
                    );
                    return (connection.DeviceId, OtherNet: otherNet);
                })
                .Where(candidate =>
                    candidate.OtherNet != null && !graph.IsSupplyOrGround(candidate.OtherNet)
                )
                .DistinctBy(candidate => candidate.DeviceId)
                .ToArray();
            if (upstreamCandidates.Length != 1)
            {
                continue;
            }

            var sidePreference = GetGatePassiveSidePreference(
                upstreamCandidates[0].OtherNet!,
                graph,
                inputNetDistances,
                outputNetDistances
            );
            if (sidePreference == SignalSidePreference.None)
            {
                continue;
            }

            result.Add(
                new InlinePassiveChainPair(
                    upstreamCandidates[0].DeviceId,
                    pair.PassiveDeviceId,
                    pair.PassiveOtherNet,
                    sidePreference
                )
            );
        }

        return result;
    }

    private static IReadOnlyList<string> FindRailConnectedPassiveIdsOnNet(
        string netName,
        CircuitGraph graph
    )
    {
        if (!graph.NetConnections.TryGetValue(netName, out var connections))
        {
            return [];
        }

        return connections
            .Where(connection =>
                graph.Devices.TryGetValue(connection.DeviceId, out var device)
                && IsPassive(device.DeviceType)
                && device.Bindings.Values.Any(graph.IsSupplyOrGround)
            )
            .Select(connection => connection.DeviceId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string? GetRailConnectedNet(string passiveId, CircuitGraph graph)
    {
        if (!graph.Devices.TryGetValue(passiveId, out var device))
        {
            return null;
        }

        return device.Bindings.Values.FirstOrDefault(graph.IsSupplyOrGround);
    }

    private static IReadOnlyList<BiasPassiveChain> FindBiasPassiveChains(CircuitGraph graph)
    {
        var result = new List<BiasPassiveChain>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (deviceId, device) in graph.Devices)
        {
            if (device.Bindings.Values.Any(graph.IsSupplyOrGround) || !IsPassive(device.DeviceType))
            {
                continue;
            }

            var nets = device
                .Bindings.Values.Where(netName => !graph.IsSupplyOrGround(netName))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (nets.Length != 2)
            {
                continue;
            }

            var leftClusterIds = FindRailConnectedPassiveIdsOnNet(nets[0], graph);
            var rightClusterIds = FindRailConnectedPassiveIdsOnNet(nets[1], graph);
            if (leftClusterIds.Count == 0 || rightClusterIds.Count == 0)
            {
                continue;
            }

            var members = leftClusterIds
                .Concat([deviceId])
                .Concat(rightClusterIds)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            var key = string.Join("|", members);
            if (!seenKeys.Add(key))
            {
                continue;
            }

            result.Add(
                new BiasPassiveChain(members, GetBiasPassiveChainConsumerIds(members, nets, graph))
            );
        }

        foreach (var link in FindGatePassiveLinks(graph))
        {
            var clusterIds = FindRailConnectedPassiveIdsOnNet(link.PassiveOtherNet, graph);
            if (clusterIds.Count == 0)
            {
                continue;
            }

            var members = clusterIds
                .Concat([link.PassiveDeviceId])
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            var key = string.Join("|", members);
            if (!seenKeys.Add(key))
            {
                continue;
            }

            result.Add(new BiasPassiveChain(members, [link.MosDeviceId]));
        }

        return result;
    }

    private static IReadOnlyList<string> GetBiasPassiveChainConsumerIds(
        IReadOnlyList<string> members,
        IReadOnlyList<string> nets,
        CircuitGraph graph
    )
    {
        var memberSet = members.ToHashSet(StringComparer.Ordinal);
        var consumers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var netName in nets)
        {
            if (!graph.NetConnections.TryGetValue(netName, out var connections))
            {
                continue;
            }

            foreach (var connection in connections)
            {
                if (!memberSet.Contains(connection.DeviceId))
                {
                    consumers.Add(connection.DeviceId);
                }
            }
        }

        return consumers.OrderBy(id => id, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<RailConnectedPassiveCluster> FindRailConnectedPassiveClusters(
        CircuitGraph graph
    )
    {
        var result = new List<RailConnectedPassiveCluster>();
        foreach (var (netName, connections) in graph.NetConnections)
        {
            if (graph.IsSupplyOrGround(netName))
            {
                continue;
            }

            var deviceIds = connections
                .Where(connection =>
                    graph.Devices.TryGetValue(connection.DeviceId, out var device)
                    && IsPassive(device.DeviceType)
                    && device.Bindings.Values.Any(graph.IsSupplyOrGround)
                )
                .Select(connection => connection.DeviceId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (deviceIds.Length < 2)
            {
                continue;
            }

            result.Add(new RailConnectedPassiveCluster(netName, deviceIds));
        }

        return result;
    }

    private static IReadOnlyList<string> GetBiasClusterConsumerIds(
        RailConnectedPassiveCluster cluster,
        CircuitGraph graph
    )
    {
        if (!graph.NetConnections.TryGetValue(cluster.NetName, out var connections))
        {
            return [];
        }

        var clusterIds = cluster.DeviceIds.ToHashSet(StringComparer.Ordinal);
        return connections
            .Where(connection => !clusterIds.Contains(connection.DeviceId))
            .Select(connection => connection.DeviceId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> GetWeightedBiasConsumerIds(
        IReadOnlyList<string> netNames,
        IReadOnlyList<string> consumerIds,
        CircuitGraph graph
    )
    {
        var weighted = new List<string>();
        foreach (var consumerId in consumerIds)
        {
            var weight = GetBiasConsumerPriorityWeight(netNames, consumerId, graph);
            for (var i = 0; i < weight; i++)
            {
                weighted.Add(consumerId);
            }
        }

        return weighted;
    }

    private static int GetBiasConsumerPriorityWeight(
        IReadOnlyList<string> netNames,
        string consumerId,
        CircuitGraph graph
    )
    {
        if (!graph.Devices.TryGetValue(consumerId, out var consumer))
        {
            return 1;
        }

        if (
            IsMosDevice(consumer.DeviceType)
            && consumer.Bindings.TryGetValue("G", out var gateNet)
            && netNames.Contains(gateNet, StringComparer.Ordinal)
        )
        {
            return DirectGateBiasConsumerWeightMultiplier;
        }

        if (!IsPassive(consumer.DeviceType))
        {
            return 1;
        }

        foreach (var netName in netNames)
        {
            foreach (
                var binding in consumer.Bindings.Where(binding =>
                    string.Equals(binding.Value, netName, StringComparison.Ordinal)
                )
            )
            {
                var otherNet = GetPassiveOtherNet(consumer, binding.Key, netName);
                if (
                    otherNet != null
                    && !graph.IsSupplyOrGround(otherNet)
                    && graph.NetConnections.TryGetValue(otherNet, out var connections)
                    && connections.Any(connection =>
                        !string.Equals(connection.DeviceId, consumerId, StringComparison.Ordinal)
                        && string.Equals(
                            connection.Terminal,
                            "G",
                            StringComparison.OrdinalIgnoreCase
                        )
                        && graph.Devices.TryGetValue(connection.DeviceId, out var gatedDevice)
                        && IsMosDevice(gatedDevice.DeviceType)
                    )
                )
                {
                    return GateDriverBiasConsumerWeightMultiplier;
                }
            }
        }

        return 1;
    }

    private static string? GetPassiveOtherNet(
        DeviceDeclaration passive,
        string connectedTerminal,
        string excludedNet
    )
    {
        var otherNets = passive
            .Bindings.Where(binding =>
                !binding.Key.Equals(connectedTerminal, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(binding.Value, excludedNet, StringComparison.Ordinal)
            )
            .Select(binding => binding.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return otherNets.Length == 1 ? otherNets[0] : null;
    }

    private static IReadOnlyList<DrainSourceStackPair> FindPointToPointDrainSourcePairs(
        CircuitGraph graph
    )
    {
        var result = new List<DrainSourceStackPair>();
        foreach (var (netName, connections) in graph.NetConnections)
        {
            if (graph.IsSupplyOrGround(netName))
            {
                continue;
            }

            var dsConnections = connections
                .Where(conn =>
                    graph.Devices.TryGetValue(conn.DeviceId, out var device)
                    && IsMosDevice(device.DeviceType)
                    && (
                        conn.Terminal.Equals("D", StringComparison.OrdinalIgnoreCase)
                        || conn.Terminal.Equals("S", StringComparison.OrdinalIgnoreCase)
                    )
                )
                .ToList();
            if (
                dsConnections.Count != 2
                || dsConnections.All(conn =>
                    conn.Terminal.Equals(
                        dsConnections[0].Terminal,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            )
            {
                continue;
            }

            var sourceDeviceId = dsConnections
                .Single(conn => conn.Terminal.Equals("S", StringComparison.OrdinalIgnoreCase))
                .DeviceId;
            var drainDeviceId = dsConnections
                .Single(conn => conn.Terminal.Equals("D", StringComparison.OrdinalIgnoreCase))
                .DeviceId;
            result.Add(new DrainSourceStackPair(sourceDeviceId, drainDeviceId, netName));
        }

        return result;
    }

    private static IReadOnlyList<MixedPolarityDrainPair> FindMixedPolarityDrainPairs(
        CircuitGraph graph
    )
    {
        var result = new List<MixedPolarityDrainPair>();
        foreach (var (netName, connections) in graph.NetConnections)
        {
            if (graph.IsSupplyOrGround(netName))
            {
                continue;
            }

            var drainConnections = connections
                .Where(conn =>
                    conn.Terminal.Equals("D", StringComparison.OrdinalIgnoreCase)
                    && graph.Devices.TryGetValue(conn.DeviceId, out var device)
                    && IsMosDevice(device.DeviceType)
                )
                .ToList();
            if (drainConnections.Count != 2)
            {
                continue;
            }

            var pmosConnections = drainConnections
                .Where(conn =>
                    graph.Devices.TryGetValue(conn.DeviceId, out var device)
                    && IsPmosDevice(device.DeviceType)
                )
                .ToArray();
            var nmosConnections = drainConnections
                .Where(conn =>
                    graph.Devices.TryGetValue(conn.DeviceId, out var device)
                    && IsNmosDevice(device.DeviceType)
                )
                .ToArray();
            if (pmosConnections.Length != 1 || nmosConnections.Length != 1)
            {
                continue;
            }

            result.Add(
                new MixedPolarityDrainPair(
                    pmosConnections[0].DeviceId,
                    nmosConnections[0].DeviceId,
                    netName
                )
            );
        }

        return result;
    }

    private static IReadOnlyList<SupplyConnectedMosLoadPair> FindSupplyConnectedMosLoadPairs(
        CircuitGraph graph
    )
    {
        var result = new List<SupplyConnectedMosLoadPair>();
        foreach (var (deviceId, device) in graph.Devices)
        {
            if (!IsPassive(device.DeviceType))
            {
                continue;
            }

            var supplyBinding = device.Bindings.FirstOrDefault(binding =>
                graph.Supplies.Contains(binding.Value)
            );
            if (string.IsNullOrEmpty(supplyBinding.Key))
            {
                continue;
            }

            var signalNet = device
                .Bindings.Where(binding =>
                    !string.Equals(binding.Key, supplyBinding.Key, StringComparison.Ordinal)
                )
                .Select(binding => binding.Value)
                .Distinct(StringComparer.Ordinal)
                .SingleOrDefault();
            if (
                signalNet == null
                || graph.IsSupplyOrGround(signalNet)
                || !graph.NetConnections.TryGetValue(signalNet, out var connections)
            )
            {
                continue;
            }

            var mosDeviceIds = connections
                .Where(connection =>
                    connection.DeviceId != deviceId
                    && graph.Devices.TryGetValue(connection.DeviceId, out var connectedDevice)
                    && IsMosDevice(connectedDevice.DeviceType)
                    && (
                        connection.Terminal.Equals("D", StringComparison.OrdinalIgnoreCase)
                        || connection.Terminal.Equals("S", StringComparison.OrdinalIgnoreCase)
                    )
                )
                .Select(connection => connection.DeviceId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (mosDeviceIds.Length != 1)
            {
                continue;
            }

            result.Add(new SupplyConnectedMosLoadPair(deviceId, mosDeviceIds[0], signalNet));
        }

        return result;
    }

    private static IReadOnlyList<OutputCouplingPassivePair> FindOutputCouplingPassivePairs(
        CircuitGraph graph
    )
    {
        var result = new List<OutputCouplingPassivePair>();
        foreach (var (deviceId, device) in graph.Devices)
        {
            if (!IsPassive(device.DeviceType))
            {
                continue;
            }

            var outputBinding = device.Bindings.FirstOrDefault(binding =>
                graph.OutputPorts.Contains(binding.Value)
            );
            if (string.IsNullOrEmpty(outputBinding.Key))
            {
                continue;
            }

            var signalNet = device
                .Bindings.Where(binding =>
                    !string.Equals(binding.Key, outputBinding.Key, StringComparison.Ordinal)
                )
                .Select(binding => binding.Value)
                .Distinct(StringComparer.Ordinal)
                .SingleOrDefault();
            if (
                signalNet == null
                || graph.IsSupplyOrGround(signalNet)
                || !graph.NetConnections.TryGetValue(signalNet, out var connections)
            )
            {
                continue;
            }

            var mosConnections = connections
                .Where(connection =>
                    connection.DeviceId != deviceId
                    && graph.Devices.TryGetValue(connection.DeviceId, out var connectedDevice)
                    && IsMosDevice(connectedDevice.DeviceType)
                    && (
                        connection.Terminal.Equals("D", StringComparison.OrdinalIgnoreCase)
                        || connection.Terminal.Equals("S", StringComparison.OrdinalIgnoreCase)
                    )
                )
                .ToArray();
            var mosDeviceIds = mosConnections
                .Select(connection => connection.DeviceId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (mosDeviceIds.Length != 1)
            {
                continue;
            }

            var mosConnection = mosConnections.First(connection =>
                connection.DeviceId == mosDeviceIds[0]
            );

            result.Add(
                new OutputCouplingPassivePair(
                    deviceId,
                    mosConnection.DeviceId,
                    mosConnection.Terminal,
                    outputBinding.Key.Equals("P", StringComparison.OrdinalIgnoreCase) ? "N" : "P",
                    signalNet
                )
            );
        }

        return result;
    }

    private sealed record DrainSourceStackPair(
        string SourceDeviceId,
        string DrainDeviceId,
        string NetName
    );

    private sealed record MixedPolarityDrainPair(
        string PmosDeviceId,
        string NmosDeviceId,
        string NetName
    );

    private sealed record GatePassiveLink(
        string PassiveDeviceId,
        string MosDeviceId,
        string PassiveOtherNet
    );

    private sealed record InlinePassiveChainPair(
        string UpstreamPassiveId,
        string DownstreamPassiveId,
        string JunctionNet,
        SignalSidePreference UpstreamSidePreference
    );

    private sealed record SupplyConnectedMosLoadPair(
        string LoadDeviceId,
        string MosDeviceId,
        string SignalNet
    );

    private sealed record OutputCouplingPassivePair(
        string PassiveDeviceId,
        string MosDeviceId,
        string MosTerminal,
        string PassiveSignalTerminal,
        string SignalNet
    );

    private sealed record RailConnectedPassiveCluster(
        string NetName,
        IReadOnlyList<string> DeviceIds
    );

    private sealed record BiasPassiveChain(
        IReadOnlyList<string> DeviceIds,
        IReadOnlyList<string> ConsumerIds
    );

    private enum SignalSidePreference
    {
        None,
        Left,
        Right,
    }
}
