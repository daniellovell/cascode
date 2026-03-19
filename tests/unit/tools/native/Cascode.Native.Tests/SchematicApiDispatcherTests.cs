using System.Text.Json;
using System.Text.Json.Nodes;
using Cascode.Language;
using Cascode.Language.Validation;
using Cascode.Native;
using Cascode.Render.Analysis;
using Cascode.Render.Placement;
using Cascode.Render.Routing;

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
    public void ApplyOperations_FirstMutationOnDiffInputDocument_SnapshotsCompleteManualRender()
    {
        using var session = ApiSession.Create();
        session.State.StdlibRoot = GetStdlibRoot();
        var opened = Dispatch(
            session.State,
            "document.open",
            new JsonObject { ["documentId"] = "diff-doc", ["text"] = BuildDiffSnapshotSource() }
        );
        var tailPosition = opened
            .RootElement.GetProperty("layout")
            .GetProperty("devices")
            .EnumerateArray()
            .Single(device => device.GetProperty("id").GetString() == "M_TAIL")
            .GetProperty("position");

        var applied = Dispatch(
            session.State,
            "schematic.applyOperations",
            new JsonObject
            {
                ["documentId"] = "diff-doc",
                ["baseRevision"] = 1,
                ["operations"] = new JsonArray(
                    new JsonObject
                    {
                        ["opId"] = "op-tail-1",
                        ["type"] = "moveDevice",
                        ["deviceId"] = "M_TAIL",
                        ["x"] = (int)Math.Round(tailPosition.GetProperty("x").GetDouble()),
                        ["y"] = (int)Math.Round(tailPosition.GetProperty("y").GetDouble()),
                    }
                ),
            }
        );

        Assert.Equal(
            "manual",
            applied
                .RootElement.GetProperty("document")
                .GetProperty("renderSource")
                .GetProperty("mode")
                .GetString()
        );

        var circuit = ParseCircuit(
            applied.RootElement.GetProperty("sourceText").GetString()!,
            "DiffSnapshot"
        );
        AssertManualRenderHasNoCompletenessErrors(circuit);
        AssertEntityHasManualDevicePlacement(circuit, "M_TAIL");
        AssertEntityHasManualNetSegments(circuit, "IN.P");
        AssertEntityHasManualNetSegments(circuit, "IN.N");
    }

    [Fact]
    public void ApplyOperations_FirstMutationOnAutoDocument_SnapshotsCompleteManualRender()
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

        var applied = Dispatch(
            session.State,
            "schematic.applyOperations",
            new JsonObject
            {
                ["documentId"] = "doc1",
                ["baseRevision"] = 1,
                ["operations"] = new JsonArray(
                    new JsonObject
                    {
                        ["opId"] = "op-1",
                        ["type"] = "moveDevice",
                        ["deviceId"] = "M1",
                        ["x"] = (int)Math.Round(m1Position.GetProperty("x").GetDouble()),
                        ["y"] = (int)Math.Round(m1Position.GetProperty("y").GetDouble()),
                    }
                ),
            }
        );

        Assert.Equal(
            "manual",
            applied
                .RootElement.GetProperty("document")
                .GetProperty("renderSource")
                .GetProperty("mode")
                .GetString()
        );

        var circuit = ParseCircuit(applied.RootElement.GetProperty("sourceText").GetString()!);
        AssertCompleteManualRender(circuit);
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
    public void ApplyOperations_UpdatesRenderAndPinsUntouchedEntriesOnFirstMutation()
    {
        using var session = ApiSession.Create();
        var opened = Dispatch(
            session.State,
            "document.open",
            new JsonObject
            {
                ["documentId"] = "doc1",
                ["text"] = BuildSampleSource(withRenderBlock: true),
            }
        );
        var m1Position = opened
            .RootElement.GetProperty("layout")
            .GetProperty("devices")
            .EnumerateArray()
            .Single(device => device.GetProperty("id").GetString() == "M1")
            .GetProperty("position");

        var applyMove = Dispatch(
            session.State,
            "schematic.applyOperations",
            new JsonObject
            {
                ["documentId"] = "doc1",
                ["baseRevision"] = 1,
                ["operations"] = new JsonArray(
                    new JsonObject
                    {
                        ["opId"] = "op-1",
                        ["type"] = "moveDevice",
                        ["deviceId"] = "M1",
                        // moveDevice expects integer pixel positions (layout coordinates)
                        ["x"] = (int)Math.Round(m1Position.GetProperty("x").GetDouble()),
                        ["y"] = (int)Math.Round(m1Position.GetProperty("y").GetDouble()),
                    }
                ),
            }
        );

        Assert.Equal(
            2,
            applyMove.RootElement.GetProperty("document").GetProperty("revision").GetInt32()
        );
        var sourceText = applyMove.RootElement.GetProperty("sourceText").GetString()!;
        var circuit = ParseCircuit(sourceText);
        Assert.Equal(RenderLayoutMode.Manual, circuit.Render!.Mode);
        var m1 = Assert.Single(circuit.Render!.Entities, entry => entry.Name == "M1");
        Assert.IsType<RenderAbsPoint>(m1.Place!.Point);
        Assert.Equal(RenderConstraintStrength.Hard, m1.Place.Strength);

        var m2 = Assert.Single(circuit.Render.Entities, entry => entry.Name == "M2");
        Assert.NotNull(m2.Place);
        Assert.Equal(RenderConstraintStrength.Hard, m2.Place!.Strength);
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
    public void RevisionConflict_ReportsCurrentRevisionAndChangedEntities()
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

        Dispatch(
            session.State,
            "schematic.applyOperations",
            new JsonObject
            {
                ["documentId"] = "doc1",
                ["baseRevision"] = 1,
                ["operations"] = new JsonArray(
                    new JsonObject
                    {
                        ["opId"] = "op-1",
                        ["type"] = "moveDevice",
                        ["deviceId"] = "M1",
                        ["x"] = (int)Math.Round(m1Position.GetProperty("x").GetDouble()),
                        ["y"] = (int)Math.Round(m1Position.GetProperty("y").GetDouble()),
                    }
                ),
            }
        );

        var ex = Assert.Throws<ApiException>(() =>
            SchematicApiDispatcher.Dispatch(
                session.State,
                "schematic.applyOperations",
                new JsonObject
                {
                    ["documentId"] = "doc1",
                    ["baseRevision"] = 1,
                    ["operations"] = new JsonArray(
                        new JsonObject
                        {
                            ["opId"] = "op-2",
                            ["type"] = "rotateDevice",
                            ["deviceId"] = "M1",
                            ["angle"] = 90,
                        }
                    ),
                }.ToJsonString()
            )
        );

        Assert.Equal("CASAPI-REVISION-CONFLICT", ex.Code);
        Assert.NotNull(ex.Details);
        Assert.Equal(2, ex.Details!["currentRevision"]!.GetValue<int>());
    }

    [Fact]
    public void Canonicalization_PrefersExplicitAndNearbyAnchorsBeforeAbs()
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

        var gate = opened
            .RootElement.GetProperty("renderCache")
            .GetProperty("terminalPoints")
            .GetProperty("M1")
            .GetProperty("G");
        var gx = gate.GetProperty("x").GetDouble();
        var gy = gate.GetProperty("y").GetDouble();

        var movePort = Dispatch(
            session.State,
            "schematic.applyOperations",
            new JsonObject
            {
                ["documentId"] = "doc1",
                ["baseRevision"] = 1,
                ["operations"] = new JsonArray(
                    new JsonObject
                    {
                        ["opId"] = "op-1",
                        ["type"] = "movePort",
                        ["port"] = "IN",
                        ["x"] = gx + 1,
                        ["y"] = gy,
                    }
                ),
            }
        );

        var circuitAfterPortMove = ParseCircuit(
            movePort.RootElement.GetProperty("sourceText").GetString()!
        );
        var inEntry = Assert.Single(
            circuitAfterPortMove.Render!.Entities,
            entry => entry.Name == "IN"
        );
        var inPoint = Assert.IsType<RenderRefPoint>(inEntry.Place!.Point);
        Assert.StartsWith("M1", inPoint.Anchor, StringComparison.Ordinal);

        var moveWithExplicitAnchor = Dispatch(
            session.State,
            "schematic.applyOperations",
            new JsonObject
            {
                ["documentId"] = "doc1",
                ["baseRevision"] = 2,
                ["operations"] = new JsonArray(
                    new JsonObject
                    {
                        ["opId"] = "op-2",
                        ["type"] = "moveDevice",
                        ["deviceId"] = "M2",
                        ["x"] = opened
                            .RootElement.GetProperty("layout")
                            .GetProperty("devices")
                            .EnumerateArray()
                            .Single(device => device.GetProperty("id").GetString() == "M2")
                            .GetProperty("position")
                            .GetProperty("x")
                            .GetDouble(),
                        ["y"] = opened
                            .RootElement.GetProperty("layout")
                            .GetProperty("devices")
                            .EnumerateArray()
                            .Single(device => device.GetProperty("id").GetString() == "M2")
                            .GetProperty("position")
                            .GetProperty("y")
                            .GetDouble(),
                        ["anchor"] = "M1.G",
                    }
                ),
            }
        );

        var circuitAfterExplicit = ParseCircuit(
            moveWithExplicitAnchor.RootElement.GetProperty("sourceText").GetString()!
        );
        var m2Ref = Assert.IsType<RenderRefPoint>(
            Assert
                .Single(circuitAfterExplicit.Render!.Entities, entry => entry.Name == "M2")
                .Place!.Point
        );
        Assert.Equal("M1.G", m2Ref.Anchor);

        var farMove = Dispatch(
            session.State,
            "schematic.applyOperations",
            new JsonObject
            {
                ["documentId"] = "doc1",
                ["baseRevision"] = 3,
                ["operations"] = new JsonArray(
                    new JsonObject
                    {
                        ["opId"] = "op-3",
                        ["type"] = "movePort",
                        ["port"] = "IN",
                        ["x"] = 1000,
                        ["y"] = 1000,
                    }
                ),
            }
        );

        var circuitAfterFarMove = ParseCircuit(
            farMove.RootElement.GetProperty("sourceText").GetString()!
        );
        Assert.IsType<RenderAbsPoint>(
            Assert
                .Single(circuitAfterFarMove.Render!.Entities, entry => entry.Name == "IN")
                .Place!.Point
        );
    }

    [Fact]
    public void ApplyOperations_ConnectTerminals_DeduplicatesBidirectionalConnections()
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

        Dispatch(
            session.State,
            "schematic.applyOperations",
            new JsonObject
            {
                ["documentId"] = "doc1",
                ["baseRevision"] = 1,
                ["operations"] = new JsonArray(
                    new JsonObject
                    {
                        ["opId"] = "op-1",
                        ["type"] = "connectTerminals",
                        ["from"] = "M1.G",
                        ["to"] = "M2.G",
                    }
                ),
            }
        );

        var second = Dispatch(
            session.State,
            "schematic.applyOperations",
            new JsonObject
            {
                ["documentId"] = "doc1",
                ["baseRevision"] = 2,
                ["operations"] = new JsonArray(
                    new JsonObject
                    {
                        ["opId"] = "op-2",
                        ["type"] = "connectTerminals",
                        ["from"] = "M2.G",
                        ["to"] = "M1.G",
                    }
                ),
            }
        );

        var circuit = ParseCircuit(second.RootElement.GetProperty("sourceText").GetString()!);
        var duplicateCount = circuit.Fill!.Connections.Count(conn =>
            (conn.From == "M1.G" && conn.To == "M2.G") || (conn.From == "M2.G" && conn.To == "M1.G")
        );
        Assert.Equal(1, duplicateCount);
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
    public void ApplyRouteEdit_InManualMode_PersistsExplicitOrthogonalSegments()
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
        var start = rendered
            .RootElement.GetProperty("document")
            .GetProperty("renderCache")
            .GetProperty("terminalPoints")
            .GetProperty("M1")
            .GetProperty("G");
        var end = rendered
            .RootElement.GetProperty("document")
            .GetProperty("layout")
            .GetProperty("ports")
            .EnumerateArray()
            .Single(port => port.GetProperty("name").GetString() == "OUT")
            .GetProperty("position");

        var applied = Dispatch(
            session.State,
            "schematic.applyRouteEdit",
            new JsonObject
            {
                ["documentId"] = "doc1",
                ["baseRevision"] = 2,
                ["mode"] = "connect",
                ["start"] = new JsonObject
                {
                    ["kind"] = "terminal",
                    ["token"] = "M1.G",
                    ["x"] = start.GetProperty("x").GetDouble(),
                    ["y"] = start.GetProperty("y").GetDouble(),
                },
                ["end"] = new JsonObject
                {
                    ["kind"] = "netAnchor",
                    ["token"] = "OUT",
                    ["x"] = end.GetProperty("x").GetDouble(),
                    ["y"] = end.GetProperty("y").GetDouble(),
                },
            }
        );

        Assert.Equal(
            "manual",
            applied
                .RootElement.GetProperty("document")
                .GetProperty("renderSource")
                .GetProperty("mode")
                .GetString()
        );

        var circuit = ParseCircuit(applied.RootElement.GetProperty("sourceText").GetString()!);
        AssertManualRenderHasNoCompletenessErrors(circuit);
        AssertOrthogonalSegments(
            applied
                .RootElement.GetProperty("document")
                .GetProperty("layout")
                .GetProperty("nets")
                .EnumerateArray()
                .SelectMany(net => net.GetProperty("segments").EnumerateArray())
        );
        Assert.Contains(
            circuit.Fill!.Connections,
            connection =>
                (connection.From == "M1.G" && connection.To == "OUT")
                || (connection.From == "OUT" && connection.To == "M1.G")
        );
    }

    [Fact]
    public void ApplyRouteEdit_InAutoMode_PreservesAutoTopologySemantics()
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
        var applied = Dispatch(
            session.State,
            "schematic.applyRouteEdit",
            new JsonObject
            {
                ["documentId"] = "doc1",
                ["baseRevision"] = 1,
                ["mode"] = "connect",
                ["start"] = new JsonObject
                {
                    ["kind"] = "terminal",
                    ["token"] = "M1.G",
                    ["x"] = 60,
                    ["y"] = 50,
                },
                ["end"] = new JsonObject
                {
                    ["kind"] = "netAnchor",
                    ["token"] = "OUT",
                    ["x"] = 120,
                    ["y"] = 50,
                },
            }
        );

        Assert.Equal(
            "auto",
            applied
                .RootElement.GetProperty("document")
                .GetProperty("renderSource")
                .GetProperty("mode")
                .GetString()
        );

        var circuit = ParseCircuit(applied.RootElement.GetProperty("sourceText").GetString()!);
        Assert.Null(circuit.Render);
        Assert.Contains(
            circuit.Fill!.Connections,
            connection =>
                (connection.From == "M1.G" && connection.To == "OUT")
                || (connection.From == "OUT" && connection.To == "M1.G")
        );
    }

    [Fact]
    public void ApplyPlacementEdits_InManualMode_RebuildsOrthogonalRoutesAfterDrag()
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
        var m1Position = rendered
            .RootElement.GetProperty("document")
            .GetProperty("layout")
            .GetProperty("devices")
            .EnumerateArray()
            .Single(device => device.GetProperty("id").GetString() == "M1")
            .GetProperty("position");
        var requestedX = m1Position.GetProperty("x").GetDouble() + 17;
        var requestedY = m1Position.GetProperty("y").GetDouble() + 9;

        var applied = Dispatch(
            session.State,
            "schematic.applyPlacementEdits",
            new JsonObject
            {
                ["documentId"] = "doc1",
                ["baseRevision"] = 2,
                ["operations"] = new JsonArray(
                    new JsonObject
                    {
                        ["opId"] = "move-placement-1",
                        ["type"] = "moveDevice",
                        ["deviceId"] = "M1",
                        ["x"] = requestedX,
                        ["y"] = requestedY,
                    }
                ),
            }
        );

        Assert.Equal(
            "manual",
            applied
                .RootElement.GetProperty("document")
                .GetProperty("renderSource")
                .GetProperty("mode")
                .GetString()
        );
        var movedDevice = applied
            .RootElement.GetProperty("document")
            .GetProperty("layout")
            .GetProperty("devices")
            .EnumerateArray()
            .Single(device => device.GetProperty("id").GetString() == "M1");
        Assert.Equal(requestedX, movedDevice.GetProperty("position").GetProperty("x").GetDouble());
        Assert.Equal(requestedY, movedDevice.GetProperty("position").GetProperty("y").GetDouble());

        var circuit = ParseCircuit(applied.RootElement.GetProperty("sourceText").GetString()!);
        AssertManualRenderHasNoCompletenessErrors(circuit);
        AssertOrthogonalSegments(
            applied
                .RootElement.GetProperty("document")
                .GetProperty("layout")
                .GetProperty("nets")
                .EnumerateArray()
                .SelectMany(net => net.GetProperty("segments").EnumerateArray())
        );
    }

    [Fact]
    public void ApplyOperations_SetPortSide_WritesRenderPortSide()
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

        var applied = Dispatch(
            session.State,
            "schematic.applyOperations",
            new JsonObject
            {
                ["documentId"] = "doc1",
                ["baseRevision"] = 1,
                ["operations"] = new JsonArray(
                    new JsonObject
                    {
                        ["opId"] = "op-side-1",
                        ["type"] = "setPortSide",
                        ["port"] = "IN",
                        ["side"] = "top",
                    }
                ),
            }
        );

        var circuit = ParseCircuit(applied.RootElement.GetProperty("sourceText").GetString()!);
        var portEntry = Assert.Single(circuit.Render!.Entities, entry => entry.Name == "IN");
        Assert.Equal(RenderPortSide.Top, portEntry.Side);
    }

    [Fact]
    public void ApplyOperations_SetPortSide_RejectsAutoForManualRender()
    {
        using var session = ApiSession.Create();
        Dispatch(
            session.State,
            "document.open",
            new JsonObject
            {
                ["documentId"] = "doc1",
                ["text"] = BuildManualSourceWithOverlappingPorts(),
            }
        );

        var ex = Assert.Throws<ApiException>(() =>
            Dispatch(
                session.State,
                "schematic.applyOperations",
                new JsonObject
                {
                    ["documentId"] = "doc1",
                    ["baseRevision"] = 1,
                    ["operations"] = new JsonArray(
                        new JsonObject
                        {
                            ["opId"] = "op-side-2",
                            ["type"] = "setPortSide",
                            ["port"] = "IN",
                            ["side"] = "auto",
                        }
                    ),
                }
            )
        );

        Assert.Equal("CASAPI-INVALID-REQUEST", ex.Code);
        Assert.Contains("explicit port side", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyOperations_FailedDelete_DoesNotCommitPartialDocumentState()
    {
        using var session = ApiSession.Create();
        var opened = Dispatch(
            session.State,
            "document.open",
            new JsonObject
            {
                ["documentId"] = "doc1",
                ["text"] = BuildManualSourceWithDeviceAnchoredPort(),
            }
        );
        var resistorPosition = opened
            .RootElement.GetProperty("layout")
            .GetProperty("devices")
            .EnumerateArray()
            .Single(device => device.GetProperty("id").GetString() == "R1")
            .GetProperty("position");

        var ex = Assert.Throws<ApiException>(() =>
            Dispatch(
                session.State,
                "schematic.applyOperations",
                new JsonObject
                {
                    ["documentId"] = "doc1",
                    ["baseRevision"] = 1,
                    ["operations"] = new JsonArray(
                        new JsonObject
                        {
                            ["opId"] = "delete-r1",
                            ["type"] = "deleteDevice",
                            ["deviceId"] = "R1",
                        }
                    ),
                }
            )
        );
        Assert.Equal(
            "Manual render could not resolve explicit port placement anchors for: OUT.",
            ex.Message
        );

        var current = Dispatch(
            session.State,
            "convert.toCas",
            new JsonObject { ["documentId"] = "doc1" }
        );
        Assert.Equal(1, current.RootElement.GetProperty("revision").GetInt32());
        Assert.Contains("Resistor R1", current.RootElement.GetProperty("sourceText").GetString());

        var moved = Dispatch(
            session.State,
            "schematic.applyPlacementEdits",
            new JsonObject
            {
                ["documentId"] = "doc1",
                ["baseRevision"] = 1,
                ["operations"] = new JsonArray(
                    new JsonObject
                    {
                        ["opId"] = "move-r1",
                        ["type"] = "moveDevice",
                        ["deviceId"] = "R1",
                        ["x"] = (int)Math.Round(resistorPosition.GetProperty("x").GetDouble()) + 40,
                        ["y"] = (int)Math.Round(resistorPosition.GetProperty("y").GetDouble()) + 20,
                    }
                ),
            }
        );

        Assert.Equal(
            2,
            moved.RootElement.GetProperty("document").GetProperty("revision").GetInt32()
        );
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

    [Fact]
    public void ApplyOperations_SetNetSegments_RewritesRenderSegments()
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

        var outSegmentsJson = BuildSampleAmpOutNetSegmentsJson();

        var applied = Dispatch(
            session.State,
            "schematic.applyOperations",
            new JsonObject
            {
                ["documentId"] = "doc1",
                ["baseRevision"] = 1,
                ["operations"] = new JsonArray(
                    new JsonObject
                    {
                        ["opId"] = "op-1",
                        ["type"] = "setNetSegments",
                        ["net"] = "OUT",
                        ["segments"] = outSegmentsJson,
                    }
                ),
            }
        );

        var circuit = ParseCircuit(applied.RootElement.GetProperty("sourceText").GetString()!);
        var outEntry = Assert.Single(circuit.Render!.Entities, entry => entry.Name == "OUT");
        Assert.Equal(outSegmentsJson.Count, outEntry.Segments.Count);
    }

    /// <summary>
    /// Auto-router segments for OUT on <see cref="BuildSampleSource"/> (pixel grid); used so setNetSegments tests stay aligned with placement.
    /// </summary>
    private static JsonArray BuildSampleAmpOutNetSegmentsJson()
    {
        var read = CascodeReader.TryParse(
            BuildSampleSource(withRenderBlock: false),
            "sample-amp.cas"
        );
        Assert.True(read.Success);
        var circuit = read.Document!.Circuits.Single(c => c.Name == "Amp");
        var graph = CircuitGraph.Build(circuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var placement = CoarseGridPlacer.Place(topology, graph);
        var routing = MazeRouter.Route(placement, graph);
        var outSegs = routing.SegmentsByNet["OUT"];
        Assert.NotEmpty(outSegs);
        var nodes = new JsonArray();
        foreach (var s in outSegs)
        {
            nodes.Add(
                new JsonObject
                {
                    ["from"] = new JsonObject { ["x"] = s.From.X, ["y"] = s.From.Y },
                    ["to"] = new JsonObject { ["x"] = s.To.X, ["y"] = s.To.Y },
                }
            );
        }

        return nodes;
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

    private static string BuildManualSourceWithDeviceAnchoredPort()
    {
        return $@"VERSION {CascodeVersion.Current}

primitive Resistor ResistorIdeal(size primSize) {{
  device ""resistor_ideal""
  params {{
    R = primSize.R
  }}
}}

circuit ManualDeleteFailure {{
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
      place ref R1.N 20 0 hard
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
