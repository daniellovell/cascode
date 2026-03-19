using System.Text.Json;
using System.Text.Json.Nodes;
using Cascode.Native;

namespace Cascode.Native.Tests;

public sealed class SourceApiDispatcherTests
{
    [Fact]
    public void RewriteSchematic_PatchesRenderEntityWithoutDroppingComments()
    {
        using var session = CreateSession();
        var response = JsonNode.Parse(
            SchematicApiDispatcher.Dispatch(
                session.State,
                "source.rewriteSchematic",
                new JsonObject
                {
                    ["path"] = "render.cas",
                    ["text"] = BuildSource(),
                    ["circuit"] = "Test",
                    ["operations"] = new JsonArray(
                        new JsonObject
                        {
                            ["type"] = "patchRenderEntity",
                            ["name"] = "M1",
                            ["place"] = new JsonObject
                            {
                                ["point"] = new JsonObject
                                {
                                    ["kind"] = "abs",
                                    ["x"] = 9,
                                    ["y"] = 7,
                                },
                                ["strength"] = "hard",
                            },
                        }
                    ),
                }.ToJsonString()
            )
        )!;

        var sourceText = response["sourceText"]!.GetValue<string>();
        Assert.Contains("place abs 9 7 hard // keep inline", sourceText);
        Assert.Contains("// comment between fields", sourceText);
        Assert.Contains("// keep sibling comment", sourceText);
    }

    [Fact]
    public void RewriteSchematic_ReportsParseFailuresWithSourceLocation()
    {
        using var session = CreateSession();
        var ex = Assert.Throws<ApiException>(() =>
        {
            SchematicApiDispatcher.Dispatch(
                session.State,
                "source.rewriteSchematic",
                new JsonObject
                {
                    ["text"] = "VERSION 4.1\n\ncircuit Broken {\n  ???\n}\n",
                    ["operations"] = new JsonArray(
                        new JsonObject { ["type"] = "setRenderMode", ["mode"] = "manual" }
                    ),
                }.ToJsonString()
            );
        });

        Assert.Equal("CASAPI-PARSE-FAILED", ex.Code);
        Assert.NotNull(ex.Details);
        Assert.True(ex.Details!["line"]!.GetValue<int>() >= 1);
        Assert.True(ex.Details!["column"]!.GetValue<int>() >= 1);
    }

    [Fact]
    public void RewriteSchematic_RefreshesManualRoutesAfterDeviceMove()
    {
        using var session = CreateSession();
        const string source = """
            VERSION 4.1

            primitive Resistor ResistorIdeal(size primSize) {
              device "resistor_ideal"
              params {
                R = primSize.R
              }
            }

            circuit ManualRewrite {
              level EL
              input IN : analog
              output OUT : analog
              fill {
                Resistor R1 = new ResistorIdeal(size(R=1k)) {
                  .P--IN
                  .N--OUT
                }
              }
              render {
                mode manual
                IN {
                  place abs 0 10 hard
                  side left
                  route ortho hard
                  seg ref IN ref R1.P
                }
                OUT {
                  place abs 20 0 hard
                  side right
                  route ortho hard
                  seg ref R1.N ref OUT
                }
                R1 place abs 10 5 hard
              }
            }
            """;

        SchematicApiDispatcher.Dispatch(
            session.State,
            "document.open",
            new JsonObject { ["documentId"] = "manual-doc", ["text"] = source }.ToJsonString()
        );

        var rewritten = JsonNode.Parse(
            SchematicApiDispatcher.Dispatch(
                session.State,
                "source.rewriteSchematic",
                new JsonObject
                {
                    ["path"] = "manual-rewrite.cas",
                    ["text"] = source,
                    ["circuit"] = "ManualRewrite",
                    ["operations"] = new JsonArray(
                        new JsonObject
                        {
                            ["type"] = "patchRenderEntity",
                            ["name"] = "R1",
                            ["place"] = new JsonObject
                            {
                                ["point"] = new JsonObject
                                {
                                    ["kind"] = "abs",
                                    ["x"] = 10,
                                    ["y"] = 10,
                                },
                                ["strength"] = "hard",
                            },
                        }
                    ),
                }.ToJsonString()
            )
        )!;

        using var updated = JsonDocument.Parse(
            SchematicApiDispatcher.Dispatch(
                session.State,
                "document.updateText",
                new JsonObject
                {
                    ["documentId"] = "manual-doc",
                    ["baseRevision"] = 1,
                    ["text"] = rewritten["sourceText"]!.GetValue<string>(),
                    ["circuit"] = "ManualRewrite",
                }.ToJsonString()
            )
        );

        Assert.Equal(
            "manual",
            updated
                .RootElement.GetProperty("document")
                .GetProperty("renderSource")
                .GetProperty("mode")
                .GetString()
        );
        AssertOrthogonalSegments(
            updated
                .RootElement.GetProperty("document")
                .GetProperty("layout")
                .GetProperty("nets")
                .EnumerateArray()
                .SelectMany(net => net.GetProperty("segments").EnumerateArray())
        );
    }

    private static string BuildSource()
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
                M1 {
                  place abs 1 2 hard // keep inline
                  // comment between fields
                  orient 0
                }
                // keep sibling comment
                IN {
                  place abs 0 5 hard
                  side left
                }
              }
            }
            """;
    }

    private static IDisposableSession CreateSession()
    {
        var id = SessionManager.CreateSession(null);
        Assert.True(SessionManager.TryGetSession(id, out var state));
        return new DisposableSession(id, state);
    }

    private static void AssertOrthogonalSegments(IEnumerable<JsonElement> segments)
    {
        var segmentList = segments.ToList();
        Assert.NotEmpty(segmentList);
        Assert.All(
            segmentList,
            segment =>
            {
                var from = segment.GetProperty("from");
                var to = segment.GetProperty("to");
                var sameX = from.GetProperty("x").GetDouble() == to.GetProperty("x").GetDouble();
                var sameY = from.GetProperty("y").GetDouble() == to.GetProperty("y").GetDouble();
                Assert.True(sameX || sameY, "Expected routed segment to be orthogonal.");
            }
        );
    }

    private sealed class DisposableSession : IDisposableSession
    {
        private readonly int _id;

        public DisposableSession(int id, SessionState state)
        {
            _id = id;
            State = state;
        }

        public SessionState State { get; }

        public void Dispose()
        {
            SessionManager.DestroySession(_id);
        }
    }

    private interface IDisposableSession : IDisposable
    {
        SessionState State { get; }
    }
}
