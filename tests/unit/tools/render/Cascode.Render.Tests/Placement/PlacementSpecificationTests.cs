namespace Cascode.Render.Tests.Placement;

using Cascode.Language;
using Cascode.Render.Analysis;
using Cascode.Render.Placement;

public sealed class PlacementSpecificationTests
{
    [Fact]
    public void Place_CompactsOccupiedColumnsAfterSolve()
    {
        var circuit = TestCircuits.TwoDevices();
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var constraints = new PlacementConstraintSet
        {
            DevicePlacements =
            [
                new DevicePlacementConstraint("M1", XRu: 2, YRu: 4, RenderConstraintStrength.Hard),
                new DevicePlacementConstraint("M2", XRu: 20, YRu: 4, RenderConstraintStrength.Hard),
            ],
        };

        var placement = CoarseGridPlacer.Place(topology, graph, constraints);
        var occupiedColumns = placement
            .DevicePlacements.Values.Select(cell => cell.Column)
            .Distinct()
            .Order()
            .ToArray();

        Assert.Equal([0, 1], occupiedColumns);
        Assert.Equal(2, placement.ColumnCount);
    }

    [Fact]
    public void Place_PointToPointGateNetFacesDrivingDevice()
    {
        var circuit = GateDrivenFromRightCircuit();
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var constraints = new PlacementConstraintSet
        {
            DevicePlacements =
            [
                new DevicePlacementConstraint("M1", XRu: 2, YRu: 4, RenderConstraintStrength.Hard),
                new DevicePlacementConstraint("R1", XRu: 11, YRu: 4, RenderConstraintStrength.Hard),
            ],
        };

        var placement = CoarseGridPlacer.Place(topology, graph, constraints);

        Assert.True(placement.DevicePlacements.TryGetValue("M1", out var mos));
        Assert.True(mos.MirrorX);
    }

    private static Circuit GateDrivenFromRightCircuit() =>
        new()
        {
            Name = "gate_driven_from_right",
            Level = CascodeLevel.EL,
            Supplies = ["VDD"],
            Grounds = ["GND"],
            Ports =
            [
                new PortDeclaration
                {
                    Direction = PortDirection.Output,
                    Name = "OUT",
                    Type = "signal",
                },
            ],
            Fill = new FillBlock
            {
                Devices =
                [
                    new DeviceDeclaration
                    {
                        Id = "M1",
                        DeviceType = "nmos",
                        Primitive = "NMOS_Level1",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "OUT",
                            ["G"] = "VGATE",
                            ["S"] = "GND",
                        },
                    },
                    new DeviceDeclaration
                    {
                        Id = "R1",
                        DeviceType = "resistor",
                        Primitive = "RES",
                        Bindings = new Dictionary<string, string>
                        {
                            ["P"] = "VGATE",
                            ["N"] = "OUT",
                        },
                    },
                ],
                Nets = [new NetDeclaration { Id = "VGATE", Domain = "signal" }],
            },
        };
}
