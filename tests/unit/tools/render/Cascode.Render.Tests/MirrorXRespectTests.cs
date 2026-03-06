using System.Text.RegularExpressions;
using Cascode.Render.Analysis;
using Cascode.Render.Placement;
using Cascode.Render.Routing;
using Cascode.Render.Svg;

namespace Cascode.Render.Tests;

public class MirrorXRespectTests
{
    [Fact]
    public void MosfetGateTerminalPosition_ChangesWhenMirrorXChanges()
    {
        var circuit = TestCircuits.SimpleCircuit();
        var graph = CircuitGraph.Build(circuit);

        var normalPlacement = new CoarseGridResult
        {
            RowCount = 1,
            ColumnCount = 1,
            SymmetryAxis = 0,
            DevicePlacements = new Dictionary<string, GridCell>(StringComparer.Ordinal)
            {
                ["M1"] = new GridCell(0, 0, rotation: 0, MirrorX: false, MirrorY: false),
            },
            HorizontalPassiveIds = new HashSet<string>(StringComparer.Ordinal),
        };
        var mirroredPlacement = new CoarseGridResult
        {
            RowCount = 1,
            ColumnCount = 1,
            SymmetryAxis = 0,
            DevicePlacements = new Dictionary<string, GridCell>(StringComparer.Ordinal)
            {
                ["M1"] = new GridCell(0, 0, rotation: 0, MirrorX: true, MirrorY: false),
            },
            HorizontalPassiveIds = new HashSet<string>(StringComparer.Ordinal),
        };

        var normalTerminals = MazeRouter.GetTerminalsByNet(normalPlacement, graph);
        var mirroredTerminals = MazeRouter.GetTerminalsByNet(mirroredPlacement, graph);
        var normalGate = GetTerminal(normalTerminals, "IN", "M1", "G");
        var mirroredGate = GetTerminal(mirroredTerminals, "IN", "M1", "G");

        Assert.NotEqual(normalGate.X, mirroredGate.X);
    }

    [Fact]
    public void SvgRenderer_AppliesMirrorTransformForMirroredMosfet()
    {
        var circuit = TestCircuits.SimpleCircuit();
        var graph = CircuitGraph.Build(circuit);
        var style = StyleSheet.Default;
        var renderer = new SvgRenderer();

        var normalPlacement = new CoarseGridResult
        {
            RowCount = 1,
            ColumnCount = 1,
            SymmetryAxis = 0,
            DevicePlacements = new Dictionary<string, GridCell>(StringComparer.Ordinal)
            {
                ["M1"] = new GridCell(0, 0, rotation: 0, MirrorX: false, MirrorY: false),
            },
            HorizontalPassiveIds = new HashSet<string>(StringComparer.Ordinal),
        };
        var mirroredPlacement = new CoarseGridResult
        {
            RowCount = 1,
            ColumnCount = 1,
            SymmetryAxis = 0,
            DevicePlacements = new Dictionary<string, GridCell>(StringComparer.Ordinal)
            {
                ["M1"] = new GridCell(0, 0, rotation: 0, MirrorX: true, MirrorY: false),
            },
            HorizontalPassiveIds = new HashSet<string>(StringComparer.Ordinal),
        };

        var normalRouting = MazeRouter.Route(normalPlacement, graph);
        var mirroredRouting = MazeRouter.Route(mirroredPlacement, graph);
        var normalSvg = renderer.Render(
            normalPlacement,
            normalRouting,
            graph,
            style,
            new RenderOptions()
        );
        var mirroredSvg = renderer.Render(
            mirroredPlacement,
            mirroredRouting,
            graph,
            style,
            new RenderOptions()
        );

        var normalDeviceGroup = ExtractDeviceGroup(normalSvg, "M1");
        var mirroredDeviceGroup = ExtractDeviceGroup(mirroredSvg, "M1");

        Assert.DoesNotContain("scale(-1, 1)", normalDeviceGroup, StringComparison.Ordinal);
        Assert.Contains("scale(-1, 1)", mirroredDeviceGroup, StringComparison.Ordinal);
    }

    private static TerminalPosition GetTerminal(
        IReadOnlyDictionary<string, IReadOnlyList<TerminalPosition>> byNet,
        string netName,
        string deviceId,
        string terminal
    )
    {
        Assert.True(byNet.TryGetValue(netName, out var terminals));
        return terminals.Single(t => t.DeviceId == deviceId && t.Terminal == terminal);
    }

    private static string ExtractDeviceGroup(string svg, string deviceId)
    {
        var match = Regex.Match(
            svg,
            $@"<g id=""{Regex.Escape(deviceId)}""[^>]*>.*?</g>",
            RegexOptions.Singleline
        );
        Assert.True(match.Success, $"Could not find SVG group for device '{deviceId}'.");
        return match.Value;
    }
}
