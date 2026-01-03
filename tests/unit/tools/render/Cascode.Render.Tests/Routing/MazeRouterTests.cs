namespace Cascode.Render.Tests.Routing;

using Cascode.ACIR;
using Cascode.Render.Analysis;
using Cascode.Render.Placement;
using Cascode.Render.Routing;

public class MazeRouterTests
{
    [Theory]
    [InlineData("tests/golden/acir/cs/CSAmpResistive.el.cir")]
    [InlineData("tests/golden/acir/ota/OTA5TSingleEnded.el.cir")]
    [InlineData("tests/golden/acir/ota/OTA5TFullyDiff.el.cir")]
    public void Route_AllNetsFullyConnected(string acirPath)
    {
        // Arrange
        var fullPath = Path.Combine(GetRepoRoot(), acirPath);
        using var reader = File.OpenText(fullPath);
        var readResult = ACIRReader.TryRead(reader, fullPath);
        Assert.True(readResult.Success, "Failed to parse ACIR file");

        var doc = readResult.Document!;
        var elCircuit = doc.Circuits.First(c => c.Level == ACIRLevel.EL);

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

                var pos = GetTerminalPosition(deviceId, terminal, deviceType, cell);
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
        GridCell cell
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
            var p = Layout.DeviceGeometry.GetPassivePlacement(cell.Row, cell.Column);
            return terminal switch
            {
                "P" => new GridPoint(p.PX, p.PY),
                "N" => new GridPoint(p.NX, p.NY),
                _ => null,
            };
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
