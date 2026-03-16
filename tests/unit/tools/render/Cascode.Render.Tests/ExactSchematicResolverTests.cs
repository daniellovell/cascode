using Cascode.Language;
using Cascode.Render.Analysis;
using Cascode.Render.Layout;

namespace Cascode.Render.Tests;

public sealed class ExactSchematicResolverTests
{
    [Fact]
    public void Resolve_ManualSegments_PreserveArbitraryAngleGeometry()
    {
        var circuit = ParseCircuit(
            $@"VERSION {CascodeVersion.Current}

primitive Resistor ResistorIdeal(size primSize) {{
  device ""resistor_ideal""
  params {{
    R = primSize.R
  }}
}}

circuit ManualArbitrary {{
  level EL
  input IN : analog
  output OUT : analog
  fill {{
    Resistor R1 = new ResistorIdeal(size(R=1k)) {{
      .P--IN
      .N--OUT
    }}
  }}
  render {{
    mode manual
    IN {{
      place abs 0 0 hard
      side left
      seg ref IN ref R1.P
    }}
    OUT {{
      place abs 20 10 hard
      side right
      seg ref R1.N ref OUT
    }}
    R1 place abs 10 5 hard
  }}
}}
"
        );

        var graph = CircuitGraph.Build(circuit);
        var result = ExactSchematicResolver.Resolve(circuit, graph, circuit.Render!);

        var inSegments = Assert.Single(result.Routing.SegmentsByNet["IN"]);
        var outSegments = Assert.Single(result.Routing.SegmentsByNet["OUT"]);

        Assert.NotEqual(inSegments.From.X, inSegments.To.X);
        Assert.NotEqual(inSegments.From.Y, inSegments.To.Y);
        Assert.NotEqual(outSegments.From.X, outSegments.To.X);
        Assert.NotEqual(outSegments.From.Y, outSegments.To.Y);
    }

    [Fact]
    public void Resolve_ManualTJunction_ConnectsEndpointOnInterior()
    {
        var circuit = ParseCircuit(BuildBranchingManualSource("seg ref R3.N abs 8 4"));
        var graph = CircuitGraph.Build(circuit);

        var result = ExactSchematicResolver.Resolve(circuit, graph, circuit.Render!);

        Assert.Contains(result.Routing.Junctions, point => point.X == 80 && point.Y == 40);
    }

    [Fact]
    public void Resolve_ManualCrossingWithoutSharedEndpoint_FailsConnectivity()
    {
        var circuit = ParseCircuit(BuildBranchingManualSource("seg ref R3.N abs 8 0"));
        var graph = CircuitGraph.Build(circuit);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ExactSchematicResolver.Resolve(circuit, graph, circuit.Render!)
        );

        Assert.Contains("disconnected terminal geometry", ex.Message, StringComparison.Ordinal);
    }

    private static Circuit ParseCircuit(string source)
    {
        var read = CascodeReader.TryParse(source, "exact-render.cas");
        Assert.True(
            read.Success,
            string.Join(Environment.NewLine, read.Diagnostics.Select(d => d.Message))
        );
        return Assert.Single(read.Document!.Circuits);
    }

    private static string BuildBranchingManualSource(string branchSegment)
    {
        return $@"VERSION {CascodeVersion.Current}

primitive Resistor ResistorIdeal(size primSize) {{
  device ""resistor_ideal""
  params {{
    R = primSize.R
  }}
}}

circuit ManualBranch {{
  level EL
  input IN : analog
  output OUT : analog
  input TAP : analog
  fill {{
    net n1 : analog
    Resistor R1 = new ResistorIdeal(size(R=1k)) {{
      .P--IN
      .N--n1
    }}
    Resistor R2 = new ResistorIdeal(size(R=1k)) {{
      .P--n1
      .N--OUT
    }}
    Resistor R3 = new ResistorIdeal(size(R=1k)) {{
      .P--TAP
      .N--n1
    }}
  }}
  render {{
    mode manual
    IN {{
      place abs 0 4 hard
      side left
      seg ref IN ref R1.P
    }}
    OUT {{
      place abs 16 4 hard
      side right
      seg ref R2.N ref OUT
    }}
    TAP {{
      place abs 8 12 hard
      side left
      seg ref TAP ref R3.P
    }}
    R1 place abs 4 4 hard
    R2 place abs 12 4 hard
    R3 place abs 8 8 hard
    n1 {{
      seg ref R1.N ref R2.P
      {branchSegment}
    }}
  }}
}}
";
    }
}
