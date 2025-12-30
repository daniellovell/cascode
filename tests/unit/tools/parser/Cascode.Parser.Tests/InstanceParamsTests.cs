using System.Linq;
using Xunit;

namespace Cascode.Parser.Tests;

/// <summary>
/// Tests for instance parameter parsing (e.g., { p=NMOS; hasTail=true }).
/// </summary>
public class InstanceParamsTests
{
    [Fact]
    public void Parse_InstanceWithSingleParam_ExtractsParam()
    {
        const string text =
            @"
package test;
motif Test {
    supply VDD; ground GND;
    ports [ OUT: analog ]
    use {
        inst = new SomeMotif { p=NMOS };
    }
}";

        var tree = CascodeParserFacade.Parse("test.cas", text);

        ParserTestHelpers.AssertNoParseErrors(tree);

        var motif = tree.Root.Members.OfType<MotifDeclarationSyntax>().Single();
        var instance = motif.UseBlock!.Statements.OfType<InstanceDeclarationSyntax>().Single();

        Assert.Equal("inst", instance.InstanceName);
        Assert.Equal("SomeMotif", instance.TypeName);
        Assert.Single(instance.Parameters);
        Assert.Equal("p", instance.Parameters[0].Name);
        Assert.Equal("NMOS", instance.Parameters[0].Value);
    }

    [Fact]
    public void Parse_InstanceWithMultipleParams_ExtractsAllParams()
    {
        const string text =
            @"
package test;
motif Test {
    supply VDD; ground GND;
    ports [ OUT: analog ]
    use {
        dp = new DiffPair { p=NMOS; hasTail=true };
    }
}";

        var tree = CascodeParserFacade.Parse("test.cas", text);

        ParserTestHelpers.AssertNoParseErrors(tree);

        var motif = tree.Root.Members.OfType<MotifDeclarationSyntax>().Single();
        var instance = motif.UseBlock!.Statements.OfType<InstanceDeclarationSyntax>().Single();

        Assert.Equal("dp", instance.InstanceName);
        Assert.Equal("DiffPair", instance.TypeName);
        Assert.Equal(2, instance.Parameters.Count);

        Assert.Equal("p", instance.Parameters[0].Name);
        Assert.Equal("NMOS", instance.Parameters[0].Value);

        Assert.Equal("hasTail", instance.Parameters[1].Name);
        Assert.Equal("true", instance.Parameters[1].Value);
    }

    [Fact]
    public void Parse_InstanceWithNumericParam_ExtractsParam()
    {
        const string text =
            @"
package test;
motif Test {
    supply VDD; ground GND;
    ports [ OUT: analog ]
    use {
        cm = new CurrentMirror { p=PMOS; taps=1 };
    }
}";

        var tree = CascodeParserFacade.Parse("test.cas", text);

        ParserTestHelpers.AssertNoParseErrors(tree);

        var motif = tree.Root.Members.OfType<MotifDeclarationSyntax>().Single();
        var instance = motif.UseBlock!.Statements.OfType<InstanceDeclarationSyntax>().Single();

        Assert.Equal(2, instance.Parameters.Count);
        Assert.Equal("p", instance.Parameters[0].Name);
        Assert.Equal("PMOS", instance.Parameters[0].Value);
        Assert.Equal("taps", instance.Parameters[1].Name);
        Assert.Equal("1", instance.Parameters[1].Value);
    }

    [Fact]
    public void Parse_InstanceWithoutParams_HasEmptyParamsList()
    {
        const string text =
            @"
package test;
motif Test {
    supply VDD; ground GND;
    ports [ OUT: analog ]
    use {
        inst = new SomeMotif {};
    }
}";

        var tree = CascodeParserFacade.Parse("test.cas", text);

        ParserTestHelpers.AssertNoParseErrors(tree);

        var motif = tree.Root.Members.OfType<MotifDeclarationSyntax>().Single();
        var instance = motif.UseBlock!.Statements.OfType<InstanceDeclarationSyntax>().Single();

        Assert.Empty(instance.Parameters);
    }

    [Fact]
    public void Parse_OTA5TSingleEnded_ExtractsInstanceParams()
    {
        const string text =
            @"package analog.ota; import lib.std.amp.*; import lib.std.prim.*;

motif OTA5TSingleEnded implements SingleEndedOpAmp {
  supply VDD = 1.8V; ground GND;
  ports [ IN: Diff, OUT: analog, VTAIL: bias ]

  use {
    dp = new DiffPair { p=NMOS; hasTail=true } {
      IN.P -> IN.P; IN.N -> IN.N; BASE -> GND; BIAS -> VTAIL;
    };

    cm = new CurrentMirror { p=PMOS; taps=1 };
    attach cm to dp;
    connect dp.OUT.N -> OUT;
  }
}";

        var tree = CascodeParserFacade.Parse("OTA5TSingleEnded.cas", text);

        ParserTestHelpers.AssertNoParseErrors(tree);

        var motif = tree.Root.Members.OfType<MotifDeclarationSyntax>().Single();
        var instances = motif.UseBlock!.Statements.OfType<InstanceDeclarationSyntax>().ToList();

        Assert.Equal(2, instances.Count);

        // DiffPair instance
        var dp = instances.First(i => i.InstanceName == "dp");
        Assert.Equal("DiffPair", dp.TypeName);
        Assert.Equal(2, dp.Parameters.Count);
        Assert.Equal("p", dp.Parameters[0].Name);
        Assert.Equal("NMOS", dp.Parameters[0].Value);
        Assert.Equal("hasTail", dp.Parameters[1].Name);
        Assert.Equal("true", dp.Parameters[1].Value);

        // CurrentMirror instance
        var cm = instances.First(i => i.InstanceName == "cm");
        Assert.Equal("CurrentMirror", cm.TypeName);
        Assert.Equal(2, cm.Parameters.Count);
        Assert.Equal("p", cm.Parameters[0].Name);
        Assert.Equal("PMOS", cm.Parameters[0].Value);
        Assert.Equal("taps", cm.Parameters[1].Name);
        Assert.Equal("1", cm.Parameters[1].Value);
    }
}
