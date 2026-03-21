using System.IO;
using System.Linq;
using Cascode.Language;

namespace Cascode.Language.Tests;

public sealed class RenderBlockTests
{
    [Fact]
    public void ParseWrite_RenderBlock_RoundTripsDeterministically()
    {
        var source =
            $@"VERSION {CascodeVersion.Current}

circuit Test {{
  level EL
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog
  fill {{
    net n1 : analog
    NMOS M1 = new NMOS_Level1(size(W=1u, L=180n)) {{
      .G--IN
      .D--n1
      .S--GND
    }}
    n1--OUT
  }}
  render {{
    M1 place ref IN 12 0 hard
    IN {{
      place abs 0 8 soft
      side left
    }}
    n1 {{
      route ortho soft
      wp [ref M1.D, rel 0 6, ref OUT]
    }}
  }}
}}
";

        var first = CascodeReader.TryParse(source, "render.cas");
        Assert.True(first.Success);
        Assert.NotNull(first.Document);

        using var w1 = new StringWriter();
        CascodeWriter.Write(first.Document!, w1);
        var output1 = w1.ToString();

        var second = CascodeReader.TryParse(output1, "render_out.cas");
        Assert.True(second.Success);

        using var w2 = new StringWriter();
        CascodeWriter.Write(second.Document!, w2);
        var output2 = w2.ToString();

        Assert.Equal(output1, output2);
        Assert.Contains("render {", output1);
        Assert.Contains("M1 place ref IN 12 0 hard", output1);
        Assert.Contains("route ortho soft", output1);
        Assert.Contains("wp [ref M1.D, rel 0 6, ref OUT]", output1);
    }

    [Fact]
    public void Parse_WithCompatibilityMinorOption_KeepsRenderBlock()
    {
        var source =
            $@"VERSION {CascodeVersion.Current}

circuit Compat {{
  level EL
  supply VDD
  ground GND
  input IN : analog
  fill {{
    NMOS M1 = new NMOS_Level1(size(W=1u, L=180n)) {{
      .G--IN
      .D--VDD
      .S--GND
    }}
  }}
  render {{
    M1 place abs 10 20 hard
  }}
}}
";

        var result = CascodeParserFacade.Parse(
            "compat.cas",
            source,
            new CascodeParseOptions(
                DesugarBundles: true,
                RunBenchSemanticChecks: true,
                RunBenchBindingChecksWhenNoIncludes: false,
                CompatibilityMinor: 1
            )
        );
        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        Assert.NotNull(result.Document!.Circuits.Single().Render);
    }

    [Fact]
    public void Parser_PrunesStaleRenderEntries()
    {
        var source =
            $@"VERSION {CascodeVersion.Current}

circuit Prune {{
  level EL
  supply VDD
  ground GND
  input IN : analog
  fill {{
    NMOS M1 = new NMOS_Level1(size(W=1u, L=180n)) {{
      .G--IN
      .D--VDD
      .S--GND
    }}
  }}
  render {{
    M1 place ref IN 1 0 hard
    Missing place abs 10 10 hard
  }}
}}
";

        var result = CascodeReader.TryParse(source, "prune.cas");
        Assert.True(result.Success);

        var circuit = result.Document!.Circuits.Single();
        Assert.NotNull(circuit.Render);
        Assert.Single(circuit.Render!.Entities);
        Assert.Equal("M1", circuit.Render.Entities[0].Name);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("CAS3200"));
        Assert.Contains(
            result.Diagnostics,
            d => d.Message.Contains("CAS3200") && d.FilePath == "prune.cas"
        );

        using var writer = new StringWriter();
        CascodeWriter.Write(result.Document, writer);
        var output = writer.ToString();
        Assert.Contains("M1 place ref IN 1 0 hard", output);
        Assert.DoesNotContain("Missing", output);
    }

    [Fact]
    public void Parse_RenderAnchorTerminal_AllowsLowercaseTerminal()
    {
        var source =
            $@"VERSION {CascodeVersion.Current}

primitive NMOS_Level1(size primSize) implements NMOS {{
  device ""nmos_level1""
  params {{
    W = primSize.W
    L = primSize.L
    m = primSize.M
  }}
}}

circuit LowercaseAnchor {{
  level EL
  input IN : analog
  output OUT : analog
  ground GND
  fill {{
    NMOS M1 = new NMOS_Level1(size(W=1u, L=180n, M=1)) {{
      .D--OUT
      .G--IN
      .S--GND
      .B--GND
    }}
  }}
  render {{
    M1 place ref M1.g 0 0 hard
  }}
}}
";

        var result = CascodeReader.TryParse(source, "lowercase-anchor.cas");
        Assert.True(result.Success);
        Assert.DoesNotContain(result.Diagnostics, d => d.Message.Contains("CAS3200"));
    }
}
