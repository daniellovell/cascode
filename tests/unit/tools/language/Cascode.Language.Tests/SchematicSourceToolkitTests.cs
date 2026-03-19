using System;
using System.Linq;
using Cascode.Language;

namespace Cascode.Language.Tests;

public sealed class SchematicSourceToolkitTests
{
    [Fact]
    public void PatchRenderEntity_PreservesSiblingCommentsAndLineEndings()
    {
        var source = BuildRenderFixture().Replace("\n", "\r\n", StringComparison.Ordinal);

        var rewritten = SchematicSourceToolkit.Rewrite(
            "render-fixture.cas",
            source,
            new SchematicSourceOperation[]
            {
                new PatchRenderEntitySourceOperation(
                    "M1",
                    new RenderEntityPatch
                    {
                        Place = new RenderPlacement
                        {
                            Point = new RenderAbsPoint(3, 4),
                            Strength = RenderConstraintStrength.Hard,
                        },
                    }
                ),
            },
            "Test"
        );

        Assert.Contains("place abs 3 4 hard // keep inline", rewritten.SourceText);
        Assert.Contains("// comment between fields", rewritten.SourceText);
        Assert.Contains("// keep sibling comment", rewritten.SourceText);
        Assert.Contains("side left // keep sibling inline", rewritten.SourceText);
        Assert.DoesNotContain("place abs 1 2 hard // keep inline", rewritten.SourceText);
        Assert.Contains("\r\n", rewritten.SourceText);
        Assert.DoesNotContain(
            "\n",
            rewritten.SourceText.Replace("\r\n", string.Empty, StringComparison.Ordinal)
        );
    }

    [Fact]
    public void ApplyRenderSnapshot_RewritesManualSnapshotWithoutTouchingSiblingComments()
    {
        const string source = """
            VERSION 4.1

            primitive NMOS NMOS_Level1(size primSize) {
              device "nmos"
              params {
                W = primSize.W
                L = primSize.L
              }
            }

            circuit Test {
              level EL
              input IN : analog
              output OUT : analog
              ground GND
              fill {
                NMOS M1 = new NMOS_Level1(size(W=1u, L=180n)) {
                  .G--IN
                  .D--OUT
                  .S--GND
                }
              }
              render {
                // old auto hint
                M1 place abs 1 2 hard
                // keep this comment with IN
                IN {
                  place abs 0 5 hard
                  side left
                }
              }
            }
            """;

        var snapshot = new[]
        {
            new RenderEntity
            {
                Name = "IN",
                Place = new RenderPlacement
                {
                    Point = new RenderAbsPoint(0, 6),
                    Strength = RenderConstraintStrength.Hard,
                },
                Side = RenderPortSide.Left,
                Segments =
                {
                    new RenderSegment
                    {
                        From = new RenderRefPoint("IN", 0, 0),
                        To = new RenderRefPoint("M1.G", 0, 0),
                    },
                },
            },
            new RenderEntity
            {
                Name = "M1",
                Place = new RenderPlacement
                {
                    Point = new RenderAbsPoint(4, 6),
                    Strength = RenderConstraintStrength.Hard,
                },
                Orientation = new RenderOrientation { Rotate = 90, MirrorX = false },
            },
        };

        var rewritten = SchematicSourceToolkit.Rewrite(
            "snapshot.cas",
            source,
            new SchematicSourceOperation[]
            {
                new ApplyRenderSnapshotSourceOperation(RenderLayoutMode.Manual, snapshot),
            },
            "Test"
        );

        Assert.Contains("mode manual", rewritten.SourceText);
        Assert.Contains("// old auto hint", rewritten.SourceText);
        Assert.Contains("// keep this comment with IN", rewritten.SourceText);
        Assert.Contains("seg ref IN ref M1.G", rewritten.SourceText);
        Assert.Contains("orient 90", rewritten.SourceText);
    }

    [Fact]
    public void SetDeviceParam_RewritesOnlySizeArgument()
    {
        const string source = """
            VERSION 4.1

            primitive NMOS NMOS_Level1(size primSize) {
              device "nmos"
              params {
                W = primSize.W
                L = primSize.L
                M = primSize.M
              }
            }

            circuit Test {
              level EL
              input IN : analog
              output OUT : analog
              ground GND
              fill {
                NMOS M1 = new NMOS_Level1(size(W=1u, L=180n, M=1)) {
                  .G--IN // preserve binding comment
                  .D--OUT
                  .S--GND
                }
              }
            }
            """;

        var rewritten = SchematicSourceToolkit.Rewrite(
            "device-param.cas",
            source,
            new SchematicSourceOperation[] { new SetDeviceParamSourceOperation("M1", "W", "2u") },
            "Test"
        );

        Assert.Contains("new NMOS_Level1(size(L=180n, M=1, W=2u))", rewritten.SourceText);
        Assert.Contains(".G--IN // preserve binding comment", rewritten.SourceText);
        Assert.DoesNotContain("size(W=1u, L=180n, M=1)", rewritten.SourceText);
    }

    private static string BuildRenderFixture()
    {
        return """
            VERSION 4.1

            primitive NMOS NMOS_Level1(size primSize) {
              device "nmos"
              params {
                W = primSize.W
                L = primSize.L
              }
            }

            circuit Test {
              level EL
              input IN : analog
              output OUT : analog
              ground GND
              fill {
                NMOS M1 = new NMOS_Level1(size(W=1u, L=180n)) {
                  .G--IN
                  .D--OUT
                  .S--GND
                }
              }
              render {
                // comment before M1
                M1 {
                  place abs 1 2 hard // keep inline
                  // comment between fields
                  orient 0
                }
                // keep sibling comment
                IN {
                  place abs 0 5 hard
                  side left // keep sibling inline
                }
              }
            }
            """;
    }
}
