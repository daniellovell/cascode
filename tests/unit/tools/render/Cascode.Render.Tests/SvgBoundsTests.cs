using System.Text.RegularExpressions;
using Cascode.Language;
using Cascode.Render.Analysis;
using Cascode.Render.Placement;
using Cascode.Render.Routing;
using Cascode.Render.Svg;

namespace Cascode.Render.Tests;

public class SvgBoundsTests
{
    [Fact]
    public void LeftPortLabels_AreNotClipped()
    {
        // Arrange: circuit with input ports that will appear on the left
        var circuit = new Circuit
        {
            Name = "test",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "IN_SIGNAL",
                    Type = "signal",
                },
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "BIAS_VOLTAGE",
                    Type = "bias",
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
                        Id = "M1",
                        DeviceType = "nmos",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "OUT",
                            ["G"] = "IN_SIGNAL",
                            ["S"] = "tail",
                        },
                    },
                    new()
                    {
                        Id = "M_tail",
                        DeviceType = "nmos",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "tail",
                            ["G"] = "BIAS_VOLTAGE",
                            ["S"] = "GND",
                        },
                    },
                },
            },
        };

        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var placement = CoarseGridPlacer.Place(topology, graph);
        var routing = MazeRouter.Route(placement, graph);
        var style = StyleSheet.Default;
        var renderer = new SvgRenderer();

        // Act
        var svg = renderer.Render(placement, routing, graph, style, new RenderOptions());

        // Assert: verify all content is within viewBox bounds
        AssertAllContentWithinBounds(svg);
    }

    [Fact]
    public void LongPortLabels_AreNotClipped()
    {
        // Arrange: circuit with very long port names
        var circuit = new Circuit
        {
            Name = "test",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "VERY_LONG_INPUT_NAME",
                    Type = "signal",
                },
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "ANOTHER_LONG_BIAS_PORT",
                    Type = "bias",
                },
                new()
                {
                    Direction = PortDirection.Output,
                    Name = "OUTPUT_SIGNAL",
                    Type = "signal",
                },
            },
            Fill = new FillBlock
            {
                Devices = new List<DeviceDeclaration>
                {
                    new()
                    {
                        Id = "M1",
                        DeviceType = "nmos",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "OUTPUT_SIGNAL",
                            ["G"] = "VERY_LONG_INPUT_NAME",
                            ["S"] = "tail",
                        },
                    },
                    new()
                    {
                        Id = "M_tail",
                        DeviceType = "nmos",
                        Bindings = new Dictionary<string, string>
                        {
                            ["D"] = "tail",
                            ["G"] = "ANOTHER_LONG_BIAS_PORT",
                            ["S"] = "GND",
                        },
                    },
                },
            },
        };

        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var placement = CoarseGridPlacer.Place(topology, graph);
        var routing = MazeRouter.Route(placement, graph);
        var style = StyleSheet.Default;
        var renderer = new SvgRenderer();

        // Act
        var svg = renderer.Render(placement, routing, graph, style, new RenderOptions());

        // Assert
        AssertAllContentWithinBounds(svg);
    }

    private static void AssertAllContentWithinBounds(string svg)
    {
        // Extract viewBox dimensions
        var viewBoxMatch = Regex.Match(
            svg,
            @"viewBox=""(\d+(?:\.\d+)?)\s+(\d+(?:\.\d+)?)\s+(\d+(?:\.\d+)?)\s+(\d+(?:\.\d+)?)"""
        );
        Assert.True(viewBoxMatch.Success, "SVG should have a viewBox");

        var viewBoxX = double.Parse(viewBoxMatch.Groups[1].Value);
        var viewBoxY = double.Parse(viewBoxMatch.Groups[2].Value);
        var viewBoxWidth = double.Parse(viewBoxMatch.Groups[3].Value);
        var viewBoxHeight = double.Parse(viewBoxMatch.Groups[4].Value);

        // Extract main content group translate
        var mainTranslateMatch = Regex.Match(
            svg,
            @"<g transform=""translate\((\d+(?:\.\d+)?),\s*(\d+(?:\.\d+)?)\)"">"
        );
        Assert.True(mainTranslateMatch.Success, "SVG should have main content group");

        var mainOffsetX = double.Parse(mainTranslateMatch.Groups[1].Value);
        var mainOffsetY = double.Parse(mainTranslateMatch.Groups[2].Value);

        // Find all port groups with their transforms
        var portPattern = new Regex(
            @"<g class=""port""[^>]*transform=""translate\((-?\d+(?:\.\d+)?),\s*(-?\d+(?:\.\d+)?)\)""[^>]*>.*?</g>",
            RegexOptions.Singleline
        );

        foreach (Match portMatch in portPattern.Matches(svg))
        {
            var portOffsetX = double.Parse(portMatch.Groups[1].Value);
            var portOffsetY = double.Parse(portMatch.Groups[2].Value);

            // Find port label within this port group
            var labelMatch = Regex.Match(
                portMatch.Value,
                @"<text[^>]*x=""(-?\d+(?:\.\d+)?)""[^>]*>([^<]+)</text>"
            );

            if (labelMatch.Success)
            {
                var labelRelX = double.Parse(labelMatch.Groups[1].Value);
                var labelText = labelMatch.Groups[2].Value;

                // Calculate absolute label end position (where text-anchor="end" anchors)
                var absoluteLabelEndX = mainOffsetX + portOffsetX + labelRelX;

                // Estimate text width: ~7px per character for bold 10px font
                var estimatedTextWidth = labelText.Length * 7;

                // For text-anchor="end", the label starts at (endX - textWidth)
                var estimatedLabelStartX = absoluteLabelEndX - estimatedTextWidth;

                // Verify label start is within viewBox
                Assert.True(
                    estimatedLabelStartX >= viewBoxX,
                    $"Port label '{labelText}' starts at estimated x={estimatedLabelStartX:F1}, "
                        + $"which is outside viewBox (x >= {viewBoxX}). "
                        + $"Main offset: {mainOffsetX}, Port offset: {portOffsetX}, Label rel x: {labelRelX}"
                );
            }
        }

        // Also verify that ports group exists and has content visible
        Assert.Contains(@"<g id=""ports"">", svg);
    }
}
