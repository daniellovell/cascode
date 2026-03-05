namespace Cascode.Render.Tests.Routing;

using Cascode.Language;
using Cascode.Render.Analysis;
using Cascode.Render.Placement;
using Cascode.Render.Routing;

public class MazeRouterTests
{
    [Theory]
    [InlineData("tests/golden/cas/cs/CSAmpResistive.el.cai")]
    [InlineData("tests/golden/cas/ota/OTA5TSingleEnded.el.cai")]
    [InlineData("tests/golden/cas/ota/OTA5TFullyDiff.el.cai")]
    [InlineData("tests/golden/render/filters/DiffRCFilter.el.cai")]
    public void Route_AllNetsFullyConnected(string cascodePath)
    {
        // Arrange
        var fullPath = Path.Combine(GetRepoRoot(), cascodePath);
        using var reader = File.OpenText(fullPath);
        var readResult = CascodeReader.TryRead(reader, fullPath);
        Assert.True(readResult.Success, "Failed to parse Cascode file");

        var doc = readResult.Document!;
        var elCircuit = doc.Circuits.First(c => c.Level == CascodeLevel.EL);

        var graph = CircuitGraph.Build(elCircuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var placement = CoarseGridPlacer.Place(topology, graph);

        // Act
        var result = MazeRouter.Route(placement, graph);

        // Assert - every net with 2+ terminals must be fully connected
        foreach (var (netName, segments) in result.SegmentsByNet)
        {
            var terminalPoints = GetTerminalPointsForNet(netName, placement, graph, result);
            if (terminalPoints.Count < 2)
            {
                continue;
            }

            // For power rails, add a virtual horizontal segment representing the rail
            var segmentsWithRail = segments.ToList();
            if (graph.Supplies.Contains(netName))
            {
                var railY = Layout.DeviceGeometry.RailMargin / 2;
                segmentsWithRail.Add(
                    new WireSegment(
                        new GridPoint(0, railY),
                        new GridPoint(result.CanvasWidth, railY),
                        netName
                    )
                );
            }
            else if (graph.Grounds.Contains(netName))
            {
                var railY = result.CanvasHeight - Layout.DeviceGeometry.RailMargin / 2;
                segmentsWithRail.Add(
                    new WireSegment(
                        new GridPoint(0, railY),
                        new GridPoint(result.CanvasWidth, railY),
                        netName
                    )
                );
            }

            var connected = AreAllPointsConnected(terminalPoints, segmentsWithRail);
            Assert.True(
                connected,
                $"Net '{netName}' is not fully connected. "
                    + $"Terminals: [{string.Join(", ", terminalPoints.Select(p => $"({p.X},{p.Y})"))}], "
                    + $"Segments: {segments.Count}"
            );
        }
    }

    /// <summary>
    /// Gets all terminal points that should be connected for a net.
    /// </summary>
    private static List<GridPoint> GetTerminalPointsForNet(
        string netName,
        CoarseGridResult placement,
        CircuitGraph graph,
        RoutingResult result
    )
    {
        var points = new List<GridPoint>();

        // Device terminals
        foreach (var (deviceId, cell) in placement.DevicePlacements)
        {
            if (!graph.Devices.TryGetValue(deviceId, out var device))
            {
                continue;
            }

            var deviceType = device.DeviceType.ToLowerInvariant();
            var terminals = GetDeviceTerminals(deviceType);

            foreach (var terminal in terminals)
            {
                var termNet = graph.GetNetForTerminal(deviceId, terminal);
                if (termNet != netName)
                {
                    continue;
                }

                var pos = GetTerminalPosition(deviceId, terminal, deviceType, cell, placement);
                if (pos.HasValue)
                {
                    points.Add(pos.Value);
                }
            }
        }

        // Port terminals
        if (graph.InputPorts.Contains(netName) || graph.BiasPorts.Contains(netName))
        {
            // Left side port - find where the wire should connect
            var leftX = 0;
            var portY = FindPortYInResult(netName, leftX, result);
            if (portY.HasValue)
            {
                points.Add(new GridPoint(leftX, portY.Value));
            }
        }

        if (graph.OutputPorts.Contains(netName))
        {
            // Right side port
            var rightX = result.CanvasWidth;
            var portY = FindPortYInResult(netName, rightX, result);
            if (portY.HasValue)
            {
                points.Add(new GridPoint(rightX, portY.Value));
            }
        }

        return points;
    }

    private static string[] GetDeviceTerminals(string deviceType)
    {
        return deviceType switch
        {
            "nmos" or "nfet" or "pmos" or "pfet" => new[] { "G", "D", "S" },
            "resistor" or "capacitor" => new[] { "P", "N" },
            _ => Array.Empty<string>(),
        };
    }

    private static GridPoint? GetTerminalPosition(
        string deviceId,
        string terminal,
        string deviceType,
        GridCell cell,
        CoarseGridResult placement
    )
    {
        if (deviceType is "nmos" or "nfet" or "pmos" or "pfet")
        {
            var isPmos = deviceType is "pmos" or "pfet";
            var p = Layout.DeviceGeometry.GetMosfetPlacement(cell.Row, cell.Column, cell.MirrorX);

            return terminal switch
            {
                "G" => new GridPoint(p.GateX, p.GateY),
                "D" => new GridPoint(p.DrainX, isPmos ? p.SourceY : p.DrainY),
                "S" => new GridPoint(p.SourceX, isPmos ? p.DrainY : p.SourceY),
                _ => null,
            };
        }

        if (deviceType is "resistor" or "capacitor")
        {
            var isHorizontalPassive = placement.HorizontalPassiveIds.Contains(deviceId);
            var isLeftOfAxis = cell.Column < placement.SymmetryAxis;

            if (isHorizontalPassive)
            {
                var p = Layout.DeviceGeometry.GetHorizontalPassivePlacement(
                    cell.Row,
                    cell.Column,
                    placement.ColumnCount,
                    isLeftOfAxis
                );
                return terminal switch
                {
                    "P" => new GridPoint(p.PX, p.PY),
                    "N" => new GridPoint(p.NX, p.NY),
                    _ => null,
                };
            }
            else
            {
                var p = Layout.DeviceGeometry.GetPassivePlacement(cell.Row, cell.Column);
                return terminal switch
                {
                    "P" => new GridPoint(p.PX, p.PY),
                    "N" => new GridPoint(p.NX, p.NY),
                    _ => null,
                };
            }
        }

        return null;
    }

    private static int? FindPortYInResult(string netName, int x, RoutingResult result)
    {
        if (!result.SegmentsByNet.TryGetValue(netName, out var segments))
        {
            return null;
        }

        // Find segment endpoint at this X coordinate
        foreach (var seg in segments)
        {
            if (seg.From.X == x)
            {
                return seg.From.Y;
            }
            if (seg.To.X == x)
            {
                return seg.To.Y;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if all points are connected via the wire segments.
    /// Uses union-find on segments - two segments are connected if they share any point.
    /// </summary>
    private static bool AreAllPointsConnected(
        List<GridPoint> terminals,
        IReadOnlyList<WireSegment> segments
    )
    {
        if (terminals.Count <= 1)
        {
            return true;
        }

        if (segments.Count == 0)
        {
            return false;
        }

        // Union-find on segment indices
        var parent = Enumerable.Range(0, segments.Count).ToArray();

        int Find(int x)
        {
            if (parent[x] != x)
            {
                parent[x] = Find(parent[x]);
            }
            return parent[x];
        }

        void Union(int x, int y)
        {
            var px = Find(x);
            var py = Find(y);
            if (px != py)
            {
                parent[px] = py;
            }
        }

        // Connect segments that share any point
        for (var i = 0; i < segments.Count; i++)
        {
            for (var j = i + 1; j < segments.Count; j++)
            {
                if (SegmentsTouch(segments[i], segments[j]))
                {
                    Union(i, j);
                }
            }
        }

        // Find which segment component each terminal belongs to
        var terminalComponents = new List<int>();
        foreach (var terminal in terminals)
        {
            var found = false;
            for (var i = 0; i < segments.Count; i++)
            {
                if (IsPointOnSegment(terminal, segments[i]))
                {
                    terminalComponents.Add(Find(i));
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                return false; // Terminal not on any segment
            }
        }

        // All terminals must be in the same component
        return terminalComponents.Distinct().Count() == 1;
    }

    /// <summary>
    /// Checks if two segments touch (share any point).
    /// </summary>
    private static bool SegmentsTouch(WireSegment a, WireSegment b)
    {
        // Check if any endpoint of one segment lies on the other
        if (IsPointOnSegment(a.From, b) || IsPointOnSegment(a.To, b))
        {
            return true;
        }
        if (IsPointOnSegment(b.From, a) || IsPointOnSegment(b.To, a))
        {
            return true;
        }

        // Check for crossing (perpendicular segments)
        var aHorizontal = a.From.Y == a.To.Y;
        var bHorizontal = b.From.Y == b.To.Y;

        if (aHorizontal != bHorizontal)
        {
            var h = aHorizontal ? a : b;
            var v = aHorizontal ? b : a;

            var hY = h.From.Y;
            var hMinX = Math.Min(h.From.X, h.To.X);
            var hMaxX = Math.Max(h.From.X, h.To.X);

            var vX = v.From.X;
            var vMinY = Math.Min(v.From.Y, v.To.Y);
            var vMaxY = Math.Max(v.From.Y, v.To.Y);

            if (vX >= hMinX && vX <= hMaxX && hY >= vMinY && hY <= vMaxY)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPointOnSegment(GridPoint point, WireSegment segment)
    {
        // Check endpoints
        if (point.Equals(segment.From) || point.Equals(segment.To))
        {
            return true;
        }

        // Check if point is on horizontal segment
        if (segment.From.Y == segment.To.Y && point.Y == segment.From.Y)
        {
            var minX = Math.Min(segment.From.X, segment.To.X);
            var maxX = Math.Max(segment.From.X, segment.To.X);
            return point.X >= minX && point.X <= maxX;
        }

        // Check if point is on vertical segment
        if (segment.From.X == segment.To.X && point.X == segment.From.X)
        {
            var minY = Math.Min(segment.From.Y, segment.To.Y);
            var maxY = Math.Max(segment.From.Y, segment.To.Y);
            return point.Y >= minY && point.Y <= maxY;
        }

        return false;
    }

    [Theory]
    [InlineData("tests/golden/cas/cs/CSAmpResistive.el.cai")]
    [InlineData("tests/golden/cas/ota/OTA5TSingleEnded.el.cai")]
    [InlineData("tests/golden/cas/ota/OTA5TFullyDiff.el.cai")]
    [InlineData("tests/golden/render/filters/DiffRCFilter.el.cai")]
    public void Route_NoOverlappingSegmentsWithinNet(string cascodePath)
    {
        // Arrange
        var fullPath = Path.Combine(GetRepoRoot(), cascodePath);
        using var reader = File.OpenText(fullPath);
        var readResult = CascodeReader.TryRead(reader, fullPath);
        Assert.True(readResult.Success, "Failed to parse Cascode file");

        var doc = readResult.Document!;
        var elCircuit = doc.Circuits.First(c => c.Level == CascodeLevel.EL);

        var graph = CircuitGraph.Build(elCircuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var placement = CoarseGridPlacer.Place(topology, graph);
        var result = MazeRouter.Route(placement, graph);

        // Assert - no two segments of the same net should overlap (share interior points)
        foreach (var (netName, segments) in result.SegmentsByNet)
        {
            for (var i = 0; i < segments.Count; i++)
            {
                for (var j = i + 1; j < segments.Count; j++)
                {
                    var overlap = GetOverlapLength(segments[i], segments[j]);
                    Assert.True(
                        overlap == 0,
                        $"Net '{netName}' has overlapping segments: "
                            + $"({segments[i].From.X},{segments[i].From.Y})->({segments[i].To.X},{segments[i].To.Y}) and "
                            + $"({segments[j].From.X},{segments[j].From.Y})->({segments[j].To.X},{segments[j].To.Y}), "
                            + $"overlap length: {overlap}"
                    );
                }
            }
        }
    }

    /// <summary>
    /// Gets the overlap length between two collinear segments. Returns 0 if no overlap or not collinear.
    /// </summary>
    private static int GetOverlapLength(WireSegment a, WireSegment b)
    {
        var aHorizontal = a.From.Y == a.To.Y;
        var bHorizontal = b.From.Y == b.To.Y;

        // Both horizontal on same Y
        if (aHorizontal && bHorizontal && a.From.Y == b.From.Y)
        {
            var aMin = Math.Min(a.From.X, a.To.X);
            var aMax = Math.Max(a.From.X, a.To.X);
            var bMin = Math.Min(b.From.X, b.To.X);
            var bMax = Math.Max(b.From.X, b.To.X);

            var overlapStart = Math.Max(aMin, bMin);
            var overlapEnd = Math.Min(aMax, bMax);

            // Overlap must be more than just touching at a point
            if (overlapEnd > overlapStart)
            {
                return overlapEnd - overlapStart;
            }
        }

        // Both vertical on same X
        if (!aHorizontal && !bHorizontal && a.From.X == b.From.X)
        {
            var aMin = Math.Min(a.From.Y, a.To.Y);
            var aMax = Math.Max(a.From.Y, a.To.Y);
            var bMin = Math.Min(b.From.Y, b.To.Y);
            var bMax = Math.Max(b.From.Y, b.To.Y);

            var overlapStart = Math.Max(aMin, bMin);
            var overlapEnd = Math.Min(aMax, bMax);

            // Overlap must be more than just touching at a point
            if (overlapEnd > overlapStart)
            {
                return overlapEnd - overlapStart;
            }
        }

        return 0;
    }

    [Theory]
    [InlineData("tests/golden/cas/ota/OTA5TFullyDiff.el.cai")]
    public void Route_JunctionsAtBranchPointsNotTerminals(string cascodePath)
    {
        // Arrange
        var fullPath = Path.Combine(GetRepoRoot(), cascodePath);
        using var reader = File.OpenText(fullPath);
        var readResult = CascodeReader.TryRead(reader, fullPath);
        Assert.True(readResult.Success, "Failed to parse Cascode file");

        var doc = readResult.Document!;
        var elCircuit = doc.Circuits.First(c => c.Level == CascodeLevel.EL);

        var graph = CircuitGraph.Build(elCircuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var placement = CoarseGridPlacer.Place(topology, graph);
        var result = MazeRouter.Route(placement, graph);

        // For each junction, verify it's at a point where segments actually diverge
        // (not just where multiple segments happen to start from the same terminal)
        foreach (var junction in result.Junctions)
        {
            // Count segment endpoints at this junction
            var endpointCount = 0;
            var segmentsAtPoint = new List<WireSegment>();

            foreach (var seg in result.Segments)
            {
                if (seg.From.Equals(junction) || seg.To.Equals(junction))
                {
                    endpointCount++;
                    segmentsAtPoint.Add(seg);
                }
            }

            // A valid junction should have segments going in different directions
            // (not all parallel segments starting from the same point)
            if (segmentsAtPoint.Count >= 2)
            {
                var directions = segmentsAtPoint
                    .Select(s => GetSegmentDirection(s, junction))
                    .Distinct()
                    .ToList();

                Assert.True(
                    directions.Count >= 2,
                    $"Junction at ({junction.X}, {junction.Y}) has {segmentsAtPoint.Count} segments "
                        + $"but only {directions.Count} distinct direction(s). "
                        + $"Segments: [{string.Join(", ", segmentsAtPoint.Select(s => $"({s.From.X},{s.From.Y})->({s.To.X},{s.To.Y})"))}]"
                );
            }
        }
    }

    /// <summary>
    /// Gets the direction a segment goes from a given point (Up, Down, Left, Right).
    /// </summary>
    private static string GetSegmentDirection(WireSegment seg, GridPoint fromPoint)
    {
        int dx,
            dy;
        if (seg.From.Equals(fromPoint))
        {
            dx = seg.To.X - seg.From.X;
            dy = seg.To.Y - seg.From.Y;
        }
        else
        {
            dx = seg.From.X - seg.To.X;
            dy = seg.From.Y - seg.To.Y;
        }

        if (dx > 0)
            return "Right";
        if (dx < 0)
            return "Left";
        if (dy > 0)
            return "Down";
        if (dy < 0)
            return "Up";
        return "None";
    }

    [Theory]
    [InlineData("tests/golden/cas/ota/OTA5TFullyDiff.el.cai")]
    public void Route_DevicesOnSameAxisConnectedDirectly(string cascodePath)
    {
        // Arrange
        var fullPath = Path.Combine(GetRepoRoot(), cascodePath);
        using var reader = File.OpenText(fullPath);
        var readResult = CascodeReader.TryRead(reader, fullPath);
        Assert.True(readResult.Success, "Failed to parse Cascode file");

        var doc = readResult.Document!;
        var elCircuit = doc.Circuits.First(c => c.Level == CascodeLevel.EL);

        var graph = CircuitGraph.Build(elCircuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var placement = CoarseGridPlacer.Place(topology, graph);
        var result = MazeRouter.Route(placement, graph);

        // For each net, if two device terminals share the same X coordinate,
        // there should be a direct vertical path between them on that X
        foreach (var (netName, segments) in result.SegmentsByNet)
        {
            // Skip power rails
            if (graph.Supplies.Contains(netName) || graph.Grounds.Contains(netName))
                continue;

            // Get device terminal positions for this net (exclude ports)
            var deviceTerminals = result
                .TerminalPositions.Where(t =>
                    !t.DeviceId.StartsWith("PORT_", StringComparison.Ordinal)
                    && graph.GetNetForTerminal(t.DeviceId, t.Terminal) == netName
                )
                .ToList();

            // Group terminals by X coordinate
            var terminalsByX = deviceTerminals
                .GroupBy(t => t.X)
                .Where(g => g.Count() >= 2)
                .ToList();

            foreach (var group in terminalsByX)
            {
                var x = group.Key;
                var ys = group.Select(t => t.Y).OrderBy(y => y).ToList();
                var minY = ys.First();
                var maxY = ys.Last();

                // Get vertical segments on this X coordinate
                var verticalSegsOnX = segments.Where(s => s.From.X == x && s.To.X == x).ToList();

                // Check that vertical segments cover the entire Y range between terminals
                var coveredRanges = verticalSegsOnX
                    .Select(s => (Math.Min(s.From.Y, s.To.Y), Math.Max(s.From.Y, s.To.Y)))
                    .OrderBy(r => r.Item1)
                    .ToList();

                // Merge overlapping/adjacent ranges
                var merged = new List<(int, int)>();
                foreach (var range in coveredRanges)
                {
                    if (merged.Count == 0 || range.Item1 > merged.Last().Item2)
                    {
                        merged.Add(range);
                    }
                    else
                    {
                        var last = merged.Last();
                        merged[merged.Count - 1] = (last.Item1, Math.Max(last.Item2, range.Item2));
                    }
                }

                // Check if the merged ranges cover minY to maxY
                var coverageOk =
                    merged.Count > 0 && merged.First().Item1 <= minY && merged.Last().Item2 >= maxY;

                Assert.True(
                    coverageOk,
                    $"Net '{netName}' has device terminals at X={x} spanning Y=[{minY}, {maxY}], "
                        + $"but vertical segments on that axis only cover: [{string.Join(", ", merged.Select(r => $"({r.Item1}-{r.Item2})"))}]. "
                        + $"Device terminals: [{string.Join(", ", group.Select(t => $"{t.DeviceId}.{t.Terminal}@({t.X},{t.Y})"))}]"
                );
            }
        }
    }

    [Theory]
    [InlineData("tests/golden/cas/cs/CSAmpResistive.el.cai")]
    [InlineData("tests/golden/cas/ota/OTA5TSingleEnded.el.cai")]
    [InlineData("tests/golden/cas/ota/OTA5TFullyDiff.el.cai")]
    [InlineData("tests/golden/render/filters/DiffRCFilter.el.cai")]
    public void Route_PortTerminalPositionsConnectedToWires(string cascodePath)
    {
        // Arrange
        var fullPath = Path.Combine(GetRepoRoot(), cascodePath);
        using var reader = File.OpenText(fullPath);
        var readResult = CascodeReader.TryRead(reader, fullPath);
        Assert.True(readResult.Success, "Failed to parse Cascode file");

        var doc = readResult.Document!;
        var elCircuit = doc.Circuits.First(c => c.Level == CascodeLevel.EL);

        var graph = CircuitGraph.Build(elCircuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var placement = CoarseGridPlacer.Place(topology, graph);
        var result = MazeRouter.Route(placement, graph);

        // Get port terminal positions from routing
        var portTerminals = result
            .TerminalPositions.Where(t => t.DeviceId.StartsWith("PORT_", StringComparison.Ordinal))
            .ToDictionary(t => t.DeviceId.Substring(5), t => new GridPoint(t.X, t.Y));

        // Assert - each output port terminal must have a wire segment reaching it
        foreach (var portName in graph.OutputPorts)
        {
            Assert.True(
                portTerminals.TryGetValue(portName, out var termPos),
                $"Port '{portName}' not found in terminal positions"
            );

            var segments = result.SegmentsByNet.GetValueOrDefault(portName);
            Assert.NotNull(segments);

            var hasWireToPort = segments.Any(seg =>
                (seg.From.X == termPos.X && seg.From.Y == termPos.Y)
                || (seg.To.X == termPos.X && seg.To.Y == termPos.Y)
            );

            Assert.True(
                hasWireToPort,
                $"Output port '{portName}' at ({termPos.X}, {termPos.Y}) has no wire endpoint. "
                    + $"Segments: [{string.Join(", ", segments.Select(s => $"({s.From.X},{s.From.Y})->({s.To.X},{s.To.Y})"))}]"
            );
        }

        // Assert - each input/bias port terminal must have a wire segment reaching it
        foreach (var portName in graph.InputPorts.Concat(graph.BiasPorts))
        {
            Assert.True(
                portTerminals.TryGetValue(portName, out var termPos),
                $"Port '{portName}' not found in terminal positions"
            );

            var segments = result.SegmentsByNet.GetValueOrDefault(portName);
            Assert.NotNull(segments);

            var hasWireToPort = segments.Any(seg =>
                (seg.From.X == termPos.X && seg.From.Y == termPos.Y)
                || (seg.To.X == termPos.X && seg.To.Y == termPos.Y)
            );

            Assert.True(
                hasWireToPort,
                $"Input port '{portName}' at ({termPos.X}, {termPos.Y}) has no wire endpoint. "
                    + $"Segments: [{string.Join(", ", segments.Select(s => $"({s.From.X},{s.From.Y})->({s.To.X},{s.To.Y})"))}]"
            );
        }
    }

    [Theory]
    [InlineData("tests/golden/cas/ota/OTA5TFullyDiff.el.cai")]
    public void Route_SymmetricTerminalsMeetAtCenterDevice(string cascodePath)
    {
        // This test verifies that when a net has terminals on opposite sides of the
        // symmetry axis PLUS a center terminal, the routing goes through the center
        // rather than taking a direct horizontal path across the schematic.
        //
        // Example: tnode in OTA5TFullyDiff has:
        //   - dp.M_N.S on left
        //   - dp.M_P.S on right
        //   - dp.M_TAIL.D in center
        // The expected routing is Y-shaped: both sources route DOWN to meet at tail drain.

        // Arrange
        var fullPath = Path.Combine(GetRepoRoot(), cascodePath);
        using var reader = File.OpenText(fullPath);
        var readResult = CascodeReader.TryRead(reader, fullPath);
        Assert.True(readResult.Success, "Failed to parse Cascode file");

        var doc = readResult.Document!;
        var elCircuit = doc.Circuits.First(c => c.Level == CascodeLevel.EL);

        var graph = CircuitGraph.Build(elCircuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var placement = CoarseGridPlacer.Place(topology, graph);
        var result = MazeRouter.Route(placement, graph);

        // Find nets that have terminals spanning both sides of the symmetry axis plus a center terminal
        var symmetryAxisX =
            placement.SymmetryAxis * Layout.DeviceGeometry.CellWidth
            + Layout.DeviceGeometry.CellWidth / 2;

        foreach (var (netName, segments) in result.SegmentsByNet)
        {
            // Skip power rails
            if (graph.Supplies.Contains(netName) || graph.Grounds.Contains(netName))
                continue;

            // Get device terminal positions for this net (exclude ports)
            var deviceTerminals = result
                .TerminalPositions.Where(t =>
                    !t.DeviceId.StartsWith("PORT_", StringComparison.Ordinal)
                    && graph.GetNetForTerminal(t.DeviceId, t.Terminal) == netName
                )
                .ToList();

            if (deviceTerminals.Count < 3)
                continue;

            // Check if terminals span both sides of symmetry axis
            var leftTerminals = deviceTerminals.Where(t => t.X < symmetryAxisX - 20).ToList();
            var rightTerminals = deviceTerminals.Where(t => t.X > symmetryAxisX + 20).ToList();
            var centerTerminals = deviceTerminals
                .Where(t => Math.Abs(t.X - symmetryAxisX) <= 20)
                .ToList();

            if (leftTerminals.Count == 0 || rightTerminals.Count == 0 || centerTerminals.Count == 0)
                continue;

            // This net spans both sides with a center terminal - verify no direct horizontal
            // connection between left and right sides that bypasses the center

            // Check for direct horizontal segments that go from left side to right side
            // without passing through the center X coordinate
            foreach (var seg in segments)
            {
                if (seg.From.Y != seg.To.Y)
                    continue; // Not horizontal

                var minX = Math.Min(seg.From.X, seg.To.X);
                var maxX = Math.Max(seg.From.X, seg.To.X);

                // Check if this horizontal segment spans from left of center to right of center
                var spansLeftOfCenter = minX < symmetryAxisX - 20;
                var spansRightOfCenter = maxX > symmetryAxisX + 20;

                if (spansLeftOfCenter && spansRightOfCenter)
                {
                    // This segment crosses the center - check if there's actually a junction at center
                    var hasCenterJunction = result.Junctions.Any(j =>
                        j.Y == seg.From.Y && j.X >= symmetryAxisX - 20 && j.X <= symmetryAxisX + 20
                    );

                    // If there's no junction at the center, this is a direct bypass
                    Assert.True(
                        hasCenterJunction,
                        $"Net '{netName}' has a direct horizontal segment from ({seg.From.X},{seg.From.Y}) to "
                            + $"({seg.To.X},{seg.To.Y}) that bypasses the center device. "
                            + $"Center terminals: [{string.Join(", ", centerTerminals.Select(t => $"{t.DeviceId}.{t.Terminal}@({t.X},{t.Y})"))}]"
                    );
                }
            }
        }
    }

    [Theory]
    [InlineData("tests/golden/cas/ota/OTA5TFullyDiff.el.cai")]
    public void Route_NoRedundantParallelPaths(string cascodePath)
    {
        // This test verifies that there are no redundant parallel paths to the same destination.
        // A proper tree has exactly one path between any two points.
        //
        // Example: OUT_N should have a single horizontal path to the port, not multiple
        // parallel horizontal segments at different Y coordinates that converge at the port.

        // Arrange
        var fullPath = Path.Combine(GetRepoRoot(), cascodePath);
        using var reader = File.OpenText(fullPath);
        var readResult = CascodeReader.TryRead(reader, fullPath);
        Assert.True(readResult.Success, "Failed to parse Cascode file");

        var doc = readResult.Document!;
        var elCircuit = doc.Circuits.First(c => c.Level == CascodeLevel.EL);

        var graph = CircuitGraph.Build(elCircuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var placement = CoarseGridPlacer.Place(topology, graph);
        var result = MazeRouter.Route(placement, graph);

        foreach (var (netName, segments) in result.SegmentsByNet)
        {
            // Skip power rails (they intentionally have parallel drops to the rail)
            if (graph.Supplies.Contains(netName) || graph.Grounds.Contains(netName))
                continue;

            // Check for redundant parallel paths: multiple horizontal segments at different Y
            // that share the same X range (indicating parallel horizontal runs)
            var horizontalSegments = segments.Where(s => s.From.Y == s.To.Y).ToList();

            for (var i = 0; i < horizontalSegments.Count; i++)
            {
                for (var j = i + 1; j < horizontalSegments.Count; j++)
                {
                    var seg1 = horizontalSegments[i];
                    var seg2 = horizontalSegments[j];

                    // Skip if they're on the same Y (would be detected by overlap test)
                    if (seg1.From.Y == seg2.From.Y)
                        continue;

                    // Check if they have overlapping X ranges
                    var min1X = Math.Min(seg1.From.X, seg1.To.X);
                    var max1X = Math.Max(seg1.From.X, seg1.To.X);
                    var min2X = Math.Min(seg2.From.X, seg2.To.X);
                    var max2X = Math.Max(seg2.From.X, seg2.To.X);

                    var overlapStart = Math.Max(min1X, min2X);
                    var overlapEnd = Math.Min(max1X, max2X);
                    var overlapLength = overlapEnd - overlapStart;

                    // Significant overlap (more than a grid cell) indicates potential parallel redundant paths
                    const int significantOverlap = 15;
                    if (overlapLength > significantOverlap)
                    {
                        // Check if both segments connect at BOTH ends of the overlap.
                        // This indicates a true redundant parallel path (two alternative routes).
                        // If they only connect at one end, it's a valid Y-shaped tree structure.
                        var connectsAtLeftEnd = min1X == min2X && min1X == overlapStart;
                        var connectsAtRightEnd = max1X == max2X && max1X == overlapEnd;

                        if (connectsAtLeftEnd && connectsAtRightEnd)
                        {
                            Assert.Fail(
                                $"Net '{netName}' has redundant parallel horizontal paths: "
                                    + $"segment at Y={seg1.From.Y} from X={min1X} to X={max1X} and "
                                    + $"segment at Y={seg2.From.Y} from X={min2X} to X={max2X} "
                                    + $"overlap for {overlapLength} units and connect at both ends"
                            );
                        }
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData("tests/golden/cas/ota/OTA5TFullyDiff.el.cai", "vcm_node")]
    public void Route_GatesToGatesConnectDirectlyOnSameY(string cascodePath, string targetNet)
    {
        // This test verifies that when a net has multiple gate terminals at the same Y level,
        // they are connected by a direct horizontal path rather than routing through other nodes.
        //
        // Example: vcm_node in OTA5TFullyDiff has:
        //   - M_LOAD_P.G and M_LOAD_N.G (both PMOS gates at same Y level)
        //   - R_CMFB_P.N and R_CMFB_N.N (resistor internal nodes below)
        // Expected routing: gates connect horizontally, then one vertical drops to resistors.

        // Arrange
        var fullPath = Path.Combine(GetRepoRoot(), cascodePath);
        using var reader = File.OpenText(fullPath);
        var readResult = CascodeReader.TryRead(reader, fullPath);
        Assert.True(readResult.Success, "Failed to parse Cascode file");

        var doc = readResult.Document!;
        var elCircuit = doc.Circuits.First(c => c.Level == CascodeLevel.EL);

        var graph = CircuitGraph.Build(elCircuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var placement = CoarseGridPlacer.Place(topology, graph);
        var result = MazeRouter.Route(placement, graph);

        // Get gate terminals for the target net
        var gateTerminals = result
            .TerminalPositions.Where(t =>
                t.Terminal == "G" && graph.GetNetForTerminal(t.DeviceId, t.Terminal) == targetNet
            )
            .ToList();

        Assert.True(
            gateTerminals.Count >= 2,
            $"Expected at least 2 gate terminals on net '{targetNet}', found {gateTerminals.Count}"
        );

        // Group gates by Y coordinate (same-Y gates should connect horizontally)
        var gatesByY = gateTerminals.GroupBy(t => t.Y).Where(g => g.Count() >= 2).ToList();

        Assert.NotEmpty(gatesByY);

        var segments = result.SegmentsByNet[targetNet];

        foreach (var group in gatesByY)
        {
            var y = group.Key;
            var gatesAtY = group.OrderBy(t => t.X).ToList();
            var leftGate = gatesAtY.First();
            var rightGate = gatesAtY.Last();

            // There should be a horizontal segment at this Y connecting the gates
            var horizontalAtY = segments.Where(s => s.From.Y == y && s.To.Y == y).ToList();

            // Verify the horizontal segments cover the span between gates
            var coveredX = new HashSet<int>();
            foreach (var seg in horizontalAtY)
            {
                var minX = Math.Min(seg.From.X, seg.To.X);
                var maxX = Math.Max(seg.From.X, seg.To.X);
                for (var x = minX; x <= maxX; x++)
                {
                    coveredX.Add(x);
                }
            }

            var allCovered = true;
            for (var x = leftGate.X; x <= rightGate.X; x++)
            {
                if (!coveredX.Contains(x))
                {
                    allCovered = false;
                    break;
                }
            }

            Assert.True(
                allCovered,
                $"Net '{targetNet}': gates at Y={y} ({leftGate.DeviceId}.G at X={leftGate.X}, "
                    + $"{rightGate.DeviceId}.G at X={rightGate.X}) are not connected by horizontal path. "
                    + $"Horizontal segments at Y={y}: [{string.Join(", ", horizontalAtY.Select(s => $"({s.From.X},{s.From.Y})->({s.To.X},{s.To.Y})"))}]"
            );
        }
    }

    [Theory]
    [InlineData("tests/golden/cas/cs/CSAmpResistive.el.cai")]
    [InlineData("tests/golden/cas/ota/OTA5TSingleEnded.el.cai")]
    [InlineData("tests/golden/cas/ota/OTA5TFullyDiff.el.cai")]
    [InlineData("tests/golden/render/filters/DiffRCFilter.el.cai")]
    public void Route_NoUselessWireStubs(string cascodePath)
    {
        // This test verifies that every wire segment endpoint either:
        // 1. Connects to a terminal (device or port)
        // 2. Connects to another wire segment (forms a junction)
        // Dead-end wire stubs that lead nowhere are routing errors.

        // Arrange
        var fullPath = Path.Combine(GetRepoRoot(), cascodePath);
        using var reader = File.OpenText(fullPath);
        var readResult = CascodeReader.TryRead(reader, fullPath);
        Assert.True(readResult.Success, "Failed to parse Cascode file");

        var doc = readResult.Document!;
        var elCircuit = doc.Circuits.First(c => c.Level == CascodeLevel.EL);

        var graph = CircuitGraph.Build(elCircuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var placement = CoarseGridPlacer.Place(topology, graph);
        var result = MazeRouter.Route(placement, graph);

        // Build set of all terminal positions
        var terminalPoints = result
            .TerminalPositions.Select(t => new GridPoint(t.X, t.Y))
            .ToHashSet();

        foreach (var (netName, segments) in result.SegmentsByNet)
        {
            // Skip power rails - they have intentional stubs for drops
            if (graph.Supplies.Contains(netName) || graph.Grounds.Contains(netName))
                continue;

            // Count how many times each point appears as a segment endpoint
            var endpointCounts = new Dictionary<GridPoint, int>();
            foreach (var seg in segments)
            {
                endpointCounts[seg.From] = endpointCounts.GetValueOrDefault(seg.From) + 1;
                endpointCounts[seg.To] = endpointCounts.GetValueOrDefault(seg.To) + 1;
            }

            // A point is a stub if:
            // - It appears only once (dead end)
            // - It's not a terminal
            foreach (var (point, count) in endpointCounts)
            {
                if (count == 1 && !terminalPoints.Contains(point))
                {
                    Assert.Fail(
                        $"Net '{netName}' has a useless wire stub at ({point.X}, {point.Y}). "
                            + $"This endpoint connects to only one segment and is not a terminal."
                    );
                }
            }
        }
    }

    [Theory]
    [InlineData("tests/golden/cas/ota/OTA5TFullyDiff.el.cai")]
    public void Route_OccupiedSegmentsMatchRenderedSegments(string cascodePath)
    {
        // This test verifies that the OccupiedSegments map only contains
        // segments that are actually in the final routing result.
        // Ghost segments from pruned paths should NOT be in the occupied map.

        // Arrange
        var fullPath = Path.Combine(GetRepoRoot(), cascodePath);
        using var reader = File.OpenText(fullPath);
        var readResult = CascodeReader.TryRead(reader, fullPath);
        Assert.True(readResult.Success, "Failed to parse Cascode file");

        var doc = readResult.Document!;
        var elCircuit = doc.Circuits.First(c => c.Level == CascodeLevel.EL);

        var graph = CircuitGraph.Build(elCircuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var placement = CoarseGridPlacer.Place(topology, graph);

        // Act - use internal test method to get occupied state
        var (result, occupied) = MazeRouter.RouteWithOccupied(placement, graph);

        // Assert - occupied count should match total segments in result
        var totalSegments = result.SegmentsByNet.Values.Sum(s => s.Count);
        Assert.Equal(totalSegments, occupied.Count);

        // Assert - every segment in final result should be in occupied
        foreach (var (netName, segments) in result.SegmentsByNet)
        {
            foreach (var seg in segments)
            {
                Assert.True(
                    occupied.Contains(seg),
                    $"Segment ({seg.From.X},{seg.From.Y})->({seg.To.X},{seg.To.Y}) for net '{netName}' "
                        + "is in final result but not in occupied map"
                );
            }
        }
    }

    /// <summary>
    /// Tests that parallel horizontal paths with one-sided vertical coverage
    /// get connectors added on BOTH sides when needed, not just one.
    ///
    /// Scenario: Differential RC filter with parallel resistors and a capacitor
    /// connecting only the output side. The input side must still get a vertical
    /// connector to maintain connectivity after parallel path elimination.
    ///
    /// Topology:
    ///   IN_P ─── R_P ───┬─── OUT_P
    ///                   │
    ///                   C
    ///                   │
    ///   IN_N ─── R_N ───┴─── OUT_N
    ///
    /// The capacitor provides vertical coverage on the output side (right),
    /// but the input side (left) needs a connector to be added.
    /// </summary>
    [Theory]
    [InlineData("tests/golden/render/filters/DiffRCFilter.el.cai")]
    public void Route_ParallelPathsWithOneSidedVerticalCoverage_ConnectorAddedToBothSides(
        string cascodePath
    )
    {
        // Arrange
        var fullPath = Path.Combine(GetRepoRoot(), cascodePath);
        using var reader = File.OpenText(fullPath);
        var readResult = CascodeReader.TryRead(reader, fullPath);
        Assert.True(readResult.Success, "Failed to parse Cascode file");

        var doc = readResult.Document!;
        var elCircuit = doc.Circuits.First(c => c.Level == CascodeLevel.EL);

        var graph = CircuitGraph.Build(elCircuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var placement = CoarseGridPlacer.Place(topology, graph);

        // Act
        var result = MazeRouter.Route(placement, graph);

        // Assert - all nets must be fully connected
        foreach (var (netName, segments) in result.SegmentsByNet)
        {
            var terminalPoints = GetTerminalPointsForNet(netName, placement, graph, result);
            if (terminalPoints.Count < 2)
            {
                continue;
            }

            // For power rails, add virtual rail segment
            var segmentsWithRail = segments.ToList();
            if (graph.Supplies.Contains(netName))
            {
                var railY = Layout.DeviceGeometry.RailMargin / 2;
                segmentsWithRail.Add(
                    new WireSegment(
                        new GridPoint(0, railY),
                        new GridPoint(result.CanvasWidth, railY),
                        netName
                    )
                );
            }
            else if (graph.Grounds.Contains(netName))
            {
                var railY = result.CanvasHeight - Layout.DeviceGeometry.RailMargin / 2;
                segmentsWithRail.Add(
                    new WireSegment(
                        new GridPoint(0, railY),
                        new GridPoint(result.CanvasWidth, railY),
                        netName
                    )
                );
            }

            var connected = AreAllPointsConnected(terminalPoints, segmentsWithRail);
            Assert.True(
                connected,
                $"Net '{netName}' is not fully connected. "
                    + $"Terminals: [{string.Join(", ", terminalPoints.Select(p => $"({p.X},{p.Y})"))}], "
                    + $"Segments: [{string.Join(", ", segments.Select(s => $"({s.From.X},{s.From.Y})->({s.To.X},{s.To.Y})"))}]"
            );
        }

        // Also verify no useless stubs were left behind
        var allTerminalPoints = result
            .TerminalPositions.Select(t => new GridPoint(t.X, t.Y))
            .ToHashSet();

        foreach (var (netName, segments) in result.SegmentsByNet)
        {
            if (graph.Supplies.Contains(netName) || graph.Grounds.Contains(netName))
                continue;

            var endpointCounts = new Dictionary<GridPoint, int>();
            foreach (var seg in segments)
            {
                endpointCounts[seg.From] = endpointCounts.GetValueOrDefault(seg.From) + 1;
                endpointCounts[seg.To] = endpointCounts.GetValueOrDefault(seg.To) + 1;
            }

            foreach (var (point, count) in endpointCounts)
            {
                if (count == 1 && !allTerminalPoints.Contains(point))
                {
                    Assert.Fail(
                        $"Net '{netName}' has a useless wire stub at ({point.X}, {point.Y})"
                    );
                }
            }
        }
    }

    [Fact]
    public void RemoveOrphanedStubs_RemovesIsolatedSegments()
    {
        // Arrange - create a network with:
        // - Connected chain: terminal A (0,0) → junction B (10,0) → terminal C (10,20)
        // - Fully isolated segment: D (100,100) → E (100,150) - neither is a terminal
        const string netName = "test_net";

        var terminalA = new GridPoint(0, 0);
        var junctionB = new GridPoint(10, 0);
        var terminalC = new GridPoint(10, 20);
        var isolatedD = new GridPoint(100, 100);
        var isolatedE = new GridPoint(100, 150);

        var segments = new List<WireSegment>
        {
            new(terminalA, junctionB, netName), // A → B (horizontal)
            new(junctionB, terminalC, netName), // B → C (vertical)
            new(isolatedD, isolatedE, netName), // D → E (isolated, should be removed)
        };

        var terminalPoints = new HashSet<GridPoint> { terminalA, terminalC };

        // Act
        var result = MazeRouter.RemoveOrphanedStubs(segments, terminalPoints);

        // Assert - isolated segment should be removed, connected segments should remain
        Assert.Equal(2, result.Count);
        Assert.Contains(result, s => s.From.Equals(terminalA) && s.To.Equals(junctionB));
        Assert.Contains(result, s => s.From.Equals(junctionB) && s.To.Equals(terminalC));
        Assert.DoesNotContain(result, s => s.From.Equals(isolatedD) || s.To.Equals(isolatedE));
    }

    [Theory]
    [InlineData("tests/golden/cas/stress/RcLowpass.cas", "IN", "OUT")]
    [InlineData("tests/golden/render/filters/DiffRCFilter.el.cai", "IN.P", "OUT.P")]
    [InlineData("tests/golden/render/filters/DiffRCFilter.el.cai", "IN.N", "OUT.N")]
    public void Route_FeedthroughPortsRemainAligned(
        string cascodePath,
        string leftPort,
        string rightPort
    )
    {
        var fullPath = Path.Combine(GetRepoRoot(), cascodePath);
        using var reader = File.OpenText(fullPath);
        var readResult = CascodeReader.TryRead(reader, fullPath);
        Assert.True(readResult.Success, "Failed to parse Cascode file");

        var doc = readResult.Document!;
        var elCircuit = doc.Circuits.First(c => c.Level == CascodeLevel.EL);

        var graph = CircuitGraph.Build(elCircuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var placement = CoarseGridPlacer.Place(topology, graph);
        var result = MazeRouter.Route(placement, graph);

        var portTerminals = result
            .TerminalPositions.Where(t => t.DeviceId.StartsWith("PORT_", StringComparison.Ordinal))
            .ToDictionary(t => t.DeviceId.Substring(5), t => t, StringComparer.Ordinal);

        Assert.True(portTerminals.TryGetValue(leftPort, out var leftPos), $"Missing {leftPort}");
        Assert.True(portTerminals.TryGetValue(rightPort, out var rightPos), $"Missing {rightPort}");
        Assert.Equal(leftPos.Y, rightPos.Y);
    }

    [Fact]
    public void Route_RcLowpass_PortsStayAligned_WithBoundaryConnections()
    {
        var fullPath = Path.Combine(GetRepoRoot(), "tests/golden/cas/stress/RcLowpass.cas");
        using var reader = File.OpenText(fullPath);
        var readResult = CascodeReader.TryRead(reader, fullPath);
        Assert.True(readResult.Success, "Failed to parse Cascode file");

        var doc = readResult.Document!;
        var elCircuit = doc.Circuits.First(c => c.Level == CascodeLevel.EL);

        var graph = CircuitGraph.Build(elCircuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var placement = CoarseGridPlacer.Place(topology, graph);
        var result = MazeRouter.Route(placement, graph);

        var inPort = result.TerminalPositions.Single(t => t.DeviceId == "PORT_IN");
        var outPort = result.TerminalPositions.Single(t => t.DeviceId == "PORT_OUT");
        Assert.Equal(inPort.Y, outPort.Y);

        var inPoint = new GridPoint(inPort.X, inPort.Y);
        var outPoint = new GridPoint(outPort.X, outPort.Y);
        var inSegments = result.SegmentsByNet["IN"];
        var outSegments = result.SegmentsByNet["OUT"];

        var hasHorizontalAtIn = inSegments.Any(s =>
            s.From.Y == s.To.Y && (s.From.Equals(inPoint) || s.To.Equals(inPoint))
        );
        var hasVerticalAtIn = inSegments.Any(s =>
            s.From.X == s.To.X && (s.From.Equals(inPoint) || s.To.Equals(inPoint))
        );
        var hasHorizontalAtPort = outSegments.Any(s =>
            s.From.Y == s.To.Y && (s.From.Equals(outPoint) || s.To.Equals(outPoint))
        );
        var hasVerticalAtPort = outSegments.Any(s =>
            s.From.X == s.To.X && (s.From.Equals(outPoint) || s.To.Equals(outPoint))
        );

        Assert.True(
            hasHorizontalAtIn || hasVerticalAtIn,
            "IN should connect to at least one routed segment at the input port"
        );
        Assert.True(
            hasHorizontalAtPort || hasVerticalAtPort,
            "OUT should connect to at least one routed segment at the output port"
        );
    }

    private static string GetRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir != null && !File.Exists(Path.Combine(dir, "Cascode.sln")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }
        return dir ?? throw new InvalidOperationException("Could not find repo root");
    }
}
