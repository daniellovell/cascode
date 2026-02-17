using System.Text.Json;
using System.Text.Json.Nodes;
using Cascode.Language;

namespace Cascode.Native;

internal static class SchematicApiDispatcher
{
    public static string Dispatch(SessionState session, string method, string requestJson)
    {
        return method switch
        {
            "document.open" => DocumentOpen(session, requestJson),
            "document.updateText" => DocumentUpdateText(session, requestJson),
            "document.close" => DocumentClose(session, requestJson),
            "convert.toStructural" => ConvertToStructural(session, requestJson),
            "convert.toCas" => ConvertToCas(session, requestJson),
            "render.schematic" => RenderSchematic(session, requestJson),
            "schematic.applyOperations" => ApplyOperations(session, requestJson),
            "job.start" => JobStart(session, requestJson),
            "job.poll" => JobPoll(session, requestJson),
            "job.cancel" => JobCancel(session, requestJson),
            "erc.run" => PassThroughStub("cascode.erc/1.0"),
            "emit.run" => PassThroughStub("cascode.emit/1.0"),
            "verify.run" => PassThroughStub("cascode.verify/1.0"),
            "command.execute" => PassThroughStub("cascode.command/1.0"),
            _ => throw new ApiException(
                "CASAPI-INVALID-REQUEST",
                $"Unknown API method '{method}'."
            ),
        };
    }

    private static string DocumentOpen(SessionState session, string requestJson)
    {
        using var doc = JsonDocument.Parse(requestJson);
        var root = doc.RootElement;

        var documentId = TryGetString(root, "documentId") ?? "doc_1";
        var sourceText = RequireString(root, "text");
        var circuitName = TryGetString(root, "circuit");

        var read = CascodeReader.TryParse(sourceText, "<api>");
        EnsureParseSuccess(read);

        var selectedCircuit = SelectCircuit(read.Document!, circuitName);

        var state = new DocumentState
        {
            DocumentId = documentId,
            SourceText = sourceText,
            Document = read.Document!,
            CircuitName = selectedCircuit.Name,
            Revision = 1,
            ChangedEntities = Array.Empty<string>(),
        };

        session.Documents[documentId] = state;

        var mode = ParseRenderMode(TryGetString(root, "mode"));
        var render = SchematicDocumentBuilder.Build(state, mode, allowRelaxation: false);
        return ApiJson.SerializeDocument(render);
    }

    private static string DocumentUpdateText(SessionState session, string requestJson)
    {
        using var doc = JsonDocument.Parse(requestJson);
        var root = doc.RootElement;

        var state = GetDocumentState(session, RequireString(root, "documentId"));
        var baseRevision = TryGetInt(root, "baseRevision");
        EnsureRevision(state, baseRevision);

        var sourceText = RequireString(root, "text");
        var read = CascodeReader.TryParse(sourceText, "<api>");
        EnsureParseSuccess(read);

        var selectedCircuit = SelectCircuit(
            read.Document!,
            TryGetString(root, "circuit") ?? state.CircuitName
        );

        state.SourceText = sourceText;
        state.Document = read.Document!;
        state.CircuitName = selectedCircuit.Name;
        state.Revision++;
        state.ChangedEntities = Array.Empty<string>();

        var render = SchematicDocumentBuilder.Build(
            state,
            RenderSchematicMode.RespectRenderBlock,
            allowRelaxation: false
        );
        var response = new JsonObject
        {
            ["schema"] = "cascode.document.update/1.0",
            ["document"] = ApiJson.SerializeDocumentNode(render),
            ["sourceText"] = state.SourceText,
        };

        return response.ToJsonString(ApiJson.Options);
    }

    private static string DocumentClose(SessionState session, string requestJson)
    {
        using var doc = JsonDocument.Parse(requestJson);
        var documentId = RequireString(doc.RootElement, "documentId");
        session.Documents.Remove(documentId);

        return new JsonObject
        {
            ["schema"] = "cascode.document.close/1.0",
            ["ok"] = true,
        }.ToJsonString(ApiJson.Options);
    }

    private static string ConvertToStructural(SessionState session, string requestJson)
    {
        using var doc = JsonDocument.Parse(requestJson);
        var state = GetDocumentState(session, RequireString(doc.RootElement, "documentId"));

        var render = SchematicDocumentBuilder.Build(
            state,
            RenderSchematicMode.RespectRenderBlock,
            allowRelaxation: false
        );
        var response = new JsonObject
        {
            ["schema"] = "cascode.structural/1.0",
            ["documentId"] = state.DocumentId,
            ["revision"] = state.Revision,
            ["structural"] = ApiJson.SerializeStructuralNode(render.Structural),
        };

        return response.ToJsonString(ApiJson.Options);
    }

    private static string ConvertToCas(SessionState session, string requestJson)
    {
        using var doc = JsonDocument.Parse(requestJson);
        var state = GetDocumentState(session, RequireString(doc.RootElement, "documentId"));

        return new JsonObject
        {
            ["schema"] = "cascode.source/1.0",
            ["documentId"] = state.DocumentId,
            ["revision"] = state.Revision,
            ["sourceText"] = state.SourceText,
        }.ToJsonString(ApiJson.Options);
    }

    private static string RenderSchematic(SessionState session, string requestJson)
    {
        using var doc = JsonDocument.Parse(requestJson);
        var root = doc.RootElement;

        var state = GetDocumentState(session, RequireString(root, "documentId"));
        var mode = ParseRenderMode(TryGetString(root, "mode"));
        var allowRelaxation = TryGetBool(root, "allowConstraintRelaxation") ?? false;
        var persist = TryGetBool(root, "persist") ?? false;

        if (mode == RenderSchematicMode.RerenderFromScratch && persist)
        {
            var circuit = FindCircuit(state);
            circuit.Render = null;
            state.SourceText = SerializeSource(state.Document);
            state.Revision++;
        }

        var render = SchematicDocumentBuilder.Build(state, mode, allowRelaxation);
        var response = new JsonObject
        {
            ["schema"] = "cascode.render/1.0",
            ["document"] = ApiJson.SerializeDocumentNode(render),
            ["sourceText"] = state.SourceText,
        };

        return response.ToJsonString(ApiJson.Options);
    }

    private static string ApplyOperations(SessionState session, string requestJson)
    {
        using var doc = JsonDocument.Parse(requestJson);
        var root = doc.RootElement;

        var state = GetDocumentState(session, RequireString(root, "documentId"));
        EnsureRevision(state, TryGetInt(root, "baseRevision"));

        var changed = new HashSet<string>(StringComparer.Ordinal);
        var operations = root.TryGetProperty("operations", out var operationsEl)
            ? operationsEl.EnumerateArray().ToArray()
            : Array.Empty<JsonElement>();

        foreach (var operation in operations)
        {
            SchematicOperationApplier.Apply(state, operation, changed);
        }

        state.Revision++;
        state.ChangedEntities = changed.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        var render = SchematicDocumentBuilder.Build(
            state,
            RenderSchematicMode.RespectRenderBlock,
            allowRelaxation: false
        );
        state.SourceText = SerializeSource(state.Document);

        var response = new JsonObject
        {
            ["schema"] = "cascode.apply/1.0",
            ["document"] = ApiJson.SerializeDocumentNode(render),
            ["sourceText"] = state.SourceText,
        };

        return response.ToJsonString(ApiJson.Options);
    }

    private static string JobStart(SessionState session, string requestJson)
    {
        _ = requestJson;
        var id = $"job_{Guid.NewGuid():N}";
        session.Jobs[id] = new BenchJob
        {
            JobId = id,
            StartedAt = DateTimeOffset.UtcNow,
            ProgressPercent = 0,
        };

        return new JsonObject
        {
            ["schema"] = "cascode.job/1.0",
            ["jobId"] = id,
            ["state"] = "running",
        }.ToJsonString(ApiJson.Options);
    }

    private static string JobPoll(SessionState session, string requestJson)
    {
        using var doc = JsonDocument.Parse(requestJson);
        var jobId = RequireString(doc.RootElement, "jobId");
        if (!session.Jobs.TryGetValue(jobId, out var job))
        {
            throw new ApiException("CASAPI-INVALID-REQUEST", $"Unknown job '{jobId}'.");
        }

        if (job.State == JobState.Running)
        {
            var elapsedMs = (DateTimeOffset.UtcNow - job.StartedAt).TotalMilliseconds;
            job.ProgressPercent = Math.Clamp((int)(elapsedMs / 25), 0, 100);
            if (job.ProgressPercent >= 100)
            {
                job.State = JobState.Completed;
            }
        }

        return new JsonObject
        {
            ["schema"] = "cascode.job.poll/1.0",
            ["jobId"] = jobId,
            ["state"] = job.State.ToString().ToLowerInvariant(),
            ["progress"] = job.ProgressPercent,
        }.ToJsonString(ApiJson.Options);
    }

    private static string JobCancel(SessionState session, string requestJson)
    {
        using var doc = JsonDocument.Parse(requestJson);
        var jobId = RequireString(doc.RootElement, "jobId");
        if (session.Jobs.TryGetValue(jobId, out var job) && job.State == JobState.Running)
        {
            job.State = JobState.Cancelled;
        }

        return new JsonObject
        {
            ["schema"] = "cascode.job.cancel/1.0",
            ["jobId"] = jobId,
            ["ok"] = true,
        }.ToJsonString(ApiJson.Options);
    }

    private static string PassThroughStub(string schema)
    {
        return new JsonObject { ["schema"] = schema, ["ok"] = true }.ToJsonString(ApiJson.Options);
    }

    private static Circuit SelectCircuit(CascodeDocument document, string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var byName = document.Circuits.FirstOrDefault(c => c.Name == requested);
            if (byName is not null)
            {
                return byName;
            }
        }

        var selected = document.Circuits.FirstOrDefault(c =>
            !c.Inline && c.Level is CascodeLevel.EL or CascodeLevel.ML
        );
        if (selected is null)
        {
            throw new ApiException(
                "CASAPI-INVALID-REQUEST",
                "No non-inline EL/ML circuit available."
            );
        }

        return selected;
    }

    private static Circuit FindCircuit(DocumentState state)
    {
        return state.Document.Circuits.First(c => c.Name == state.CircuitName);
    }

    private static DocumentState GetDocumentState(SessionState session, string documentId)
    {
        if (!session.Documents.TryGetValue(documentId, out var state))
        {
            throw new ApiException("CASAPI-INVALID-REQUEST", $"Unknown document '{documentId}'.");
        }

        return state;
    }

    private static void EnsureParseSuccess(CascodeReadResult read)
    {
        if (read.Success)
        {
            return;
        }

        var first = read.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        throw new ApiException(
            "CASAPI-PARSE-FAILED",
            first?.Message ?? "Failed to parse Cascode source.",
            new JsonObject
            {
                ["line"] = first?.Line,
                ["column"] = first?.Column,
                ["diagnostics"] = new JsonArray(
                    read.Diagnostics.Select(d => (JsonNode?)d.Message).ToArray()
                ),
            }
        );
    }

    private static void EnsureRevision(DocumentState state, int? baseRevision)
    {
        if (baseRevision is null || baseRevision.Value == state.Revision)
        {
            return;
        }

        throw new ApiException(
            "CASAPI-REVISION-CONFLICT",
            "Base revision does not match current document revision.",
            new JsonObject
            {
                ["currentRevision"] = state.Revision,
                ["changedEntities"] = new JsonArray(
                    state.ChangedEntities.Select(name => (JsonNode?)name).ToArray()
                ),
            }
        );
    }

    private static string SerializeSource(CascodeDocument document)
    {
        using var writer = new StringWriter();
        CascodeWriter.Write(document, writer);
        return writer.ToString();
    }

    private static RenderSchematicMode ParseRenderMode(string? raw)
    {
        return raw?.ToLowerInvariant() switch
        {
            "reflowunlocked" => RenderSchematicMode.ReflowUnlocked,
            "rerenderfromscratch" => RenderSchematicMode.RerenderFromScratch,
            _ => RenderSchematicMode.RespectRenderBlock,
        };
    }

    private static string RequireString(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var child) && child.ValueKind == JsonValueKind.String)
        {
            return child.GetString()!;
        }

        throw new ApiException("CASAPI-INVALID-REQUEST", $"Missing string field '{name}'.");
    }

    private static string? TryGetString(JsonElement element, string name)
    {
        return
            element.TryGetProperty(name, out var child) && child.ValueKind == JsonValueKind.String
            ? child.GetString()
            : null;
    }

    private static int RequireInt(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var child) && child.TryGetInt32(out var value))
        {
            return value;
        }

        throw new ApiException("CASAPI-INVALID-REQUEST", $"Missing integer field '{name}'.");
    }

    private static int? TryGetInt(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var child) && child.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static bool? TryGetBool(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var child) && child.ValueKind == JsonValueKind.True
                ? true
            : element.TryGetProperty(name, out child) && child.ValueKind == JsonValueKind.False
                ? false
            : null;
    }
}
