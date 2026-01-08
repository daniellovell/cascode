using System.IO;
using System.Linq;
using Cascode.ACIR;

namespace Cascode.ACIR.Tests;

public class SizePacksTests
{
    [Fact]
    public void TryRead_SizeDeclarationsAndAssignments_ParseSuccessfully()
    {
        var acir =
            $@"ACIR {ACIRVersion.Current}

circuit Top
  level EL
  supply VDD
  ground GND
  port OUT : analog
  fill:
    inst leaf (VDD->VDD, GND->GND, OUT->OUT) : Leaf
      size InputPair = (W=2u, L=180n, M=1)

circuit Leaf
  level EL
  inline
  size InputPair
  size Tail = (W=4u, L=180n, M=1)
  supply VDD
  ground GND
  port OUT : analog
  fill:
    net t : analog
    nmos M1 (B->GND, D->OUT, G->OUT, S->t) : size=InputPair nmos
";

        var result = ACIRReader.TryParse(acir, "sizes.cir");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);

        var doc = result.Document!;
        var leaf = doc.Circuits.Single(c => c.Name == "Leaf");
        Assert.Equal(2, leaf.Sizes.Count);

        var inputPair = leaf.Sizes.Single(s => s.Name == "InputPair");
        Assert.Null(inputPair.Default);

        var tail = leaf.Sizes.Single(s => s.Name == "Tail");
        Assert.NotNull(tail.Default);
        Assert.Equal("4u", tail.Default!.Entries["W"]);
        Assert.Equal("180n", tail.Default.Entries["L"]);
        Assert.Equal("1", tail.Default.Entries["M"]);

        var top = doc.Circuits.Single(c => c.Name == "Top");
        var inst = top.Fill!.Instances.Single(i => i.Id == "leaf");
        Assert.True(inst.Sizes.ContainsKey("InputPair"));
        Assert.Equal("2u", inst.Sizes["InputPair"].Entries["W"]);
        Assert.Equal("180n", inst.Sizes["InputPair"].Entries["L"]);
        Assert.Equal("1", inst.Sizes["InputPair"].Entries["M"]);
    }

    [Fact]
    public void SpiceEmitter_ExpandsSizePack_WithExplicitOverrides()
    {
        var acir =
            $@"ACIR {ACIRVersion.Current}

circuit Top
  level EL
  supply VDD
  ground GND
  port OUT : analog
  fill:
    inst leaf (VDD->VDD, GND->GND, OUT->OUT) : Leaf
      size InputPair = (W=2u, L=180n, M=1)

circuit Leaf
  level EL
  inline
  size InputPair
  supply VDD
  ground GND
  port OUT : analog
  fill:
    net t : analog
    // Explicit W should override size-pack W
    nmos M1 (B->GND, D->OUT, G->OUT, S->t) : size=InputPair W=3u nmos
";

        var result = ACIRReader.TryParse(acir, "sizes.cir");
        Assert.True(result.Success);
        var doc = result.Document!;
        var top = doc.Circuits.Single(c => c.Name == "Top");

        using var writer = new StringWriter();
        SpiceEmitter.EmitDesign(top, writer, document: doc);
        var spice = writer.ToString();

        // Inline expansion should emit a MOS line with expanded size parameters.
        Assert.Contains("Mleaf__M1", spice);
        Assert.Contains("W=3u", spice); // override wins
        Assert.Contains("L=180n", spice);
        Assert.Contains("m=1", spice);
    }
}
