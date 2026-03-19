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

        Assert.Contains(result.Routing.Junctions, point => point.X == 8 && point.Y == 4);
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

    [Fact]
    public void Resolve_ManualSnapshotStyleSegments_AllowMixedAnchorAndAbsRouting()
    {
        var circuit = ParseCircuit(
            $@"VERSION {CascodeVersion.Current}

primitive Resistor ResistorIdeal(size primSize) {{
  device ""resistor_ideal""
  params {{
    R = primSize.R
  }}
}}

circuit SnapshotMixed {{
  level EL
  input IN : analog
  output OUT : analog
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
    R1 place abs 4 4 hard
    R2 place abs 12 4 hard
    n1 {{
      seg ref R1.N abs 8 4
      seg abs 8 4 ref R2.P
    }}
  }}
}}
"
        );
        var graph = CircuitGraph.Build(circuit);

        var result = ExactSchematicResolver.Resolve(circuit, graph, circuit.Render!);

        Assert.Single(result.Routing.SegmentsByNet["IN"]);
        Assert.Single(result.Routing.SegmentsByNet["OUT"]);
        Assert.Equal(2, result.Routing.SegmentsByNet["n1"].Count);
    }

    [Fact]
    public void Resolve_ManualRender_WithIncludeDefinedDiffLeafPorts_RoutesExpandedPorts()
    {
        var circuit = ParseCircuit(
            $@"VERSION {CascodeVersion.Current}
include lib.std

primitive Resistor ResistorIdeal(size primSize) {{
  device ""resistor_ideal""
  params {{
    R = primSize.R
  }}
}}

circuit SnapshotDiff {{
  level EL
  input IN : Diff
  output OUT : analog
  fill {{
    Resistor R1 = new ResistorIdeal(size(R=1k)) {{
      .P--IN.P
      .N--OUT
    }}
    Resistor R2 = new ResistorIdeal(size(R=1k)) {{
      .P--IN.N
      .N--OUT
    }}
  }}
  render {{
    mode manual
    IN.P {{
      place abs 0 2 hard
      side left
      seg ref IN.P ref R1.P
    }}
    IN.N {{
      place abs 0 6 hard
      side left
      seg ref IN.N ref R2.P
    }}
    OUT {{
      place abs 16 4 hard
      side right
      seg ref R1.N abs 12 4
      seg ref R2.N abs 12 4
      seg abs 12 4 ref OUT
    }}
    R1 place abs 4 2 hard
    R2 place abs 4 6 hard
  }}
}}
"
        );
        var graph = CircuitGraph.Build(circuit);

        var result = ExactSchematicResolver.Resolve(circuit, graph, circuit.Render!);

        Assert.Single(result.Routing.SegmentsByNet["IN.P"]);
        Assert.Single(result.Routing.SegmentsByNet["IN.N"]);
        Assert.Equal(3, result.Routing.SegmentsByNet["OUT"].Count);
        Assert.Contains(result.Routing.Junctions, point => point.X == 12 && point.Y == 4);
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
