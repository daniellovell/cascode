using System.IO;
using Cascode.ACIR;
using Cascode.Render.Analysis;
using Cascode.Render.Placement;
using Cascode.Render.Routing;
using Cascode.TestSupport;

namespace Cascode.Render.Tests.Routing;

public class RoutingSanityTests
{
    [Theory]
    [InlineData("tests/golden/acir/cs/CSAmpResistive.el.cir")]
    [InlineData("tests/golden/acir/ota/OTA5TSingleEnded.el.cir")]
    [InlineData("tests/golden/acir/ota/OTA5TFullyDiff.el.cir")]
    [InlineData("tests/golden/acir/filters/DiffRCFilter.el.cir")]
    public void RoutedWires_ConnectAllTerminals_AndAvoidForeignTerminals(string relativeAcirPath)
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var inputPath = Path.Combine(repoRoot, relativeAcirPath);

        ACIRReadResult readResult;
        using (var reader = File.OpenText(inputPath))
        {
            readResult = ACIRReader.TryRead(reader, inputPath);
        }

        Assert.True(readResult.Success);
        var doc = readResult.Document!;
        var elCircuit = doc.Circuits.Single(c => c.Level == ACIRLevel.EL);

        var graph = CircuitGraph.Build(elCircuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var placement = CoarseGridPlacer.Place(topology, graph);
        var routing = MazeRouter.Route(placement, graph);
        var terminalsByNet = MazeRouter.GetTerminalsByNet(placement, graph);

        AssertAllTerminalsConnected(routing, terminalsByNet);
        AssertNoForeignTerminalIntersections(routing, terminalsByNet);
        AssertNoColinearOverlapsBetweenNets(routing);
    }

    private static void AssertAllTerminalsConnected(
        RoutingResult routing,
        IReadOnlyDictionary<string, IReadOnlyList<TerminalPosition>> terminalsByNet
    )
    {
        foreach (var (netName, terminals) in terminalsByNet)
        {
            if (terminals.Count == 0)
            {
                continue;
            }

            if (!routing.SegmentsByNet.TryGetValue(netName, out var segments))
            {
                Assert.Fail($"Missing routed segments for net '{netName}'.");
                continue;
            }

            AssertNetIsConnected(netName, segments, terminals);

            foreach (var terminal in terminals)
            {
                var p = new GridPoint(terminal.X, terminal.Y);
                if (!IsPointOnAnySegment(p, segments))
                {
                    Assert.Fail(
                        $"Net '{netName}' does not connect terminal {terminal.DeviceId}.{terminal.Terminal} at ({terminal.X}, {terminal.Y})."
                    );
                }
            }
        }
    }

    private static void AssertNetIsConnected(
        string netName,
        IReadOnlyList<WireSegment> segments,
        IReadOnlyList<TerminalPosition> terminals
    )
    {
        var terminalPoints = terminals.Select(t => new GridPoint(t.X, t.Y)).ToList();
        var terminalSegments = new List<int>();

        for (var i = 0; i < terminalPoints.Count; i++)
        {
            var point = terminalPoints[i];
            var segIndex = FindAnySegmentIndexContaining(point, segments);
            if (segIndex < 0)
            {
                Assert.Fail(
                    $"Net '{netName}' does not contain a segment for terminal at ({point.X}, {point.Y})."
                );
                return;
            }
            terminalSegments.Add(segIndex);
        }

        var adjacency = BuildSegmentAdjacency(segments);
        var seen = new HashSet<int>();
        var queue = new Queue<int>();
        queue.Enqueue(terminalSegments[0]);
        seen.Add(terminalSegments[0]);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var next in adjacency[current])
            {
                if (seen.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        for (var i = 0; i < terminalSegments.Count; i++)
        {
            if (!seen.Contains(terminalSegments[i]))
            {
                var p = terminalPoints[i];
                Assert.Fail(
                    $"Net '{netName}' is disconnected: terminal at ({p.X}, {p.Y}) is not connected to the net."
                );
            }
        }
    }

    private static int FindAnySegmentIndexContaining(
        GridPoint point,
        IReadOnlyList<WireSegment> segments
    )
    {
        for (var i = 0; i < segments.Count; i++)
        {
            if (IsPointOnSegment(point, segments[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static IReadOnlyList<int>[] BuildSegmentAdjacency(IReadOnlyList<WireSegment> segments)
    {
        var adjacency = new List<int>[segments.Count];
        for (var i = 0; i < adjacency.Length; i++)
        {
            adjacency[i] = new List<int>();
        }

        for (var i = 0; i < segments.Count; i++)
        {
            for (var j = i + 1; j < segments.Count; j++)
            {
                if (!SegmentsTouchOrCross(segments[i], segments[j]))
                {
                    continue;
                }

                adjacency[i].Add(j);
                adjacency[j].Add(i);
            }
        }

        return adjacency.Select(a => (IReadOnlyList<int>)a).ToArray();
    }

    private static bool SegmentsTouchOrCross(WireSegment a, WireSegment b)
    {
        var aHorizontal = a.From.Y == a.To.Y;
        var bHorizontal = b.From.Y == b.To.Y;

        if (aHorizontal && bHorizontal)
        {
            if (a.From.Y != b.From.Y)
            {
                return false;
            }

            var aMinX = Math.Min(a.From.X, a.To.X);
            var aMaxX = Math.Max(a.From.X, a.To.X);
            var bMinX = Math.Min(b.From.X, b.To.X);
            var bMaxX = Math.Max(b.From.X, b.To.X);

            return Math.Min(aMaxX, bMaxX) >= Math.Max(aMinX, bMinX);
        }

        if (!aHorizontal && !bHorizontal)
        {
            if (a.From.X != b.From.X)
            {
                return false;
            }

            var aMinY = Math.Min(a.From.Y, a.To.Y);
            var aMaxY = Math.Max(a.From.Y, a.To.Y);
            var bMinY = Math.Min(b.From.Y, b.To.Y);
            var bMaxY = Math.Max(b.From.Y, b.To.Y);

            return Math.Min(aMaxY, bMaxY) >= Math.Max(aMinY, bMinY);
        }

        var h = aHorizontal ? a : b;
        var v = aHorizontal ? b : a;

        var x = v.From.X;
        var y = h.From.Y;

        var hMinX = Math.Min(h.From.X, h.To.X);
        var hMaxX = Math.Max(h.From.X, h.To.X);
        var vMinY = Math.Min(v.From.Y, v.To.Y);
        var vMaxY = Math.Max(v.From.Y, v.To.Y);

        return x >= hMinX && x <= hMaxX && y >= vMinY && y <= vMaxY;
    }

    private static void AssertNoForeignTerminalIntersections(
        RoutingResult routing,
        IReadOnlyDictionary<string, IReadOnlyList<TerminalPosition>> terminalsByNet
    )
    {
        foreach (var (netName, segments) in routing.SegmentsByNet)
        {
            foreach (var (otherNetName, otherNetTerminals) in terminalsByNet)
            {
                if (otherNetName == netName)
                {
                    continue;
                }

                foreach (var terminal in otherNetTerminals)
                {
                    var p = new GridPoint(terminal.X, terminal.Y);
                    if (IsPointOnAnySegment(p, segments))
                    {
                        Assert.Fail(
                            $"Net '{netName}' passes through foreign terminal {terminal.DeviceId}.{terminal.Terminal} on net '{otherNetName}' at ({terminal.X}, {terminal.Y})."
                        );
                    }
                }
            }
        }
    }

    private static void AssertNoColinearOverlapsBetweenNets(RoutingResult routing)
    {
        var nets = routing.SegmentsByNet.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

        for (var i = 0; i < nets.Count; i++)
        {
            var netA = nets[i];
            var segsA = routing.SegmentsByNet[netA];

            for (var j = i + 1; j < nets.Count; j++)
            {
                var netB = nets[j];
                var segsB = routing.SegmentsByNet[netB];

                foreach (var a in segsA)
                {
                    foreach (var b in segsB)
                    {
                        if (SegmentsColinearlyOverlap(a, b))
                        {
                            Assert.Fail(
                                $"Nets '{netA}' and '{netB}' have a colinear overlap: ({a.From.X},{a.From.Y})->({a.To.X},{a.To.Y}) vs ({b.From.X},{b.From.Y})->({b.To.X},{b.To.Y})."
                            );
                        }
                    }
                }
            }
        }
    }

    private static bool SegmentsColinearlyOverlap(WireSegment a, WireSegment b)
    {
        var aHorizontal = a.From.Y == a.To.Y;
        var bHorizontal = b.From.Y == b.To.Y;

        if (aHorizontal && bHorizontal)
        {
            if (a.From.Y != b.From.Y)
            {
                return false;
            }

            var aMinX = Math.Min(a.From.X, a.To.X);
            var aMaxX = Math.Max(a.From.X, a.To.X);
            var bMinX = Math.Min(b.From.X, b.To.X);
            var bMaxX = Math.Max(b.From.X, b.To.X);

            return Math.Min(aMaxX, bMaxX) > Math.Max(aMinX, bMinX);
        }

        var aVertical = a.From.X == a.To.X;
        var bVertical = b.From.X == b.To.X;

        if (aVertical && bVertical)
        {
            if (a.From.X != b.From.X)
            {
                return false;
            }

            var aMinY = Math.Min(a.From.Y, a.To.Y);
            var aMaxY = Math.Max(a.From.Y, a.To.Y);
            var bMinY = Math.Min(b.From.Y, b.To.Y);
            var bMaxY = Math.Max(b.From.Y, b.To.Y);

            return Math.Min(aMaxY, bMaxY) > Math.Max(aMinY, bMinY);
        }

        return false;
    }

    private static bool IsPointOnAnySegment(GridPoint point, IReadOnlyList<WireSegment> segments)
    {
        foreach (var segment in segments)
        {
            if (IsPointOnSegment(point, segment))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPointOnSegment(GridPoint point, WireSegment segment)
    {
        if (point.Equals(segment.From) || point.Equals(segment.To))
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
}
