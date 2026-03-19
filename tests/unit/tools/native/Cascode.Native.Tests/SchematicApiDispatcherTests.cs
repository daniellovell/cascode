using System.Text.Json;
using System.Text.Json.Nodes;
using Cascode.Language;
using Cascode.Language.Validation;
using Cascode.Native;

namespace Cascode.Native.Tests;

public sealed class SchematicApiDispatcherTests
{
    [Fact]
    public void RenderSchematic_PersistManualOnDiffInputDocument_SnapshotsCompleteManualRender()
    {
        using var session = ApiSession.Create();
        session.State.StdlibRoot = GetStdlibRoot();
        Dispatch(
            session.State,
            "document.open",
            new JsonObject { ["documentId"] = "diff-doc", ["text"] = BuildDiffSnapshotSource() }
        );

        var rendered = Dispatch(
            session.State,
            "render.schematic",
            new JsonObject
            {
                ["documentId"] = "diff-doc",
                ["mode"] = "manual",
                ["persist"] = true,
            }
        );

        Assert.Equal(
            "manual",
            rendered
                .RootElement.GetProperty("document")
                .GetProperty("renderSource")
                .GetProperty("mode")
                .GetString()
        );

        var circuit = ParseCircuit(
            rendered.RootElement.GetProperty("sourceText").GetString()!,
            "DiffSnapshot"
        );
        AssertManualRenderHasNoCompletenessErrors(circuit);
        AssertEntityHasManualDevicePlacement(circuit, "M_TAIL");
        AssertEntityHasManualNetSegments(circuit, "IN.P");
        AssertEntityHasManualNetSegments(circuit, "IN.N");
    }

    [Fact]
    public void CaptureManualSnapshot_OnAutoDocument_RoundTripsThroughSourceRewriteToCompleteManualRender()
    {
        using var session = ApiSession.Create();
        Dispatch(
            session.State,
            "document.open",
            new JsonObject
            {
                ["documentId"] = "doc1",
                ["text"] = BuildSampleSource(withRenderBlock: false),
            }
        );

        using var snapshot = Dispatch(
            session.State,
            "schematic.captureManualSnapshot",
            new JsonObject { ["documentId"] = "doc1", ["baseRevision"] = 1 }
        );
        using var rewritten = Dispatch(
            session.State,
            "source.rewriteSchematic",
            new JsonObject
            {
                ["path"] = "doc1",
                ["text"] = BuildSampleSource(withRenderBlock: false),
                ["circuit"] = "Amp",
                ["operations"] = new JsonArray(
                    new JsonObject
                    {
                        ["type"] = "applyRenderSnapshot",
                        ["mode"] = "manual",
                        ["entities"] = JsonNode.Parse(
                            snapshot.RootElement.GetProperty("entities").GetRawText()
                        )!,
                    }
                ),
            }
        );

        var circuit = ParseCircuit(rewritten.RootElement.GetProperty("sourceText").GetString()!);
        AssertCompleteManualRender(circuit);
    }

    [Fact]
    public void UpdateText_PrunesStaleRenderEntries()
    {
        using var session = ApiSession.Create();
        var opened = Dispatch(
            session.State,
            "document.open",
            new JsonObject
            {
                ["documentId"] = "doc1",
                ["text"] = BuildSampleSource(withRenderBlock: false),
            }
        );
        var m1Position = opened
            .RootElement.GetProperty("layout")
            .GetProperty("devices")
            .EnumerateArray()
            .Single(device => device.GetProperty("id").GetString() == "M1")
            .GetProperty("position");
        var m2Position = opened
            .RootElement.GetProperty("layout")
            .GetProperty("devices")
            .EnumerateArray()
            .Single(device => device.GetProperty("id").GetString() == "M2")
            .GetProperty("position");

        var sourceWithRender = BuildSourceWithRender(
            m1DeviceId: "M1",
            includeM2Device: true,
            includeM1Render: true,
            includeM2Render: true,
            m1X: m1Position.GetProperty("x").GetDouble(),
            m1Y: m1Position.GetProperty("y").GetDouble(),
            m2X: m2Position.GetProperty("x").GetDouble(),
            m2Y: m2Position.GetProperty("y").GetDouble()
        );
        Dispatch(
            session.State,
            "document.updateText",
            new JsonObject
            {
                ["documentId"] = "doc1",
                ["baseRevision"] = 1,
                ["text"] = sourceWithRender,
            }
        );

        var renamedSource = BuildSourceWithRender(
            m1DeviceId: "M1_RENAMED",
            includeM2Device: true,
            includeM1Render: true,
            includeM2Render: true,
            m1X: m1Position.GetProperty("x").GetDouble(),
            m1Y: m1Position.GetProperty("y").GetDouble(),
            m2X: m2Position.GetProperty("x").GetDouble(),
            m2Y: m2Position.GetProperty("y").GetDouble()
        );
        var updateRenamed = Dispatch(
            session.State,
            "document.updateText",
            new JsonObject
            {
                ["documentId"] = "doc1",
                ["baseRevision"] = 2,
                ["text"] = renamedSource,
            }
        );

        var renamedCircuit = ParseCircuit(
            updateRenamed.RootElement.GetProperty("sourceText").GetString()!
        );
        Assert.DoesNotContain(renamedCircuit.Render!.Entities, entry => entry.Name == "M1");

        var deletedSource = BuildSourceWithRender(
            m1DeviceId: "M1_RENAMED",
            includeM2Device: false,
            includeM1Render: true,
            includeM2Render: true,
            m1X: m1Position.GetProperty("x").GetDouble(),
            m1Y: m1Position.GetProperty("y").GetDouble(),
            m2X: m2Position.GetProperty("x").GetDouble(),
            m2Y: m2Position.GetProperty("y").GetDouble()
        );
        var updateDeleted = Dispatch(
            session.State,
            "document.updateText",
            new JsonObject
            {
                ["documentId"] = "doc1",
                ["baseRevision"] = 3,
                ["text"] = deletedSource,
            }
        );

        var deletedCircuit = ParseCircuit(
            updateDeleted.RootElement.GetProperty("sourceText").GetString()!
        );
        Assert.True(
            deletedCircuit.Render is null
                || deletedCircuit.Render.Entities.All(entry => entry.Name != "M2")
        );
    }

    [Fact]
    public void PreviewRoute_ReturnsOrthogonalSegmentsBetweenTerminalAndTerminal()
    {
        using var session = ApiSession.Create();
        var opened = Dispatch(
            session.State,
            "document.open",
            new JsonObject
            {
                ["documentId"] = "doc1",
                ["text"] = BuildSampleSource(withRenderBlock: false),
            }
        );
        var start = opened
            .RootElement.GetProperty("renderCache")
            .GetProperty("terminalPoints")
            .GetProperty("M1")
            .GetProperty("G");
        var target = opened
            .RootElement.GetProperty("renderCache")
            .GetProperty("terminalPoints")
            .GetProperty("M2")
            .GetProperty("G");

        var preview = Dispatch(
            session.State,
            "schematic.previewRoute",
            new JsonObject
            {
                ["documentId"] = "doc1",
                ["baseRevision"] = 1,
                ["mode"] = "connect",
                ["start"] = new JsonObject
                {
                    ["kind"] = "terminal",
                    ["token"] = "M1.G",
                    ["x"] = start.GetProperty("x").GetDouble(),
                    ["y"] = start.GetProperty("y").GetDouble(),
                },
                ["target"] = new JsonObject
                {
                    ["kind"] = "terminal",
                    ["token"] = "M2.G",
                    ["x"] = target.GetProperty("x").GetDouble(),
                    ["y"] = target.GetProperty("y").GetDouble(),
                },
            }
        );

        Assert.True(preview.RootElement.GetProperty("valid").GetBoolean());
        AssertOrthogonalSegments(preview.RootElement.GetProperty("segments").EnumerateArray());
    }

    [Fact]
    public void PreviewRoute_ReturnsOrthogonalSegmentsBetweenTerminalAndExistingNetAnchor()
    {
        using var session = ApiSession.Create();
        var opened = Dispatch(
            session.State,
            "document.open",
            new JsonObject
            {
                ["documentId"] = "doc1",
                ["text"] = BuildSampleSource(withRenderBlock: false),
            }
        );
        var start = opened
            .RootElement.GetProperty("renderCache")
            .GetProperty("terminalPoints")
            .GetProperty("M1")
            .GetProperty("G");
        var netPoint = opened
            .RootElement.GetProperty("layout")
            .GetProperty("nets")
            .EnumerateArray()
            .Single(net => net.GetProperty("name").GetString() == "n1")
            .GetProperty("segments")
            .EnumerateArray()
            .First()
            .GetProperty("from");

        var preview = Dispatch(
            session.State,
            "schematic.previewRoute",
            new JsonObject
            {
                ["documentId"] = "doc1",
                ["baseRevision"] = 1,
                ["mode"] = "connect",
                ["start"] = new JsonObject
                {
                    ["kind"] = "terminal",
                    ["token"] = "M1.G",
                    ["x"] = start.GetProperty("x").GetDouble(),
                    ["y"] = start.GetProperty("y").GetDouble(),
                },
                ["target"] = new JsonObject
                {
                    ["kind"] = "netAnchor",
                    ["token"] = "n1",
                    ["x"] = netPoint.GetProperty("x").GetDouble(),
                    ["y"] = netPoint.GetProperty("y").GetDouble(),
                },
            }
        );

        Assert.True(preview.RootElement.GetProperty("valid").GetBoolean());
        AssertOrthogonalSegments(preview.RootElement.GetProperty("segments").EnumerateArray());
    }

    [Fact]
    public void RenderSchematic_ThrowsApiExceptionWhenSelectedCircuitIsMissing()
    {
        using var session = ApiSession.Create();
        Dispatch(
            session.State,
            "document.open",
            new JsonObject
            {
                ["documentId"] = "doc1",
                ["text"] = BuildSampleSource(withRenderBlock: false),
            }
        );

        session.State.Documents["doc1"].CircuitName = "MissingCircuit";
        var ex = Assert.Throws<ApiException>(() =>
            Dispatch(session.State, "render.schematic", new JsonObject { ["documentId"] = "doc1" })
        );
        Assert.Contains("MissingCircuit", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void JobPoll_UsesInjectedClockForDeterministicProgress()
    {
        var originalClock = SchematicApiDispatcher.UtcNowProvider;
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00+00:00");
        SchematicApiDispatcher.UtcNowProvider = () => now;
        try
        {
            using var session = ApiSession.Create();
            var started = Dispatch(session.State, "job.start", new JsonObject());
            var jobId = started.RootElement.GetProperty("jobId").GetString();
            Assert.NotNull(jobId);

            now = now.AddMilliseconds(2600);
            var polled = Dispatch(session.State, "job.poll", new JsonObject { ["jobId"] = jobId });

            Assert.Equal("completed", polled.RootElement.GetProperty("state").GetString());
            Assert.Equal(100, polled.RootElement.GetProperty("progress").GetInt32());
        }
        finally
        {
            SchematicApiDispatcher.UtcNowProvider = originalClock;
        }
    }

    [Fact]
    public void DocumentOpen_ManualRender_ReturnsManualModeAndStructuredDiagnostics()
    {
        using var session = ApiSession.Create();
        var opened = Dispatch(
            session.State,
            "document.open",
            new JsonObject
            {
                ["documentId"] = "doc1",
                ["text"] = BuildManualSourceWithOverlappingPorts(),
            }
        );

        var document = opened.RootElement;
        Assert.Equal(
            "manual",
            document.GetProperty("renderSource").GetProperty("mode").GetString()
        );

        var diagnostic = document.GetProperty("diagnostics").EnumerateArray().First();
        Assert.Equal("warning", diagnostic.GetProperty("severity").GetString());
        Assert.Equal("CASRENDER-MANUAL-PORT-OVERLAP", diagnostic.GetProperty("code").GetString());
        Assert.True(diagnostic.TryGetProperty("message", out _));
        Assert.Equal(
            "IN",
            diagnostic.GetProperty("entityRefs").GetProperty("portName").GetString()
        );
        var point = diagnostic.GetProperty("geometry").GetProperty("point");
        Assert.Equal(0, point.GetProperty("x").GetDouble());
        Assert.Equal(0, point.GetProperty("y").GetDouble());
    }

    [Fact]
    public void DocumentOpen_ManualRender_StaleNetConnectivity_ReturnsDiagnosticsAndLayout()
    {
        using var session = ApiSession.Create();
        var opened = Dispatch(
            session.State,
            "document.open",
            new JsonObject
            {
                ["documentId"] = "docStale",
                ["text"] = BuildBranchingManualStaleConnectivitySource(),
            }
        );

        var document = opened.RootElement;
        Assert.Equal(
            "manual",
            document.GetProperty("renderSource").GetProperty("mode").GetString()
        );

        Assert.Contains(
            document.GetProperty("diagnostics").EnumerateArray(),
            diagnostic =>
                diagnostic.GetProperty("entityRefs").TryGetProperty("netName", out var netProp)
                && netProp.GetString() == "n1"
                && diagnostic.GetProperty("code").GetString()
                    is "CASRENDER-MANUAL-NET-DISCONNECTED"
                        or "CASRENDER-MANUAL-NET-DANGLING-SEGMENTS"
                        or "CASRENDER-MANUAL-NET-TERMINAL-OFF-WIRE"
        );

        var nets = document.GetProperty("layout").GetProperty("nets").EnumerateArray().ToList();
        Assert.NotEmpty(nets);
    }

    [Fact]
    public void DocumentOpen_WithAutoMode_NormalizesInMemoryRenderBlockToAuto()
    {
        using var session = ApiSession.Create();
        Dispatch(
            session.State,
            "document.open",
            new JsonObject
            {
                ["documentId"] = "doc1",
                ["text"] = BuildManualSourceWithOverlappingPorts(),
                ["mode"] = "auto",
            }
        );

        var state = session.State.Documents["doc1"];
        var circuit = state.Document.Circuits.Single(c => c.Name == state.CircuitName);
        Assert.NotNull(circuit.Render);
        Assert.Equal(RenderLayoutMode.Auto, circuit.Render.Mode);
        Assert.NotEmpty(circuit.Render.Entities);
    }

    [Fact]
    public void RenderSchematic_PersistManualOnAutoDocument_SnapshotsCompleteManualRender()
    {
        using var session = ApiSession.Create();
        Dispatch(
            session.State,
            "document.open",
            new JsonObject
            {
                ["documentId"] = "doc1",
                ["text"] = BuildSampleSource(withRenderBlock: false),
            }
        );

        var rendered = Dispatch(
            session.State,
            "render.schematic",
            new JsonObject
            {
                ["documentId"] = "doc1",
                ["mode"] = "manual",
                ["persist"] = true,
            }
        );

        Assert.Equal(
            "manual",
            rendered
                .RootElement.GetProperty("document")
                .GetProperty("renderSource")
                .GetProperty("mode")
                .GetString()
        );

        var circuit = ParseCircuit(rendered.RootElement.GetProperty("sourceText").GetString()!);
        AssertCompleteManualRender(circuit);
    }

    private static JsonDocument Dispatch(SessionState session, string method, JsonObject payload)
    {
        var response = SchematicApiDispatcher.Dispatch(session, method, payload.ToJsonString());
        return JsonDocument.Parse(response);
    }

    private static Circuit ParseCircuit(string sourceText, string circuitName = "Amp")
    {
        var read = CascodeReader.TryParse(sourceText, "<native-test>");
        Assert.True(
            read.Success,
            string.Join(Environment.NewLine, read.Diagnostics.Select(d => d.Message))
        );
        return read.Document!.Circuits.Single(circuit => circuit.Name == circuitName);
    }

    private static void AssertManualRenderHasNoCompletenessErrors(Circuit circuit)
    {
        var validation = RenderBlockValidator.Validate(circuit);
        Assert.DoesNotContain(
            validation.Messages,
            message => message.Text.Contains("Manual render requires", StringComparison.Ordinal)
        );
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
                Assert.True(sameX || sameY, "Expected preview segment to be orthogonal.");
            }
        );
    }

    private static void AssertCompleteManualRender(Circuit circuit)
    {
        Assert.NotNull(circuit.Render);
        Assert.Equal(RenderLayoutMode.Manual, circuit.Render!.Mode);

        AssertEntityHasManualDevicePlacement(circuit, "M1");
        AssertEntityHasManualDevicePlacement(circuit, "M2");
        AssertEntityHasManualPortPlacement(circuit, "IN");
        AssertEntityHasManualPortPlacement(circuit, "OUT");
        AssertEntityHasManualNetSegments(circuit, "GND");
        AssertEntityHasManualNetSegments(circuit, "n1");
    }

    private static void AssertEntityHasManualDevicePlacement(Circuit circuit, string name)
    {
        var entity = Assert.Single(circuit.Render!.Entities, entry => entry.Name == name);
        Assert.Equal(RenderEntityKind.Device, entity.Kind);
        Assert.NotNull(entity.Place);
        Assert.True(
            entity.Place!.Point is RenderAbsPoint or RenderRefPoint,
            $"Expected {name} placement point to be abs/ref but found {entity.Place.Point.GetType().Name}."
        );
        Assert.Equal(RenderConstraintStrength.Hard, entity.Place.Strength);
        Assert.NotNull(entity.Orientation);
    }

    private static void AssertEntityHasManualPortPlacement(Circuit circuit, string name)
    {
        var entity = Assert.Single(circuit.Render!.Entities, entry => entry.Name == name);
        Assert.Equal(RenderEntityKind.Port, entity.Kind);
        Assert.NotNull(entity.Place);
        Assert.IsType<RenderAbsPoint>(entity.Place!.Point);
        Assert.Equal(RenderConstraintStrength.Hard, entity.Place.Strength);
        Assert.NotNull(entity.Side);
        Assert.NotEqual(RenderPortSide.Auto, entity.Side!.Value);
    }

    private static void AssertEntityHasManualNetSegments(Circuit circuit, string name)
    {
        var entity = Assert.Single(circuit.Render!.Entities, entry => entry.Name == name);
        Assert.True(
            entity.Kind is RenderEntityKind.Net or RenderEntityKind.Port,
            $"Expected {name} entity kind to be net/port but found {entity.Kind}."
        );
        Assert.NotEmpty(entity.Segments);
        Assert.All(
            entity.Segments,
            segment =>
            {
                Assert.True(
                    segment.From is RenderAbsPoint or RenderRefPoint,
                    $"Expected {name} segment.From to be abs/ref but found {segment.From.GetType().Name}."
                );
                Assert.True(
                    segment.To is RenderAbsPoint or RenderRefPoint,
                    $"Expected {name} segment.To to be abs/ref but found {segment.To.GetType().Name}."
                );
            }
        );
    }

    private static string BuildSampleSource(bool withRenderBlock)
    {
        var renderBlock = withRenderBlock
            ? @"
  render {
    M1 place abs 0 0 soft
    M2 place abs 200 200 hint
  }
"
            : string.Empty;

        return $@"VERSION {CascodeVersion.Current}

primitive NMOS NMOS_Level1(size primSize) {{
  device ""nmos_level1""
  params {{
    W = primSize.W
    L = primSize.L
    m = primSize.M
  }}
}}

circuit Amp {{
  level EL
  input IN : analog
  output OUT : analog
  ground GND
  fill {{
    net n1 : analog
    size Unit = size(W=1u, L=180n, M=1)
    NMOS M1 = new NMOS_Level1(Unit) {{
      .D--OUT
      .G--IN
      .S--n1
      .B--GND
    }}
    NMOS M2 = new NMOS_Level1(Unit) {{
      .D--OUT
      .G--n1
      .S--GND
      .B--GND
    }}
  }}
{renderBlock}}}
";
    }

    private static string BuildSourceWithRender(
        string m1DeviceId,
        bool includeM2Device,
        bool includeM1Render,
        bool includeM2Render,
        double m1X,
        double m1Y,
        double m2X,
        double m2Y
    )
    {
        // Cascode language only supports integer pixel positions for abs placement,
        // so round the exact layout coordinates when writing source text.
        var m1Xi = (int)Math.Round(m1X);
        var m1Yi = (int)Math.Round(m1Y);
        var m2Xi = (int)Math.Round(m2X);
        var m2Yi = (int)Math.Round(m2Y);

        var m2Device = includeM2Device
            ? @"
    NMOS M2 = new NMOS_Level1(Unit) {
      .D--OUT
      .G--n1
      .S--GND
      .B--GND
    }"
            : string.Empty;
        var m1Render = includeM1Render
            ? $"    M1 place abs {m1Xi} {m1Yi} hard{Environment.NewLine}"
            : string.Empty;
        var m2Render = includeM2Render
            ? $"    M2 place abs {m2Xi} {m2Yi} hard{Environment.NewLine}"
            : string.Empty;

        return $@"VERSION {CascodeVersion.Current}

primitive NMOS NMOS_Level1(size primSize) {{
  device ""nmos_level1""
  params {{
    W = primSize.W
    L = primSize.L
    m = primSize.M
  }}
}}

circuit Amp {{
  level EL
  input IN : analog
  output OUT : analog
  ground GND
  fill {{
    net n1 : analog
    size Unit = size(W=1u, L=180n, M=1)
    NMOS {m1DeviceId} = new NMOS_Level1(Unit) {{
      .D--OUT
      .G--IN
      .S--n1
      .B--GND
    }}
{m2Device}
  }}
  render {{
{m1Render}{m2Render}  }}
}}
";
    }

    private static string BuildBranchingManualStaleConnectivitySource()
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
      seg ref R3.N abs 8 0
    }}
  }}
}}
";
    }

    private static string BuildManualSourceWithOverlappingPorts()
    {
        return $@"VERSION {CascodeVersion.Current}

primitive Resistor ResistorIdeal(size primSize) {{
  device ""resistor_ideal""
  params {{
    R = primSize.R
  }}
}}

circuit ManualNative {{
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
      place abs 0 0 hard
      side right
      seg ref R1.N ref OUT
    }}
    R1 place abs 100 50 hard
  }}
}}
";
    }

    private static string BuildDiffSnapshotSource()
    {
        return $@"VERSION {CascodeVersion.Current}
include lib.std

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

circuit DiffSnapshot {{
  level EL
  supply VDD
  input IN : Diff
  output OUT : analog
  input VBP_TAIL : bias
  fill {{
    net tail_node : analog

    PMOS M_TAIL = new PMOS_Level1(size(W=40u, L=500n, M=2)) {{
      .B--VDD
      .D--tail_node
      .G--VBP_TAIL
      .S--VDD
    }}

    PMOS M_INP = new PMOS_Level1(size(W=50u, L=500n, M=2)) {{
      .B--VDD
      .D--OUT
      .G--IN.P
      .S--tail_node
    }}

    PMOS M_INM = new PMOS_Level1(size(W=50u, L=500n, M=2)) {{
      .B--VDD
      .D--OUT
      .G--IN.N
      .S--tail_node
    }}
  }}
}}
";
    }

    private static string GetStdlibRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "lib", "std");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate lib/std from test base directory.");
    }

    private sealed class ApiSession : IDisposable
    {
        private readonly int _id;

        private ApiSession(int id, SessionState state)
        {
            _id = id;
            State = state;
        }

        public SessionState State { get; }

        public static ApiSession Create()
        {
            var id = SessionManager.CreateSession(null);
            Assert.True(SessionManager.TryGetSession(id, out var state));
            return new ApiSession(id, state);
        }

        public void Dispose()
        {
            SessionManager.DestroySession(_id);
        }
    }
}
