using System.IO;
using System.Linq;
using Cascode.Language;
using Cascode.Language.Validation;

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
    Leaf leaf = new Leaf(InputPair=size(W=2u, L=180n, M=1)) {{
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
    NMOS M1 = new NMOS_Level1(InputPair) {{
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

primitive NMOS NMOS_Level1(size primSize) {{
  device ""nmos_level1""
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
    Leaf leaf = new Leaf(InputPair=size(W=2u, L=180n, M=1)) {{
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
    NMOS M1 = new NMOS_Level1(size(W=3u, L=InputPair.L, M=InputPair.M)) {{
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
    public void HierarchyAndEmitter_ResolveNamedSizePackReferencesAcrossInlineCircuits()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

primitive PMOS PMOS_Level1(size primSize) {{
  device ""pmos_level1""
  params {{
    W = primSize.W
    L = primSize.L
    m = primSize.M
  }}
}}

primitive NMOS NMOS_Level1(size primSize) {{
  device ""nmos_level1""
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
  input IN : analog
  output OUT : analog
  fill {{
    size NCore = size(W=2u, L=180n, M=2)
    size PCore = size(W=4u, L=180n, M=3)

    Buffer buf = new Buffer(NmosSize=NCore, PmosSize=PCore) {{
      .VDD--VDD
      .GND--GND
      .IN--IN
      .OUT--OUT
    }}
  }}
}}

circuit Buffer(size NmosSize, size PmosSize) {{
  level EL
  inline
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog
  fill {{
    Inverter stage = new Inverter(NmosSize=NmosSize, PmosSize=PmosSize) {{
      .VDD--VDD
      .GND--GND
      .IN--IN
      .OUT--OUT
    }}
  }}
}}

circuit Inverter(size NmosSize, size PmosSize) {{
  level EL
  inline
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog
  fill {{
    PMOS MP = new PMOS_Level1(PmosSize) {{
      .B--VDD
      .D--OUT
      .G--IN
      .S--VDD
    }}

    NMOS MN = new NMOS_Level1(NmosSize) {{
      .B--GND
      .D--OUT
      .G--IN
      .S--GND
    }}
  }}
}}
";

        var parse = CascodeReader.TryParse(cascode, "named_size_refs.cas");
        Assert.True(parse.Success, string.Join(", ", parse.Diagnostics.Select(d => d.Message)));

        var doc = parse.Document!;
        var validation = HierarchyValidator.Validate(doc);
        Assert.True(
            validation.IsValid,
            string.Join(", ", validation.GetErrors().Select(e => e.Message))
        );

        var top = doc.Circuits.Single(c => c.Name == "Top");

        using var writer = new StringWriter();
        SpiceEmitter.EmitDesign(top, writer, document: doc);
        var spice = writer.ToString();

        Assert.Contains("Mbuf__stage__MP", spice);
        Assert.Contains("Mbuf__stage__MN", spice);
        Assert.Contains("W=4u", spice);
        Assert.Contains("W=2u", spice);
        Assert.Contains("L=180n", spice);
        Assert.Contains("m=3", spice);
        Assert.Contains("m=2", spice);
    }

    [Fact]
    public void EmitDesign_ResolveNamedScalarParameterReferencesAcrossHierarchy()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

primitive NMOS NMOS_Level1(size primSize) {{
  device ""nmos_level1""
  params {{
    W = primSize.W
    L = primSize.L
    m = primSize.M
  }}
}}

circuit Top(real width = 2u, int mult = 3, bool enabled = true) {{
  level EL
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog
  fill {{
    Wrapper stage = new Wrapper(width=width, mult=mult, enabled=enabled) {{
      .VDD--VDD
      .GND--GND
      .IN--IN
      .OUT--OUT
    }}
  }}
}}

circuit Wrapper(real width, int mult, bool enabled) {{
  level EL
  inline
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog
  fill {{
    Configurable cfg = new Configurable(width=width, mult=mult, enabled=enabled) {{
      .VDD--VDD
      .GND--GND
      .IN--IN
      .OUT--OUT
    }}
  }}
}}

circuit Configurable(real width, int mult, bool enabled) {{
  level EL
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog
  fill {{
    net t : analog

    NMOS M1 = new NMOS_Level1(size(W=width, L=180n, M=mult)) {{
      .B--GND
      .D--OUT
      .G--IN
      .S--t
    }}

    NMOS M2 = new NMOS_Level1(size(W=1u, L=180n, M=1)) {{
      .B--GND
      .D--t
      .G--IN
      .S--GND
    }}
  }}
}}
";

        var parse = CascodeReader.TryParse(cascode, "named_scalar_refs.cas");
        Assert.True(parse.Success, string.Join(", ", parse.Diagnostics.Select(d => d.Message)));

        var doc = parse.Document!;
        var top = doc.Circuits.Single(c => c.Name == "Top");

        using var writer = new StringWriter();
        SpiceEmitter.EmitDesign(top, writer, document: doc);
        var spice = writer.ToString();

        Assert.Contains("Xstage__cfg", spice);
        Assert.Contains("Configurable_enabled_true_mult_3_width_2u", spice);
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
    Leaf leaf = new Leaf(InputPair=size(W=2u, L=180n, W=3u)) {{
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
    NMOS M1 = new NMOS_Level1(InputPair) {{
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
    NMOS M1 = new NMOS_Level1(Params) {{
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
