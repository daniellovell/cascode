namespace Cascode.Render.Routing;

using Cascode.Render.Analysis;
using Cascode.Render.Layout;
using Cascode.Render.Placement;

/// <summary>
/// Terminal and port placement methods for MazeRouter.
/// </summary>
public static partial class MazeRouter
{
    private enum TerminalBreakoutPreference
    {
        None,
        PerpendicularToHorizontalPassive,
        OutwardFromVerticalTerminal,
        InwardFromVerticalTerminal,
        SidewaysFromVerticalPassive,
    }

    /// <summary>
    /// Computes terminal positions for all devices and ports.
    /// </summary>
    private static List<TerminalPosition> ComputeTerminalPositions(
        CoarseGridResult placement,
        CircuitGraph graph,
        int canvasWidth,
        int canvasHeight
    )
    {
        var positions = new List<TerminalPosition>();

        // Device terminals
        foreach (var (deviceId, cell) in placement.DevicePlacements)
        {
            if (!graph.Devices.TryGetValue(deviceId, out var device))
            {
                continue;
            }

            var deviceType = device.DeviceType.ToLowerInvariant();

            if (deviceType == "instance")
            {
                var blockInfo = graph.InstanceBlocks.FirstOrDefault(b => b.InstanceId == deviceId);
                var signalPorts =
                    blockInfo?.SignalPortNames ?? (IReadOnlyList<string>)Array.Empty<string>();
                var bp = DeviceGeometry.GetInstanceBlockPlacement(
                    cell.Row,
                    cell.Column,
                    signalPorts,
                    graph.Supplies,
                    graph.Grounds,
                    device.Bindings
                );
                foreach (var (terminal, pos) in bp.Terminals)
                {
                    positions.Add(new TerminalPosition(deviceId, terminal, pos.X, pos.Y));
                }
            }
            else if (deviceType is "nmos" or "nfet" or "pmos" or "pfet")
            {
                var isPmos = deviceType is "pmos" or "pfet";
                var p = DeviceGeometry.GetMosfetPlacement(cell.Row, cell.Column, cell.MirrorX);

                positions.Add(new TerminalPosition(deviceId, "G", p.GateX, p.GateY));
                positions.Add(
                    new TerminalPosition(deviceId, "D", p.DrainX, isPmos ? p.SourceY : p.DrainY)
                );
                positions.Add(
                    new TerminalPosition(deviceId, "S", p.SourceX, isPmos ? p.DrainY : p.SourceY)
                );
            }
            else if (deviceType is "resistor" or "capacitor" or "inductor")
            {
                var isHorizontalPassive = placement.HorizontalPassiveIds.Contains(deviceId);

                if (isHorizontalPassive)
                {
                    var p = DeviceGeometry.GetHorizontalPassivePlacement(
                        cell.Row,
                        cell.Column,
                        placement.ColumnCount,
                        pOnLeft: !cell.MirrorX
                    );
                    positions.Add(new TerminalPosition(deviceId, "P", p.PX, p.PY));
                    positions.Add(new TerminalPosition(deviceId, "N", p.NX, p.NY));
                }
                else
                {
                    var p = DeviceGeometry.GetPassivePlacement(cell.Row, cell.Column);
                    positions.Add(new TerminalPosition(deviceId, "P", p.PX, p.PY));
                    positions.Add(new TerminalPosition(deviceId, "N", p.NX, p.NY));
                }
            }
        }

        // Port terminals
        var terminalYByNet = ComputeTerminalYByNet(positions, graph);
        var preferredPortYs = new Dictionary<string, int>(
            placement.PortYHints,
            StringComparer.Ordinal
        );
        foreach (var (port, y) in ComputeFeedthroughPortHints(placement, graph))
        {
            preferredPortYs[port] = y;
        }

        // Left ports (inputs, bias) - use average Y
        var leftPorts = graph.InputPorts.Concat(graph.BiasPorts).ToList();
        var leftYs = ComputePortYPositions(
            leftPorts,
            terminalYByNet,
            preferMinY: false,
            preferredPortYs
        );
        foreach (var port in leftPorts)
        {
            var y = leftYs.GetValueOrDefault(port, DeviceGeometry.RailMargin + 50);
            positions.Add(new TerminalPosition($"PORT_{port}", "P", 0, y));
        }

        // Right ports (outputs) - use average Y for balanced routing
        var rightYs = ComputePortYPositions(
            graph.OutputPorts.ToList(),
            terminalYByNet,
            preferMinY: false,
            preferredPortYs
        );
        foreach (var port in graph.OutputPorts)
        {
            var y = rightYs.GetValueOrDefault(port, DeviceGeometry.RailMargin + 50);
            positions.Add(new TerminalPosition($"PORT_{port}", "P", canvasWidth, y));
        }

        return positions;
    }

    private static IReadOnlyDictionary<string, int> ComputeFeedthroughPortHints(
        CoarseGridResult placement,
        CircuitGraph graph
    )
    {
        var pairs = new List<(string LeftPort, string RightPort, int BaseY)>();
        foreach (var (deviceId, device) in graph.Devices)
        {
            var deviceType = device.DeviceType.ToLowerInvariant();
            if (deviceType is not ("resistor" or "capacitor" or "inductor"))
            {
                continue;
            }

            if (
                !placement.DevicePlacements.TryGetValue(deviceId, out var cell)
                || !device.Bindings.TryGetValue("P", out var pNet)
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
            pairs.Add((pIsLeft ? pNet : nNet, pIsRight ? pNet : nNet, y));
        }

        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        var usedYs = new List<int>();
        foreach (
            var (leftPort, rightPort, baseY) in pairs
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

    /// <summary>
    /// Groups terminal Y positions by net for port alignment.
    /// </summary>
    private static Dictionary<string, List<int>> ComputeTerminalYByNet(
        List<TerminalPosition> positions,
        CircuitGraph graph
    )
    {
        var result = new Dictionary<string, List<int>>();

        foreach (var pos in positions)
        {
            var netName = graph.GetNetForTerminal(pos.DeviceId, pos.Terminal);
            if (netName == null)
            {
                continue;
            }

            if (!result.TryGetValue(netName, out var list))
            {
                list = new List<int>();
                result[netName] = list;
            }
            list.Add(pos.Y);
        }

        return result;
    }

    /// <summary>
    /// Computes Y positions for ports based on connected terminals.
    /// </summary>
    private static Dictionary<string, int> ComputePortYPositions(
        List<string> portNames,
        Dictionary<string, List<int>> terminalYByNet,
        bool preferMinY,
        IReadOnlyDictionary<string, int>? preferredYHints = null
    )
    {
        var result = new Dictionary<string, int>();
        var usedYs = new List<int>();
        const int minSpacing = 15;

        foreach (var port in portNames)
        {
            int y;

            if (preferredYHints != null && preferredYHints.TryGetValue(port, out var hintedY))
            {
                y = hintedY;
            }
            else if (terminalYByNet.TryGetValue(port, out var ys) && ys.Count > 0)
            {
                // Use minimum Y when preferMinY is true, average Y when false (for balanced routing)
                y = preferMinY ? ys.Min() : (int)ys.Average();
            }
            else
            {
                y = DeviceGeometry.RailMargin + 50 + usedYs.Count * 20;
            }

            // Avoid collisions
            while (usedYs.Any(used => Math.Abs(used - y) < minSpacing))
            {
                y += minSpacing;
            }

            result[port] = y;
            usedYs.Add(y);
        }

        return result;
    }

    /// <summary>
    /// Groups terminals by net name.
    /// </summary>
    private static Dictionary<string, List<TerminalPosition>> GroupTerminalsByNet(
        List<TerminalPosition> terminals,
        CircuitGraph graph
    )
    {
        var byNet = new Dictionary<string, List<TerminalPosition>>();

        foreach (var term in terminals)
        {
            string? netName;

            if (term.DeviceId.StartsWith("PORT_", StringComparison.Ordinal))
            {
                netName = term.DeviceId.Substring(5);
            }
            else
            {
                netName = graph.GetNetForTerminal(term.DeviceId, term.Terminal);
            }

            if (netName == null)
            {
                continue;
            }

            if (!byNet.TryGetValue(netName, out var list))
            {
                list = new List<TerminalPosition>();
                byNet[netName] = list;
            }
            list.Add(term);
        }

        return byNet;
    }

    private static List<TerminalPosition> ComputeRouteTerminalPositions(
        CoarseGridResult placement,
        CircuitGraph graph,
        List<TerminalPosition> terminals
    )
    {
        var terminalsByNet = GroupTerminalsByNet(terminals, graph);
        var mstDegreesByNet = ComputeTerminalMstDegreesByNet(terminalsByNet);
        var allTerminalPoints = terminals
            .Select(terminal => new GridPoint(terminal.X, terminal.Y))
            .ToHashSet();
        var routeTerminals = new List<TerminalPosition>(terminals.Count);
        var chosenStubs = new List<WireSegment>();
        foreach (var terminal in terminals)
        {
            var netName = terminal.DeviceId.StartsWith("PORT_", StringComparison.Ordinal)
                ? terminal.DeviceId[5..]
                : graph.GetNetForTerminal(terminal.DeviceId, terminal.Terminal);
            var isBranchingSignalNet =
                netName != null
                && !graph.IsSupplyOrGround(netName)
                && terminalsByNet.GetValueOrDefault(netName, []).Count > 2;
            var peerTerminals =
                netName != null
                    ? terminalsByNet
                        .GetValueOrDefault(netName, [])
                        .Where(other =>
                            other.DeviceId != terminal.DeviceId
                            || other.Terminal != terminal.Terminal
                        )
                        .ToArray()
                    : Array.Empty<TerminalPosition>();
            var mstDegree =
                netName != null
                && mstDegreesByNet.TryGetValue(netName, out var netDegrees)
                && netDegrees.TryGetValue((terminal.DeviceId, terminal.Terminal), out var degree)
                    ? degree
                    : 0;
            var breakoutPreference = GetTerminalBreakoutPreference(
                terminal,
                netName,
                isBranchingSignalNet,
                mstDegree,
                placement,
                graph,
                peerTerminals
            );
            if (
                terminal.DeviceId.StartsWith("PORT_", StringComparison.Ordinal)
                || (
                    breakoutPreference == TerminalBreakoutPreference.None
                    && (
                        !isBranchingSignalNet
                        || (
                            mstDegree <= 1
                            && IsSeriesPassiveTerminal(terminal, graph)
                            && ShouldKeepLeafSeriesPassiveTerminalAtDeviceTerminal(netName!, graph)
                        )
                    )
                )
                || !placement.DevicePlacements.TryGetValue(terminal.DeviceId, out var cell)
            )
            {
                routeTerminals.Add(terminal);
                continue;
            }

            var centerX = DeviceGeometry.GetCellCenterX(cell.Column);
            var centerY = DeviceGeometry.GetCellCenterY(cell.Row);
            var deltaX = terminal.X - centerX;
            var deltaY = terminal.Y - centerY;
            var routed = SelectRouteTerminalPosition(
                terminal,
                netName!,
                cell,
                placement,
                deltaX,
                deltaY,
                breakoutPreference,
                GetPreferredPerpendicularVerticalDirection(
                    terminal,
                    netName!,
                    cell,
                    placement,
                    breakoutPreference,
                    graph
                ),
                peerTerminals,
                allTerminalPoints,
                chosenStubs
            );
            routeTerminals.Add(routed);
            if (routed.X != terminal.X || routed.Y != terminal.Y)
            {
                chosenStubs.Add(
                    new WireSegment(
                        new GridPoint(terminal.X, terminal.Y),
                        new GridPoint(routed.X, routed.Y),
                        netName!
                    )
                );
            }
        }

        return routeTerminals;
    }

    private static Dictionary<
        string,
        Dictionary<(string DeviceId, string Terminal), int>
    > ComputeTerminalMstDegreesByNet(
        IReadOnlyDictionary<string, List<TerminalPosition>> terminalsByNet
    )
    {
        var degreesByNet = new Dictionary<
            string,
            Dictionary<(string DeviceId, string Terminal), int>
        >(StringComparer.Ordinal);
        foreach (var (netName, netTerminals) in terminalsByNet)
        {
            var degrees = netTerminals.ToDictionary(
                terminal => (terminal.DeviceId, terminal.Terminal),
                _ => 0
            );
            foreach (var (fromIndex, toIndex) in ComputeMST(netTerminals))
            {
                degrees[(netTerminals[fromIndex].DeviceId, netTerminals[fromIndex].Terminal)]++;
                degrees[(netTerminals[toIndex].DeviceId, netTerminals[toIndex].Terminal)]++;
            }

            degreesByNet[netName] = degrees;
        }

        return degreesByNet;
    }

    private static TerminalPosition SelectRouteTerminalPosition(
        TerminalPosition terminal,
        string netName,
        GridCell cell,
        CoarseGridResult placement,
        double deltaX,
        double deltaY,
        TerminalBreakoutPreference breakoutPreference,
        int preferredPerpendicularVerticalDirection,
        IReadOnlyList<TerminalPosition> peerTerminals,
        IReadOnlySet<GridPoint> allTerminalPoints,
        IReadOnlyList<WireSegment> chosenStubs
    )
    {
        foreach (var pitchMultiplier in new[] { 1, 2 })
        {
            foreach (
                var candidate in RankRouteTerminalCandidates(
                    terminal,
                    cell,
                    placement,
                    deltaX,
                    deltaY,
                    breakoutPreference,
                    preferredPerpendicularVerticalDirection,
                    peerTerminals,
                    pitchMultiplier
                )
            )
            {
                if (
                    !IsValidRouteTerminalCandidate(
                        terminal,
                        netName,
                        candidate,
                        allTerminalPoints,
                        chosenStubs
                    )
                )
                {
                    continue;
                }

                return candidate;
            }
        }

        return terminal;
    }

    private static IEnumerable<TerminalPosition> RankRouteTerminalCandidates(
        TerminalPosition terminal,
        GridCell cell,
        CoarseGridResult placement,
        double deltaX,
        double deltaY,
        TerminalBreakoutPreference breakoutPreference,
        int preferredPerpendicularVerticalDirection,
        IReadOnlyList<TerminalPosition> peerTerminals,
        int pitchMultiplier
    )
    {
        if (
            breakoutPreference == TerminalBreakoutPreference.None
            && ComputePeerBoundsDistance(terminal, peerTerminals) == 0
        )
        {
            foreach (
                var candidate in GetRouteTerminalCandidates(
                        terminal,
                        cell,
                        placement,
                        deltaX,
                        deltaY,
                        preferredPerpendicularVerticalDirection,
                        pitchMultiplier
                    )
                    .DistinctBy(candidate => (candidate.X, candidate.Y))
            )
            {
                yield return candidate;
            }

            yield break;
        }

        var rankedCandidates = GetRouteTerminalCandidates(
                terminal,
                cell,
                placement,
                deltaX,
                deltaY,
                preferredPerpendicularVerticalDirection,
                pitchMultiplier
            )
            .DistinctBy(candidate => (candidate.X, candidate.Y))
            .Select(
                (candidate, index) =>
                    new
                    {
                        Candidate = candidate,
                        Index = index,
                        Score = ScoreRouteTerminalCandidate(
                            terminal,
                            candidate,
                            peerTerminals,
                            breakoutPreference,
                            deltaX,
                            deltaY
                        ),
                    }
            )
            .OrderBy(item => item.Score.BreakoutPenalty)
            .ThenBy(item => item.Score.MovesAwayFromPeerBounds)
            .ThenBy(item => item.Score.PeerBoundsDistance)
            .ThenBy(item => item.Score.TotalPeerDistance)
            .ThenByDescending(item => item.Score.AlignedPeerCount)
            .ThenBy(item => item.Score.StubLength)
            .ThenBy(item => item.Index);
        foreach (var candidate in rankedCandidates)
        {
            yield return candidate.Candidate;
        }
    }

    private static IEnumerable<TerminalPosition> GetRouteTerminalCandidates(
        TerminalPosition terminal,
        GridCell cell,
        CoarseGridResult placement,
        double deltaX,
        double deltaY,
        int preferredPerpendicularVerticalDirection,
        int pitchMultiplier
    )
    {
        var pitch = DeviceGeometry.RoutingPitch * pitchMultiplier;
        if (Math.Abs(deltaX) >= Math.Abs(deltaY) && deltaX != 0)
        {
            yield return new TerminalPosition(
                terminal.DeviceId,
                terminal.Terminal,
                terminal.X + Math.Sign(deltaX) * pitch,
                terminal.Y
            );

            var preferredDirection =
                preferredPerpendicularVerticalDirection != 0
                    ? preferredPerpendicularVerticalDirection
                : cell.Row <= placement.RowCount / 2 ? -1
                : 1;
            yield return new TerminalPosition(
                terminal.DeviceId,
                terminal.Terminal,
                terminal.X,
                terminal.Y + preferredDirection * pitch
            );
            yield return new TerminalPosition(
                terminal.DeviceId,
                terminal.Terminal,
                terminal.X,
                terminal.Y - preferredDirection * pitch
            );
            yield break;
        }

        if (deltaY != 0)
        {
            yield return new TerminalPosition(
                terminal.DeviceId,
                terminal.Terminal,
                terminal.X,
                terminal.Y + Math.Sign(deltaY) * pitch
            );

            var preferredDirection = cell.Column <= placement.SymmetryAxis ? -1 : 1;
            yield return new TerminalPosition(
                terminal.DeviceId,
                terminal.Terminal,
                terminal.X + preferredDirection * pitch,
                terminal.Y
            );
            yield return new TerminalPosition(
                terminal.DeviceId,
                terminal.Terminal,
                terminal.X - preferredDirection * pitch,
                terminal.Y
            );
        }
    }

    private static bool IsValidRouteTerminalCandidate(
        TerminalPosition terminal,
        string netName,
        TerminalPosition candidate,
        IReadOnlySet<GridPoint> allTerminalPoints,
        IReadOnlyList<WireSegment> chosenStubs
    )
    {
        var stub = new WireSegment(
            new GridPoint(terminal.X, terminal.Y),
            new GridPoint(candidate.X, candidate.Y),
            string.Empty
        );
        if (
            allTerminalPoints.Any(point =>
                (point.X != terminal.X || point.Y != terminal.Y)
                && (
                    point.X == candidate.X && point.Y == candidate.Y
                    || IsStrictlyOnStub(point, stub)
                )
            )
        )
        {
            return false;
        }

        return !chosenStubs.Any(existing =>
            !string.Equals(existing.NetName, netName, StringComparison.Ordinal)
            && OccupiedSegments.SegmentsCoincide(
                stub.From.X,
                stub.From.Y,
                stub.To.X,
                stub.To.Y,
                existing.From.X,
                existing.From.Y,
                existing.To.X,
                existing.To.Y
            )
        );
    }

    private static (
        int BreakoutPenalty,
        int MovesAwayFromPeerBounds,
        int PeerBoundsDistance,
        int TotalPeerDistance,
        int AlignedPeerCount,
        int StubLength
    ) ScoreRouteTerminalCandidate(
        TerminalPosition terminal,
        TerminalPosition candidate,
        IReadOnlyList<TerminalPosition> peerTerminals,
        TerminalBreakoutPreference breakoutPreference,
        double deltaX,
        double deltaY
    )
    {
        if (peerTerminals.Count == 0)
        {
            return (
                BreakoutPenalty: ComputeBreakoutPenalty(
                    terminal,
                    candidate,
                    breakoutPreference,
                    deltaX,
                    deltaY
                ),
                MovesAwayFromPeerBounds: 0,
                PeerBoundsDistance: 0,
                TotalPeerDistance: 0,
                AlignedPeerCount: 0,
                StubLength: Math.Abs(candidate.X - terminal.X) + Math.Abs(candidate.Y - terminal.Y)
            );
        }

        var originalPeerBoundsDistance = ComputePeerBoundsDistance(terminal, peerTerminals);
        var candidatePeerBoundsDistance = ComputePeerBoundsDistance(candidate, peerTerminals);
        return (
            BreakoutPenalty: ComputeBreakoutPenalty(
                terminal,
                candidate,
                breakoutPreference,
                deltaX,
                deltaY
            ),
            MovesAwayFromPeerBounds: candidatePeerBoundsDistance > originalPeerBoundsDistance
                ? 1
                : 0,
            PeerBoundsDistance: candidatePeerBoundsDistance,
            TotalPeerDistance: peerTerminals.Sum(peer =>
                Math.Abs(candidate.X - peer.X) + Math.Abs(candidate.Y - peer.Y)
            ),
            AlignedPeerCount: peerTerminals.Count(peer =>
                peer.X == candidate.X || peer.Y == candidate.Y
            ),
            StubLength: Math.Abs(candidate.X - terminal.X) + Math.Abs(candidate.Y - terminal.Y)
        );
    }

    private static TerminalBreakoutPreference GetTerminalBreakoutPreference(
        TerminalPosition terminal,
        string? netName,
        bool isBranchingSignalNet,
        int mstDegree,
        CoarseGridResult placement,
        CircuitGraph graph,
        IReadOnlyList<TerminalPosition> peerTerminals
    )
    {
        if (
            netName == null
            || graph.IsSupplyOrGround(netName)
            || peerTerminals.Count == 0
            || !graph.Devices.TryGetValue(terminal.DeviceId, out var device)
        )
        {
            return TerminalBreakoutPreference.None;
        }

        var deviceType = device.DeviceType.ToLowerInvariant();
        if (
            placement.HorizontalPassiveIds.Contains(terminal.DeviceId)
            && deviceType is "resistor" or "capacitor" or "inductor"
            && !(graph.OutputPorts.Contains(netName!) && deviceType == "capacitor")
            && (
                ShouldBreakOutFromHorizontalPassiveTerminal(terminal, peerTerminals)
                || (
                    isBranchingSignalNet
                    && HorizontalExitRunsIntoPeerTerminal(terminal, placement, peerTerminals)
                )
            )
        )
        {
            return TerminalBreakoutPreference.PerpendicularToHorizontalPassive;
        }

        if (
            !placement.HorizontalPassiveIds.Contains(terminal.DeviceId)
            && deviceType is "resistor" or "capacitor" or "inductor"
            && terminal.Terminal is "P" or "N"
            && isBranchingSignalNet
            && !IsPassiveShuntToRail(terminal, graph)
            && peerTerminals.Any(peer => peer.X != terminal.X)
        )
        {
            return mstDegree <= 1
                ? TerminalBreakoutPreference.InwardFromVerticalTerminal
                : TerminalBreakoutPreference.SidewaysFromVerticalPassive;
        }

        if (
            !placement.HorizontalPassiveIds.Contains(terminal.DeviceId)
            && deviceType is "resistor" or "capacitor" or "inductor"
            && terminal.Terminal is "P" or "N"
            && isBranchingSignalNet
            && IsPassiveShuntToRail(terminal, graph)
            && peerTerminals.Any(peer => peer.X != terminal.X)
        )
        {
            if (deviceType == "capacitor" && peerTerminals.Any(peer => peer.X == terminal.X))
            {
                return TerminalBreakoutPreference.SidewaysFromVerticalPassive;
            }

            return TerminalBreakoutPreference.OutwardFromVerticalTerminal;
        }

        if (
            deviceType is "nmos" or "nfet" or "pmos" or "pfet"
            && terminal.Terminal is "D" or "S"
            && isBranchingSignalNet
            && !graph.OutputPorts.Contains(netName!)
            && peerTerminals.Any(peer => peer.X != terminal.X)
        )
        {
            return TerminalBreakoutPreference.OutwardFromVerticalTerminal;
        }

        return TerminalBreakoutPreference.None;
    }

    private static bool IsPassiveShuntToRail(TerminalPosition terminal, CircuitGraph graph)
    {
        var oppositeTerminal = terminal.Terminal == "P" ? "N" : "P";
        var oppositeNet = graph.GetNetForTerminal(terminal.DeviceId, oppositeTerminal);
        return oppositeNet != null && graph.IsSupplyOrGround(oppositeNet);
    }

    private static bool IsSeriesPassiveTerminal(TerminalPosition terminal, CircuitGraph graph)
    {
        if (!graph.Devices.TryGetValue(terminal.DeviceId, out var device))
        {
            return false;
        }

        var deviceType = device.DeviceType.ToLowerInvariant();
        return deviceType is "resistor" or "capacitor" or "inductor"
            && terminal.Terminal is "P" or "N"
            && !IsPassiveShuntToRail(terminal, graph);
    }

    private static bool ShouldKeepLeafSeriesPassiveTerminalAtDeviceTerminal(
        string netName,
        CircuitGraph graph
    )
    {
        return graph.OutputPorts.Contains(netName);
    }

    private static bool ShouldBreakOutFromHorizontalPassiveTerminal(
        TerminalPosition terminal,
        IReadOnlyList<TerminalPosition> peerTerminals
    )
    {
        return terminal.Terminal switch
        {
            "P" => peerTerminals.Count > 0 && peerTerminals.All(peer => peer.X > terminal.X),
            "N" => peerTerminals.Count > 0 && peerTerminals.All(peer => peer.X < terminal.X),
            _ => false,
        };
    }

    private static int GetPreferredPerpendicularVerticalDirection(
        TerminalPosition terminal,
        string netName,
        GridCell cell,
        CoarseGridResult placement,
        TerminalBreakoutPreference breakoutPreference,
        CircuitGraph graph
    )
    {
        if (
            breakoutPreference != TerminalBreakoutPreference.PerpendicularToHorizontalPassive
            || !graph.OutputPorts.Contains(netName)
            || !graph.Devices.TryGetValue(terminal.DeviceId, out var device)
            || device.DeviceType.ToLowerInvariant() == "capacitor"
        )
        {
            return 0;
        }

        return cell.Row <= placement.RowCount / 2 ? 1 : -1;
    }

    private static bool HorizontalExitRunsIntoPeerTerminal(
        TerminalPosition terminal,
        CoarseGridResult placement,
        IReadOnlyList<TerminalPosition> peerTerminals
    )
    {
        if (!placement.DevicePlacements.TryGetValue(terminal.DeviceId, out var cell))
        {
            return false;
        }

        var centerX = DeviceGeometry.GetCellCenterX(cell.Column);
        var horizontalDirection = terminal.X >= centerX ? 1 : -1;
        var exitX = terminal.X + horizontalDirection * DeviceGeometry.RoutingPitch;
        var minX = Math.Min(terminal.X, exitX);
        var maxX = Math.Max(terminal.X, exitX);
        return peerTerminals.Any(peer =>
            peer.Y == terminal.Y
            && Math.Sign(peer.X - terminal.X) == horizontalDirection
            && peer.X >= minX
            && peer.X <= maxX
        );
    }

    private static int ComputeBreakoutPenalty(
        TerminalPosition terminal,
        TerminalPosition candidate,
        TerminalBreakoutPreference breakoutPreference,
        double deltaX,
        double deltaY
    )
    {
        return breakoutPreference switch
        {
            TerminalBreakoutPreference.PerpendicularToHorizontalPassive => candidate.X == terminal.X
                ? 0
                : 1,
            TerminalBreakoutPreference.SidewaysFromVerticalPassive => candidate.Y == terminal.Y
                ? 0
                : 1,
            TerminalBreakoutPreference.OutwardFromVerticalTerminal => candidate.Y != terminal.Y
            && Math.Sign(candidate.Y - terminal.Y) == Math.Sign(deltaY)
                ? 0
                : 1,
            TerminalBreakoutPreference.InwardFromVerticalTerminal => candidate.Y != terminal.Y
            && Math.Sign(candidate.Y - terminal.Y) == -Math.Sign(deltaY)
                ? 0
                : 1,
            _ => 0,
        };
    }

    private static int ComputePeerBoundsDistance(
        TerminalPosition terminal,
        IReadOnlyList<TerminalPosition> peerTerminals
    )
    {
        if (peerTerminals.Count == 0)
        {
            return 0;
        }

        var minX = peerTerminals.Min(peer => peer.X);
        var maxX = peerTerminals.Max(peer => peer.X);
        var minY = peerTerminals.Min(peer => peer.Y);
        var maxY = peerTerminals.Max(peer => peer.Y);
        var deltaX =
            terminal.X < minX ? minX - terminal.X
            : terminal.X > maxX ? terminal.X - maxX
            : 0;
        var deltaY =
            terminal.Y < minY ? minY - terminal.Y
            : terminal.Y > maxY ? terminal.Y - maxY
            : 0;
        return deltaX + deltaY;
    }

    private static bool IsStrictlyOnStub(GridPoint point, WireSegment stub)
    {
        if (
            point.X == stub.From.X && point.Y == stub.From.Y
            || point.X == stub.To.X && point.Y == stub.To.Y
        )
        {
            return false;
        }

        if (stub.From.X == stub.To.X && point.X == stub.From.X)
        {
            var minY = Math.Min(stub.From.Y, stub.To.Y);
            var maxY = Math.Max(stub.From.Y, stub.To.Y);
            return point.Y > minY && point.Y < maxY;
        }

        if (stub.From.Y == stub.To.Y && point.Y == stub.From.Y)
        {
            var minX = Math.Min(stub.From.X, stub.To.X);
            var maxX = Math.Max(stub.From.X, stub.To.X);
            return point.X > minX && point.X < maxX;
        }

        return false;
    }

    private static IReadOnlyDictionary<string, List<WireSegment>> ComputeTerminalStubSegmentsByNet(
        IReadOnlyList<TerminalPosition> terminals,
        IReadOnlyList<TerminalPosition> routeTerminals,
        CircuitGraph graph
    )
    {
        var stubsByNet = new Dictionary<string, List<WireSegment>>(StringComparer.Ordinal);
        for (var i = 0; i < terminals.Count; i++)
        {
            var terminal = terminals[i];
            var routeTerminal = routeTerminals[i];
            if (terminal.X == routeTerminal.X && terminal.Y == routeTerminal.Y)
            {
                continue;
            }

            var netName = terminal.DeviceId.StartsWith("PORT_", StringComparison.Ordinal)
                ? terminal.DeviceId[5..]
                : graph.GetNetForTerminal(terminal.DeviceId, terminal.Terminal);
            if (netName == null)
            {
                continue;
            }

            if (!stubsByNet.TryGetValue(netName, out var stubs))
            {
                stubs = new List<WireSegment>();
                stubsByNet[netName] = stubs;
            }

            stubs.Add(
                new WireSegment(
                    new GridPoint(terminal.X, terminal.Y),
                    new GridPoint(routeTerminal.X, routeTerminal.Y),
                    netName
                )
            );
        }

        return stubsByNet;
    }
}
