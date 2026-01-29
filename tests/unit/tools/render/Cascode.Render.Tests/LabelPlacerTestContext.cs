using Cascode.Language;
using Cascode.Render.Analysis;
using Cascode.Render.Layout;
using Cascode.Render.Placement;
using Cascode.Render.Routing;
using Cascode.Render.Svg;

namespace Cascode.Render.Tests;

/// <summary>
/// Fluent builder for constructing label placer test scenarios with minimal boilerplate.
/// </summary>
internal sealed class LabelPlacerTestContextBuilder
{
    private int _rows = 3;
    private int _cols = 3;
    private int _axis = 1;
    private readonly Dictionary<string, GridCell> _devices = new();
    private readonly HashSet<string> _horizontalPassives = new();
    private List<TerminalPosition> _terminals = new();
    private Circuit? _circuit;
    private StyleSheet _style = StyleSheet.Default;

    public LabelPlacerTestContextBuilder WithGrid(int rows, int cols, int axis)
    {
        _rows = rows;
        _cols = cols;
        _axis = axis;
        return this;
    }

    public LabelPlacerTestContextBuilder WithDevice(
        string id,
        int row,
        int col,
        bool mirror = false
    )
    {
        _devices[id] = new GridCell(row, col, mirror);
        return this;
    }

    public LabelPlacerTestContextBuilder WithHorizontalPassive(string id)
    {
        _horizontalPassives.Add(id);
        return this;
    }

    public LabelPlacerTestContextBuilder WithTerminals(params TerminalPosition[] terminals)
    {
        _terminals = terminals.ToList();
        return this;
    }

    public LabelPlacerTestContextBuilder WithCircuit(Circuit circuit)
    {
        _circuit = circuit;
        return this;
    }

    public LabelPlacerTestContextBuilder WithStyle(StyleSheet style)
    {
        _style = style;
        return this;
    }

    public LabelPlacerTestResult Build()
    {
        if (_circuit is null)
        {
            throw new InvalidOperationException(
                "Circuit must be set via WithCircuit before calling Build"
            );
        }

        var graph = CircuitGraph.Build(_circuit);

        var placement = new CoarseGridResult
        {
            RowCount = _rows,
            ColumnCount = _cols,
            SymmetryAxis = _axis,
            HorizontalPassiveIds = _horizontalPassives,
            DevicePlacements = _devices,
        };

        var canvasHeight =
            DeviceGeometry.RailMargin
            + placement.RowCount * DeviceGeometry.CellHeight
            + DeviceGeometry.RailMargin;
        var canvasWidth = placement.ColumnCount * DeviceGeometry.CellWidth;

        var routing = new RoutingResult
        {
            Segments = new List<WireSegment>(),
            Junctions = new List<GridPoint>(),
            SegmentsByNet = new Dictionary<string, IReadOnlyList<WireSegment>>(),
            CanvasWidth = canvasWidth,
            CanvasHeight = canvasHeight,
            TerminalPositions = _terminals,
        };

        var placer = new LabelPlacer();
        var labels = placer.PlaceLabels(placement, routing, graph, _style);

        return new LabelPlacerTestResult(
            labels,
            graph,
            placement,
            routing,
            canvasWidth,
            canvasHeight
        );
    }
}

/// <summary>
/// Result of building a label placer test context, containing all artifacts needed for assertions.
/// </summary>
internal sealed record LabelPlacerTestResult(
    IReadOnlyList<LabelPlacement> Labels,
    CircuitGraph Graph,
    CoarseGridResult Placement,
    RoutingResult Routing,
    double CanvasWidth,
    double CanvasHeight
);
