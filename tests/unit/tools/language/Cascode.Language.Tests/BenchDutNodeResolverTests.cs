using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cascode.Language.BenchRuntime;
using Xunit;

namespace Cascode.Language.Tests;

public sealed class BenchDutNodeResolverTests
{
    [Fact]
    public void ResolveNodeKeys_NonInlineHierarchy_UsesXInstances()
    {
        var doc = Read(
            """
            VERSION 3.2

            circuit Leaf {
              level EL
              fill { net n : analog }
            }

            circuit Mid {
              level EL
              fill {
                Leaf inst = new Leaf() { }
              }
            }

            circuit Top {
              level EL
              fill {
                Mid mid = new Mid() { }
              }
            }
            """
        );

        var circuitsByName = doc.Circuits.ToDictionary(c => c.Name, StringComparer.Ordinal);
        var top = doc.Circuits.Single(c => c.Name == "Top");

        var map = BenchDutNodeResolver.ResolveNodeKeys(circuitsByName, top, new[] { "mid.inst.n" });
        Assert.Equal("XDUT.Xmid.Xinst.n", map["mid.inst.n"]);
    }

    [Fact]
    public void ResolveNodeKeys_InlineNet_FlattensIntoNetName()
    {
        var doc = Read(
            """
            VERSION 3.2

            circuit InlineLeaf {
              level EL
              inline
              fill { net leaf : analog }
            }

            circuit Mid {
              level EL
              fill {
                InlineLeaf inl = new InlineLeaf() { }
              }
            }

            circuit Top {
              level EL
              fill {
                Mid mid = new Mid() { }
              }
            }
            """
        );

        var circuitsByName = doc.Circuits.ToDictionary(c => c.Name, StringComparer.Ordinal);
        var top = doc.Circuits.Single(c => c.Name == "Top");

        var map = BenchDutNodeResolver.ResolveNodeKeys(
            circuitsByName,
            top,
            new[] { "mid.inl.leaf" }
        );
        Assert.Equal("XDUT.Xmid.inl__leaf", map["mid.inl.leaf"]);
    }

    [Fact]
    public void ResolveNodeKeys_NonInlineInstanceInsideInline_UsesInlinePrefixedInstanceName()
    {
        var doc = Read(
            """
            VERSION 3.2

            circuit NonInlineLeaf {
              level EL
              fill { net x : analog }
            }

            circuit InlineHost {
              level EL
              inline
              fill {
                NonInlineLeaf ni = new NonInlineLeaf() { }
              }
            }

            circuit Top {
              level EL
              fill {
                InlineHost inl = new InlineHost() { }
              }
            }
            """
        );

        var circuitsByName = doc.Circuits.ToDictionary(c => c.Name, StringComparer.Ordinal);
        var top = doc.Circuits.Single(c => c.Name == "Top");

        var map = BenchDutNodeResolver.ResolveNodeKeys(circuitsByName, top, new[] { "inl.ni.x" });
        Assert.Equal("XDUT.Xinl__ni.x", map["inl.ni.x"]);
    }

    [Fact]
    public void ResolveNodeKeys_LeafMustBeDeclaredFillNet()
    {
        var doc = Read(
            """
            VERSION 3.2

            circuit Leaf {
              level EL
              fill { }
            }

            circuit Top {
              level EL
              fill { Leaf inst = new Leaf() { } }
            }
            """
        );

        var circuitsByName = doc.Circuits.ToDictionary(c => c.Name, StringComparer.Ordinal);
        var top = doc.Circuits.Single(c => c.Name == "Top");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            BenchDutNodeResolver.ResolveNodeKeys(circuitsByName, top, new[] { "inst.nope" })
        );
        Assert.Contains(
            "not a net declared in fill",
            ex.Message,
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static CascodeDocument Read(string text)
    {
        var reader = new StringReader(text);
        var result = CascodeReader.TryRead(reader, "test.cas");
        Assert.True(result.Success, result.Diagnostics.ToString());

        var doc = result.Document;
        Assert.NotNull(doc);
        return doc!;
    }
}
