using Cascode.Language;
using Cascode.Native;
using Cascode.Render.Placement;
using Cascode.Render.Routing;
using Cascode.TestSupport;

namespace Cascode.Native.Tests;

public sealed class SchematicLayoutProjectionTests
{
    [Fact]
    public void BuildLayout_ScopesJunctionsPerNet()
    {
        using var cascodeHome = CascodeHome.CreateInTemp("schematic-layout-projection");

        var circuit = new Circuit
        {
            Name = "Amp",
            Level = CascodeLevel.EL,
            Ports =
            [
                new PortDeclaration
                {
                    Name = "IN",
                    Direction = PortDirection.Input,
                    Type = "analog",
                },
                new PortDeclaration
                {
                    Name = "OUT",
                    Direction = PortDirection.Output,
                    Type = "analog",
                },
            ],
        };

        var placement = new CoarseGridResult
        {
            RowCount = 1,
            ColumnCount = 1,
            DevicePlacements = new Dictionary<string, GridCell>(),
            SymmetryAxis = 0,
            HorizontalPassiveIds = new HashSet<string>(),
        };

        var n1Segments = new[]
        {
            new WireSegment(new GridPoint(0, 40), new GridPoint(80, 40), "N1"),
        };
        var n2Segments = new[]
        {
            new WireSegment(new GridPoint(0, 120), new GridPoint(80, 120), "N2"),
        };
        var routing = new RoutingResult
        {
            Segments = n1Segments.Concat(n2Segments).ToArray(),
            Junctions = [new GridPoint(40, 40), new GridPoint(40, 120)],
            SegmentsByNet = new Dictionary<string, IReadOnlyList<WireSegment>>(
                StringComparer.Ordinal
            )
            {
                ["N1"] = n1Segments,
                ["N2"] = n2Segments,
            },
            CanvasWidth = 200,
            CanvasHeight = 200,
            TerminalPositions =
            [
                new TerminalPosition("PORT_IN", "IN", 0, 40),
                new TerminalPosition("PORT_OUT", "OUT", 80, 120),
            ],
        };

        var layout = SchematicLayoutProjection.BuildLayout(circuit, null, placement, routing);
        var netN1 = Assert.Single(layout.Nets, net => net.Name == "N1");
        var netN2 = Assert.Single(layout.Nets, net => net.Name == "N2");

        Assert.Single(netN1.Junctions);
        Assert.Single(netN2.Junctions);
        Assert.NotEqual(netN1.Junctions[0].Y, netN2.Junctions[0].Y);
    }
}
