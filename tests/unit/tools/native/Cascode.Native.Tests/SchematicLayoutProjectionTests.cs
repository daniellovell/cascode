using Cascode.Language;
using Cascode.Native;
using Cascode.Render.Layout;
using Cascode.Render.Placement;
using Cascode.Render.Routing;
using Cascode.Render.Svg;
using Cascode.TestSupport;

namespace Cascode.Native.Tests;

public sealed class SchematicLayoutProjectionTests
{
    [Fact]
    public void ScalePathD_TransformsAbsoluteMoveTo()
    {
        // M 2.6 4.5 L 2.6 21.5 with cx=8.55, cy=13, sx=sy=0.1
        var result = SchematicLayoutProjection.ScalePathD(
            "M 2.6 4.5 L 2.6 21.5",
            0.1, 0.1, 8.55, 13.0
        );
        Assert.Equal("M -0.595 -0.85 L -0.595 0.85", result);
    }

    [Fact]
    public void ScalePathD_TransformsRelativeCubicBezier()
    {
        // Relative c command — deltas scaled, no center offset
        var result = SchematicLayoutProjection.ScalePathD(
            "M 5 5 c 10 0 10 20 0 20",
            0.1, 0.1, 5.0, 5.0
        );
        // M: (5-5)*0.1=0, (5-5)*0.1=0 → "M 0 0"
        // c: 10*0.1=1, 0*0.1=0, 10*0.1=1, 20*0.1=2, 0*0.1=0, 20*0.1=2
        Assert.Equal("M 0 0 c 1 0 1 2 0 2", result);
    }

    [Fact]
    public void ScalePathD_PreservesZCommand()
    {
        var result = SchematicLayoutProjection.ScalePathD(
            "M 5.1 4.5 L 5.1 21.5 L 15.1 13 Z",
            0.1, 0.1, 8.55, 13.0
        );
        Assert.Contains("Z", result);
        Assert.StartsWith("M", result);
    }

    [Fact]
    public void ScalePathD_HandlesCompactPathWithCommasAndNegatives()
    {
        // Inductor-style compact path: M12.9881,8.5c-.9491,-.7692,-1.5262,-2.0907
        // Just verify it doesn't throw and produces output
        var result = SchematicLayoutProjection.ScalePathD(
            "M12.9881,8.5c-.9491,-.7692,-1.5262,-2.0907,-1.5262,-3.6461",
            0.1, 0.1, 13.0, 4.515
        );
        Assert.False(string.IsNullOrEmpty(result));
        Assert.StartsWith("M", result);
    }

    [Fact]
    public void BuildSymbolCatalog_ReturnsPreScaledCoordinates()
    {
        var structural = new StructuralInfo
        {
            Devices = [new StructuralDevice { Id = "M1", Type = "nmos", Terminals = ["G", "D", "S"] }],
            Ports = [],
            Nets = [],
            Supplies = [],
            Grounds = [],
        };

        var catalog = SchematicLayoutProjection.BuildSymbolCatalog(structural);
        Assert.True(catalog.ContainsKey("nmos"));

        var nmos = catalog["nmos"];

        // ViewBox should be in render units (17.1/10 ≈ 1.71, 26/10 = 2.6)
        Assert.Equal(0, nmos.ViewBox[0]);
        Assert.Equal(0, nmos.ViewBox[1]);
        Assert.InRange(nmos.ViewBox[2], 1.5, 2.0); // ~1.71
        Assert.InRange(nmos.ViewBox[3], 2.4, 2.8); // ~2.6

        // Terminals should be centered at origin and in render units
        var termG = nmos.Terminals["G"];
        var termD = nmos.Terminals["D"];
        var termS = nmos.Terminals["S"];

        // G should be on the left (negative x)
        Assert.True(termG.X < 0, $"G.X should be negative (centered), got {termG.X}");
        // D and S should be on the right (positive x)
        Assert.True(termD.X > 0, $"D.X should be positive, got {termD.X}");
        Assert.True(termS.X > 0, $"S.X should be positive, got {termS.X}");
        // D should be above center (negative y), S below (positive y)
        Assert.True(termD.Y < 0, $"D.Y should be negative (above center), got {termD.Y}");
        Assert.True(termS.Y > 0, $"S.Y should be positive (below center), got {termS.Y}");

        // All coordinates should be small (render units, not SVG units)
        Assert.InRange(Math.Abs(termG.X), 0.5, 1.5);
        Assert.InRange(Math.Abs(termD.Y), 0.5, 2.0);
    }


    [Fact]
    public void TerminalPositions_AlignBetweenCatalogAndRenderCache()
    {
        // Set up a single NMOS at cell (0,0), no mirror.
        // Terminal pixel positions from DeviceGeometry.GetMosfetPlacement(0,0,false):
        // G=(14,40), D=(30,28), S=(30,53)
        var mosfet = Cascode.Render.Layout.DeviceGeometry.GetMosfetPlacement(0, 0, false);
        int gx = mosfet.GateX, gy = mosfet.GateY;
        int dx = mosfet.DrainX, dy = mosfet.DrainY;
        int sx = mosfet.SourceX, sy = mosfet.SourceY;

        var circuit = new Circuit
        {
            Name = "Align",
            Level = CascodeLevel.EL,
            Ports = [],
        };

        var placement = new CoarseGridResult
        {
            RowCount = 1,
            ColumnCount = 1,
            DevicePlacements = new Dictionary<string, GridCell>
            {
                ["M1"] = new GridCell(0, 0, false),
            },
            SymmetryAxis = 0,
            HorizontalPassiveIds = new HashSet<string>(),
        };

        var routing = new RoutingResult
        {
            Segments = [],
            Junctions = [],
            SegmentsByNet = new Dictionary<string, IReadOnlyList<WireSegment>>(StringComparer.Ordinal),
            CanvasWidth = 100,
            CanvasHeight = 100,
            TerminalPositions =
            [
                new TerminalPosition("M1", "G", gx, gy),
                new TerminalPosition("M1", "D", dx, dy),
                new TerminalPosition("M1", "S", sx, sy),
            ],
        };

        // Build all three outputs
        var structural = new StructuralInfo
        {
            Devices = [new StructuralDevice { Id = "M1", Type = "nmos", Terminals = ["G", "D", "S"] }],
            Ports = [],
            Nets = [],
            Supplies = [],
            Grounds = [],
        };
        var catalog = SchematicLayoutProjection.BuildSymbolCatalog(structural);
        var layout = SchematicLayoutProjection.BuildLayout(circuit, null, placement, routing);
        var cache = SchematicLayoutProjection.BuildRenderCache(circuit, placement, routing);

        // Verify alignment: catalog_terminal + device.position ≈ renderCache terminal
        var device = Assert.Single(layout.Devices);
        var nmosCatalog = catalog["nmos"];
        var cacheTerminals = cache.TerminalPoints["M1"];

        const double tolerance = 0.1; // 0.1 render units = 1 pixel

        foreach (var (termName, catalogTerm) in nmosCatalog.Terminals)
        {
            var cacheTerm = cacheTerminals[termName];

            // No rotation/mirror: world = catalog_offset + device.position
            var worldX = catalogTerm.X + device.Position.X;
            var worldY = catalogTerm.Y + device.Position.Y;

            Assert.True(
                Math.Abs(worldX - cacheTerm.X) < tolerance,
                $"{termName}.X: catalog({catalogTerm.X:F4}) + pos({device.Position.X:F4}) = {worldX:F4}, " +
                $"cache = {cacheTerm.X:F4}, delta = {Math.Abs(worldX - cacheTerm.X):F4}"
            );
            Assert.True(
                Math.Abs(worldY - cacheTerm.Y) < tolerance,
                $"{termName}.Y: catalog({catalogTerm.Y:F4}) + pos({device.Position.Y:F4}) = {worldY:F4}, " +
                $"cache = {cacheTerm.Y:F4}, delta = {Math.Abs(worldY - cacheTerm.Y):F4}"
            );
        }
    }

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
