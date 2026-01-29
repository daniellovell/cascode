using System.IO;
using System.Linq;
using Cascode.Language;

namespace Cascode.Language.Tests;

public class SizePacksTests
{
    [Fact]
    public void TryParseSizeLiteral_ValidInput_ReturnsTrueWithParsedPack()
    {
        var success = SizePacks.TryParseSizeLiteral(
            "W=2u, L=180n, M=1",
            out var pack,
            out var error
        );

        Assert.True(success);
        Assert.Empty(error);
        Assert.Equal(3, pack.Entries.Count);
        Assert.Equal("2u", pack.Entries["W"]);
        Assert.Equal("180n", pack.Entries["L"]);
        Assert.Equal("1", pack.Entries["M"]);
    }

    [Fact]
    public void TryParseSizeLiteral_DuplicateKey_ReturnsFalseWithError()
    {
        var success = SizePacks.TryParseSizeLiteral(
            "W=2u, L=180n, W=3u",
            out var pack,
            out var error
        );

        Assert.False(success);
        Assert.Contains("Duplicate size key 'W'", error);
    }

    [Fact]
    public void TryParseSizeLiteral_TrailingComma_IgnoresEmptyEntries()
    {
        var success = SizePacks.TryParseSizeLiteral("W=2u, L=180n,", out var pack, out var error);

        Assert.True(success);
        Assert.Empty(error);
        Assert.Equal(2, pack.Entries.Count);
        Assert.Equal("2u", pack.Entries["W"]);
        Assert.Equal("180n", pack.Entries["L"]);
    }

    [Fact]
    public void TryParseSizeLiteral_EmptyInput_ReturnsFalse()
    {
        var success = SizePacks.TryParseSizeLiteral("", out _, out var error);

        Assert.False(success);
        Assert.Contains("Empty size literal", error);
    }

    [Fact]
    public void TryParseSizeLiteral_WhitespaceOnlyInput_ReturnsFalse()
    {
        var success = SizePacks.TryParseSizeLiteral("   ", out _, out var error);

        Assert.False(success);
        Assert.Contains("Empty size literal", error);
    }

    [Fact]
    public void TryParseSizeLiteral_MissingEquals_ReturnsFalse()
    {
        var success = SizePacks.TryParseSizeLiteral("W 2u", out _, out var error);

        Assert.False(success);
        Assert.Contains("Invalid size entry", error);
    }

    [Fact]
    public void TryParseSizeLiteral_MissingValue_ReturnsFalse()
    {
        var success = SizePacks.TryParseSizeLiteral("W=", out _, out var error);

        Assert.False(success);
        Assert.Contains("key or value is empty", error);
    }

    [Fact]
    public void TryRead_SizeDeclarationsAndAssignments_ParseSuccessfully()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit Top {{
  level EL
  supply VDD
  ground GND
  output OUT : analog
  fill {{
    leaf = new Leaf(InputPair=size(W=2u, L=180n, M=1)) {{
      .VDD--VDD
      .GND--GND
      .OUT--OUT
    }}
  }}
}}

circuit Leaf(size InputPair, size Tail = size(W=4u, L=180n, M=1)) {{
  level EL
  inline
  supply VDD
  ground GND
  output OUT : analog
  fill {{
    net t : analog
    nmos M1 = new Level1_NMOS(InputPair) {{
      .B--GND
      .D--OUT
      .G--OUT
      .S--t
    }}
  }}
}}
";

        var result = CascodeReader.TryParse(cascode, "sizes.cas");

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
        var cascode =
            $@"VERSION {CascodeVersion.Current}

primitive nmos Level1_NMOS(size primSize) {{
  device ""level1_nmos""
  params {{
    W = primSize.W
    L = primSize.L
    m = primSize.M
  }}
}}

circuit Top {{
  level EL
  supply VDD
  ground GND
  output OUT : analog
  fill {{
    leaf = new Leaf(InputPair=size(W=2u, L=180n, M=1)) {{
      .VDD--VDD
      .GND--GND
      .OUT--OUT
    }}
  }}
}}

circuit Leaf(size InputPair) {{
  level EL
  inline
  supply VDD
  ground GND
  output OUT : analog
  fill {{
    net t : analog
    // Explicit W should override size-pack W
    nmos M1 = new Level1_NMOS(size(W=3u, L=InputPair.L, M=InputPair.M)) {{
      .B--GND
      .D--OUT
      .G--OUT
      .S--t
    }}
  }}
}}
";

        var result = CascodeReader.TryParse(cascode, "sizes.cas");
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

    [Fact]
    public void CascodeReader_InstanceSizeDuplicateKey_ReturnsParseError()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit Top {{
  level EL
  supply VDD
  ground GND
  output OUT : analog
  fill {{
    leaf = new Leaf(InputPair=size(W=2u, L=180n, W=3u)) {{
      .VDD--VDD
      .GND--GND
      .OUT--OUT
    }}
  }}
}}

circuit Leaf(size InputPair) {{
  level EL
  inline
  supply VDD
  ground GND
  output OUT : analog
  fill {{
    net t : analog
    nmos M1 = new Level1_NMOS(InputPair) {{
      .B--GND
      .D--OUT
      .G--OUT
      .S--t
    }}
  }}
}}
";

        var result = CascodeReader.TryParse(cascode, "duplicate_size_key.cas");

        Assert.False(result.Success);
        var error = Assert.Single(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("Duplicate size key 'W'", error.Message);
    }

    [Fact]
    public void CascodeReader_SizeDeclarationDuplicateKey_ReturnsParseError()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit Top(size Params = size(W=2u, L=180n, W=3u)) {{
  level EL
  supply VDD
  ground GND
  output OUT : analog
  fill {{
    nmos M1 = new Level1_NMOS(Params) {{
      .B--GND
      .D--OUT
      .G--OUT
      .S--GND
    }}
  }}
}}
";

        var result = CascodeReader.TryParse(cascode, "duplicate_size_key.cas");

        Assert.False(result.Success);
        var error = Assert.Single(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("Duplicate size key 'W'", error.Message);
    }
}
