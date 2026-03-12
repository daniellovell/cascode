namespace Cascode.Render.Tests.Stress;

using System.Xml.Linq;
using Cascode.Language;
using Cascode.Render.Analysis;
using Cascode.Render.Layout;
using Cascode.Render.Placement;
using Cascode.Render.Routing;
using Cascode.Render.Svg;
using Cascode.TestSupport;

public sealed class StressRenderRegressionTests
{
    [Theory]
    [MemberData(nameof(StressCases))]
    public void StressRenderings_SatisfyHardConstraints_AndConnectEveryTerminal(string relativePath)
    {
        foreach (var scenario in LoadScenarios(relativePath))
        {
            AssertExpectedTerminalsArePresent(scenario);
            AssertHardConstraints(scenario);
            AssertAllNetTerminalsAreConnected(scenario);
            AssertSvgConnectsEveryTerminal(scenario);
        }
    }

    public static IEnumerable<object[]> StressCases()
    {
        yield return ["tests/golden/cas/stress/SST12LN01_Sky130.cas"];
        yield return ["tests/golden/cas/stress/TLC2272A_Sky130.cas"];
        yield return ["tests/golden/cas/stress/OTA5TFullyDiff_Ideal.cas"];
        yield return
        [
            "tests/golden/cas/stress/LNA_CSCascodeInductivelyDegenerated_TwoStage_Sky130.cas",
        ];
        yield return ["tests/golden/cas/stress/LNA_CSCascodeInductivelyDegenerated_Sky130.cas"];
    }

    private static IReadOnlyList<RenderScenario> LoadScenarios(string relativePath)
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var inputPath = Path.Combine(repoRoot, relativePath);

        using var linkDir = new TemporaryDirectory();
        var link = CascodeLinker.LinkFile(inputPath, linkDir.Path, repoRoot);
        Assert.True(
            link.Success,
            string.Join(Environment.NewLine, link.Diagnostics.Select(d => d.Message))
        );
        Assert.False(string.IsNullOrWhiteSpace(link.LinkedCasPath));

        CascodeReadResult readResult;
        using (var reader = File.OpenText(link.LinkedCasPath!))
        {
            readResult = CascodeReader.TryRead(reader, link.LinkedCasPath!);
        }

        Assert.True(readResult.Success, $"Failed to parse linked file '{link.LinkedCasPath}'.");
        var document = readResult.Document!;
        var attachResolution = new AttachResolver(document).Resolve();
        var scenarios = new List<RenderScenario>();
        var style = StyleSheet.GetByName("default");

        foreach (
            var circuit in document
                .Circuits.Where(c => c.Level == CascodeLevel.EL && !c.Inline)
                .OrderBy(c => c.Name, StringComparer.Ordinal)
        )
        {
            var resolution = attachResolution.CircuitResults.GetValueOrDefault(circuit.Name);
            var flattened = CircuitFlattener.Flatten(circuit, document, resolution);
            var graph = CircuitGraph.Build(flattened);
            var topology = TopologyAnalyzer.Analyze(graph);
            var placement = CoarseGridPlacer.Place(topology, graph);
            var routing = MazeRouter.Route(placement, graph);
            var svg = new SvgRenderer().Render(
                placement,
                routing,
                graph,
                style,
                new RenderOptions()
            );

            scenarios.Add(
                new RenderScenario(
                    relativePath,
                    circuit.Name,
                    graph,
                    topology,
                    placement,
                    routing,
                    svg
                )
            );
        }

        return scenarios;
    }

    private static void AssertExpectedTerminalsArePresent(RenderScenario scenario)
    {
        var actual = scenario
            .Routing.TerminalPositions.Where(t =>
                !t.DeviceId.StartsWith("PORT_", StringComparison.Ordinal)
            )
            .GroupBy(t => t.DeviceId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(t => t.Terminal).ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal
            );

        foreach (
            var (deviceId, device) in scenario.Graph.Devices.OrderBy(
                kv => kv.Key,
                StringComparer.Ordinal
            )
        )
        {
            Assert.True(
                actual.TryGetValue(deviceId, out var terminals),
                FailurePrefix(scenario, $"Missing routed terminals for device '{deviceId}'.")
            );
            foreach (var terminal in GetExpectedRenderableTerminals(device))
            {
                Assert.True(
                    terminals!.Contains(terminal),
                    FailurePrefix(scenario, $"Missing routed terminal {deviceId}.{terminal}.")
                );
            }
        }
    }

    private static IEnumerable<string> GetExpectedRenderableTerminals(DeviceDeclaration device)
    {
        var type = device.DeviceType.ToLowerInvariant();
        if (type is "nmos" or "nfet" or "pmos" or "pfet")
        {
            return ["G", "D", "S"];
        }

        if (type is "resistor" or "capacitor" or "inductor")
        {
            return ["P", "N"];
        }

        if (type == "instance")
        {
            return device.Bindings.Keys.OrderBy(name => name, StringComparer.Ordinal);
        }

        return Array.Empty<string>();
    }

    private static void AssertAllNetTerminalsAreConnected(RenderScenario scenario)
    {
        foreach (
            var (netName, terminals) in GroupTerminalsByNet(scenario)
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
        )
        {
            Assert.True(
                scenario.Routing.SegmentsByNet.TryGetValue(netName, out var segments),
                FailurePrefix(scenario, $"Missing routed segments for net '{netName}'.")
            );

            var segmentList = segments!;
            var parent = Enumerable.Range(0, segmentList.Count).ToArray();

            int Find(int value)
            {
                if (parent[value] != value)
                {
                    parent[value] = Find(parent[value]);
                }

                return parent[value];
            }

            void Union(int left, int right)
            {
                var leftRoot = Find(left);
                var rightRoot = Find(right);
                if (leftRoot != rightRoot)
                {
                    parent[leftRoot] = rightRoot;
                }
            }

            for (var i = 0; i < segmentList.Count; i++)
            {
                for (var j = i + 1; j < segmentList.Count; j++)
                {
                    if (SegmentsTouchOrCross(segmentList[i], segmentList[j]))
                    {
                        Union(i, j);
                    }
                }
            }

            var roots = new HashSet<int>();
            foreach (var terminal in terminals)
            {
                var point = new GridPoint(terminal.X, terminal.Y);
                var index = Enumerable
                    .Range(0, segmentList.Count)
                    .FirstOrDefault(i => IsPointOnSegment(point, segmentList[i]), -1);
                Assert.True(
                    index >= 0,
                    FailurePrefix(
                        scenario,
                        $"Net '{netName}' does not reach terminal {terminal.DeviceId}.{terminal.Terminal} at ({terminal.X}, {terminal.Y})."
                    )
                );
                roots.Add(Find(index));
            }

            Assert.True(
                roots.Count <= 1,
                FailurePrefix(
                    scenario,
                    $"Net '{netName}' is disconnected across multiple routed segment components."
                )
            );
        }
    }

    private static void AssertSvgConnectsEveryTerminal(RenderScenario scenario)
    {
        var svgSegmentsByNet = ParseSvgSegmentsByNet(scenario.Svg);

        foreach (var terminal in scenario.Routing.TerminalPositions)
        {
            var netName = GetNetName(scenario.Graph, terminal);
            if (netName == null)
            {
                continue;
            }

            Assert.True(
                svgSegmentsByNet.TryGetValue(netName, out var segments),
                FailurePrefix(scenario, $"SVG is missing geometry for net '{netName}'.")
            );

            var point = new GridPoint(terminal.X, terminal.Y);
            Assert.True(
                segments!.Any(segment => IsPointOnSegment(point, segment)),
                FailurePrefix(
                    scenario,
                    $"SVG does not connect {terminal.DeviceId}.{terminal.Terminal} at ({terminal.X}, {terminal.Y}) on net '{netName}'."
                )
            );
        }
    }

    private static IReadOnlyDictionary<string, List<TerminalPosition>> GroupTerminalsByNet(
        RenderScenario scenario
    )
    {
        var result = new Dictionary<string, List<TerminalPosition>>(StringComparer.Ordinal);
        foreach (var terminal in scenario.Routing.TerminalPositions)
        {
            var netName = GetNetName(scenario.Graph, terminal);
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
        return terminal.DeviceId.StartsWith("PORT_", StringComparison.Ordinal)
            ? terminal.DeviceId[5..]
            : graph.GetNetForTerminal(terminal.DeviceId, terminal.Terminal);
    }

    private static Dictionary<string, List<WireSegment>> ParseSvgSegmentsByNet(string svg)
    {
        var result = new Dictionary<string, List<WireSegment>>(StringComparer.Ordinal);
        var document = XDocument.Parse(svg);
        var ns = document.Root?.Name.Namespace ?? XNamespace.None;

        foreach (
            var group in document
                .Descendants(ns + "g")
                .Where(element => HasCssClass(element, "net"))
        )
        {
            var netName = (string?)group.Attribute("data-net");
            if (string.IsNullOrWhiteSpace(netName))
            {
                continue;
            }

            foreach (var line in group.Elements(ns + "line"))
            {
                AddSegment(result, netName!, line);
            }
        }

        foreach (
            var line in document
                .Descendants(ns + "line")
                .Where(element => HasCssClass(element, "rail"))
        )
        {
            var netName = (string?)line.Attribute("data-net");
            if (!string.IsNullOrWhiteSpace(netName))
            {
                AddSegment(result, netName!, line);
            }
        }

        return result;
    }

    private static void AddSegment(
        Dictionary<string, List<WireSegment>> result,
        string netName,
        XElement line
    )
    {
        if (!result.TryGetValue(netName, out var list))
        {
            list = new List<WireSegment>();
            result[netName] = list;
        }

        list.Add(
            new WireSegment(
                new GridPoint(
                    (int)Math.Round((double?)line.Attribute("x1") ?? 0),
                    (int)Math.Round((double?)line.Attribute("y1") ?? 0)
                ),
                new GridPoint(
                    (int)Math.Round((double?)line.Attribute("x2") ?? 0),
                    (int)Math.Round((double?)line.Attribute("y2") ?? 0)
                ),
                netName
            )
        );
    }

    private static bool HasCssClass(XElement element, string className)
    {
        var classValue = (string?)element.Attribute("class");
        if (string.IsNullOrWhiteSpace(classValue))
        {
            return false;
        }

        return classValue
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(className, StringComparer.Ordinal);
    }

    private static void AssertHardConstraints(RenderScenario scenario)
    {
        AssertRailEdgeClearances(scenario);
        AssertGateFacingRules(scenario);
        AssertSymmetricGroupConstraints(scenario);
        AssertBranchingHorizontalPassivesStayHorizontal(scenario);
    }

    private static void AssertRailEdgeClearances(RenderScenario scenario)
    {
        foreach (var (deviceId, cell) in scenario.Placement.DevicePlacements)
        {
            if (!scenario.Graph.Devices.TryGetValue(deviceId, out var device))
            {
                continue;
            }

            foreach (var terminal in GetExpectedRenderableTerminals(device))
            {
                var netName = scenario.Graph.GetNetForTerminal(deviceId, terminal);
                var edge = GetTerminalEdge(scenario, deviceId, device, terminal, cell);
                if (netName == null || edge == null)
                {
                    continue;
                }

                if (scenario.Graph.Supplies.Contains(netName) && edge == "North")
                {
                    Assert.True(
                        scenario
                            .Placement.DevicePlacements.Where(kv => kv.Key != deviceId)
                            .All(kv => kv.Value.Column != cell.Column || kv.Value.Row >= cell.Row),
                        FailurePrefix(
                            scenario,
                            $"Device '{deviceId}' has a north-edge supply terminal but is blocked above in column {cell.Column}."
                        )
                    );
                }

                if (scenario.Graph.Grounds.Contains(netName) && edge == "South")
                {
                    Assert.True(
                        scenario
                            .Placement.DevicePlacements.Where(kv => kv.Key != deviceId)
                            .All(kv => kv.Value.Column != cell.Column || kv.Value.Row <= cell.Row),
                        FailurePrefix(
                            scenario,
                            $"Device '{deviceId}' has a south-edge ground terminal but is blocked below in column {cell.Column}."
                        )
                    );
                }
            }
        }
    }

    private static void AssertGateFacingRules(RenderScenario scenario)
    {
        var diffPairDevices = scenario
            .Topology.SymmetricGroups.Where(group => group.Type == SymmetryType.DiffPair)
            .SelectMany(group => group.DeviceIds)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (deviceId, device) in scenario.Graph.Devices)
        {
            if (
                !IsMos(device.DeviceType)
                || !scenario.Placement.DevicePlacements.TryGetValue(deviceId, out var cell)
            )
            {
                continue;
            }

            var gateNet = scenario.Graph.GetNetForTerminal(deviceId, "G");
            if (gateNet == null)
            {
                continue;
            }

            if (
                !diffPairDevices.Contains(deviceId)
                && (
                    scenario.Graph.InputPorts.Contains(gateNet)
                    || scenario.Graph.BiasPorts.Contains(gateNet)
                )
            )
            {
                Assert.False(
                    cell.MirrorX,
                    FailurePrefix(
                        scenario,
                        $"Input/bias-driven gate on '{deviceId}' must face west."
                    )
                );
            }

            var partner = GetPointToPointGatePartner(scenario.Graph, deviceId);
            if (
                partner != null
                && scenario.Placement.DevicePlacements.TryGetValue(partner, out var other)
                && other.Column != cell.Column
            )
            {
                var expectedMirrorX = other.Column > cell.Column;
                Assert.True(
                    cell.MirrorX == expectedMirrorX,
                    FailurePrefix(
                        scenario,
                        $"Gate on '{deviceId}' must face '{partner}' on point-to-point gate net '{gateNet}'."
                    )
                );
            }
        }
    }

    private static void AssertSymmetricGroupConstraints(RenderScenario scenario)
    {
        foreach (var group in scenario.Topology.SymmetricGroups)
        {
            var cells = group
                .DeviceIds.Select(id => scenario.Placement.DevicePlacements[id])
                .ToList();
            Assert.True(
                cells.Select(cell => cell.Row).Distinct().Count() == 1,
                FailurePrefix(scenario, $"{group.Type} devices must share a row.")
            );
            Assert.True(
                cells.Select(cell => cell.Column).Distinct().Count() == cells.Count,
                FailurePrefix(scenario, $"{group.Type} devices must occupy distinct columns.")
            );

            if (group.Type != SymmetryType.DiffPair || group.DeviceIds.Count != 2)
            {
                continue;
            }

            var ordered = group
                .DeviceIds.OrderBy(id => scenario.Placement.DevicePlacements[id].Column)
                .ThenBy(id => id, StringComparer.Ordinal)
                .ToArray();
            Assert.False(
                scenario.Placement.DevicePlacements[ordered[0]].MirrorX,
                FailurePrefix(scenario, $"Left diff-pair device '{ordered[0]}' must face west.")
            );
            Assert.True(
                scenario.Placement.DevicePlacements[ordered[1]].MirrorX,
                FailurePrefix(scenario, $"Right diff-pair device '{ordered[1]}' must face east.")
            );
        }

        foreach (
            var (left, right, _) in TopologyAnalyzer.DetectSymmetricPassivePairs(
                scenario.Graph,
                scenario.Topology
            )
        )
        {
            var leftCell = scenario.Placement.DevicePlacements[left];
            var rightCell = scenario.Placement.DevicePlacements[right];
            Assert.True(
                leftCell.Row == rightCell.Row,
                FailurePrefix(
                    scenario,
                    $"Symmetric passive pair '{left}'/'{right}' must share a row."
                )
            );
            Assert.True(
                leftCell.Column != rightCell.Column,
                FailurePrefix(
                    scenario,
                    $"Symmetric passive pair '{left}'/'{right}' must occupy distinct columns."
                )
            );
        }
    }

    private static void AssertStraightConnectionClearanceRules(RenderScenario scenario)
    {
        var terminalsByDevice = scenario
            .Routing.TerminalPositions.GroupBy(t => t.DeviceId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var (netName, terminals) in GroupTerminalsByNet(scenario))
        {
            if (scenario.Graph.IsSupplyOrGround(netName) || terminals.Count != 2)
            {
                continue;
            }

            for (var i = 0; i < terminals.Count; i++)
            {
                for (var j = i + 1; j < terminals.Count; j++)
                {
                    var a = terminals[i];
                    var b = terminals[j];
                    if ((a.X != b.X && a.Y != b.Y) || (a.X == b.X && a.Y == b.Y))
                    {
                        continue;
                    }

                    foreach (var (deviceId, cell) in scenario.Placement.DevicePlacements)
                    {
                        if (
                            deviceId == a.DeviceId
                            || deviceId == b.DeviceId
                            || !IntersectsSegmentInterior(
                                cell,
                                scenario.Graph.Devices[deviceId].DeviceType,
                                a,
                                b
                            )
                        )
                        {
                            continue;
                        }

                        var ownsNetOnSegment = terminalsByDevice
                            .GetValueOrDefault(deviceId, [])
                            .Any(t =>
                                GetNetName(scenario.Graph, t) == netName
                                && IsPointOnSegment(
                                    new GridPoint(t.X, t.Y),
                                    new WireSegment(
                                        new GridPoint(a.X, a.Y),
                                        new GridPoint(b.X, b.Y),
                                        netName
                                    )
                                )
                            );
                        Assert.True(
                            ownsNetOnSegment,
                            FailurePrefix(
                                scenario,
                                $"Device '{deviceId}' blocks straight net '{netName}' between {a.DeviceId}.{a.Terminal} and {b.DeviceId}.{b.Terminal} without a same-net terminal on that segment."
                            )
                        );
                    }

                    foreach (
                        var terminal in scenario.Routing.TerminalPositions.Where(t =>
                            GetNetName(scenario.Graph, t) != netName
                        )
                    )
                    {
                        Assert.False(
                            IsStrictlyOnSegment(new GridPoint(terminal.X, terminal.Y), a, b),
                            FailurePrefix(
                                scenario,
                                $"Foreign terminal {terminal.DeviceId}.{terminal.Terminal} lies on straight net '{netName}'."
                            )
                        );
                    }
                }
            }
        }
    }

    private static void AssertBranchingHorizontalPassivesStayHorizontal(RenderScenario scenario)
    {
        var symmetricPassives = TopologyAnalyzer
            .DetectSymmetricPassivePairs(scenario.Graph, scenario.Topology)
            .SelectMany(pair => new[] { pair.Left, pair.Right })
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (deviceId, orientation) in scenario.Topology.PassiveOrientations)
        {
            if (
                orientation != PassiveOrientation.Horizontal
                || symmetricPassives.Contains(deviceId)
            )
            {
                continue;
            }

            var touchesBranchingNet = new[]
            {
                scenario.Graph.GetNetForTerminal(deviceId, "P"),
                scenario.Graph.GetNetForTerminal(deviceId, "N"),
            }
                .Where(net => net != null && !scenario.Graph.IsSupplyOrGround(net))
                .Any(net =>
                    scenario
                        .Graph.NetConnections[net!]
                        .Count(connection => !IsIgnoredPlacementTerminal(connection.Terminal)) > 2
                );

            if (touchesBranchingNet)
            {
                Assert.True(
                    scenario.Placement.HorizontalPassiveIds.Contains(deviceId),
                    FailurePrefix(
                        scenario,
                        $"Branching passive '{deviceId}' must remain horizontal."
                    )
                );
            }
        }
    }

    private static string? GetTerminalEdge(
        RenderScenario scenario,
        string deviceId,
        DeviceDeclaration device,
        string terminal,
        GridCell cell
    )
    {
        var type = device.DeviceType.ToLowerInvariant();
        if (type is "nmos" or "nfet" or "pmos" or "pfet")
        {
            return terminal switch
            {
                "G" => cell.MirrorX ? "East" : "West",
                "D" => type is "pmos" or "pfet" ? "South" : "North",
                "S" => type is "pmos" or "pfet" ? "North" : "South",
                _ => null,
            };
        }

        if (type is "resistor" or "capacitor" or "inductor")
        {
            if (!scenario.Placement.HorizontalPassiveIds.Contains(deviceId))
            {
                return terminal switch
                {
                    "P" => "North",
                    "N" => "South",
                    _ => null,
                };
            }

            var leftOfAxis = PlacementAxis.IsLeftOfAxis(scenario.Placement, cell.Column);
            return (terminal, leftOfAxis) switch
            {
                ("P", true) => "West",
                ("N", true) => "East",
                ("P", false) => "East",
                ("N", false) => "West",
                _ => null,
            };
        }

        return null;
    }

    private static string? GetPointToPointGatePartner(CircuitGraph graph, string deviceId)
    {
        var gateNet = graph.GetNetForTerminal(deviceId, "G");
        if (gateNet == null || !graph.NetConnections.TryGetValue(gateNet, out var connections))
        {
            return null;
        }

        var filtered = connections
            .Where(connection => !IsIgnoredPlacementTerminal(connection.Terminal))
            .ToList();
        if (
            filtered.Count != 2
            || filtered.Count(connection =>
                connection.DeviceId == deviceId
                && connection.Terminal.Equals("G", StringComparison.OrdinalIgnoreCase)
            ) != 1
        )
        {
            return null;
        }

        var other = filtered.Single(connection =>
            connection.DeviceId != deviceId
            || !connection.Terminal.Equals("G", StringComparison.OrdinalIgnoreCase)
        );
        return other.Terminal.Equals("G", StringComparison.OrdinalIgnoreCase)
            ? null
            : other.DeviceId;
    }

    private static bool IntersectsSegmentInterior(
        GridCell cell,
        string deviceType,
        TerminalPosition a,
        TerminalPosition b
    )
    {
        var colMargin = IsMos(deviceType) ? 1 : 0;
        var rowMargin = IsMos(deviceType) ? 1 : 0;
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

    private static bool IsIgnoredPlacementTerminal(string terminal)
    {
        return terminal.ToUpperInvariant() is "B" or "BODY" or "BULK" or "SH" or "SHIELD" or "TAP";
    }

    private static bool IsMos(string deviceType)
    {
        return deviceType.ToLowerInvariant() is "nmos" or "nfet" or "pmos" or "pfet";
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

    private static bool SegmentsTouchOrCross(WireSegment a, WireSegment b)
    {
        if (
            IsPointOnSegment(a.From, b)
            || IsPointOnSegment(a.To, b)
            || IsPointOnSegment(b.From, a)
            || IsPointOnSegment(b.To, a)
        )
        {
            return true;
        }

        var aHorizontal = a.From.Y == a.To.Y;
        var bHorizontal = b.From.Y == b.To.Y;
        if (aHorizontal == bHorizontal)
        {
            return false;
        }

        var h = aHorizontal ? a : b;
        var v = aHorizontal ? b : a;
        var hMinX = Math.Min(h.From.X, h.To.X);
        var hMaxX = Math.Max(h.From.X, h.To.X);
        var vMinY = Math.Min(v.From.Y, v.To.Y);
        var vMaxY = Math.Max(v.From.Y, v.To.Y);
        return v.From.X >= hMinX && v.From.X <= hMaxX && h.From.Y >= vMinY && h.From.Y <= vMaxY;
    }

    private static bool IsPointOnSegment(GridPoint point, WireSegment segment)
    {
        if (point == segment.From || point == segment.To)
        {
            return true;
        }

        if (segment.From.X == segment.To.X && point.X == segment.From.X)
        {
            var minY = Math.Min(segment.From.Y, segment.To.Y);
            var maxY = Math.Max(segment.From.Y, segment.To.Y);
            return point.Y >= minY && point.Y <= maxY;
        }

        if (segment.From.Y == segment.To.Y && point.Y == segment.From.Y)
        {
            var minX = Math.Min(segment.From.X, segment.To.X);
            var maxX = Math.Max(segment.From.X, segment.To.X);
            return point.X >= minX && point.X <= maxX;
        }

        return false;
    }

    private static string FailurePrefix(RenderScenario scenario, string message)
    {
        return $"{scenario.RelativePath}::{scenario.CircuitName}: {message}";
    }

    private sealed record RenderScenario(
        string RelativePath,
        string CircuitName,
        CircuitGraph Graph,
        TopologyResult Topology,
        CoarseGridResult Placement,
        RoutingResult Routing,
        string Svg
    );
}
