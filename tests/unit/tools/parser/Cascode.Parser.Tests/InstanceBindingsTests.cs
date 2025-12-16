using System.Linq;
using Xunit;

namespace Cascode.Parser.Tests;

/// <summary>
/// Tests for instance binding parsing (e.g., { IN.P -> IN.P; IN.N -> IN.N; }).
/// </summary>
public class InstanceBindingsTests
{
    [Fact]
    public void Parse_InstanceWithSingleBinding_ExtractsBinding()
    {
        const string text = @"
package test;
motif Test {
    supply VDD; ground GND;
    ports [ IN: analog, OUT: analog ]
    use {
        inst = new SomeMotif {} { IN -> IN; };
    }
}";

        var tree = CascodeParserFacade.Parse("test.cas", text);

        ParserTestHelpers.AssertNoParseErrors(tree);

        var motif = tree.Root.Members.OfType<MotifDeclarationSyntax>().Single();
        var instance = motif.UseBlock!.Statements.OfType<InstanceDeclarationSyntax>().Single();

        Assert.Single(instance.Bindings);
        Assert.Equal("IN", instance.Bindings[0].FromPin);
        Assert.Equal("IN", instance.Bindings[0].ToPin);
    }

    [Fact]
    public void Parse_InstanceWithMultipleBindings_ExtractsAllBindings()
    {
        const string text = @"
package test;
motif Test {
    supply VDD; ground GND;
    ports [ IN: Diff, OUT: analog ]
    use {
        dp = new DiffPair { p=NMOS } {
            IN.P -> IN.P; IN.N -> IN.N; BASE -> GND;
        };
    }
}";

        var tree = CascodeParserFacade.Parse("test.cas", text);

        ParserTestHelpers.AssertNoParseErrors(tree);

        var motif = tree.Root.Members.OfType<MotifDeclarationSyntax>().Single();
        var instance = motif.UseBlock!.Statements.OfType<InstanceDeclarationSyntax>().Single();

        Assert.Equal(3, instance.Bindings.Count);

        Assert.Equal("IN.P", instance.Bindings[0].FromPin);
        Assert.Equal("IN.P", instance.Bindings[0].ToPin);

        Assert.Equal("IN.N", instance.Bindings[1].FromPin);
        Assert.Equal("IN.N", instance.Bindings[1].ToPin);

        Assert.Equal("BASE", instance.Bindings[2].FromPin);
        Assert.Equal("GND", instance.Bindings[2].ToPin);
    }

    [Fact]
    public void Parse_InstanceWithoutBindings_HasEmptyBindingsList()
    {
        const string text = @"
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

        Assert.Empty(instance.Bindings);
    }

    [Fact]
    public void Parse_OTA5TSingleEnded_ExtractsInstanceBindings()
    {
        const string text = @"package analog.ota; import lib.std.amp.*; import lib.std.prim.*;

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

        // DiffPair instance has bindings
        var dp = instances.First(i => i.InstanceName == "dp");
        Assert.Equal(4, dp.Bindings.Count);

        Assert.Equal("IN.P", dp.Bindings[0].FromPin);
        Assert.Equal("IN.P", dp.Bindings[0].ToPin);

        Assert.Equal("IN.N", dp.Bindings[1].FromPin);
        Assert.Equal("IN.N", dp.Bindings[1].ToPin);

        Assert.Equal("BASE", dp.Bindings[2].FromPin);
        Assert.Equal("GND", dp.Bindings[2].ToPin);

        Assert.Equal("BIAS", dp.Bindings[3].FromPin);
        Assert.Equal("VTAIL", dp.Bindings[3].ToPin);

        // CurrentMirror instance has no bindings
        var cm = instances.First(i => i.InstanceName == "cm");
        Assert.Empty(cm.Bindings);
    }
}

