using Cascode.Language;
using Cascode.Render.Analysis;
using Cascode.Render.Layout;
using Cascode.Render.Placement;
using Cascode.Render.Routing;
using Cascode.Render.Svg;

namespace Cascode.Render.Tests;

public class InstanceBlockTests
{
    private static Circuit MakeSubCircuit() =>
        new()
        {
            Name = "Mirror",
            Level = CascodeLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "SENSE",
                    Type = "analog",
                },
                new()
                {
                    Direction = PortDirection.Output,
                    Name = "TAP",
                    Type = "analog",
                },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        Id = "Ms",
                        DeviceType = "pmos",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "SENSE",
                            ["G"] = "SENSE",
                            ["S"] = "VDD",
                        },
                    },
                    new()
                    {
                        Id = "Mt",
                        DeviceType = "pmos",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "TAP",
                            ["G"] = "SENSE",
                            ["S"] = "VDD",
                        },
                    },
                },
            },
        };

    private static (Circuit Parent, CascodeDocument Doc) MakeParentWithNonInlineInstance()
    {
        var sub = MakeSubCircuit();
        var parent = new Circuit
        {
            Name = "Top",
            Level = CascodeLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "IN",
                    Type = "signal",
                },
                new()
                {
                    Direction = PortDirection.Output,
                    Name = "OUT",
                    Type = "signal",
                },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        Id = "Mn",
                        DeviceType = "nmos",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "OUT",
                            ["G"] = "IN",
                            ["S"] = "GND",
                        },
                    },
                },
                Instances = new List<InstanceDeclaration>
                {
                    new()
                    {
                        Id = "cm",
                        Type = "Mirror",
                        Bindings = new Dictionary<string, string>
                        {
                            ["VDD"] = "VDD",
                            ["GND"] = "GND",
                            ["SENSE"] = "OUT",
                            ["TAP"] = "OUT",
                        },
                    },
                },
            },
        };

        var doc = new CascodeDocument
        {
            Circuits = new List<Circuit> { parent, sub },
        };
        return (parent, doc);
    }

    [Fact]
    public void Flatten_NonInlineInstance_CreatesSyntheticDevice()
    {
        var (parent, doc) = MakeParentWithNonInlineInstance();

        var flattened = CircuitFlattener.Flatten(parent, doc);

        Assert.True(flattened.Devices.ContainsKey("cm"));
        Assert.Equal("instance", flattened.Devices["cm"].DeviceType);
    }

    [Fact]
    public void Flatten_NonInlineInstance_ResolvesBindings()
    {
        var (parent, doc) = MakeParentWithNonInlineInstance();

        var flattened = CircuitFlattener.Flatten(parent, doc);

        var bindings = flattened.Devices["cm"].Bindings;
        Assert.Equal("VDD", bindings["VDD"]);
        Assert.Equal("GND", bindings["GND"]);
        Assert.Equal("OUT", bindings["SENSE"]);
        Assert.Equal("OUT", bindings["TAP"]);
    }

    [Fact]
    public void Flatten_NonInlineInstance_CreatesInstanceBlockInfo()
    {
        var (parent, doc) = MakeParentWithNonInlineInstance();

        var flattened = CircuitFlattener.Flatten(parent, doc);

        Assert.Single(flattened.InstanceBlocks);
        var block = flattened.InstanceBlocks[0];
        Assert.Equal("cm", block.InstanceId);
        Assert.Equal("Mirror", block.CircuitType);
        Assert.Contains("SENSE", block.SignalPortNames);
        Assert.Contains("TAP", block.SignalPortNames);
    }

    [Fact]
    public void Flatten_NonInlineInstance_SignalPortsExcludeSupplyGround()
    {
        var (parent, doc) = MakeParentWithNonInlineInstance();

        var flattened = CircuitFlattener.Flatten(parent, doc);

        var block = flattened.InstanceBlocks[0];
        Assert.DoesNotContain("VDD", block.SignalPortNames);
        Assert.DoesNotContain("GND", block.SignalPortNames);
    }

    [Fact]
    public void CircuitGraph_InstanceBlock_AppearsInDevices()
    {
        var (parent, doc) = MakeParentWithNonInlineInstance();
        var flattened = CircuitFlattener.Flatten(parent, doc);

        var graph = CircuitGraph.Build(flattened);

        Assert.True(graph.Devices.ContainsKey("cm"));
        Assert.Equal("instance", graph.Devices["cm"].DeviceType);
    }

    [Fact]
    public void CircuitGraph_InstanceBlock_RegistersNetConnections()
    {
        var (parent, doc) = MakeParentWithNonInlineInstance();
        var flattened = CircuitFlattener.Flatten(parent, doc);

        var graph = CircuitGraph.Build(flattened);

        Assert.Equal("OUT", graph.GetNetForTerminal("cm", "SENSE"));
        Assert.Equal("VDD", graph.GetNetForTerminal("cm", "VDD"));
    }

    [Fact]
    public void CircuitGraph_InstanceBlocks_Propagated()
    {
        var (parent, doc) = MakeParentWithNonInlineInstance();
        var flattened = CircuitFlattener.Flatten(parent, doc);

        var graph = CircuitGraph.Build(flattened);

        Assert.Single(graph.InstanceBlocks);
        Assert.Equal("cm", graph.InstanceBlocks[0].InstanceId);
    }

    [Fact]
    public void Topology_InstanceBlock_AssignedDifferentRowThanConnectedDevices()
    {
        var (parent, doc) = MakeParentWithNonInlineInstance();
        var flattened = CircuitFlattener.Flatten(parent, doc);
        var graph = CircuitGraph.Build(flattened);
        var topology = TopologyAnalyzer.Analyze(graph);
        var placement = CoarseGridPlacer.Place(topology, graph);

        Assert.True(placement.DevicePlacements.ContainsKey("cm"));
        Assert.True(placement.DevicePlacements.ContainsKey("Mn"));

        var cmRow = placement.DevicePlacements["cm"].Row;
        var cmCol = placement.DevicePlacements["cm"].Column;
        var mnRow = placement.DevicePlacements["Mn"].Row;
        var mnCol = placement.DevicePlacements["Mn"].Column;
        Assert.True(
            cmRow != mnRow || cmCol != mnCol,
            "Instance block and connected NMOS must not overlap the same cell."
        );
    }

    [Fact]
    public void SvgRenderer_NonInlineInstanceBlock_RendersAsFiftyByFifty()
    {
        var (parent, doc) = MakeParentWithNonInlineInstance();
        var flattened = CircuitFlattener.Flatten(parent, doc);
        var graph = CircuitGraph.Build(flattened);
        var topology = TopologyAnalyzer.Analyze(graph);
        var placement = CoarseGridPlacer.Place(topology, graph);
        var routing = MazeRouter.Route(placement, graph);
        var svg = new SvgRenderer().Render(
            placement,
            routing,
            graph,
            StyleSheet.Default,
            new RenderOptions()
        );

        Assert.Contains(
            @"<rect class=""block"" width=""50"" height=""50"" />",
            svg,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Place_NoInterveningTreatsInstanceAsThreeByThreeFootprint()
    {
        var sub = MakeSubCircuit();
        var parent = new Circuit
        {
            Name = "instance_no_intervening",
            Level = CascodeLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>(),
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        Id = "RCAS_TOP",
                        DeviceType = "resistor",
                        Primitive = "ResistorIdeal",
                        Bindings = new Dictionary<string, string> { ["P"] = "VDD", ["N"] = "vcas" },
                    },
                    new()
                    {
                        Id = "RCAS_BOT",
                        DeviceType = "resistor",
                        Primitive = "ResistorIdeal",
                        Bindings = new Dictionary<string, string> { ["P"] = "vcas", ["N"] = "GND" },
                    },
                },
                Instances = new List<InstanceDeclaration>
                {
                    new()
                    {
                        Id = "X1",
                        Type = "Mirror",
                        Bindings = new Dictionary<string, string>
                        {
                            ["VDD"] = "VDD",
                            ["GND"] = "GND",
                            ["SENSE"] = "sense",
                            ["TAP"] = "tap",
                        },
                    },
                },
            },
        };

        var flattened = CircuitFlattener.Flatten(
            parent,
            new CascodeDocument
            {
                Circuits = new List<Circuit> { parent, sub },
            }
        );
        var graph = CircuitGraph.Build(flattened);
        var topology = TopologyAnalyzer.Analyze(graph);
        var constraints = new PlacementConstraintSet
        {
            DevicePlacements =
            [
                new DevicePlacementConstraint("RCAS_TOP", 7, 4, RenderConstraintStrength.Hard),
                new DevicePlacementConstraint("RCAS_BOT", 7, 24, RenderConstraintStrength.Hard),
                new DevicePlacementConstraint("X1", 12, 14, RenderConstraintStrength.Hard),
            ],
            AllowConstraintRelaxation = false,
        };

        Assert.Throws<RenderConstraintUnsatException>(() =>
            CoarseGridPlacer.Place(topology, graph, constraints)
        );
    }

    [Fact]
    public void Route_Tlc2272TopLevel_NoSegmentRunsThroughInstanceBlockArea()
    {
        var (placement, graph, routing) = LoadTlc2272TopLevelRenderState();

        foreach (var (deviceId, cell) in placement.DevicePlacements)
        {
            if (
                !graph.Devices.TryGetValue(deviceId, out var device)
                || device.DeviceType != "instance"
            )
            {
                continue;
            }

            var centerX = DeviceGeometry.GetCellCenterX(cell.Column);
            var centerY = DeviceGeometry.GetCellCenterY(cell.Row);
            var minX = centerX - DeviceGeometry.InstanceBlockWidth / 2.0;
            var maxX = minX + DeviceGeometry.InstanceBlockWidth;
            var minY = centerY - DeviceGeometry.InstanceBlockHeight / 2.0;
            var maxY = minY + DeviceGeometry.InstanceBlockHeight;

            foreach (var segment in routing.Segments)
            {
                if (segment.NetName is "VDD" or "GND")
                {
                    continue;
                }

                Assert.False(
                    SegmentOverlapsRectangleBeyondPoint(segment, minX, minY, maxX, maxY),
                    $"Segment ({segment.From.X},{segment.From.Y})->({segment.To.X},{segment.To.Y}) on net '{segment.NetName}' overlaps instance block '{deviceId}' area."
                );
            }
        }
    }

    [Fact]
    public void Render_Tlc2272TopLevel_InlineBoundaryKeepsClearanceFromStageBlocks()
    {
        var (_, _, _, svg) = LoadTlc2272TopLevelSvg();
        var inlineRectMatch = System.Text.RegularExpressions.Regex.Match(
            svg,
            @"<rect class=""inline-boundary"" x=""(?<x>-?\d+(?:\.\d+)?)"" y=""(?<y>-?\d+(?:\.\d+)?)"" width=""(?<w>\d+(?:\.\d+)?)"" height=""(?<h>\d+(?:\.\d+)?)""\s*/>"
        );
        Assert.True(inlineRectMatch.Success, "Expected one inline boundary rectangle in SVG.");

        var inlineMinX = double.Parse(inlineRectMatch.Groups["x"].Value);
        var inlineMinY = double.Parse(inlineRectMatch.Groups["y"].Value);
        var inlineMaxX = inlineMinX + double.Parse(inlineRectMatch.Groups["w"].Value);
        var inlineMaxY = inlineMinY + double.Parse(inlineRectMatch.Groups["h"].Value);

        var stageMatches = System.Text.RegularExpressions.Regex.Matches(
            svg,
            @"<g id=""(?<id>stage1|stage2)""[^>]*transform=""translate\((?<x>-?\d+(?:\.\d+)?),\s*(?<y>-?\d+(?:\.\d+)?)\)"""
        );
        Assert.Equal(2, stageMatches.Count);

        foreach (System.Text.RegularExpressions.Match stageMatch in stageMatches)
        {
            var stageId = stageMatch.Groups["id"].Value;
            var stageMinX = double.Parse(stageMatch.Groups["x"].Value);
            var stageMinY = double.Parse(stageMatch.Groups["y"].Value);
            var stageMaxX = stageMinX + DeviceGeometry.InstanceBlockWidth;
            var stageMaxY = stageMinY + DeviceGeometry.InstanceBlockHeight;

            var overlapsX = inlineMinX < stageMaxX && inlineMaxX > stageMinX;
            var overlapsY = inlineMinY < stageMaxY && inlineMaxY > stageMinY;
            Assert.False(
                overlapsX && overlapsY,
                $"Inline boundary overlaps {stageId}; expected positive clearance."
            );

            var horizontalGap =
                inlineMaxX <= stageMinX ? stageMinX - inlineMaxX
                : stageMaxX <= inlineMinX ? inlineMinX - stageMaxX
                : 0;
            Assert.True(
                horizontalGap >= 10,
                $"Inline boundary is too close to {stageId}; expected at least 10px horizontal clearance, got {horizontalGap}px."
            );
        }
    }

    private static bool SegmentOverlapsRectangleBeyondPoint(
        WireSegment segment,
        double minX,
        double minY,
        double maxX,
        double maxY
    )
    {
        var x1 = segment.From.X;
        var y1 = segment.From.Y;
        var x2 = segment.To.X;
        var y2 = segment.To.Y;

        if (y1 == y2)
        {
            if (y1 <= minY || y1 >= maxY)
            {
                return false;
            }

            var segMinX = Math.Min(x1, x2);
            var segMaxX = Math.Max(x1, x2);
            var overlapMin = Math.Max(segMinX, minX);
            var overlapMax = Math.Min(segMaxX, maxX);
            return overlapMax - overlapMin > 0;
        }

        if (x1 == x2)
        {
            if (x1 <= minX || x1 >= maxX)
            {
                return false;
            }

            var segMinY = Math.Min(y1, y2);
            var segMaxY = Math.Max(y1, y2);
            var overlapMin = Math.Max(segMinY, minY);
            var overlapMax = Math.Min(segMaxY, maxY);
            return overlapMax - overlapMin > 0;
        }

        return false;
    }

    private static (
        CoarseGridResult Placement,
        CircuitGraph Graph,
        RoutingResult Routing
    ) LoadTlc2272TopLevelRenderState()
    {
        var fullPath = Path.Combine(GetRepoRoot(), "tests/golden/cas/stress/TLC2272A_Sky130.cas");
        using var reader = File.OpenText(fullPath);
        var readResult = CascodeReader.TryRead(reader, fullPath);
        Assert.True(readResult.Success, "Failed to parse Cascode file");

        var doc = readResult.Document!;
        var top = doc.Circuits.Single(c => c.Name == "TLC2272A_Sky130");
        var flattened = CircuitFlattener.Flatten(top, doc);
        var graph = CircuitGraph.Build(flattened);
        var topology = TopologyAnalyzer.Analyze(graph);
        var placement = CoarseGridPlacer.Place(topology, graph);
        var routing = MazeRouter.Route(placement, graph);
        return (placement, graph, routing);
    }

    private static (
        CoarseGridResult Placement,
        CircuitGraph Graph,
        RoutingResult Routing,
        string Svg
    ) LoadTlc2272TopLevelSvg()
    {
        var (placement, graph, routing) = LoadTlc2272TopLevelRenderState();
        var svg = new SvgRenderer().Render(
            placement,
            routing,
            graph,
            StyleSheet.Default,
            new RenderOptions()
        );
        return (placement, graph, routing, svg);
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
