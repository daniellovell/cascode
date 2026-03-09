namespace Cascode.Render.Placement;

using Cascode.Language;
using Cascode.Render.Analysis;
using Cascode.Render.OrTools;
using Google.OrTools.Sat;

/// <summary>
/// A component's coarse-grid placement.
/// Rotation is clockwise and must be one of 0, 90, 180, 270.
/// </summary>
public sealed record GridCell
{
    public GridCell(int row, int column, bool MirrorX = false)
        : this(row, column, 0, MirrorX, false) { }

    public GridCell(int row, int column, int rotation, bool MirrorX = false, bool MirrorY = false)
    {
        Row = row;
        Column = column;
        Rotation = rotation;
        this.MirrorX = MirrorX;
        this.MirrorY = MirrorY;
    }

    public int Row { get; init; }
    public int Column { get; init; }
    public int Rotation { get; init; }
    public bool MirrorX { get; init; }
    public bool MirrorY { get; init; }
    public int TransformIndex => EncodeTransform(Rotation, MirrorX, MirrorY);

    public static int EncodeTransform(int rotation, bool mirrorX, bool mirrorY)
    {
        var normalized = ((rotation % 360) + 360) % 360;
        var rotationIndex = normalized / 90;
        return rotationIndex * 4 + (mirrorX ? 2 : 0) + (mirrorY ? 1 : 0);
    }
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

/// <summary>
/// SAT-based coarse placement following Placement_Guidelines.md.
/// </summary>
public static class CoarseGridPlacer
{
    private const double MaxSolveTimeSeconds = 4.0;
    private const int InPortWeight = 0;
    private const int OutPortWeight = 0;
    private const int SymmetryWeight = 8;
    private const int SharedSignalCmosAxisPenaltyWeight = 4;
    private const int SameFlavorDrainSourceMirrorMismatchPenaltyWeight = 12;
    private const int AxisMismatchPenaltyWeight = 1;
    private const int UTurnPenaltyWeight = 128;
    private const int ExpandedColumnPitch = 2;
    private const int ColumnSpacingThreshold = 4;

    private enum Edge
    {
        North,
        East,
        South,
        West,
    }

    public static CoarseGridResult Place(
        TopologyResult topology,
        CircuitGraph graph,
        PlacementConstraintSet? constraints = null
    )
    {
        var deviceIds = graph.Devices.Keys.OrderBy(id => id, StringComparer.Ordinal).ToList();
        if (deviceIds.Count == 0)
        {
            return new CoarseGridResult
            {
                RowCount = 1,
                ColumnCount = 1,
                DevicePlacements = new Dictionary<string, GridCell>(StringComparer.Ordinal),
                SymmetryAxis = 0,
                HorizontalPassiveIds = new HashSet<string>(StringComparer.Ordinal),
            };
        }

        var horizontalPassiveIds = new HashSet<string>(
            topology
                .PassiveOrientations.Where(kv => kv.Value == PassiveOrientation.Horizontal)
                .Select(kv => kv.Key),
            StringComparer.Ordinal
        );

        var rowDomain = Math.Max(
            topology.RowCount + 2,
            (int)Math.Ceiling(Math.Sqrt(deviceIds.Count)) + 3
        );
        var colDomain = Math.Max(5, (int)Math.Ceiling(Math.Sqrt(deviceIds.Count)) + 3);
        var symmetryAxis = colDomain / 2;

        var model = new CpModel();
        var rows = new Dictionary<string, IntVar>(StringComparer.Ordinal);
        var cols = new Dictionary<string, IntVar>(StringComparer.Ordinal);
        var transforms = new Dictionary<string, IntVar>(StringComparer.Ordinal);
        foreach (var id in deviceIds)
        {
            rows[id] = model.NewIntVar(0, rowDomain - 1, $"row_{ToVarToken(id)}");
            cols[id] = model.NewIntVar(0, colDomain - 1, $"col_{ToVarToken(id)}");
            transforms[id] = model.NewIntVar(0, 15, $"xfm_{ToVarToken(id)}");
        }

        AddNoOverlapConstraints(model, rows, cols, deviceIds);
        var hardConstraintEntities = new List<string>();
        var hasHardPlacementConstraints = AddRenderPlacementConstraints(
            model,
            rows,
            cols,
            rowDomain,
            colDomain,
            constraints,
            hardConstraintEntities
        );
        AddRailPassiveVerticalConstraints(model, graph, deviceIds, transforms);
        AddRailEdgeConstraints(model, graph, deviceIds, rows, cols, transforms);
        AddNoInterveningDeviceConstraints(model, graph, deviceIds, rows, cols, transforms);

        var objectives = new List<LinearExpr>();
        AddWireLengthObjective(model, graph, rows, cols, transforms, objectives, colDomain);
        AddPortSideObjectives(graph, cols, objectives, colDomain);
        AddSymmetrySoftObjectives(
            model,
            topology.SymmetricGroups,
            rows,
            cols,
            objectives,
            symmetryAxis
        );
        AddSameFlavorDrainSourceMirrorObjectives(
            model,
            graph,
            transforms,
            objectives,
            SameFlavorDrainSourceMirrorMismatchPenaltyWeight
        );
        AddSharedSignalCmosAxisObjectives(model, graph, rows, cols, objectives);
        model.Minimize(LinearExpr.Sum(objectives));

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

            return FallbackPlacement(topology, deviceIds, horizontalPassiveIds);
        }

        var rawPlacements = new Dictionary<string, GridCell>(StringComparer.Ordinal);
        foreach (var id in deviceIds)
        {
            var transform = (int)solver.Value(transforms[id]);
            var rotation = (transform / 4) * 90;
            var mirrorX = (transform % 4) / 2 == 1;
            var mirrorY = transform % 2 == 1;
            rawPlacements[id] = new GridCell(
                row: (int)solver.Value(rows[id]),
                column: (int)solver.Value(cols[id]),
                rotation: rotation,
                MirrorX: mirrorX,
                MirrorY: mirrorY
            );
        }

        var compacted = CompactPlacement(rawPlacements, symmetryAxis);
        return new CoarseGridResult
        {
            RowCount = compacted.RowCount,
            ColumnCount = compacted.ColumnCount,
            SymmetryAxis = compacted.SymmetryAxis,
            DevicePlacements = compacted.Cells,
            HorizontalPassiveIds = horizontalPassiveIds,
        };
    }

    private static void AddNoOverlapConstraints(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> rows,
        IReadOnlyDictionary<string, IntVar> cols,
        IReadOnlyList<string> deviceIds
    )
    {
        for (var i = 0; i < deviceIds.Count; i++)
        {
            for (var j = i + 1; j < deviceIds.Count; j++)
            {
                var a = deviceIds[i];
                var b = deviceIds[j];
                var sameRow = model.NewBoolVar($"same_row_{ToVarToken(a)}_{ToVarToken(b)}");
                var sameCol = model.NewBoolVar($"same_col_{ToVarToken(a)}_{ToVarToken(b)}");
                model.Add(rows[a] == rows[b]).OnlyEnforceIf(sameRow);
                model.Add(rows[a] != rows[b]).OnlyEnforceIf(sameRow.Not());
                model.Add(cols[a] == cols[b]).OnlyEnforceIf(sameCol);
                model.Add(cols[a] != cols[b]).OnlyEnforceIf(sameCol.Not());
                model.AddBoolOr([sameRow.Not(), sameCol.Not()]);
            }
        }
    }

    private static void AddRailEdgeConstraints(
        CpModel model,
        CircuitGraph graph,
        IReadOnlyList<string> deviceIds,
        IReadOnlyDictionary<string, IntVar> rows,
        IReadOnlyDictionary<string, IntVar> cols,
        IReadOnlyDictionary<string, IntVar> transforms
    )
    {
        foreach (var deviceId in deviceIds)
        {
            if (!graph.Devices.TryGetValue(deviceId, out var device))
            {
                continue;
            }

            var hasSupplyConnection = device.Bindings.Any(b =>
                graph.Supplies.Contains(b.Value) && !IsBodyOrShieldTerminal(b.Key)
            );
            var hasGroundConnection = device.Bindings.Any(b =>
                graph.Grounds.Contains(b.Value) && !IsBodyOrShieldTerminal(b.Key)
            );
            var forceTopOfColumn = hasSupplyConnection && !hasGroundConnection;
            var forceBottomOfColumn = hasGroundConnection && !hasSupplyConnection;

            var vddNorthByTransform = BuildRailEdgeTruthTable(
                device,
                graph,
                railPredicate: graph.Supplies.Contains,
                requiredEdge: Edge.North
            );
            var gndSouthByTransform = BuildRailEdgeTruthTable(
                device,
                graph,
                railPredicate: graph.Grounds.Contains,
                requiredEdge: Edge.South
            );

            var vddNorthInt = model.NewIntVar(0, 1, $"vdd_n_{ToVarToken(deviceId)}");
            var gndSouthInt = model.NewIntVar(0, 1, $"gnd_s_{ToVarToken(deviceId)}");
            model.AddElement(transforms[deviceId], vddNorthByTransform, vddNorthInt);
            model.AddElement(transforms[deviceId], gndSouthByTransform, gndSouthInt);

            var vddNorth = model.NewBoolVar($"vdd_n_bool_{ToVarToken(deviceId)}");
            var gndSouth = model.NewBoolVar($"gnd_s_bool_{ToVarToken(deviceId)}");
            model.Add(vddNorthInt == 1).OnlyEnforceIf(vddNorth);
            model.Add(vddNorthInt == 0).OnlyEnforceIf(vddNorth.Not());
            model.Add(gndSouthInt == 1).OnlyEnforceIf(gndSouth);
            model.Add(gndSouthInt == 0).OnlyEnforceIf(gndSouth.Not());

            foreach (var otherId in deviceIds)
            {
                if (otherId == deviceId)
                {
                    continue;
                }

                var sameCol = model.NewBoolVar(
                    $"rail_same_col_{ToVarToken(deviceId)}_{ToVarToken(otherId)}"
                );
                model.Add(cols[deviceId] == cols[otherId]).OnlyEnforceIf(sameCol);
                model.Add(cols[deviceId] != cols[otherId]).OnlyEnforceIf(sameCol.Not());
                model.Add(rows[deviceId] <= rows[otherId]).OnlyEnforceIf([sameCol, vddNorth]);
                model.Add(rows[deviceId] >= rows[otherId]).OnlyEnforceIf([sameCol, gndSouth]);

                if (forceTopOfColumn)
                {
                    model.Add(rows[deviceId] <= rows[otherId]).OnlyEnforceIf(sameCol);
                }

                if (forceBottomOfColumn)
                {
                    model.Add(rows[deviceId] >= rows[otherId]).OnlyEnforceIf(sameCol);
                }
            }
        }
    }

    private static void AddRailPassiveVerticalConstraints(
        CpModel model,
        CircuitGraph graph,
        IReadOnlyList<string> deviceIds,
        IReadOnlyDictionary<string, IntVar> transforms
    )
    {
        foreach (var deviceId in deviceIds)
        {
            if (
                !graph.Devices.TryGetValue(deviceId, out var device)
                || !transforms.TryGetValue(deviceId, out var transformVar)
            )
            {
                continue;
            }

            var deviceType = device.DeviceType.ToLowerInvariant();
            if (deviceType is not ("resistor" or "capacitor" or "inductor"))
            {
                continue;
            }

            var touchesRail = device.Bindings.Any(b =>
                graph.Supplies.Contains(b.Value) || graph.Grounds.Contains(b.Value)
            );
            if (!touchesRail)
            {
                continue;
            }

            foreach (var (terminal, net) in device.Bindings)
            {
                Edge requiredEdge;
                if (graph.Supplies.Contains(net))
                {
                    requiredEdge = Edge.North;
                }
                else if (graph.Grounds.Contains(net))
                {
                    requiredEdge = Edge.South;
                }
                else
                {
                    continue;
                }

                var edgeByTransform = BuildTerminalEdgeTruthTable(
                    device.DeviceType,
                    terminal,
                    requiredEdge
                );
                var edgeMatch = model.NewIntVar(
                    0,
                    1,
                    $"rail_passive_{ToVarToken(deviceId)}_{ToVarToken(terminal)}_{requiredEdge}"
                );
                model.AddElement(transformVar, edgeByTransform, edgeMatch);
                model.Add(edgeMatch == 1);
            }
        }
    }

    private static int[] BuildRailEdgeTruthTable(
        Cascode.Language.DeviceDeclaration device,
        CircuitGraph graph,
        Func<string, bool> railPredicate,
        Edge requiredEdge
    )
    {
        var truth = new int[16];
        for (var transform = 0; transform < 16; transform++)
        {
            var match = false;
            foreach (var (terminal, net) in device.Bindings)
            {
                if (!railPredicate(net))
                {
                    continue;
                }

                var baseEdge = GetDefaultEdge(device.DeviceType, terminal);
                if (!baseEdge.HasValue)
                {
                    continue;
                }

                if (TransformEdge(baseEdge.Value, transform) == requiredEdge)
                {
                    match = true;
                    break;
                }
            }

            truth[transform] = match ? 1 : 0;
        }

        return truth;
    }

    private static int[] BuildTerminalEdgeTruthTable(
        string deviceType,
        string terminal,
        Edge requiredEdge
    )
    {
        var truth = new int[16];
        var baseEdge = GetDefaultEdge(deviceType, terminal);
        if (!baseEdge.HasValue)
        {
            return truth;
        }

        for (var transform = 0; transform < 16; transform++)
        {
            truth[transform] = TransformEdge(baseEdge.Value, transform) == requiredEdge ? 1 : 0;
        }

        return truth;
    }

    private static void AddNoInterveningDeviceConstraints(
        CpModel model,
        CircuitGraph graph,
        IReadOnlyList<string> deviceIds,
        IReadOnlyDictionary<string, IntVar> rows,
        IReadOnlyDictionary<string, IntVar> cols,
        IReadOnlyDictionary<string, IntVar> transforms
    )
    {
        var occupancyRadius = BuildInterveningOccupancyRadius(graph, deviceIds);
        var rowAxisPassThrough = new Dictionary<(string DeviceId, string NetName), BoolVar>();
        var colAxisPassThrough = new Dictionary<(string DeviceId, string NetName), BoolVar>();

        BoolVar GetAxisPassThrough(string deviceId, string netName, bool verticalAxis)
        {
            var store = verticalAxis ? colAxisPassThrough : rowAxisPassThrough;
            var key = (deviceId, netName);
            if (store.TryGetValue(key, out var cached))
            {
                return cached;
            }

            if (
                !graph.Devices.TryGetValue(deviceId, out var device)
                || !transforms.TryGetValue(deviceId, out var transformVar)
            )
            {
                var none = model.NewBoolVar(
                    $"{(verticalAxis ? "col" : "row")}_axis_passthrough_none_{ToVarToken(deviceId)}_{ToVarToken(netName)}"
                );
                model.Add(none == 0);
                store[key] = none;
                return none;
            }

            var truth = BuildAxisPassThroughTruthTable(
                device,
                netName,
                verticalAxis ? AxisDirection.Vertical : AxisDirection.Horizontal
            );
            var truthInt = model.NewIntVar(
                0,
                1,
                $"{(verticalAxis ? "col" : "row")}_axis_passthrough_int_{ToVarToken(deviceId)}_{ToVarToken(netName)}"
            );
            model.AddElement(transformVar, truth, truthInt);

            var truthBool = model.NewBoolVar(
                $"{(verticalAxis ? "col" : "row")}_axis_passthrough_bool_{ToVarToken(deviceId)}_{ToVarToken(netName)}"
            );
            model.Add(truthInt == 1).OnlyEnforceIf(truthBool);
            model.Add(truthInt == 0).OnlyEnforceIf(truthBool.Not());
            store[key] = truthBool;
            return truthBool;
        }

        foreach (var (netName, refs) in graph.NetConnections)
        {
            var participants = refs.Select(r => r.DeviceId)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (participants.Count < 2)
            {
                continue;
            }

            for (var i = 0; i < participants.Count; i++)
            {
                for (var j = i + 1; j < participants.Count; j++)
                {
                    var a = participants[i];
                    var b = participants[j];
                    if (!rows.ContainsKey(a) || !rows.ContainsKey(b))
                    {
                        continue;
                    }

                    foreach (var k in deviceIds)
                    {
                        if (k == a || k == b || !rows.ContainsKey(k))
                        {
                            continue;
                        }

                        var kParticipatesOnNet = participants.Contains(k, StringComparer.Ordinal);
                        var allowBetweenOnRow = kParticipatesOnNet
                            ? GetAxisPassThrough(k, netName, verticalAxis: false)
                            : null;
                        var allowBetweenOnCol = kParticipatesOnNet
                            ? GetAxisPassThrough(k, netName, verticalAxis: true)
                            : null;

                        AddNotBetweenOnRow(
                            model,
                            rows,
                            cols,
                            occupancyRadius,
                            a,
                            b,
                            k,
                            allowBetweenOnRow
                        );
                        AddNotBetweenOnColumn(
                            model,
                            rows,
                            cols,
                            occupancyRadius,
                            a,
                            b,
                            k,
                            allowBetweenOnCol
                        );
                    }
                }
            }
        }
    }

    private static void AddNotBetweenOnRow(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> rows,
        IReadOnlyDictionary<string, IntVar> cols,
        IReadOnlyDictionary<string, int> occupancyRadius,
        string a,
        string b,
        string k,
        BoolVar? allowBetween
    )
    {
        var rowAligned = BuildAxisTripleOverlapBool(
            model,
            rows[a],
            occupancyRadius[a],
            rows[b],
            occupancyRadius[b],
            rows[k],
            occupancyRadius[k],
            $"row_aligned_{ToVarToken(a)}_{ToVarToken(b)}_{ToVarToken(k)}"
        );

        var between = BuildBetweenBool(
            model,
            cols[a],
            occupancyRadius[a],
            cols[b],
            occupancyRadius[b],
            cols[k],
            occupancyRadius[k],
            $"row_between_{ToVarToken(a)}_{ToVarToken(b)}_{ToVarToken(k)}"
        );
        if (allowBetween is null)
        {
            model.AddBoolOr([rowAligned.Not(), between.Not()]);
        }
        else
        {
            model.AddBoolOr([rowAligned.Not(), between.Not(), allowBetween]);
        }
    }

    private static void AddNotBetweenOnColumn(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> rows,
        IReadOnlyDictionary<string, IntVar> cols,
        IReadOnlyDictionary<string, int> occupancyRadius,
        string a,
        string b,
        string k,
        BoolVar? allowBetween
    )
    {
        var colAligned = BuildAxisTripleOverlapBool(
            model,
            cols[a],
            occupancyRadius[a],
            cols[b],
            occupancyRadius[b],
            cols[k],
            occupancyRadius[k],
            $"col_aligned_{ToVarToken(a)}_{ToVarToken(b)}_{ToVarToken(k)}"
        );

        var between = BuildBetweenBool(
            model,
            rows[a],
            occupancyRadius[a],
            rows[b],
            occupancyRadius[b],
            rows[k],
            occupancyRadius[k],
            $"col_between_{ToVarToken(a)}_{ToVarToken(b)}_{ToVarToken(k)}"
        );
        if (allowBetween is null)
        {
            model.AddBoolOr([colAligned.Not(), between.Not()]);
        }
        else
        {
            model.AddBoolOr([colAligned.Not(), between.Not(), allowBetween]);
        }
    }

    private static BoolVar BuildBetweenBool(
        CpModel model,
        IntVar a,
        int aRadius,
        IntVar b,
        int bRadius,
        IntVar k,
        int kRadius,
        string token
    )
    {
        var aLtK = model.NewBoolVar($"a_lt_k_{token}");
        var kLtB = model.NewBoolVar($"k_lt_b_{token}");
        var bLtK = model.NewBoolVar($"b_lt_k_{token}");
        var kLtA = model.NewBoolVar($"k_lt_a_{token}");
        model.Add(a + aRadius + kRadius + 1 <= k).OnlyEnforceIf(aLtK);
        model.Add(a + aRadius + kRadius >= k).OnlyEnforceIf(aLtK.Not());
        model.Add(k + kRadius + bRadius + 1 <= b).OnlyEnforceIf(kLtB);
        model.Add(k + kRadius + bRadius >= b).OnlyEnforceIf(kLtB.Not());
        model.Add(b + bRadius + kRadius + 1 <= k).OnlyEnforceIf(bLtK);
        model.Add(b + bRadius + kRadius >= k).OnlyEnforceIf(bLtK.Not());
        model.Add(k + kRadius + aRadius + 1 <= a).OnlyEnforceIf(kLtA);
        model.Add(k + kRadius + aRadius >= a).OnlyEnforceIf(kLtA.Not());

        var betweenForward = model.NewBoolVar($"between_f_{token}");
        var betweenReverse = model.NewBoolVar($"between_r_{token}");
        model.AddBoolAnd([aLtK, kLtB]).OnlyEnforceIf(betweenForward);
        model.AddBoolOr([aLtK.Not(), kLtB.Not(), betweenForward]);
        model.AddBoolAnd([bLtK, kLtA]).OnlyEnforceIf(betweenReverse);
        model.AddBoolOr([bLtK.Not(), kLtA.Not(), betweenReverse]);

        var between = model.NewBoolVar($"between_{token}");
        model.AddBoolOr([betweenForward, betweenReverse]).OnlyEnforceIf(between);
        model.AddImplication(betweenForward, between);
        model.AddImplication(betweenReverse, between);
        return between;
    }

    private static BoolVar BuildAxisTripleOverlapBool(
        CpModel model,
        IntVar a,
        int aRadius,
        IntVar b,
        int bRadius,
        IntVar k,
        int kRadius,
        string token
    )
    {
        var overlapAB = BuildAxisOverlapBool(model, a, aRadius, b, bRadius, $"ab_{token}");
        var overlapAK = BuildAxisOverlapBool(model, a, aRadius, k, kRadius, $"ak_{token}");
        var overlapBK = BuildAxisOverlapBool(model, b, bRadius, k, kRadius, $"bk_{token}");

        var aligned = model.NewBoolVar(token);
        model.AddBoolAnd([overlapAB, overlapAK, overlapBK]).OnlyEnforceIf(aligned);
        model.AddBoolOr([overlapAB.Not(), overlapAK.Not(), overlapBK.Not(), aligned]);
        return aligned;
    }

    private static BoolVar BuildAxisOverlapBool(
        CpModel model,
        IntVar a,
        int aRadius,
        IntVar b,
        int bRadius,
        string token
    )
    {
        var radiusSum = aRadius + bRadius;
        var aLeBMax = model.NewBoolVar($"a_le_bmax_{token}");
        var bLeAMax = model.NewBoolVar($"b_le_amax_{token}");
        model.Add(a <= b + radiusSum).OnlyEnforceIf(aLeBMax);
        model.Add(a > b + radiusSum).OnlyEnforceIf(aLeBMax.Not());
        model.Add(b <= a + radiusSum).OnlyEnforceIf(bLeAMax);
        model.Add(b > a + radiusSum).OnlyEnforceIf(bLeAMax.Not());

        var overlap = model.NewBoolVar($"overlap_{token}");
        model.AddBoolAnd([aLeBMax, bLeAMax]).OnlyEnforceIf(overlap);
        model.AddBoolOr([aLeBMax.Not(), bLeAMax.Not(), overlap]);
        return overlap;
    }

    private static Dictionary<string, int> BuildInterveningOccupancyRadius(
        CircuitGraph graph,
        IReadOnlyList<string> deviceIds
    )
    {
        var radii = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var deviceId in deviceIds)
        {
            if (!graph.Devices.TryGetValue(deviceId, out var device))
            {
                radii[deviceId] = 0;
                continue;
            }

            var type = device.DeviceType.ToLowerInvariant();
            radii[deviceId] = type is "nmos" or "nfet" or "pmos" or "pfet" ? 1 : 0;
        }

        return radii;
    }

    private enum AxisDirection
    {
        Horizontal,
        Vertical,
    }

    private static int[] BuildAxisPassThroughTruthTable(
        Cascode.Language.DeviceDeclaration device,
        string netName,
        AxisDirection axis
    )
    {
        var truth = new int[16];
        for (var transform = 0; transform < 16; transform++)
        {
            var match = false;
            foreach (var (terminal, net) in device.Bindings)
            {
                if (!string.Equals(net, netName, StringComparison.Ordinal))
                {
                    continue;
                }

                var baseEdge = GetDefaultEdge(device.DeviceType, terminal);
                if (!baseEdge.HasValue)
                {
                    continue;
                }

                var edge = TransformEdge(baseEdge.Value, transform);
                match = axis switch
                {
                    AxisDirection.Horizontal => edge is Edge.West or Edge.East,
                    AxisDirection.Vertical => edge is Edge.North or Edge.South,
                    _ => false,
                };
                if (match)
                {
                    break;
                }
            }

            truth[transform] = match ? 1 : 0;
        }

        return truth;
    }

    private static void AddWireLengthObjective(
        CpModel model,
        CircuitGraph graph,
        IReadOnlyDictionary<string, IntVar> rows,
        IReadOnlyDictionary<string, IntVar> cols,
        IReadOnlyDictionary<string, IntVar> transforms,
        List<LinearExpr> objectives,
        int colDomain
    )
    {
        var rightPortX = (colDomain - 1) * 2 + 1;
        foreach (var (netName, refs) in graph.NetConnections)
        {
            if (graph.Supplies.Contains(netName) || graph.Grounds.Contains(netName))
            {
                // Rails are routed as dedicated full-width tracks; optimizing their span skews
                // placement away from minimizing signal-wire length.
                continue;
            }

            var terminals = refs.Where(r => rows.ContainsKey(r.DeviceId)).ToList();
            if (terminals.Count == 0)
            {
                continue;
            }

            var xCoords = new List<IntVar>(terminals.Count);
            var yCoords = new List<IntVar>(terminals.Count);
            var xOffsets = new List<IntVar>(terminals.Count);
            var yOffsets = new List<IntVar>(terminals.Count);
            for (var i = 0; i < terminals.Count; i++)
            {
                var terminal = terminals[i];
                var token =
                    $"{ToVarToken(netName)}_{ToVarToken(terminal.DeviceId)}_{ToVarToken(terminal.Terminal)}_{i}";

                var xOffsetTable = BuildAxisOffsetTable(
                    graph,
                    terminal.DeviceId,
                    terminal.Terminal,
                    axis: "x"
                );
                var yOffsetTable = BuildAxisOffsetTable(
                    graph,
                    terminal.DeviceId,
                    terminal.Terminal,
                    axis: "y"
                );
                var xOffset = model.NewIntVar(-1, 1, $"xoff_{token}");
                var yOffset = model.NewIntVar(-1, 1, $"yoff_{token}");
                model.AddElement(transforms[terminal.DeviceId], xOffsetTable, xOffset);
                model.AddElement(transforms[terminal.DeviceId], yOffsetTable, yOffset);

                var xCoord = model.NewIntVar(-200, 200, $"xcoord_{token}");
                var yCoord = model.NewIntVar(-200, 200, $"ycoord_{token}");
                model.Add(xCoord == cols[terminal.DeviceId] * 2 + xOffset);
                model.Add(yCoord == rows[terminal.DeviceId] * 2 + yOffset);
                xCoords.Add(xCoord);
                yCoords.Add(yCoord);
                xOffsets.Add(xOffset);
                yOffsets.Add(yOffset);
            }

            var minX = model.NewIntVar(-200, 200, $"minx_{ToVarToken(netName)}");
            var maxX = model.NewIntVar(-200, 200, $"maxx_{ToVarToken(netName)}");
            var minY = model.NewIntVar(-200, 200, $"miny_{ToVarToken(netName)}");
            var maxY = model.NewIntVar(-200, 200, $"maxy_{ToVarToken(netName)}");

            foreach (var xCoord in xCoords)
            {
                model.Add(xCoord >= minX);
                model.Add(xCoord <= maxX);
            }
            foreach (var yCoord in yCoords)
            {
                model.Add(yCoord >= minY);
                model.Add(yCoord <= maxY);
            }

            // Treat port-to-component traces as regular net span by pinning the port X anchor
            // into the same min/max envelope used for component terminals.
            if (graph.InputPorts.Contains(netName) || graph.BiasPorts.Contains(netName))
            {
                model.Add(minX <= -1);
                model.Add(maxX >= -1);
            }
            if (graph.OutputPorts.Contains(netName))
            {
                model.Add(minX <= rightPortX);
                model.Add(maxX >= rightPortX);
            }

            var spanX = model.NewIntVar(0, 400, $"spanx_{ToVarToken(netName)}");
            var spanY = model.NewIntVar(0, 400, $"spany_{ToVarToken(netName)}");
            model.Add(spanX == maxX - minX);
            model.Add(spanY == maxY - minY);
            objectives.Add(spanX + spanY);

            AddCornerPenaltyObjective(
                model,
                xCoords,
                yCoords,
                xOffsets,
                yOffsets,
                objectives,
                netName,
                AxisMismatchPenaltyWeight
            );
        }
    }

    private static void AddCornerPenaltyObjective(
        CpModel model,
        IReadOnlyList<IntVar> xCoords,
        IReadOnlyList<IntVar> yCoords,
        IReadOnlyList<IntVar> xOffsets,
        IReadOnlyList<IntVar> yOffsets,
        List<LinearExpr> objectives,
        string netName,
        int cornerPenaltyWeight
    )
    {
        for (var i = 0; i < xCoords.Count; i++)
        {
            for (var j = i + 1; j < xCoords.Count; j++)
            {
                var token = $"{ToVarToken(netName)}_{i}_{j}";
                var sameX = model.NewBoolVar($"corner_same_x_{token}");
                var sameY = model.NewBoolVar($"corner_same_y_{token}");
                model.Add(xCoords[i] == xCoords[j]).OnlyEnforceIf(sameX);
                model.Add(xCoords[i] != xCoords[j]).OnlyEnforceIf(sameX.Not());
                model.Add(yCoords[i] == yCoords[j]).OnlyEnforceIf(sameY);
                model.Add(yCoords[i] != yCoords[j]).OnlyEnforceIf(sameY.Not());

                var sourceVertical = model.NewBoolVar($"corner_src_vertical_{token}");
                model.Add(xOffsets[i] == 0).OnlyEnforceIf(sourceVertical);
                model.Add(xOffsets[i] != 0).OnlyEnforceIf(sourceVertical.Not());

                var sourceHorizontal = model.NewBoolVar($"corner_src_horizontal_{token}");
                model.Add(yOffsets[i] == 0).OnlyEnforceIf(sourceHorizontal);
                model.Add(yOffsets[i] != 0).OnlyEnforceIf(sourceHorizontal.Not());

                var targetVertical = model.NewBoolVar($"corner_dst_vertical_{token}");
                model.Add(xOffsets[j] == 0).OnlyEnforceIf(targetVertical);
                model.Add(xOffsets[j] != 0).OnlyEnforceIf(targetVertical.Not());

                var targetHorizontal = model.NewBoolVar($"corner_dst_horizontal_{token}");
                model.Add(yOffsets[j] == 0).OnlyEnforceIf(targetHorizontal);
                model.Add(yOffsets[j] != 0).OnlyEnforceIf(targetHorizontal.Not());

                var sourceEast = model.NewBoolVar($"corner_src_east_{token}");
                model.Add(xOffsets[i] == 1).OnlyEnforceIf(sourceEast);
                model.Add(xOffsets[i] != 1).OnlyEnforceIf(sourceEast.Not());
                var sourceWest = model.NewBoolVar($"corner_src_west_{token}");
                model.Add(xOffsets[i] == -1).OnlyEnforceIf(sourceWest);
                model.Add(xOffsets[i] != -1).OnlyEnforceIf(sourceWest.Not());
                var sourceSouth = model.NewBoolVar($"corner_src_south_{token}");
                model.Add(yOffsets[i] == 1).OnlyEnforceIf(sourceSouth);
                model.Add(yOffsets[i] != 1).OnlyEnforceIf(sourceSouth.Not());
                var sourceNorth = model.NewBoolVar($"corner_src_north_{token}");
                model.Add(yOffsets[i] == -1).OnlyEnforceIf(sourceNorth);
                model.Add(yOffsets[i] != -1).OnlyEnforceIf(sourceNorth.Not());

                var targetEast = model.NewBoolVar($"corner_dst_east_{token}");
                model.Add(xOffsets[j] == 1).OnlyEnforceIf(targetEast);
                model.Add(xOffsets[j] != 1).OnlyEnforceIf(targetEast.Not());
                var targetWest = model.NewBoolVar($"corner_dst_west_{token}");
                model.Add(xOffsets[j] == -1).OnlyEnforceIf(targetWest);
                model.Add(xOffsets[j] != -1).OnlyEnforceIf(targetWest.Not());
                var targetSouth = model.NewBoolVar($"corner_dst_south_{token}");
                model.Add(yOffsets[j] == 1).OnlyEnforceIf(targetSouth);
                model.Add(yOffsets[j] != 1).OnlyEnforceIf(targetSouth.Not());
                var targetNorth = model.NewBoolVar($"corner_dst_north_{token}");
                model.Add(yOffsets[j] == -1).OnlyEnforceIf(targetNorth);
                model.Add(yOffsets[j] != -1).OnlyEnforceIf(targetNorth.Not());

                var xjLtXi = model.NewBoolVar($"corner_xj_lt_xi_{token}");
                model.Add(xCoords[j] <= xCoords[i] - 1).OnlyEnforceIf(xjLtXi);
                model.Add(xCoords[j] >= xCoords[i]).OnlyEnforceIf(xjLtXi.Not());
                var xjGtXi = model.NewBoolVar($"corner_xj_gt_xi_{token}");
                model.Add(xCoords[j] >= xCoords[i] + 1).OnlyEnforceIf(xjGtXi);
                model.Add(xCoords[j] <= xCoords[i]).OnlyEnforceIf(xjGtXi.Not());
                var yjLtYi = model.NewBoolVar($"corner_yj_lt_yi_{token}");
                model.Add(yCoords[j] <= yCoords[i] - 1).OnlyEnforceIf(yjLtYi);
                model.Add(yCoords[j] >= yCoords[i]).OnlyEnforceIf(yjLtYi.Not());
                var yjGtYi = model.NewBoolVar($"corner_yj_gt_yi_{token}");
                model.Add(yCoords[j] >= yCoords[i] + 1).OnlyEnforceIf(yjGtYi);
                model.Add(yCoords[j] <= yCoords[i]).OnlyEnforceIf(yjGtYi.Not());

                var srcOppXEast = model.NewBoolVar($"corner_src_opp_x_e_{token}");
                model.AddBoolAnd([sourceEast, xjLtXi]).OnlyEnforceIf(srcOppXEast);
                model.AddBoolOr([sourceEast.Not(), xjLtXi.Not(), srcOppXEast]);
                var srcOppXWest = model.NewBoolVar($"corner_src_opp_x_w_{token}");
                model.AddBoolAnd([sourceWest, xjGtXi]).OnlyEnforceIf(srcOppXWest);
                model.AddBoolOr([sourceWest.Not(), xjGtXi.Not(), srcOppXWest]);
                var srcOppX = model.NewBoolVar($"corner_src_opp_x_{token}");
                model.AddBoolOr([srcOppXEast, srcOppXWest]).OnlyEnforceIf(srcOppX);
                model.AddImplication(srcOppXEast, srcOppX);
                model.AddImplication(srcOppXWest, srcOppX);

                var srcOppYNorth = model.NewBoolVar($"corner_src_opp_y_n_{token}");
                model.AddBoolAnd([sourceNorth, yjGtYi]).OnlyEnforceIf(srcOppYNorth);
                model.AddBoolOr([sourceNorth.Not(), yjGtYi.Not(), srcOppYNorth]);
                var srcOppYSouth = model.NewBoolVar($"corner_src_opp_y_s_{token}");
                model.AddBoolAnd([sourceSouth, yjLtYi]).OnlyEnforceIf(srcOppYSouth);
                model.AddBoolOr([sourceSouth.Not(), yjLtYi.Not(), srcOppYSouth]);
                var srcOppY = model.NewBoolVar($"corner_src_opp_y_{token}");
                model.AddBoolOr([srcOppYNorth, srcOppYSouth]).OnlyEnforceIf(srcOppY);
                model.AddImplication(srcOppYNorth, srcOppY);
                model.AddImplication(srcOppYSouth, srcOppY);

                var dstOppXEast = model.NewBoolVar($"corner_dst_opp_x_e_{token}");
                model.AddBoolAnd([targetEast, xjGtXi]).OnlyEnforceIf(dstOppXEast);
                model.AddBoolOr([targetEast.Not(), xjGtXi.Not(), dstOppXEast]);
                var dstOppXWest = model.NewBoolVar($"corner_dst_opp_x_w_{token}");
                model.AddBoolAnd([targetWest, xjLtXi]).OnlyEnforceIf(dstOppXWest);
                model.AddBoolOr([targetWest.Not(), xjLtXi.Not(), dstOppXWest]);
                var dstOppX = model.NewBoolVar($"corner_dst_opp_x_{token}");
                model.AddBoolOr([dstOppXEast, dstOppXWest]).OnlyEnforceIf(dstOppX);
                model.AddImplication(dstOppXEast, dstOppX);
                model.AddImplication(dstOppXWest, dstOppX);

                var dstOppYNorth = model.NewBoolVar($"corner_dst_opp_y_n_{token}");
                model.AddBoolAnd([targetNorth, yjLtYi]).OnlyEnforceIf(dstOppYNorth);
                model.AddBoolOr([targetNorth.Not(), yjLtYi.Not(), dstOppYNorth]);
                var dstOppYSouth = model.NewBoolVar($"corner_dst_opp_y_s_{token}");
                model.AddBoolAnd([targetSouth, yjGtYi]).OnlyEnforceIf(dstOppYSouth);
                model.AddBoolOr([targetSouth.Not(), yjGtYi.Not(), dstOppYSouth]);
                var dstOppY = model.NewBoolVar($"corner_dst_opp_y_{token}");
                model.AddBoolOr([dstOppYNorth, dstOppYSouth]).OnlyEnforceIf(dstOppY);
                model.AddImplication(dstOppYNorth, dstOppY);
                model.AddImplication(dstOppYSouth, dstOppY);

                var cornerNeeded = model.NewBoolVar($"corner_needed_{token}");
                model.AddBoolAnd([sameX.Not(), sameY.Not()]).OnlyEnforceIf(cornerNeeded);
                model.AddBoolOr([sameX, sameY, cornerNeeded]);
                objectives.Add(cornerNeeded * cornerPenaltyWeight);

                // If both terminals exit along the same axis and are separated in both X/Y,
                // outward-only routing requires 4 corners total.
                // cornerNeeded already contributes 1, so add 3 more in this case.
                var extraVerticalDogleg = model.NewBoolVar($"corner_extra_vertical_{token}");
                model
                    .AddBoolAnd([sameX.Not(), sameY.Not(), sourceVertical, targetVertical])
                    .OnlyEnforceIf(extraVerticalDogleg);
                model.AddBoolOr([
                    sameX,
                    sameY,
                    sourceVertical.Not(),
                    targetVertical.Not(),
                    extraVerticalDogleg,
                ]);
                objectives.Add(extraVerticalDogleg * (3 * cornerPenaltyWeight));

                var extraHorizontalDogleg = model.NewBoolVar($"corner_extra_horizontal_{token}");
                model
                    .AddBoolAnd([sameX.Not(), sameY.Not(), sourceHorizontal, targetHorizontal])
                    .OnlyEnforceIf(extraHorizontalDogleg);
                model.AddBoolOr([
                    sameX,
                    sameY,
                    sourceHorizontal.Not(),
                    targetHorizontal.Not(),
                    extraHorizontalDogleg,
                ]);
                objectives.Add(extraHorizontalDogleg * (3 * cornerPenaltyWeight));

                var srcForwardXWitnesses = new List<BoolVar>();
                var srcForwardYWitnesses = new List<BoolVar>();
                var dstForwardXWitnesses = new List<BoolVar>();
                var dstForwardYWitnesses = new List<BoolVar>();
                for (var k = 0; k < xCoords.Count; k++)
                {
                    if (k == i || k == j)
                    {
                        continue;
                    }

                    var sameYik = model.NewBoolVar($"corner_same_y_{token}_{k}_ik");
                    model.Add(yCoords[i] == yCoords[k]).OnlyEnforceIf(sameYik);
                    model.Add(yCoords[i] != yCoords[k]).OnlyEnforceIf(sameYik.Not());
                    var xkGtXi = model.NewBoolVar($"corner_xk_gt_xi_{token}_{k}");
                    model.Add(xCoords[k] >= xCoords[i] + 1).OnlyEnforceIf(xkGtXi);
                    model.Add(xCoords[k] <= xCoords[i]).OnlyEnforceIf(xkGtXi.Not());
                    var xkLtXi = model.NewBoolVar($"corner_xk_lt_xi_{token}_{k}");
                    model.Add(xCoords[k] <= xCoords[i] - 1).OnlyEnforceIf(xkLtXi);
                    model.Add(xCoords[k] >= xCoords[i]).OnlyEnforceIf(xkLtXi.Not());
                    var srcForwardX = model.NewBoolVar($"corner_src_fwd_x_{token}_{k}");
                    model.AddBoolAnd([sameYik, sourceEast, xkGtXi]).OnlyEnforceIf(srcForwardX);
                    model.AddBoolOr([sameYik.Not(), sourceEast.Not(), xkGtXi.Not(), srcForwardX]);
                    var srcForwardXAlt = model.NewBoolVar($"corner_src_fwd_x_alt_{token}_{k}");
                    model.AddBoolAnd([sameYik, sourceWest, xkLtXi]).OnlyEnforceIf(srcForwardXAlt);
                    model.AddBoolOr([
                        sameYik.Not(),
                        sourceWest.Not(),
                        xkLtXi.Not(),
                        srcForwardXAlt,
                    ]);
                    var srcForwardXAny = model.NewBoolVar($"corner_src_fwd_x_any_{token}_{k}");
                    model.AddBoolOr([srcForwardX, srcForwardXAlt]).OnlyEnforceIf(srcForwardXAny);
                    model.AddImplication(srcForwardX, srcForwardXAny);
                    model.AddImplication(srcForwardXAlt, srcForwardXAny);
                    srcForwardXWitnesses.Add(srcForwardXAny);

                    var sameXik = model.NewBoolVar($"corner_same_x_{token}_{k}_ik");
                    model.Add(xCoords[i] == xCoords[k]).OnlyEnforceIf(sameXik);
                    model.Add(xCoords[i] != xCoords[k]).OnlyEnforceIf(sameXik.Not());
                    var ykGtYi = model.NewBoolVar($"corner_yk_gt_yi_{token}_{k}");
                    model.Add(yCoords[k] >= yCoords[i] + 1).OnlyEnforceIf(ykGtYi);
                    model.Add(yCoords[k] <= yCoords[i]).OnlyEnforceIf(ykGtYi.Not());
                    var ykLtYi = model.NewBoolVar($"corner_yk_lt_yi_{token}_{k}");
                    model.Add(yCoords[k] <= yCoords[i] - 1).OnlyEnforceIf(ykLtYi);
                    model.Add(yCoords[k] >= yCoords[i]).OnlyEnforceIf(ykLtYi.Not());
                    var srcForwardY = model.NewBoolVar($"corner_src_fwd_y_{token}_{k}");
                    model.AddBoolAnd([sameXik, sourceSouth, ykGtYi]).OnlyEnforceIf(srcForwardY);
                    model.AddBoolOr([sameXik.Not(), sourceSouth.Not(), ykGtYi.Not(), srcForwardY]);
                    var srcForwardYAlt = model.NewBoolVar($"corner_src_fwd_y_alt_{token}_{k}");
                    model.AddBoolAnd([sameXik, sourceNorth, ykLtYi]).OnlyEnforceIf(srcForwardYAlt);
                    model.AddBoolOr([
                        sameXik.Not(),
                        sourceNorth.Not(),
                        ykLtYi.Not(),
                        srcForwardYAlt,
                    ]);
                    var srcForwardYAny = model.NewBoolVar($"corner_src_fwd_y_any_{token}_{k}");
                    model.AddBoolOr([srcForwardY, srcForwardYAlt]).OnlyEnforceIf(srcForwardYAny);
                    model.AddImplication(srcForwardY, srcForwardYAny);
                    model.AddImplication(srcForwardYAlt, srcForwardYAny);
                    srcForwardYWitnesses.Add(srcForwardYAny);

                    var sameYjk = model.NewBoolVar($"corner_same_y_{token}_{k}_jk");
                    model.Add(yCoords[j] == yCoords[k]).OnlyEnforceIf(sameYjk);
                    model.Add(yCoords[j] != yCoords[k]).OnlyEnforceIf(sameYjk.Not());
                    var xkGtXj = model.NewBoolVar($"corner_xk_gt_xj_{token}_{k}");
                    model.Add(xCoords[k] >= xCoords[j] + 1).OnlyEnforceIf(xkGtXj);
                    model.Add(xCoords[k] <= xCoords[j]).OnlyEnforceIf(xkGtXj.Not());
                    var xkLtXj = model.NewBoolVar($"corner_xk_lt_xj_{token}_{k}");
                    model.Add(xCoords[k] <= xCoords[j] - 1).OnlyEnforceIf(xkLtXj);
                    model.Add(xCoords[k] >= xCoords[j]).OnlyEnforceIf(xkLtXj.Not());
                    var dstForwardX = model.NewBoolVar($"corner_dst_fwd_x_{token}_{k}");
                    model.AddBoolAnd([sameYjk, targetEast, xkGtXj]).OnlyEnforceIf(dstForwardX);
                    model.AddBoolOr([sameYjk.Not(), targetEast.Not(), xkGtXj.Not(), dstForwardX]);
                    var dstForwardXAlt = model.NewBoolVar($"corner_dst_fwd_x_alt_{token}_{k}");
                    model.AddBoolAnd([sameYjk, targetWest, xkLtXj]).OnlyEnforceIf(dstForwardXAlt);
                    model.AddBoolOr([
                        sameYjk.Not(),
                        targetWest.Not(),
                        xkLtXj.Not(),
                        dstForwardXAlt,
                    ]);
                    var dstForwardXAny = model.NewBoolVar($"corner_dst_fwd_x_any_{token}_{k}");
                    model.AddBoolOr([dstForwardX, dstForwardXAlt]).OnlyEnforceIf(dstForwardXAny);
                    model.AddImplication(dstForwardX, dstForwardXAny);
                    model.AddImplication(dstForwardXAlt, dstForwardXAny);
                    dstForwardXWitnesses.Add(dstForwardXAny);

                    var sameXjk = model.NewBoolVar($"corner_same_x_{token}_{k}_jk");
                    model.Add(xCoords[j] == xCoords[k]).OnlyEnforceIf(sameXjk);
                    model.Add(xCoords[j] != xCoords[k]).OnlyEnforceIf(sameXjk.Not());
                    var ykGtYj = model.NewBoolVar($"corner_yk_gt_yj_{token}_{k}");
                    model.Add(yCoords[k] >= yCoords[j] + 1).OnlyEnforceIf(ykGtYj);
                    model.Add(yCoords[k] <= yCoords[j]).OnlyEnforceIf(ykGtYj.Not());
                    var ykLtYj = model.NewBoolVar($"corner_yk_lt_yj_{token}_{k}");
                    model.Add(yCoords[k] <= yCoords[j] - 1).OnlyEnforceIf(ykLtYj);
                    model.Add(yCoords[k] >= yCoords[j]).OnlyEnforceIf(ykLtYj.Not());
                    var dstForwardY = model.NewBoolVar($"corner_dst_fwd_y_{token}_{k}");
                    model.AddBoolAnd([sameXjk, targetSouth, ykGtYj]).OnlyEnforceIf(dstForwardY);
                    model.AddBoolOr([sameXjk.Not(), targetSouth.Not(), ykGtYj.Not(), dstForwardY]);
                    var dstForwardYAlt = model.NewBoolVar($"corner_dst_fwd_y_alt_{token}_{k}");
                    model.AddBoolAnd([sameXjk, targetNorth, ykLtYj]).OnlyEnforceIf(dstForwardYAlt);
                    model.AddBoolOr([
                        sameXjk.Not(),
                        targetNorth.Not(),
                        ykLtYj.Not(),
                        dstForwardYAlt,
                    ]);
                    var dstForwardYAny = model.NewBoolVar($"corner_dst_fwd_y_any_{token}_{k}");
                    model.AddBoolOr([dstForwardY, dstForwardYAlt]).OnlyEnforceIf(dstForwardYAny);
                    model.AddImplication(dstForwardY, dstForwardYAny);
                    model.AddImplication(dstForwardYAlt, dstForwardYAny);
                    dstForwardYWitnesses.Add(dstForwardYAny);
                }

                var srcHasForwardX = model.NewBoolVar($"corner_src_has_fwd_x_{token}");
                if (srcForwardXWitnesses.Count == 0)
                {
                    model.Add(srcHasForwardX == 0);
                }
                else
                {
                    model.AddBoolOr(srcForwardXWitnesses).OnlyEnforceIf(srcHasForwardX);
                    foreach (var witness in srcForwardXWitnesses)
                    {
                        model.AddImplication(witness, srcHasForwardX);
                    }
                }

                var srcHasForwardY = model.NewBoolVar($"corner_src_has_fwd_y_{token}");
                if (srcForwardYWitnesses.Count == 0)
                {
                    model.Add(srcHasForwardY == 0);
                }
                else
                {
                    model.AddBoolOr(srcForwardYWitnesses).OnlyEnforceIf(srcHasForwardY);
                    foreach (var witness in srcForwardYWitnesses)
                    {
                        model.AddImplication(witness, srcHasForwardY);
                    }
                }

                var dstHasForwardX = model.NewBoolVar($"corner_dst_has_fwd_x_{token}");
                if (dstForwardXWitnesses.Count == 0)
                {
                    model.Add(dstHasForwardX == 0);
                }
                else
                {
                    model.AddBoolOr(dstForwardXWitnesses).OnlyEnforceIf(dstHasForwardX);
                    foreach (var witness in dstForwardXWitnesses)
                    {
                        model.AddImplication(witness, dstHasForwardX);
                    }
                }

                var dstHasForwardY = model.NewBoolVar($"corner_dst_has_fwd_y_{token}");
                if (dstForwardYWitnesses.Count == 0)
                {
                    model.Add(dstHasForwardY == 0);
                }
                else
                {
                    model.AddBoolOr(dstForwardYWitnesses).OnlyEnforceIf(dstHasForwardY);
                    foreach (var witness in dstForwardYWitnesses)
                    {
                        model.AddImplication(witness, dstHasForwardY);
                    }
                }

                var srcUturnXPenalty = model.NewBoolVar($"corner_src_uturn_x_{token}");
                model
                    .AddBoolAnd([sameY, srcOppX, srcHasForwardX.Not()])
                    .OnlyEnforceIf(srcUturnXPenalty);
                model.AddBoolOr([sameY.Not(), srcOppX.Not(), srcHasForwardX, srcUturnXPenalty]);
                objectives.Add(srcUturnXPenalty * UTurnPenaltyWeight);

                var srcUturnYPenalty = model.NewBoolVar($"corner_src_uturn_y_{token}");
                model
                    .AddBoolAnd([sameX, srcOppY, srcHasForwardY.Not()])
                    .OnlyEnforceIf(srcUturnYPenalty);
                model.AddBoolOr([sameX.Not(), srcOppY.Not(), srcHasForwardY, srcUturnYPenalty]);
                objectives.Add(srcUturnYPenalty * UTurnPenaltyWeight);

                var dstUturnXPenalty = model.NewBoolVar($"corner_dst_uturn_x_{token}");
                model
                    .AddBoolAnd([sameY, dstOppX, dstHasForwardX.Not()])
                    .OnlyEnforceIf(dstUturnXPenalty);
                model.AddBoolOr([sameY.Not(), dstOppX.Not(), dstHasForwardX, dstUturnXPenalty]);
                objectives.Add(dstUturnXPenalty * UTurnPenaltyWeight);

                var dstUturnYPenalty = model.NewBoolVar($"corner_dst_uturn_y_{token}");
                model
                    .AddBoolAnd([sameX, dstOppY, dstHasForwardY.Not()])
                    .OnlyEnforceIf(dstUturnYPenalty);
                model.AddBoolOr([sameX.Not(), dstOppY.Not(), dstHasForwardY, dstUturnYPenalty]);
                objectives.Add(dstUturnYPenalty * UTurnPenaltyWeight);
            }
        }
    }

    private static int[] BuildAxisOffsetTable(
        CircuitGraph graph,
        string deviceId,
        string terminal,
        string axis
    )
    {
        var result = new int[16];
        if (!graph.Devices.TryGetValue(deviceId, out var device))
        {
            return result;
        }

        var deviceType = device.DeviceType.ToLowerInvariant();
        if (deviceType is "nmos" or "nfet" or "pmos" or "pfet")
        {
            return BuildMosfetAxisOffsetTable(deviceType, terminal, axis);
        }

        var baseEdge = GetDefaultEdge(device.DeviceType, terminal);
        for (var t = 0; t < 16; t++)
        {
            if (!baseEdge.HasValue)
            {
                result[t] = 0;
                continue;
            }

            var edge = TransformEdge(baseEdge.Value, t);
            result[t] =
                axis == "x"
                    ? edge switch
                    {
                        Edge.East => 1,
                        Edge.West => -1,
                        _ => 0,
                    }
                    : edge switch
                    {
                        Edge.South => 1,
                        Edge.North => -1,
                        _ => 0,
                    };
        }

        return result;
    }

    private static int[] BuildMosfetAxisOffsetTable(string deviceType, string terminal, string axis)
    {
        var result = new int[16];
        var t = terminal.ToUpperInvariant();
        var isPmos = deviceType is "pmos" or "pfet";
        for (var transform = 0; transform < 16; transform++)
        {
            var mirrorX = (transform % 4) / 2 == 1;
            if (axis == "x")
            {
                result[transform] = t == "G" ? (mirrorX ? 1 : -1) : 0;
                continue;
            }

            result[transform] = t switch
            {
                "D" => isPmos ? 1 : -1,
                "S" => isPmos ? -1 : 1,
                _ => 0,
            };
        }

        return result;
    }

    private static void AddPortSideObjectives(
        CircuitGraph graph,
        IReadOnlyDictionary<string, IntVar> cols,
        List<LinearExpr> objectives,
        int colDomain
    )
    {
        foreach (var port in graph.InputPorts)
        {
            if (!graph.NetConnections.TryGetValue(port, out var refs))
            {
                continue;
            }

            foreach (var terminal in refs)
            {
                if (cols.TryGetValue(terminal.DeviceId, out var c))
                {
                    objectives.Add(c * InPortWeight);
                }
            }
        }

        foreach (var port in graph.OutputPorts)
        {
            if (!graph.NetConnections.TryGetValue(port, out var refs))
            {
                continue;
            }

            foreach (var terminal in refs)
            {
                if (cols.TryGetValue(terminal.DeviceId, out var c))
                {
                    objectives.Add((colDomain - 1 - c) * OutPortWeight);
                }
            }
        }
    }

    private static void AddSymmetrySoftObjectives(
        CpModel model,
        IReadOnlyList<SymmetricGroup> groups,
        IReadOnlyDictionary<string, IntVar> rows,
        IReadOnlyDictionary<string, IntVar> cols,
        List<LinearExpr> objectives,
        int symmetryAxis
    )
    {
        foreach (var group in groups)
        {
            var ids = group.DeviceIds.Where(id => cols.ContainsKey(id)).ToList();
            for (var i = 0; i < ids.Count / 2; i++)
            {
                var left = ids[i];
                var right = ids[ids.Count - 1 - i];
                var axisDiff = model.NewIntVar(
                    0,
                    500,
                    $"sym_col_{ToVarToken(left)}_{ToVarToken(right)}"
                );
                model.AddAbsEquality(axisDiff, cols[left] + cols[right] - 2 * symmetryAxis);
                var rowDiff = model.NewIntVar(
                    0,
                    500,
                    $"sym_row_{ToVarToken(left)}_{ToVarToken(right)}"
                );
                model.AddAbsEquality(rowDiff, rows[left] - rows[right]);
                objectives.Add(axisDiff * SymmetryWeight);
                objectives.Add(rowDiff * SymmetryWeight);
            }
        }
    }

    private static void AddSharedSignalCmosAxisObjectives(
        CpModel model,
        CircuitGraph graph,
        IReadOnlyDictionary<string, IntVar> rows,
        IReadOnlyDictionary<string, IntVar> cols,
        List<LinearExpr> objectives
    )
    {
        foreach (var (deviceA, deviceB, netName) in EnumerateSharedSignalCmosPairs(graph))
        {
            if (
                !rows.TryGetValue(deviceA, out var rowA)
                || !rows.TryGetValue(deviceB, out var rowB)
                || !cols.TryGetValue(deviceA, out var colA)
                || !cols.TryGetValue(deviceB, out var colB)
            )
            {
                continue;
            }

            var token = $"{ToVarToken(netName)}_{ToVarToken(deviceA)}_{ToVarToken(deviceB)}";
            var sameRow = model.NewBoolVar($"cmos_axis_same_row_{token}");
            model.Add(rowA == rowB).OnlyEnforceIf(sameRow);
            model.Add(rowA != rowB).OnlyEnforceIf(sameRow.Not());

            var sameCol = model.NewBoolVar($"cmos_axis_same_col_{token}");
            model.Add(colA == colB).OnlyEnforceIf(sameCol);
            model.Add(colA != colB).OnlyEnforceIf(sameCol.Not());

            // Penalize only when both row and column differ.
            var misaligned = model.NewBoolVar($"cmos_axis_misaligned_{token}");
            model.AddBoolAnd([sameRow.Not(), sameCol.Not()]).OnlyEnforceIf(misaligned);
            model.AddBoolOr([sameRow, sameCol, misaligned]);
            objectives.Add(misaligned * SharedSignalCmosAxisPenaltyWeight);
        }
    }

    private static void AddSameFlavorDrainSourceMirrorObjectives(
        CpModel model,
        CircuitGraph graph,
        IReadOnlyDictionary<string, IntVar> transforms,
        List<LinearExpr> objectives,
        int weight
    )
    {
        if (weight <= 0)
        {
            return;
        }

        var mirrorXByDevice = new Dictionary<string, BoolVar>(StringComparer.Ordinal);

        BoolVar GetMirrorXBool(string deviceId)
        {
            if (mirrorXByDevice.TryGetValue(deviceId, out var cached))
            {
                return cached;
            }

            var token = ToVarToken(deviceId);
            var mirrorXByTransform = new[] { 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1 };
            var mirrorXInt = model.NewIntVar(0, 1, $"mirror_x_int_{token}");
            model.AddElement(transforms[deviceId], mirrorXByTransform, mirrorXInt);

            var mirrorX = model.NewBoolVar($"mirror_x_{token}");
            model.Add(mirrorXInt == 1).OnlyEnforceIf(mirrorX);
            model.Add(mirrorXInt == 0).OnlyEnforceIf(mirrorX.Not());
            mirrorXByDevice[deviceId] = mirrorX;
            return mirrorX;
        }

        foreach (var (deviceA, deviceB, netName) in EnumerateSameFlavorDrainSourcePairs(graph))
        {
            if (!transforms.ContainsKey(deviceA) || !transforms.ContainsKey(deviceB))
            {
                continue;
            }

            var mirrorXA = GetMirrorXBool(deviceA);
            var mirrorXB = GetMirrorXBool(deviceB);
            var token = $"{ToVarToken(netName)}_{ToVarToken(deviceA)}_{ToVarToken(deviceB)}";
            var mismatch = model.NewBoolVar($"same_flavor_ds_mirror_mismatch_{token}");
            model.Add(mirrorXA != mirrorXB).OnlyEnforceIf(mismatch);
            model.Add(mirrorXA == mirrorXB).OnlyEnforceIf(mismatch.Not());
            objectives.Add(mismatch * weight);
        }
    }

    private static IEnumerable<(
        string DeviceA,
        string DeviceB,
        string NetName
    )> EnumerateSharedSignalCmosPairs(CircuitGraph graph)
    {
        var yielded = new HashSet<(string DeviceA, string DeviceB)>();
        foreach (var (netName, refs) in graph.NetConnections)
        {
            if (graph.Supplies.Contains(netName) || graph.Grounds.Contains(netName))
            {
                continue;
            }

            var cmosIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var terminalRef in refs)
            {
                if (IsBodyOrShieldTerminal(terminalRef.Terminal))
                {
                    continue;
                }

                if (!graph.Devices.TryGetValue(terminalRef.DeviceId, out var device))
                {
                    continue;
                }

                var type = device.DeviceType.ToLowerInvariant();
                if (type is "nmos" or "nfet" or "pmos" or "pfet")
                {
                    cmosIds.Add(terminalRef.DeviceId);
                }
            }

            var sorted = cmosIds.OrderBy(id => id, StringComparer.Ordinal).ToList();
            for (var i = 0; i < sorted.Count; i++)
            {
                for (var j = i + 1; j < sorted.Count; j++)
                {
                    var key = (sorted[i], sorted[j]);
                    if (yielded.Add(key))
                    {
                        yield return (sorted[i], sorted[j], netName);
                    }
                }
            }
        }
    }

    private static IEnumerable<(
        string DeviceA,
        string DeviceB,
        string NetName
    )> EnumerateSameFlavorDrainSourcePairs(CircuitGraph graph)
    {
        var yielded = new HashSet<(string DeviceA, string DeviceB)>();
        foreach (var (netName, refs) in graph.NetConnections)
        {
            if (graph.IsSupplyOrGround(netName))
            {
                continue;
            }

            var drainSourceRefs = refs.Where(r => IsDrainOrSourceTerminal(r.Terminal)).ToList();
            if (drainSourceRefs.Count != 2)
            {
                continue;
            }

            var first = drainSourceRefs[0];
            var second = drainSourceRefs[1];
            if (
                first.DeviceId == second.DeviceId
                || !IsDrainSourcePair(first.Terminal, second.Terminal)
            )
            {
                continue;
            }

            var firstFlavor = GetMosFlavor(graph, first.DeviceId);
            var secondFlavor = GetMosFlavor(graph, second.DeviceId);
            if (
                firstFlavor is null
                || secondFlavor is null
                || !string.Equals(firstFlavor, secondFlavor, StringComparison.Ordinal)
            )
            {
                continue;
            }

            var deviceA =
                string.CompareOrdinal(first.DeviceId, second.DeviceId) <= 0
                    ? first.DeviceId
                    : second.DeviceId;
            var deviceB = deviceA == first.DeviceId ? second.DeviceId : first.DeviceId;
            if (yielded.Add((deviceA, deviceB)))
            {
                yield return (deviceA, deviceB, netName);
            }
        }
    }

    private static bool AddRenderPlacementConstraints(
        CpModel model,
        IReadOnlyDictionary<string, IntVar> deviceRow,
        IReadOnlyDictionary<string, IntVar> deviceColumn,
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

            if (entry.Strength == RenderConstraintStrength.Hard)
            {
                model.Add(rowVar == targetRow);
                model.Add(colVar == targetCol);
                hasHardConstraints = true;
                hardConstraintEntities.Add(entry.DeviceId);
            }
        }

        return hasHardConstraints;
    }

    private static CoarseGridResult FallbackPlacement(
        TopologyResult topology,
        IReadOnlyList<string> deviceIds,
        IReadOnlySet<string> horizontalPassiveIds
    )
    {
        var placements = new Dictionary<string, GridCell>(StringComparer.Ordinal);
        foreach (var id in deviceIds)
        {
            var row = topology.DeviceRows.TryGetValue(id, out var r) ? r : 0;
            var col = Math.Abs(id.GetHashCode(StringComparison.Ordinal)) % 5;
            placements[id] = new GridCell(row, col);
        }

        var compacted = CompactPlacement(placements, symmetryAxis: 2);
        return new CoarseGridResult
        {
            RowCount = compacted.RowCount,
            ColumnCount = compacted.ColumnCount,
            SymmetryAxis = compacted.SymmetryAxis,
            DevicePlacements = compacted.Cells,
            HorizontalPassiveIds = horizontalPassiveIds,
        };
    }

    private static (
        Dictionary<string, GridCell> Cells,
        int RowCount,
        int ColumnCount,
        int SymmetryAxis
    ) CompactPlacement(IReadOnlyDictionary<string, GridCell> rawCells, int symmetryAxis)
    {
        if (rawCells.Count == 0)
        {
            return (new Dictionary<string, GridCell>(StringComparer.Ordinal), 1, 1, 0);
        }

        var usedRows = rawCells.Values.Select(c => c.Row).Distinct().OrderBy(r => r).ToList();
        var usedCols = rawCells.Values.Select(c => c.Column).Distinct().OrderBy(c => c).ToList();
        var columnPitch = usedCols.Count >= ColumnSpacingThreshold ? ExpandedColumnPitch : 1;
        var rowMap = usedRows
            .Select((row, idx) => (row, idx))
            .ToDictionary(pair => pair.row, pair => pair.idx);
        var colMap = usedCols
            .Select((col, idx) => (col, idx * columnPitch))
            .ToDictionary(pair => pair.col, pair => pair.Item2);

        var cells = new Dictionary<string, GridCell>(StringComparer.Ordinal);
        foreach (var (id, cell) in rawCells)
        {
            cells[id] = new GridCell(
                row: rowMap[cell.Row],
                column: colMap[cell.Column],
                rotation: cell.Rotation,
                MirrorX: cell.MirrorX,
                MirrorY: cell.MirrorY
            );
        }

        var rowCount = usedRows.Count;
        var colCount = usedCols.Count == 0 ? 1 : colMap[usedCols[^1]] + 1;
        var mappedSymmetryAxis = colMap.TryGetValue(symmetryAxis, out var exactAxis)
            ? exactAxis
            : usedCols.Count(col => col < symmetryAxis) * columnPitch;
        mappedSymmetryAxis = Math.Clamp(mappedSymmetryAxis, 0, colCount - 1);
        return (cells, rowCount, colCount, mappedSymmetryAxis);
    }

    internal static (int XOffset2, int YOffset2) GetTerminalEdgeOffset2(
        string deviceType,
        string terminal,
        GridCell cell
    )
    {
        var baseEdge = GetDefaultEdge(deviceType, terminal);
        if (!baseEdge.HasValue)
        {
            return (0, 0);
        }

        var transformed = TransformEdge(baseEdge.Value, cell.TransformIndex);
        return transformed switch
        {
            Edge.North => (0, -1),
            Edge.East => (1, 0),
            Edge.South => (0, 1),
            Edge.West => (-1, 0),
            _ => (0, 0),
        };
    }

    private static Edge? GetDefaultEdge(string deviceType, string terminal)
    {
        var type = deviceType.ToLowerInvariant();
        var t = terminal.ToUpperInvariant();
        if (type is "nmos" or "nfet" or "pmos" or "pfet")
        {
            return t switch
            {
                "G" => Edge.West,
                "D" => Edge.North,
                "S" => Edge.South,
                _ => null,
            };
        }

        if (type is "resistor" or "capacitor" or "inductor")
        {
            return t switch
            {
                "P" => Edge.West,
                "N" => Edge.East,
                _ => null,
            };
        }

        return null;
    }

    private static Edge TransformEdge(Edge edge, int transformIndex)
    {
        var rotation = (transformIndex / 4) * 90;
        var mirrorX = (transformIndex % 4) / 2 == 1;
        var mirrorY = transformIndex % 2 == 1;
        var x = edge switch
        {
            Edge.East => 1,
            Edge.West => -1,
            _ => 0,
        };
        var y = edge switch
        {
            Edge.South => 1,
            Edge.North => -1,
            _ => 0,
        };

        if (mirrorX)
        {
            x = -x;
        }

        if (mirrorY)
        {
            y = -y;
        }

        (x, y) = rotation switch
        {
            90 => (y, -x),
            180 => (-x, -y),
            270 => (-y, x),
            _ => (x, y),
        };

        if (x == 1)
        {
            return Edge.East;
        }

        if (x == -1)
        {
            return Edge.West;
        }

        if (y == 1)
        {
            return Edge.South;
        }

        return Edge.North;
    }

    private static string ToVarToken(string value)
    {
        var chars = value.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        return new string(chars);
    }

    private static bool IsBodyOrShieldTerminal(string terminal)
    {
        var t = terminal.Trim().ToUpperInvariant();
        return t is "B" or "BULK" or "BODY" or "SH" or "SHIELD";
    }

    private static bool IsDrainOrSourceTerminal(string terminal)
    {
        var t = terminal.Trim().ToUpperInvariant();
        return t is "D" or "S";
    }

    private static bool IsDrainSourcePair(string firstTerminal, string secondTerminal)
    {
        var first = firstTerminal.Trim().ToUpperInvariant();
        var second = secondTerminal.Trim().ToUpperInvariant();
        return (first == "D" && second == "S") || (first == "S" && second == "D");
    }

    private static string? GetMosFlavor(CircuitGraph graph, string deviceId)
    {
        if (!graph.Devices.TryGetValue(deviceId, out var device))
        {
            return null;
        }

        return device.DeviceType.ToLowerInvariant() switch
        {
            "nmos" or "nfet" => "nmos",
            "pmos" or "pfet" => "pmos",
            _ => null,
        };
    }
}
